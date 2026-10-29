using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FlaUI.Mcp.Core;

/// <summary>
/// Holds a resource (typically a Windows power availability request) while tool
/// calls are actively arriving, releasing it after a sliding idle period.
///
/// <see cref="Poke"/> acquires the resource on first call and extends the hold
/// on every subsequent call; when no poke arrives for the hold duration, the
/// resource is released so the machine returns to its normal power/lock policy.
/// </summary>
public sealed class KeepAwake : IDisposable
{
    private readonly object _lock = new();
    private readonly TimeSpan _holdDuration;
    private readonly Action _acquire;
    private readonly Action _release;
    private readonly IDisposable? _ownedResource;
    private readonly Func<bool>? _isAcquired;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private long _pokedAt;
    private bool _active;
    private bool _acquireFailureLogged;
    private bool _disposed;

    public KeepAwake(
        TimeSpan holdDuration,
        Action acquire,
        Action release,
        IDisposable? ownedResource = null,
        TimeProvider? time = null
    )
        : this(holdDuration, acquire, release, ownedResource, time, null) { }

    internal KeepAwake(
        TimeSpan holdDuration,
        Action acquire,
        Action release,
        IDisposable? ownedResource,
        TimeProvider? time,
        Func<bool>? isAcquired
    )
    {
        if (holdDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(holdDuration), "Hold duration must be greater than zero.");
        }

        _holdDuration = holdDuration;
        _acquire = acquire;
        _release = release;
        _ownedResource = ownedResource;
        _isAcquired = isAcquired;
        _time = time ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => OnTimerFired(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Create a KeepAwake that tells Windows the display is in use (the same
    /// signal video players and conferencing apps send), preventing display
    /// timeout, sleep and the idle-triggered lock screen while tools are active.
    /// Returns null if the power request could not be created.
    /// </summary>
    public static KeepAwake? CreateDisplayKeepAwake(TimeSpan holdDuration, string reason)
    {
        var request = PowerAvailabilityRequest.Create(reason);
        if (request == null)
        {
            return null;
        }

        return new KeepAwake(holdDuration, request.Set, request.Clear, request, null, () => request.IsFullySet);
    }

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _active && (_isAcquired?.Invoke() ?? true);
            }
        }
    }

    /// <summary>
    /// Signal activity: acquire the resource if not already held, and extend
    /// the hold so it is released only after the idle period elapses.
    /// </summary>
    public void Poke()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (!_active || _isAcquired is not null && !_isAcquired())
            {
                try
                {
                    _acquire();
                    _acquireFailureLogged = false;
                }
                catch (Exception ex)
                {
                    StopIfResourceDisposed();
                    if (_ownedResource is PowerAvailabilityRequest { HasPending: true })
                    {
                        _active = true;
                        _timer.Change(_holdDuration, Timeout.InfiniteTimeSpan);
                    }

                    if (!_acquireFailureLogged)
                    {
                        Console.Error.WriteLine($"Power request acquisition failed: {ex.Message}");
                        _acquireFailureLogged = true;
                    }

                    return;
                }

                _active = true;
            }

            _pokedAt = _time.GetTimestamp();
            _timer.Change(_holdDuration, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimerFired()
    {
        lock (_lock)
        {
            if (_disposed || !_active)
            {
                return;
            }

            // A Poke may have raced with this callback; only release once the
            // most recently extended deadline has actually passed.
            var remaining = _holdDuration - _time.GetElapsedTime(_pokedAt);
            if (remaining > TimeSpan.Zero)
            {
                _timer.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            try
            {
                _release();
                _active = false;
            }
            catch (Exception ex)
            {
                var stopped = StopIfResourceDisposed();
                Console.Error.WriteLine(
                    $"Power request release failed; {(stopped ? "handle closed" : "retrying")}: {ex.Message}"
                );
                if (!stopped)
                {
                    _timer.Change(_holdDuration, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private bool StopIfResourceDisposed()
    {
        if (_ownedResource is not PowerAvailabilityRequest { IsDisposed: true })
        {
            return false;
        }

        _active = false;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return true;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Dispose();
            if (_active)
            {
                try
                {
                    _release();
                }
                catch { }

                _active = false;
            }

            _ownedResource?.Dispose();
        }
    }
}

/// <summary>
/// Wraps a Windows power availability request (PowerCreateRequest) that, while
/// set, tells the OS the display is required. This suppresses display timeout,
/// automatic sleep and the inactivity lock screen - the same mechanism browsers
/// use during video playback. The request is visible in `powercfg /requests`
/// together with the reason string.
/// </summary>
public sealed class PowerAvailabilityRequest : IDisposable
{
    private readonly Func<bool> _setDisplay;
    private readonly Func<bool> _setSystem;
    private readonly Func<bool> _clearDisplay;
    private readonly Func<bool> _clearSystem;
    private readonly Func<bool> _close;
    private bool _displaySet;
    private bool _systemSet;
    private bool _disposed;

    internal bool IsFullySet => !_disposed && _displaySet && _systemSet;
    internal bool IsDisposed => _disposed;
    internal bool HasPending => !_disposed && (_displaySet || _systemSet);

    internal PowerAvailabilityRequest(
        Func<bool> setDisplay,
        Func<bool> setSystem,
        Func<bool> clearDisplay,
        Func<bool> clearSystem,
        Func<bool> close
    )
    {
        _setDisplay = setDisplay;
        _setSystem = setSystem;
        _clearDisplay = clearDisplay;
        _clearSystem = clearSystem;
        _close = close;
    }

    /// <summary>
    /// Create a power request with the given diagnostic reason, or null on failure.
    /// </summary>
    public static PowerAvailabilityRequest? Create(string reason)
    {
        var reasonPtr = Marshal.StringToHGlobalUni(reason);
        try
        {
            var context = new REASON_CONTEXT
            {
                Version = POWER_REQUEST_CONTEXT_VERSION,
                Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                SimpleReasonString = reasonPtr,
            };

            var handle = PowerCreateRequest(ref context);
            if (handle == 0 || handle == INVALID_HANDLE_VALUE)
            {
                return null;
            }

            return new PowerAvailabilityRequest(
                () => PowerSetRequest(handle, POWER_REQUEST_TYPE.PowerRequestDisplayRequired),
                () => PowerSetRequest(handle, POWER_REQUEST_TYPE.PowerRequestSystemRequired),
                () => PowerClearRequest(handle, POWER_REQUEST_TYPE.PowerRequestDisplayRequired),
                () => PowerClearRequest(handle, POWER_REQUEST_TYPE.PowerRequestSystemRequired),
                () => CloseHandle(handle)
            );
        }
        finally
        {
            Marshal.FreeHGlobal(reasonPtr);
        }
    }

    /// <summary>Activate the request: display must stay on, system must stay awake.</summary>
    public void Set()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_displaySet)
        {
            if (!_setDisplay())
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _displaySet = true;
        }

        if (!_systemSet)
        {
            if (!_setSystem())
            {
                var error = Marshal.GetLastPInvokeError();
                RollbackDisplayAfterSystemFailure();
                throw new Win32Exception(error);
            }

            _systemSet = true;
        }
    }

    private void RollbackDisplayAfterSystemFailure()
    {
        if (_clearDisplay())
        {
            _displaySet = false;
            return;
        }

        if (_close())
        {
            _disposed = true;
            _displaySet = false;
        }
    }

    /// <summary>Deactivate the request, restoring normal power/lock policy.</summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int? error = null;
        if (_displaySet)
        {
            if (_clearDisplay())
            {
                _displaySet = false;
            }
            else
            {
                error = Marshal.GetLastPInvokeError();
            }
        }

        if (_systemSet)
        {
            if (_clearSystem())
            {
                _systemSet = false;
            }
            else
            {
                error ??= Marshal.GetLastPInvokeError();
            }
        }

        if (error is not null)
        {
            throw new Win32Exception(error.Value);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Clear();
        }
        catch { }

        if (!_close())
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _disposed = true;
    }

    private const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    private const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;
    private static readonly nint INVALID_HANDLE_VALUE = -1;

    private enum POWER_REQUEST_TYPE
    {
        PowerRequestDisplayRequired = 0,
        PowerRequestSystemRequired = 1,
        PowerRequestAwayModeRequired = 2,
        PowerRequestExecutionRequired = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct REASON_CONTEXT
    {
        public uint Version;
        public uint Flags;
        public nint SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint PowerCreateRequest(ref REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerSetRequest(nint powerRequest, POWER_REQUEST_TYPE requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerClearRequest(nint powerRequest, POWER_REQUEST_TYPE requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}

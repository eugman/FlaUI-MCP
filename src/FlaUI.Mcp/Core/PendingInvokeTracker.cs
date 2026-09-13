namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Information about a UI Automation pattern call that has not returned yet.
/// While such a call is in flight, the target process's UIA provider typically
/// cannot answer any other request, so tools should fail fast instead of hanging.
/// </summary>
public sealed class PendingInvokeInfo
{
    public string OperationId { get; } = Guid.NewGuid().ToString("N");
    public string? ParentOperationId { get; } = OperationContext.Current.Value?.Id;
    public long ProcessStartedTicks { get; }
	internal PendingInvokeInfo(int processId, string description, long startedTicks)
	{
		ProcessStartedTicks = startedTicks;
		ProcessId = processId;
		Description = description;
		StartedUtc = DateTime.UtcNow;
	}

	/// <summary>Process id whose UIA provider is occupied by this call.</summary>
	public int ProcessId { get; }

	/// <summary>Human-readable description of the call, e.g. "Invoke on 'Open...'".</summary>
	public string Description { get; }

	/// <summary>When the call started.</summary>
	public DateTime StartedUtc { get; }

	/// <summary>Title of the modal window detected after the call started, if any.</summary>
	public string? ModalTitle { get; set; }
}

/// <summary>
/// Tracks UI Automation pattern calls that are still executing per process, so
/// other tools can detect a blocked provider and return a helpful error
/// immediately instead of waiting for the global tool timeout.
/// </summary>
public class PendingInvokeTracker
{
    private readonly Func<int, long?> _processStart;
    public PendingInvokeTracker() : this(ReadProcessStart) { }
    internal PendingInvokeTracker(Func<int, long?> processStart) => _processStart = processStart;

    // null means exited/missing; zero means identity could not be inspected.
    private static long? ReadProcessStart(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.HasExited ? null : process.StartTime.ToUniversalTime().Ticks;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return 0; }
    }
    public object Status() { lock (_lock) return _pending.Select(p => new { operationId = p.OperationId, parentOperationId = p.ParentOperationId, p.ProcessId, p.ProcessStartedTicks, p.Description, p.StartedUtc, p.ModalTitle, status = "pending" }).ToArray(); }
    public string[] ForOperation(string operationId)
    {
        lock (_lock) return _pending.Where(p => p.ParentOperationId == operationId).Select(p => p.OperationId).ToArray();
    }
	private readonly object _lock = new();
	private readonly List<PendingInvokeInfo> _pending = new();

	/// <summary>
	/// Record the start of a pattern call against a process.
	/// </summary>
	public PendingInvokeInfo Begin(int processId, string description)
	{
		var info = new PendingInvokeInfo(processId, description, _processStart(processId) ?? 0);
		lock (_lock)
		{
			_pending.Add(info);
		}
		return info;
	}

	/// <summary>
	/// Record that a pattern call has returned (successfully or not).
	/// </summary>
	public void Complete(PendingInvokeInfo info)
	{
		lock (_lock)
		{
			_pending.Remove(info);
		}
	}

	/// <summary>
	/// Check whether a process has a pattern call still in flight.
	/// A processId of 0 (unknown) never matches.
	/// </summary>
	public bool TryGetPending(int processId, out PendingInvokeInfo info)
	{
        // Process inspection must not hold the shared pending-list lock.
        var started = processId == 0 ? 0 : _processStart(processId);
		lock (_lock)
		{
            _pending.RemoveAll(p => p.ProcessId == processId && p.ProcessStartedTicks != 0 &&
                (started == null || started > 0 && started != p.ProcessStartedTicks));
			var match = processId != 0 ? _pending.FirstOrDefault(p => p.ProcessId == processId) : null;
			info = match!;
			return match != null;
		}
	}

	/// <summary>
	/// Build the standard guidance message for a blocked provider.
	/// </summary>
	public static string DescribeBlocked(PendingInvokeInfo info)
	{
		var modalPart = info.ModalTitle != null
			? $" — it opened a modal dialog \"{info.ModalTitle}\" that is waiting for input"
			: "";
		var elapsed = (int)(DateTime.UtcNow - info.StartedUtc).TotalSeconds;
        return $"UI Automation for this app is blocked by pending operation {info.OperationId}, '{info.Description}' call " +
			   $"started {elapsed}s ago{modalPart}. Ref-based tools on this app will fail until it completes. " +
			   "To interact with the dialog: find its window handle via windows_list_windows (it is a separate " +
			   "window of the same process), see it with windows_screenshot using that handle, use " +
			   "windows_send_keys with that explicit dialog handle (without ref) for keyboard input; then check operation status before continuing.";
	}
}

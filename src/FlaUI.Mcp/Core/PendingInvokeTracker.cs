namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Information about a UI Automation pattern call that has not returned yet.
/// While such a call is in flight, the target process's UIA provider typically
/// cannot answer any other request, so tools should fail fast instead of hanging.
/// </summary>
public sealed class PendingInvokeInfo
{
	internal PendingInvokeInfo(int processId, string description)
	{
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
	private readonly object _lock = new();
	private readonly List<PendingInvokeInfo> _pending = new();

	/// <summary>
	/// Record the start of a pattern call against a process.
	/// </summary>
	public PendingInvokeInfo Begin(int processId, string description)
	{
		var info = new PendingInvokeInfo(processId, description);
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
		lock (_lock)
		{
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
		return $"UI Automation for this app is blocked by a pending '{info.Description}' call " +
			   $"started {elapsed}s ago{modalPart}. UIA-based tools will hang until it completes. " +
			   "Interact with the dialog using windows_screenshot (fullScreen: true) to see it, " +
			   "windows_send_keys (without ref) for keyboard input, or dismiss it; then retry.";
	}
}

namespace FlaUI.Mcp.Core;

/// <summary>
/// Keeps the display and the system on while a batch is approved, so the screen does not go dark (and its wake-up
/// input does not end the batch) between the agent's calls. A power request, not SetThreadExecutionState: the batch
/// begins on whatever pool thread runs the call. Windows releases the request if the process dies.
/// </summary>
public sealed partial class InteractionGrant
{
    private const string DisplayReason = "FlaUI-MCP: the user cleared the machine for an interactive batch";
    private IDisposable? displayHold;
    private string? displayProblem;

    /// <summary>Why the display could not be held on (or released) for the batch; null when it was. A failure never
    /// blocks or ends the grant.</summary>
    public string? DisplayRequestProblem
    {
        get
        {
            lock (sync)
            {
                return displayProblem;
            }
        }
    }

    private static PowerAvailabilityRequest? DisplayRequest()
    {
        if (Environment.GetEnvironmentVariable(InputDesktop.OfflineVariable) == "1")
        {
            return null;
        }

        var request =
            PowerAvailabilityRequest.Create(DisplayReason)
            ?? throw new InvalidOperationException("PowerCreateRequest failed.");
        try
        {
            request.Set();
            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private void HoldDisplay()
    {
        ReleaseDisplay();
        try
        {
            displayHold = keepDisplayOn();
            displayProblem = null;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            displayProblem = "Could not keep the display on: " + error.Message;
        }
    }

    private void ReleaseDisplay()
    {
        var hold = displayHold;
        displayHold = null;
        try
        {
            hold?.Dispose();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            displayProblem = "Could not release the display request: " + error.Message;
        }
    }
}

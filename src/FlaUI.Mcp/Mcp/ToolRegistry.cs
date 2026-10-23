using System.Diagnostics;
using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp;

/// <summary>
/// Registry for MCP tools - maps tool names to handlers
/// </summary>
public partial class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly TimeSpan _toolTimeout;
    private readonly Action? _onToolActivity;
    public OperationCoordinator Operations { get; } = new();
    public Func<JsonElement?, ProcessIdentity?>? ResolveTarget { get; set; }
    public Func<string, JsonElement?, ProcessIdentity?>? ResolveTargetForTool { get; set; }
    public PendingInvokeTracker? Pending { get; set; }
    public PendingInvokeTracker? SecondaryPending { get; set; }

    /// <summary>Optional per-call permission check installed at each guarded mutation.</summary>
    public Func<string, McpToolAnnotations?, Action?>? MutationPermissionForTool { get; set; }

    /// <summary>Optional warning prepended to results for a nonpreferred app; it never blocks the call.</summary>
    public Func<string, JsonElement?, ProcessIdentity?, string?>? AdvisoryWarning { get; set; }

    /// <summary>
    /// Called when a top-level call failed after sending input (inputsSent above 0, or a refusal that says input went
    /// out), with the tool's name and outcome, so a host can pause its grant until the app has been observed.
    /// </summary>
    public Action<string, ToolOutcome>? FailedAfterInput { get; set; }

    /// <summary>A settled, successful zero-input call can inform a host-selected recovery observation.</summary>
    public Action<string, JsonElement?, ToolOutcome, ProcessIdentity?>? ObservedAfterSuccess { get; set; }

    /// <summary>A host can serialize Core calls with its own UI gate. The scope lives until the tool task settles.</summary>
    public Func<
        string,
        McpToolAnnotations?,
        CancellationToken,
        ValueTask<IAsyncDisposable?>
    >? ExecutionScopeForTool { get; set; }

    /// <summary>Target-aware companion to FailedAfterInput for product grant recovery.</summary>
    public Action<string, ToolOutcome, ProcessIdentity?>? FailedAfterInputWithTarget { get; set; }

    private readonly TimeProvider _time;

    /// <param name="time">The clock for tool timeouts; the system clock by default (tests inject one).</param>
    public ToolRegistry(TimeSpan? toolTimeout = null, Action? onToolActivity = null, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _toolTimeout = toolTimeout ?? TimeSpan.FromSeconds(30);
        if (_toolTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(toolTimeout), "Tool timeout must be greater than zero.");
        }

        _onToolActivity = onToolActivity;
    }

    /// <summary>
    /// A status tool: read-only with the status hint, or windows_operation_status by name. The server answers
    /// it at once, so status stays readable while other calls block.
    /// </summary>
    public bool IsStatusTool(string name) =>
        name == Tools.OperationStatusTool.ToolName
        || _tools.TryGetValue(name, out var tool)
            && tool.GetDefinition().Annotations is { ReadOnlyHint: true, StatusHint: true };

    /// <summary>The registered tools, for contract tests.</summary>
    internal IReadOnlyCollection<ITool> All => _tools.Values;

    public List<McpTool> GetToolDefinitions()
    {
        return _tools.Values.Select(t => t.GetDefinition()).ToList();
    }

    /// <param name="whenStranded">
    /// Called when the call returns before its tool task ended because the caller cancelled. It receives a
    /// task that completes when the tool returns or its timeout would have expired; a serialized caller
    /// holds its slot until then, so a cancel never lets the next tool run beside a still-running one.
    /// </param>
    public async Task<McpToolResult> ExecuteToolAsync(
        string name,
        JsonElement? arguments,
        CancellationToken cancellationToken = default,
        string? requestKey = null,
        Action<Task>? whenStranded = null
    )
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            return new McpToolResult
            {
                Content = new List<McpContent>
                {
                    new() { Type = "text", Text = $"Unknown tool: {name}" },
                },
                IsError = true,
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (UndeclaredArgument(name, arguments) is { } refused)
        {
            return refused;
        }

        try
        {
            _onToolActivity?.Invoke();
            if (OperationContext.Current.Value is { } parent)
            {
                return await RunNested(tool, arguments, parent);
            }

            var operation = Operations.Begin(name, requestKey, tool.GetDefinition().Annotations);
            using var cancellation = cancellationToken.Register(() => operation.Stop.Cancel());
            var toolTask = Task.Run(() => RunOperation(tool, arguments, operation));
            var started = _time.GetTimestamp();
            using var timer = new CancellationTokenSource();
            var timeoutTask = Task.Delay(_toolTimeout, _time, timer.Token);
            var cancelledTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var finished = await Task.WhenAny(toolTask, timeoutTask, cancelledTask);
            // Stop the timer once the tool returns, so a fast call doesn't leave it running for the full timeout.
            await timer.CancelAsync();
            if (finished != toolTask)
            {
                var settled =
                    finished == cancelledTask ? SettledOrTimedOut(toolTask, _time.GetElapsedTime(started)) : null;
                return Strand(name, operation, toolTask, settled, whenStranded);
            }

            return CompleteAndObserve(name, arguments, operation, await toolTask);
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new List<McpContent>
                {
                    new() { Type = "text", Text = $"Error: {ex.Message}" },
                },
                IsError = true,
            };
        }
    }

    /// <summary>A step of a composite tool shares its parent's operation but reports only its own input.</summary>
    private async Task<McpToolResult> RunNested(ITool tool, JsonElement? arguments, OperationContext parent)
    {
        var sentBefore = parent.InputsSent;
        var refusalBefore = parent.Refusal;
        var previousHook = MutationGuard.BeforeMutation.Value;
        McpToolResult result;
        string? warning = null;
        try
        {
            var hook = MutationPermissionForTool?.Invoke(tool.Name, tool.GetDefinition().Annotations);
            if (hook != null)
            {
                MutationGuard.BeforeMutation.Value = ComposeMutationPermission(previousHook, hook);
            }

            var target = ResolveTargetForTool is { } resolve
                ? resolve(tool.Name, arguments)
                : ResolveTarget?.Invoke(arguments);
            warning = AdvisoryWarning?.Invoke(tool.Name, arguments, target);
            OperationContext.Check();
            result = WithAdvisory(await tool.ExecuteAsync(arguments), warning);
        }
        catch (Exception ex)
        {
            result = WithAdvisory(ErrorText($"Error: {ex.Message}"), warning);
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = previousHook;
        }

        var refusal = ReferenceEquals(parent.Refusal, refusalBefore) ? null : parent.Refusal;
        return WithDispatch(result, parent.InputsSent - sentBefore, refusal);
    }

    private async Task<McpToolResult> RunOperation(ITool tool, JsonElement? arguments, OperationContext operation)
    {
        OperationContext.Current.Value = operation;
        var previousHook = MutationGuard.BeforeMutation.Value;
        IAsyncDisposable? executionScope = null;
        try
        {
            try
            {
                executionScope = await (
                    ExecutionScopeForTool?.Invoke(tool.Name, tool.GetDefinition().Annotations, operation.Stop.Token)
                    ?? ValueTask.FromResult<IAsyncDisposable?>(null)
                );
            }
            catch (Exception ex)
            {
                if (ex is AutomationRefusal refusal)
                {
                    operation.Refusal ??= new InputRefusal(refusal.Code, refusal.Message)
                    {
                        ActionDispatched = refusal.ActionDispatched,
                    };
                }

                return FinalizeOperation(
                    tool.Name,
                    operation,
                    WithDispatch(ErrorText($"Error: {ex.Message}"), operation.InputsSent, operation.Refusal)
                );
            }

            return await ExecuteOperationBody(tool, arguments, operation, previousHook);
        }
        finally
        {
            try
            {
                if (executionScope != null)
                {
                    await executionScope.DisposeAsync();
                }
            }
            finally
            {
                MutationGuard.BeforeMutation.Value = previousHook;
                OperationContext.Current.Value = null;
            }
        }
    }

    private async Task<McpToolResult> ExecuteOperationBody(
        ITool tool,
        JsonElement? arguments,
        OperationContext operation,
        Action? previousHook
    )
    {
        McpToolResult result;
        string? warning = null;
        try
        {
            // Metadata collection is inside the timeout. Target validation
            // belongs to the tool, preserving its specific error contract.
            var target = ResolveTargetForTool is { } resolve
                ? resolve(tool.Name, arguments)
                : ResolveTarget?.Invoke(arguments);
            var hook = MutationPermissionForTool?.Invoke(tool.Name, tool.GetDefinition().Annotations);
            if (hook != null)
            {
                MutationGuard.BeforeMutation.Value = ComposeMutationPermission(previousHook, hook);
            }

            warning = AdvisoryWarning?.Invoke(tool.Name, arguments, target);
            operation.AdvisoryWarning = warning;
            operation.ProcessId = target?.ProcessId ?? 0;
            operation.ProcessStartedTicks = target?.StartedTicks ?? 0;
            OperationContext.Check();
            var returned = await tool.ExecuteAsync(arguments);
            result = WithAdvisory(
                WithDispatch(returned, operation.InputsSent, operation.Refusal, ProviderPending(operation)),
                warning
            );
            result = result with { Outcome = result.Outcome! with { HitTest = operation.HitTest } };
        }
        catch (Exception ex)
        {
            // A tool that throws still reports how much of its input went out first.
            result = WithDispatch(
                WithAdvisory(ErrorText($"Error: {ex.Message}"), warning),
                operation.InputsSent,
                operation.Refusal,
                ProviderPending(operation)
            );
        }

        return FinalizeOperation(tool.Name, operation, result);
    }

    private McpToolResult CompleteAndObserve(
        string name,
        JsonElement? arguments,
        OperationContext operation,
        McpToolResult result
    )
    {
        var completed = CompleteResult(result, operation);
        var observed = completed.Outcome;
        if (IsSettledObservation(completed, operation))
        {
            try
            {
                ObservedAfterSuccess?.Invoke(name, arguments, observed, IdentityOf(operation));
            }
            catch
            {
                // A recovery observer must not change the already-settled tool result.
            }
        }

        return completed;
    }

    private bool IsSettledObservation(McpToolResult result, OperationContext operation)
    {
        if (result.IsError == true)
        {
            return false;
        }

        var outcome = result.Outcome;
        return outcome?.Status == "ok"
            && outcome.InputsSent == 0
            && outcome.Provider != "pending"
            && !ProviderPending(operation);
    }

    private McpToolResult FinalizeOperation(string name, OperationContext operation, McpToolResult result)
    {
        operation.ResultOutcome = result.Outcome;
        if (result.Outcome!.Status == "failed")
        {
            var error = string.Join("; ", result.Content.Select(c => c.Text));
            operation.Error = string.IsNullOrWhiteSpace(error) ? "Tool reported a failure." : error;
        }

        operation.Finished = true;
        NotifyFailedAfterInput(name, operation, result.Outcome);
        return result;
    }

    private void NotifyFailedAfterInput(string name, OperationContext operation, ToolOutcome? outcome)
    {
        if (outcome is not { Status: "failed" } failed)
        {
            return;
        }

        if (failed.InputsSent == 0 && failed.Failure?.ActionDispatched != true)
        {
            return;
        }

        if (!operation.TryNotifyFailure())
        {
            return;
        }

        FailedAfterInput?.Invoke(name, failed);
        FailedAfterInputWithTarget?.Invoke(name, failed, IdentityOf(operation));
    }

    private static ProcessIdentity? IdentityOf(OperationContext operation) =>
        operation.ProcessId == 0 ? null : new ProcessIdentity(operation.ProcessId, operation.ProcessStartedTicks);

    private static Action ComposeMutationPermission(Action? ambient, Action tool)
    {
        if (ambient == null || ambient == tool)
        {
            return tool;
        }

        return () =>
        {
            ambient();
            tool();
        };
    }

    private static McpToolResult WithAdvisory(McpToolResult result, string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return result;
        }

        var content = new List<McpContent>
        {
            new() { Type = "text", Text = $"Warning: {warning}" },
        };
        content.AddRange(result.Content);
        return result with { Content = content };
    }

    /// <summary>
    /// Every result, errors included, says whether the tool failed (status) and how much of its input
    /// went out (inputsSent, and dispatch when the tool did not report its own). A failed call carries the
    /// refusal guarded input raised during it, so clients read its fields instead of parsing the text.
    /// </summary>
    /// <summary>Whether a pattern call this operation started still holds the app's provider.</summary>
    private bool ProviderPending(OperationContext operation) =>
        Pending?.ForOperation(operation.Id).Length > 0 || SecondaryPending?.ForOperation(operation.Id).Length > 0;

    internal static McpToolResult WithDispatch(
        McpToolResult result,
        int inputsSent,
        InputRefusal? refusal = null,
        bool providerPending = false
    )
    {
        var failed = result.IsError == true || result.Outcome?.Status == "failed";
        // A composite call's steps carry their own refusals. The operation keeps only the latest, which
        // may belong to an earlier step that the batch went past, so report the last failed step's.
        if (result.Outcome?.Steps is { } steps)
        {
            refusal = steps.LastOrDefault(step => step.IsError)?.Outcome?.Failure;
        }

        var outcome =
            result.Outcome
            ?? new ToolOutcome(ToolOutcome.DispatchFor(failed, inputsSent), providerPending ? "pending" : "not-tracked")
            {
                Verification = "not-established",
            };
        return result with
        {
            Outcome = outcome with
            {
                Status = StatusOf(failed, outcome, inputsSent),
                // Unverified because nothing checked the input's effect: say so, as the registry's own outcome does.
                Verification =
                    outcome.Verification
                    ?? (!failed && outcome.Proof == null && inputsSent > 0 ? "not-established" : null),
                InputsSent = inputsSent,
                Failure =
                    // A refusal recorded as coming after input (a counted Select, say) keeps that, whatever this call counted.
                    failed && refusal != null
                        ? refusal with
                        {
                            ActionDispatched = refusal.ActionDispatched == true || inputsSent > 0,
                        }
                        : outcome.Failure,
            },
        };
    }

    // An error is always failed; otherwise a proof decides between ok, failed and unverified. Without one, a call
    // that sent input is unverified (nothing checked its effect: verification not-established), and one that sent
    // none, a read or a mutating tool with nothing to do, is ok.
    private static string StatusOf(bool failed, ToolOutcome outcome, int inputsSent)
    {
        if (failed)
        {
            return "failed";
        }

        if (outcome.Proof is { } proof)
        {
            return Proof.StatusFor(proof.Verdict);
        }

        // A composite (a batch) that rolled an unverified step up keeps it.
        return outcome.Status == "unverified" || inputsSent > 0 ? "unverified" : "ok";
    }

    private static McpToolResult ErrorText(string message) =>
        new()
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = message },
            },
            IsError = true,
        };

    private static McpToolResult CompleteResult(McpToolResult result, OperationContext operation) =>
        result with
        {
            Outcome = result.Outcome! with { OperationId = result.Outcome.OperationId ?? operation.Id },
        };
}

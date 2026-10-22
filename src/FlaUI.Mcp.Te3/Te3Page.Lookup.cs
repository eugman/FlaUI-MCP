using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    private sealed record PopupObservation(long Hwnd, string Title, System.Drawing.Rectangle? Bounds, QueryResult Tree);

    private async Task<string> Reference(Target target) => host.Elements.Register(handle, await Resolve(target));

    public async Task Expect(Target target, string? text = null) => _ = await Resolve(target, text);

    private async Task Absent(Target target)
    {
        reportStep?.Invoke($"Wait for absence: {target.Selector}");
        await Absence.WaitFor(() =>
            Task.Run(() =>
                    query.IsPresent(
                        handle,
                        target.Selector,
                        target.Within,
                        target.IncludeOwned,
                        new SearchBudget(3000, TimeSpan.FromSeconds(2))
                    )
                )
                .WaitAsync(TimeSpan.FromSeconds(5))
        );
    }

    public static Task WaitForAbsence(Func<bool> present, int timeoutMs = 15000, int settleMs = 500) =>
        Absence.WaitFor(() => Task.FromResult(present()), timeoutMs, settleMs);

    private async Task Count(Target target, int expected)
    {
        reportStep?.Invoke($"Wait for {expected} rows: {target.Selector}");
        var counted = await Poll.Until(
            async () =>
            {
                var result = await Task.Run(() =>
                        query.Find(
                            handle,
                            target.Selector,
                            target.Within,
                            maxResults: expected + 1,
                            includeOwned: target.IncludeOwned,
                            budget: new SearchBudget(3000, TimeSpan.FromSeconds(2))
                        )
                    )
                    .WaitAsync(TimeSpan.FromSeconds(5));
                return !result.Truncated && result.Unreadable == 0 && result.Elements.Count == expected;
            },
            TimeSpan.FromSeconds(15)
        );
        if (!counted)
        {
            throw new TimeoutException($"Expected exactly {expected} rows");
        }
    }
}

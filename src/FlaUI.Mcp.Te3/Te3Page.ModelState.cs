using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    /// <summary>
    /// Read the status bar. Observation only: no clicks and no Invoke. The analyzer panel is a
    /// Button and invoking it would open the Best Practice Analyzer, so it is read and never
    /// activated.
    /// </summary>
    /// <returns>The interpreted model state.</returns>
    public Te3ModelState ObserveModelState()
    {
        const int panelLimit = 40;
        var found = query.Find(
            handle,
            new(),
            StatusBarTarget().Selector,
            maxDepth: 1,
            maxResults: panelLimit,
            budget: new SearchBudget(600, TimeSpan.FromSeconds(5))
        );
        if (ModelStateReadFailure(found.Elements.Count, panelLimit, found.Unreadable) is { } failure)
        {
            throw new InvalidOperationException(failure);
        }

        var panels = found.Elements.Where(e => e.Depth == 1).ToArray();
        if (panels.Length == 0)
        {
            throw new InvalidOperationException("The status bar reported no panels.");
        }

        return Te3StatusBar.Map(panels);
    }

    /// <summary>
    /// Whether a status-bar read is too partial to map positionally.
    /// <para>
    /// Deliberately ignores <c>Truncated</c>. BoundedSearch raises that flag when a node *at* the
    /// depth limit still has children, so with maxDepth 1 it means "there is content inside a
    /// panel" — which this read does not want and does not use. Treating it as a failure made the
    /// tool throw on any bar whose panels contain a child node, which for a WinForms status strip
    /// is the normal case.
    /// </para>
    /// What would actually corrupt a positional mapping is panels going missing, which shows up as
    /// hitting the result cap, or panels failing to read at all.
    /// </summary>
    /// <param name="count">Elements returned.</param>
    /// <param name="limit">The result cap the search was given.</param>
    /// <param name="unreadable">Nodes that threw while being read.</param>
    /// <returns>The refusal, or null when the read can be mapped.</returns>
    internal static string? ModelStateReadFailure(int count, int limit, int unreadable)
    {
        if (unreadable > 0)
        {
            return $"{unreadable} status-bar node(s) could not be read; its panels are identified by "
                + "position, so a partial read cannot be mapped.";
        }

        if (count >= limit)
        {
            return $"The status bar returned at least {limit} elements, so panels may have been "
                + "dropped at the cap; its panels are identified by position, so this cannot be mapped.";
        }

        return null;
    }
}

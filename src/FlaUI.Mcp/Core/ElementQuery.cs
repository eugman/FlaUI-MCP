using FlaUI.Core.AutomationElements;
using System.Diagnostics;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;

namespace PlaywrightWindows.Mcp.Core;

public sealed record ElementSelector(string? AutomationId = null, string? Name = null,
    string? ControlType = null, string? ClassName = null, bool? Visible = null, string? Value = null, string? Pattern = null,
    bool RootOnly = false);
public sealed record ElementInfo(string Ref, string Name, string AutomationId, string ControlType,
    string ClassName, bool Enabled, bool Offscreen, string? Value, string[] Patterns, int Depth,
    bool? Selected = null, bool ValueTruncated = false);
public sealed record QueryResult(List<ElementInfo> Elements, int Visited, bool Truncated, int Unreadable);

/// <summary>Bounded raw-tree discovery. Selectors use exact, case-sensitive UIA values.</summary>
public sealed class ElementQuery(SessionManager sessions, ElementRegistry refs, PendingInvokeTracker pending)
{
    public static void ValidateSelector(ElementSelector selector)
    {
        if (selector.ControlType != null && (!Enum.TryParse<ControlType>(selector.ControlType, out var type) || !Enum.IsDefined(type)))
            throw new ArgumentException($"Unknown controlType: {selector.ControlType}");
    }

    private List<AutomationElement> SearchRoots(string handle, ElementSelector selector, ElementSelector? within,
        bool includeOwned, SearchBudget budget)
    {
        ValidateSelector(selector);
        if (within != null) ValidateSelector(within);
        var pid = sessions.GetWindowProcessId(handle);
        if (pending.TryGetPending(pid, out var call)) throw new InvalidOperationException(PendingInvokeTracker.DescribeBlocked(call));
        var root = sessions.GetWindow(handle) ?? throw new ArgumentException("Unknown window handle");
        var hwnd = sessions.GetWindowHwnd(handle);
        if (hwnd != 0) refs.SetWindowIdentity(handle, pid, hwnd);
        else refs.SetWindowProcessId(handle, pid);
        var scope = within == null ? root : Resolve(handle, within, includeOwned: includeOwned, budget: budget);
        var roots = new List<AutomationElement> { scope };
        if (includeOwned && within == null)
            foreach (var window in Win32Desktop.GetTopLevelWindows(pid).Where(w => w.Hwnd != hwnd))
                roots.Add(sessions.Automation.FromHandle(window.Hwnd));
        return roots;
    }

    public object Ancestors(string reference)
    {
        var result = new List<object>();
        for (var node = refs.GetElement(reference); node != null && result.Count < 16; node = node.Parent)
            result.Add(new { name = node.Properties.Name.ValueOrDefault, id = node.Properties.AutomationId.ValueOrDefault,
                type = node.Properties.ControlType.ValueOrDefault.ToString(), hwnd = (long)node.Properties.NativeWindowHandle.ValueOrDefault,
                pid = node.Properties.ProcessId.ValueOrDefault });
        return result;
    }
    public QueryResult Find(string handle, ElementSelector selector, ElementSelector? within = null,
        int maxDepth = 24, int maxNodes = 3000, int maxResults = 30, bool includeOwned = false, SearchBudget? budget = null)
    {
        if (maxDepth is < 0 or > 64 || maxNodes is < 1 or > 20000 || maxResults is < 1 or > 1000)
            throw new ArgumentException("Limits: depth 0..64, nodes 1..20000, results 1..1000");
        budget ??= new SearchBudget(maxNodes, TimeSpan.FromSeconds(10));
        var roots = SearchRoots(handle, selector, within, includeOwned, budget);
        var walker = sessions.Automation.TreeWalkerFactory.GetRawViewWalker();
        IEnumerable<AutomationElement> Children(AutomationElement node)
        {
            if (selector.RootOnly) yield break;
            for (var child = walker.GetFirstChild(node); child != null; child = walker.GetNextSibling(child)) yield return child;
        }
        var cache = new FlaUI.Core.CacheRequest { TreeScope = FlaUI.Core.Definitions.TreeScope.Element };
        var properties = sessions.Automation.PropertyLibrary;
        cache.Add(properties.Element.AutomationId); cache.Add(properties.Element.Name);
        cache.Add(properties.Element.ControlType); cache.Add(properties.Element.ClassName);
        cache.Add(properties.Element.IsOffscreen); cache.Add(properties.Element.IsPassword);
        cache.Add(properties.PatternAvailability.IsValuePatternAvailable); cache.Add(properties.PatternAvailability.IsSelectionItemPatternAvailable);
        cache.Add(sessions.Automation.PatternLibrary.ValuePattern);
        cache.Add(sessions.Automation.PatternLibrary.SelectionItemPattern);
        cache.Add(properties.Value.Value);
        bool CachedMatch(AutomationElement node)
        {
            // Cache only this node: never request a whole subtree or reuse cached
            // values for subsequent mutations. One provider read serves selectors.
            using (cache.Activate()) return Matches(node.FrameworkAutomationElement.GetUpdatedCache() ?? throw new InvalidOperationException("Cache unavailable"), selector);
        }
        // Runtime IDs identify UIA nodes. Native HWNDs identify host windows and
        // can be shared by distinct logical controls; never deduplicate by HWND.
        var found = BoundedSearch.Find(roots, Children, CachedMatch, n =>
            RuntimeIdentity(n.Properties.RuntimeId.ValueOrDefault),
            budget, maxDepth, maxResults);
        var result = new List<ElementInfo>();
        var unreadable = found.Unreadable;
        foreach (var (node, depth) in found.Matches)
        {
            OperationContext.Check();
            if (budget.Expired) break;
            try { result.Add(Describe(refs.Register(handle, node), node, depth)); }
            catch { unreadable++; }
        }
        return new(result, budget.Visited, found.Truncated || budget.Expired, unreadable);
    }

    public static string? RuntimeIdentity(int[]? runtimeId)
        => runtimeId is { Length: > 0 } ? string.Join(",", runtimeId) : null;

    public AutomationElement Resolve(string handle, ElementSelector selector, ElementSelector? within = null, bool includeOwned = false, SearchBudget? budget = null)
    {
        if (selector.AutomationId != null || selector.Name != null)
        {
            var matches = FindExact(handle, selector, within, includeOwned, budget, 2);
            if (matches.Count > 1) throw new System.Reflection.AmbiguousMatchException("Multiple controls matched; refine selector/scope.");
            if (matches.Count != 1) throw new InvalidOperationException($"Expected one element; found {matches.Count}. Refine selector/scope.");
            return matches[0];
        }
        var found = Find(handle, selector, within, maxResults: 2, includeOwned: includeOwned, budget: budget);
        if (found.Elements.Count > 1) throw new System.Reflection.AmbiguousMatchException("Multiple controls matched; refine selector/scope.");
        if (found.Truncated || found.Unreadable > 0)
            throw new InvalidOperationException("Incomplete UIA search; narrow the scope or inspect diagnostics.");
        if (found.Elements.Count != 1)
            throw new InvalidOperationException($"Expected one element; found {found.Elements.Count}. Refine selector/scope.");
        return refs.GetElement(found.Elements[0].Ref)!;
    }

    // Absence covers provider-realized nodes only, not unrealized virtual rows.
    // Provider failures and exhausted budgets propagate; neither means absent.
    public bool IsPresent(string handle, ElementSelector selector, ElementSelector? within = null,
        bool includeOwned = false, SearchBudget? budget = null)
    {
        if (selector.AutomationId != null || selector.Name != null)
            return FindExact(handle, selector, within, includeOwned, budget, 1).Count > 0;
        var found = Find(handle, selector, within, maxResults: 100, includeOwned: includeOwned, budget: budget);
        if (found.Unreadable > 0 || found.Truncated)
            throw new InvalidOperationException("Incomplete assertion search");
        return found.Elements.Count > 0;
    }

    private List<AutomationElement> FindExact(string handle, ElementSelector selector, ElementSelector? within,
        bool includeOwned, SearchBudget? budget, int stopAfter)
    {
        // Provider-side exact filtering avoids walking large unrelated subtrees.
        // Calls are not interruptible; mutation guards check cancellation afterwards.
            budget ??= new SearchBudget(3000, TimeSpan.FromSeconds(10));
            var roots = SearchRoots(handle, selector, within, includeOwned, budget);
            var factory = sessions.Automation.ConditionFactory;
            ConditionBase condition = selector.AutomationId != null ? factory.ByAutomationId(selector.AutomationId) : factory.ByName(selector.Name!);
            if (selector.AutomationId != null && selector.Name != null) condition = condition.And(factory.ByName(selector.Name));
            if (selector.ControlType != null) condition = condition.And(factory.ByControlType(Enum.Parse<ControlType>(selector.ControlType)));
            if (selector.ClassName != null) condition = condition.And(factory.ByClassName(selector.ClassName));
            var matches = new List<AutomationElement>();
            var identities = new HashSet<string>();
            foreach (var candidateRoot in roots)
            {
                if (!budget.Available) throw new InvalidOperationException("Exact lookup budget exhausted");
                // Root-only explicitly observes selected/owned windows (or the
                // resolved Within root), not unrelated document descendants.
                var candidates = selector.RootOnly ? new[] { candidateRoot } : candidateRoot.FindAll(TreeScope.Subtree, condition);
                if (budget.Expired) throw new InvalidOperationException("Exact lookup deadline exceeded");
                foreach (var candidate in candidates)
                {
                    if (!budget.Available) throw new InvalidOperationException("Exact lookup budget exhausted");
                    budget.Visit();
                    if (!Matches(candidate, selector)) continue;
                    var identity = RuntimeIdentity(candidate.Properties.RuntimeId.ValueOrDefault);
                    if (identity != null && !identities.Add(identity)) continue;
                    matches.Add(candidate);
                    if (budget.Expired) throw new InvalidOperationException("Exact lookup deadline exceeded");
                    // One proves presence; two prove ambiguity. Zero requires
                    // exhausting every root successfully before returning.
                    if (matches.Count >= stopAfter) return matches;
                }
            }
            if (budget.Expired) throw new InvalidOperationException("Exact lookup deadline exceeded");
            return matches;
    }

    private static bool Matches(AutomationElement e, ElementSelector s) =>
        (s.Pattern == null || (s.Pattern == "Value" && e.Patterns.Value.IsSupported) || (s.Pattern == "SelectionItem" && e.Patterns.SelectionItem.IsSupported)) &&
        (s.AutomationId == null || e.Properties.AutomationId.ValueOrDefault == s.AutomationId) &&
        (s.Name == null || e.Properties.Name.ValueOrDefault == s.Name) &&
        (s.ControlType == null || e.Properties.ControlType.ValueOrDefault.ToString() == s.ControlType) &&
        (s.ClassName == null || e.Properties.ClassName.ValueOrDefault == s.ClassName) &&
        (s.Visible == null || !e.Properties.IsOffscreen.ValueOrDefault == s.Visible) &&
        (s.Value == null || (!e.Properties.IsPassword.ValueOrDefault && e.Patterns.Value.IsSupported && e.Patterns.Value.Pattern.Value.ValueOrDefault == s.Value));

    private static ElementInfo Describe(string reference, AutomationElement e, int depth)
    {
        var patterns = new List<string>();
        if (e.Patterns.Invoke.IsSupported) patterns.Add("Invoke");
        if (e.Patterns.Value.IsSupported) patterns.Add("Value");
        if (e.Patterns.Text.IsSupported) patterns.Add("Text");
        if (e.Patterns.SelectionItem.IsSupported) patterns.Add("SelectionItem");
        if (e.Patterns.ExpandCollapse.IsSupported) patterns.Add("ExpandCollapse");
        if (e.Patterns.Toggle.IsSupported) patterns.Add("Toggle");
        string? value = null;
        if (!e.Properties.IsPassword.ValueOrDefault && e.Patterns.Value.IsSupported)
            value = e.Patterns.Value.Pattern.Value.ValueOrDefault;
        var valueTruncated = value?.Length > 512;
        if (valueTruncated) value = value![..512] + "…";
        bool? selected = null;
        if (e.Patterns.SelectionItem.IsSupported && e.Patterns.SelectionItem.Pattern.IsSelected.TryGetValue(out var isSelected))
            selected = isSelected;
        return new(reference, e.Properties.Name.ValueOrDefault ?? "", e.Properties.AutomationId.ValueOrDefault ?? "",
            e.Properties.ControlType.ValueOrDefault.ToString(), e.Properties.ClassName.ValueOrDefault ?? "",
            e.Properties.IsEnabled.ValueOrDefault, e.Properties.IsOffscreen.ValueOrDefault, value, patterns.ToArray(), depth, selected, valueTruncated);
    }
}

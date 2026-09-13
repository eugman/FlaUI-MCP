using System.Diagnostics;

namespace PlaywrightWindows.Mcp.Core;

public sealed class SearchBudget(int maxNodes, TimeSpan duration, Func<TimeSpan>? elapsed = null)
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public int Visited { get; private set; }
    public int Remaining => Math.Max(0, maxNodes - Visited);
    public bool Expired { get { OperationContext.Check(); return (elapsed?.Invoke() ?? watch.Elapsed) >= duration; } }
    public bool Available => Remaining > 0 && !Expired;
    public void Visit() => Visited++;
}

public sealed record SearchResult<T>(List<(T Node, int Depth)> Matches, bool Truncated, int Unreadable);

/// <summary>Provider-neutral traversal. Limits apply between calls, never interrupt a COM call.</summary>
public static class BoundedSearch
{
    public static SearchResult<T> Find<T>(IEnumerable<T> roots, Func<T, IEnumerable<T>> children,
        Func<T, bool> matches, Func<T, string?> identity, SearchBudget budget, int depthLimit, int resultLimit)
    {
        var queue = new Queue<(T Node, int Depth)>();
        var result = new List<(T Node, int Depth)>();
        var seen = new HashSet<string>();
        var truncated = false;
        var unreadable = 0;
        foreach (var root in roots)
        {
            if (!budget.Available || queue.Count >= budget.Remaining) { truncated = true; break; }
            queue.Enqueue((root, 0));
        }
        while (queue.Count > 0)
        {
            if (!budget.Available) { truncated = true; break; }
            var item = queue.Dequeue();
            budget.Visit();
            try
            {
                var key = identity(item.Node);
                if (key != null && !seen.Add(key)) continue;
                if (matches(item.Node))
                {
                    result.Add(item);
                    if (result.Count >= resultLimit) { truncated = true; break; }
                }
                // Do not enter an uninterruptible provider call outside the requested depth.
                if (item.Depth >= depthLimit) { truncated = true; continue; }
                using var iterator = children(item.Node).GetEnumerator();
                while (true)
                {
                    if (!budget.Available) { truncated = true; break; }
                    if (!iterator.MoveNext()) break;
                    if (item.Depth >= depthLimit || queue.Count >= budget.Remaining) { truncated = true; break; }
                    queue.Enqueue((iterator.Current, item.Depth + 1));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { unreadable++; }
        }
        return new(result, truncated, unreadable);
    }
}

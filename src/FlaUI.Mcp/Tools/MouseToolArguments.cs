namespace FlaUI.Mcp.Tools;

/// <summary>windows_mouse's schema, generated from its argument record.</summary>
public sealed partial class MouseTool
{
    public override object InputSchema => ToolSchema.For<MouseToolArguments>();
}

/// <summary>windows_mouse's arguments; its schema is generated from them (kept apart to hold the tool's coupling down).</summary>
internal sealed record MouseToolArguments
{
    [ToolEnum("park", "move", "hover", "scroll", "drag", "click")]
    [ToolDoc("park, move, hover, scroll, drag or click; see the tool description for what each needs.")]
    public required string Action { get; init; }

    [ToolDoc("Window handle; points are relative to its capture.")]
    public string? Handle { get; init; }

    [ToolDoc("Element to move to, hover, scroll over, or drag from.")]
    public string? Ref { get; init; }

    [ToolDoc("Drag start in window pixels (alternative to ref).")]
    public MousePoint? From { get; init; }

    [ToolDoc("Target in window pixels: move/hover/scroll/click point, or drag end.")]
    public MousePoint? To { get; init; }

    [ToolDoc("scroll: wheel ticks, positive is up.")]
    public int? Ticks { get; init; }

    [ToolDoc("hover: how long to rest (default 600).")]
    public int? DurationMs { get; init; }

    [ToolDoc("drag: intermediate moves (default 12).")]
    public int? Steps { get; init; }

    [ToolEnum("center", "left", "right", "top", "bottom")]
    [ToolDoc("drag: grab this edge of ref instead of its centre (e.g. right for a column header's divider).")]
    public string? FromEdge { get; init; }

    [ToolDoc("drag: release on this element instead of a `to` point.")]
    public string? ToRef { get; init; }

    [ToolDoc(
        "drag: refs the start point must not land on (a neighbouring pane's caption, say); the drag is refused before the button goes down when UI Automation reports one of them, or something inside it, there."
    )]
    public string[]? FromMustNotHit { get; init; }

    [ToolEnum("center", "left", "right", "top", "bottom")]
    [ToolDoc("drag: which part of toRef to release on (default center; left drops a column header before it).")]
    public string? ToEdge { get; init; }

    [ToolDoc(
        "drag with ref + fromEdge: release where ref becomes this many pixels wide (left/right) or tall (top/bottom). One call, no screenshot needed."
    )]
    public int? ResizeTo { get; init; }
}

/// <summary>A point in window pixels.</summary>
internal sealed record MousePoint
{
    public required int X { get; init; }

    public required int Y { get; init; }
}

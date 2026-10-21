using FlaUI.Mcp;

/// <summary>What te3_layout and te3_layout_read accept and how they describe themselves.</summary>
public sealed partial class Te3Layout
{
    private static readonly string[] ReadActions = ["list", "plan-preset", "show-pane"];

    private static readonly string[] ChangeActions =
    [
        "close",
        "keep",
        "reopen-pane",
        "hide-pane",
        "apply-preset",
        "maximize-pane",
        "restore-pane",
        "resize",
        "close-document",
        "snapshot",
        "restore",
    ];

    private string[] Actions => readOnly ? ReadActions : ChangeActions;

    // Before the grant or TE3 is touched: an action the other tool owns is refused, so a read never needs the
    // grant and a change is never served by the read-only tool.
    private void RequireOwnAction(string? action)
    {
        if (Actions.Contains(action))
        {
            return;
        }

        if (readOnly && ChangeActions.Contains(action))
        {
            throw new ArgumentException($"{ReadToolName} only reads; {action} is a {ToolName} action.");
        }

        if (!readOnly && ReadActions.Contains(action))
        {
            throw new ArgumentException($"{action} sends no input; call {ReadToolName}.");
        }

        throw new ArgumentException("Unsupported TE3 layout action");
    }

    public override string Description =>
        readOnly
            ? "Read TE3's layout for a screenshot; sends no input and needs no grant. list: docked panes and document "
                + "tabs with window-relative bounds. show-pane: confirm the AI Assistant, TOM Explorer or Properties "
                + "pane is visible; sends no input, so a hidden pane is reopened with te3_layout reopen-pane. plan-preset: offline "
                + "assistant-model-review geometry (windowWidth/windowHeight default to 1920x1080). Pane titles match "
                + "with or without TE3's \"(Changed)\" suffix. Changes are te3_layout."
            : "Arrange TE3 for a screenshot. close: hide named docked panes by title; document tabs are never closed. "
                + "keep: hide every docked pane except these titles (closing a tab-group pane surfaces the next tab, "
                + "so this repeats until only the kept ones remain); a title no visible pane or tab has is refused before "
                + "input. hide-pane: named AI Assistant, TOM Explorer, or "
                + "Properties dock surfaces. reopen-pane: reopen a hidden pane from the View menu (View, then the popup "
                + "entry named after the pane); a visible pane is left alone and a pane without a mapped View entry is "
                + "refused before input. apply-preset: live guarded resizing. maximize-pane / restore-pane: the "
                + "pane's own Maximize or Restore button, read back as the other button showing (TE3 remembers a maximized pane across restarts, so restore "
                + "it when done). Pane titles match with or without TE3's \"(Changed)\" suffix. resize: drag the "
                + "splitter a pane shares with its neighbour until the pane is `width` or `height` physical pixels; a "
                + "pane more than 4 px off it fails the call with the measured step. "
                + "close-document: press a document tab's own Close (DAX Query, DAX Package Manager), refused before "
                + "input for a pane or an unknown title, and read back as gone; a dialog it opens is reported. "
                + "snapshot: copy Layouts.json, UiPreferences.json and Preferences.json from %LocalAppData%/TabularEditor3, which TE3 rewrites on "
                + "close, into `path` (a new folder under the host's snapshot folder) with their hashes; restore: copy a snapshot back and read it back, "
                + "refused while TE3 runs unless `whileRunning` (the user asked) and when the snapshot changed. "
                + "Every other action takes desktop focus and needs an active te3_interaction_begin grant; snapshot and "
                + "restore touch only files and need none. No model edits. "
                + "Reads (list, show-pane, plan-preset) are te3_layout_read.";

    public override object InputSchema =>
        new
        {
            type = "object",
            properties = new
            {
                processId = new { type = "integer", minimum = 1 },
                action = new { type = "string", @enum = Actions },
                titles = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "close/keep: docked pane titles, exactly as list shows them; document tabs are preserved.",
                },
                title = new
                {
                    type = "string",
                    description = "show-pane/reopen-pane/hide-pane/maximize-pane/restore-pane/resize: the pane title; close-document: the document tab's title.",
                },
                preset = new { type = "string", @enum = new[] { FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview } },
                windowWidth = new
                {
                    type = "integer",
                    minimum = 1,
                    description = "plan-preset: physical window width; defaults to 1920.",
                },
                windowHeight = new
                {
                    type = "integer",
                    minimum = 1,
                    description = "plan-preset: physical window height; defaults to 1080.",
                },
                width = new { type = "integer", description = "resize: target width in physical pixels." },
                height = new { type = "integer", description = "resize: target height in physical pixels." },
                path = new
                {
                    type = "string",
                    description = "snapshot: the name of a new folder under %LOCALAPPDATA%/FlaUI.Mcp.Te3/layout-snapshots; restore: the name of a snapshot there. A full path, or one that leaves that folder, is refused.",
                },
                whileRunning = new
                {
                    type = "boolean",
                    description = "restore: only when the user asked to restore while TE3 runs (its close overwrites it).",
                },
            },
            required = new[] { "action" },
            additionalProperties = false,
        };
}

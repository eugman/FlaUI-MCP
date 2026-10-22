/// <summary>
/// Every tool the TE3 server adds beside Core's windows_* tools, listed in one place (each name is defined once, on
/// its tool): the registry parity test and the generated table in automation/README.md read them from here.
/// </summary>
public static class Te3ToolNames
{
    public const string InteractionBegin = Te3InteractionTool.Begin;
    public const string InteractionEnd = Te3InteractionTool.End;
    public const string Navigate = Te3Companion.Navigate;
    public const string ModelState = Te3Companion.ModelState;
    public const string Layout = Te3Layout.ToolName;
    public const string LayoutRead = Te3Layout.ReadToolName;
    public const string NativePreflight = FlaUI.Mcp.Te3.Te3NativePreflightTool.ToolName;
    public const string EnvCheck = Te3EnvCheck.ToolName;

    /// <summary>Every TE3 tool the server registers.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        InteractionBegin,
        InteractionEnd,
        Navigate,
        ModelState,
        Layout,
        LayoutRead,
        NativePreflight,
        EnvCheck,
    ];
}

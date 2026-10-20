namespace FlaUI.Mcp.Core;

/// <summary>A process token's mandatory integrity level (a RID such as 0x2000 for medium) and UIAccess flag.</summary>
public sealed record ProcessIntegrity(int Level, bool UiAccess)
{
    public const int Medium = 0x2000;
    public const int High = 0x3000;

    public static ProcessIntegrity? Read(int processId) => null;

    internal static InputRefusal? DescribeBlocked(
        ProcessIntegrity self,
        ProcessIntegrity? target,
        int targetPid,
        string? targetName
    ) => null;
}

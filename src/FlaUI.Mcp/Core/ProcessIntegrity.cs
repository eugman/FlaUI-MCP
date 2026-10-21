using System.Runtime.InteropServices;

namespace FlaUI.Mcp.Core;

/// <summary>A process token's mandatory integrity level (a RID such as 0x2000 for medium) and UIAccess flag.</summary>
public sealed record ProcessIntegrity(int Level, bool UiAccess)
{
    public const int Low = 0x1000;
    public const int Medium = 0x2000;
    public const int High = 0x3000;
    public const int SystemLevel = 0x4000;

    private const uint QueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int TokenUiAccess = 26;
    private const int LabelBufferSize = 64;

    public string LevelName =>
        Level switch
        {
            >= SystemLevel => "system",
            >= High => "high",
            >= Medium => "medium",
            >= Low => "low",
            _ => "untrusted",
        };

    /// <summary>
    /// UIPI lets a process drive only windows at or below its own integrity, and a UIAccess process sits
    /// above a plain one at the same level.
    /// </summary>
    public bool Outranks(ProcessIntegrity other) =>
        Level > other.Level || (Level == other.Level && UiAccess && !other.UiAccess);

    /// <summary>Whether this server cannot drive the target; an unreadable target token blocks.</summary>
    internal static bool Blocks(ProcessIntegrity self, ProcessIntegrity? target) =>
        target == null || target.Outranks(self);

    /// <summary>
    /// The refusal when this server cannot drive the target, or null when it can. An unreadable target token
    /// fails closed: an elevated process's token is exactly the one a medium process cannot open.
    /// </summary>
    internal static InputRefusal? DescribeBlocked(
        ProcessIntegrity self,
        ProcessIntegrity? target,
        int targetPid,
        string? targetName
    )
    {
        if (!Blocks(self, target))
        {
            return null;
        }

        var name = targetName ?? "unknown";
        var level = target?.LevelName ?? "unknown";
        var uiAccess = target is { UiAccess: true } ? " with UIAccess" : "";
        var server = self.UiAccess ? $"{self.LevelName} with UIAccess" : self.LevelName;
        var reason =
            target == null
                ? $"the integrity level of '{name}' (PID {targetPid}) cannot be read, so it may be above this server ({self.LevelName})"
                : $"'{name}' (PID {targetPid}) runs at {level} integrity{uiAccess}, above this server ({server})";
        return new InputRefusal(
            "integrity-blocked",
            $"integrity-blocked: {reason}. Windows (UIPI) silently drops input and window messages sent to it. "
                + "No input sent. Run the server at the target's integrity level, or drive the target another way."
        )
        {
            Target = new ProcessRef(targetName, targetPid) { IntegrityLevel = level, UiAccess = target?.UiAccess },
        };
    }

    /// <summary>The integrity of <paramref name="processId"/>'s token, or null when it cannot be read.</summary>
    public static ProcessIntegrity? Read(int processId)
    {
        var process = OpenProcess(QueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return null;
            }

            try
            {
                return ReadToken(token);
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static ProcessIntegrity? ReadToken(nint token)
    {
        var label = Marshal.AllocHGlobal(LabelBufferSize);
        try
        {
            // TOKEN_MANDATORY_LABEL starts with the label SID pointer; the RID is its last sub-authority.
            if (!GetTokenInformation(token, TokenIntegrityLevel, label, LabelBufferSize, out _))
            {
                return null;
            }

            var sid = Marshal.ReadIntPtr(label);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            var level = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            if (!GetTokenInformation(token, TokenUiAccess, out var uiAccess, sizeof(int), out _))
            {
                return null;
            }

            return new ProcessIntegrity(level, uiAccess != 0);
        }
        finally
        {
            Marshal.FreeHGlobal(label);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int infoClass, nint info, int length, out int returned);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        nint token,
        int infoClass,
        out int info,
        int length,
        out int returned
    );

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint index);
}

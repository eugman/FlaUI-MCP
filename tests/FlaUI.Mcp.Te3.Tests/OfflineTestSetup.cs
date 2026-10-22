using System.Runtime.CompilerServices;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Turns off the real desktop for this test assembly before any test runs; see OfflineDesktopTests.</summary>
internal static class OfflineTestSetup
{
    [ModuleInitializer]
    internal static void DisableRealDesktop()
    {
        Environment.SetEnvironmentVariable(InputDesktop.OfflineVariable, "1");
        // A lease of this process's own (Core honours the name only offline), so the tests never wait on or block a
        // host holding the machine-wide input lease.
        Environment.SetEnvironmentVariable(
            DesktopLease.NameVariable,
            $@"Local\FlaUI-MCP-Te3-Tests-{Environment.ProcessId}"
        );
    }
}

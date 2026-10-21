using System.Runtime.CompilerServices;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Turns off the real desktop for this test assembly before any test runs; see OfflineDesktopTests.</summary>
internal static class OfflineTestSetup
{
    [ModuleInitializer]
    internal static void DisableRealDesktop() => Environment.SetEnvironmentVariable(InputDesktop.OfflineVariable, "1");
}

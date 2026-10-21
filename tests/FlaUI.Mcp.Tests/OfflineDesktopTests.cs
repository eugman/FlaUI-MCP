using System.Runtime.CompilerServices;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Turns off the real desktop for this test assembly before any test runs.</summary>
internal static class OfflineTestSetup
{
    [ModuleInitializer]
    internal static void DisableRealDesktop() => Environment.SetEnvironmentVariable(InputDesktop.OfflineVariable, "1");
}

/// <summary>
/// A test that forgot to install the fake desktop, or a mutant that skips a guard, must not type into whatever
/// window is in front. In this assembly the real desktop refuses every change.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class OfflineDesktopTests
{
    [Fact]
    public void TheRealDesktopRefusesInputInOfflineTests()
    {
        Assert.Null(InputDesktop.Override.Value);
        var sent = false;

        Assert.Throws<InvalidOperationException>(() => InputDesktop.Current.Dispatch(() => sent = true));
        Assert.Throws<InvalidOperationException>(() => InputDesktop.Current.Focus(1));
        Assert.Throws<InvalidOperationException>(() => InputDesktop.Current.PostClose(1));
        Assert.Throws<InvalidOperationException>(() => InputDesktop.Current.SetWindowState(1, "normal"));
        Assert.Throws<InvalidOperationException>(() => InputDesktop.Current.SetClipboardText("x"));
        Assert.Throws<InvalidOperationException>(() =>
            InputDesktop.Current.FocusElement(
                (FlaUI.Core.AutomationElements.AutomationElement)
                    RuntimeHelpers.GetUninitializedObject(typeof(FlaUI.Core.AutomationElements.AutomationElement))
            )
        );

        Assert.False(sent);
    }

    [Theory]
    [InlineData("FlaUI.Mcp.Tests")]
    [InlineData("FlaUI.Mcp.PowerBi.Tests")]
    [InlineData("FlaUI.Mcp.Camtasia.Tests")]
    [InlineData("FlaUI.Mcp.Te3.Tests")]
    public void ProductTestAssembliesCanInstallAFakeDesktop(string assembly)
    {
        var friends = typeof(InputDesktop)
            .Assembly.GetCustomAttributes(typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute), false)
            .Cast<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName);

        Assert.Contains(assembly, friends);
    }

    [Fact]
    public void TheRealDesktopStartsNoProcessInOfflineTests()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}", "missing.exe");

        var error = Assert.Throws<InvalidOperationException>(() =>
            WindowMutation.Start(new System.Diagnostics.ProcessStartInfo(missing) { UseShellExecute = false })
        );

        Assert.Contains(InputDesktop.OfflineVariable, error.Message);
    }

    [Fact]
    public void TheRealDesktopMakesNoPatternCallInOfflineTests()
    {
        var called = false;

        Assert.ThrowsAny<InvalidOperationException>(() =>
            ClickGeometry.Scroll(0, new PendingInvokeTracker(), () => { }, () => called = true, () => default)
        );

        Assert.False(called);
    }
}

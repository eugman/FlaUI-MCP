using System.Diagnostics;
using PlaywrightWindows.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public class ProcessPolicyTests
{
    [Fact]
    public void AllowAll_IsNotRestricted_AllowsEverything()
    {
        var policy = ProcessPolicy.AllowAll;

        Assert.False(policy.IsRestricted);
        Assert.True(policy.IsNameAllowed("explorer"));
        Assert.True(policy.IsNameAllowed(null));
        Assert.True(policy.IsProcessAllowed(0));
        Assert.True(policy.IsExecutableAllowed(@"C:\Windows\explorer.exe"));
    }

    [Fact]
    public void IsNameAllowed_MatchesCaseInsensitivelyAndIgnoresExeSuffix()
    {
        var policy = new ProcessPolicy(new[] { "TabularEditor3.exe" });

        Assert.True(policy.IsRestricted);
        Assert.True(policy.IsNameAllowed("TabularEditor3"));
        Assert.True(policy.IsNameAllowed("tabulareditor3"));
        Assert.True(policy.IsNameAllowed("TABULAREDITOR3.EXE"));
        Assert.False(policy.IsNameAllowed("TabularEditor2"));
        Assert.False(policy.IsNameAllowed("explorer"));
        Assert.False(policy.IsNameAllowed(null));
        Assert.False(policy.IsNameAllowed(""));
    }

    [Fact]
    public void IsExecutableAllowed_ReducesFullPathsToFileName()
    {
        var policy = new ProcessPolicy(new[] { "TabularEditor3" });

        Assert.True(policy.IsExecutableAllowed(@"C:\Program Files\Tabular Editor 3\TabularEditor3.exe"));
        Assert.True(policy.IsExecutableAllowed("TabularEditor3.exe"));
        Assert.True(policy.IsExecutableAllowed("TabularEditor3"));
        Assert.False(policy.IsExecutableAllowed(@"C:\Windows\explorer.exe"));
        Assert.False(policy.IsExecutableAllowed("cmd.exe"));
    }

    [Fact]
    public void AllowlistEntries_AcceptFullPaths()
    {
        var policy = new ProcessPolicy(new[] { @"C:\Program Files\Tabular Editor 3\TabularEditor3.exe" });

        Assert.True(policy.IsNameAllowed("TabularEditor3"));
    }

    [Fact]
    public void IsProcessAllowed_ChecksTheProcessName()
    {
        using var current = Process.GetCurrentProcess();

        var allowing = new ProcessPolicy(new[] { current.ProcessName });
        var denying = new ProcessPolicy(new[] { "SomeOtherApp" });

        Assert.True(allowing.IsProcessAllowed(current.Id));
        Assert.False(denying.IsProcessAllowed(current.Id));
    }

    [Fact]
    public void IsProcessAllowed_DeniesUnknownProcessesWhenRestricted()
    {
        var policy = new ProcessPolicy(new[] { "TabularEditor3" });

        Assert.False(policy.IsProcessAllowed(0));
        Assert.False(policy.IsProcessAllowed(-1));
    }

    [Fact]
    public void FromEnvironment_ParsesSemicolonAndCommaSeparatedEntries()
    {
        var original = Environment.GetEnvironmentVariable(ProcessPolicy.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ProcessPolicy.EnvironmentVariable, " TabularEditor3.exe ; notepad, calc ");
            var policy = ProcessPolicy.FromEnvironment();

            Assert.True(policy.IsRestricted);
            Assert.True(policy.IsNameAllowed("TabularEditor3"));
            Assert.True(policy.IsNameAllowed("notepad"));
            Assert.True(policy.IsNameAllowed("calc"));
            Assert.False(policy.IsNameAllowed("explorer"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProcessPolicy.EnvironmentVariable, original);
        }
    }

    [Fact]
    public void FromEnvironment_UnsetOrEmpty_AllowsAll()
    {
        var original = Environment.GetEnvironmentVariable(ProcessPolicy.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ProcessPolicy.EnvironmentVariable, null);
            Assert.False(ProcessPolicy.FromEnvironment().IsRestricted);

            Environment.SetEnvironmentVariable(ProcessPolicy.EnvironmentVariable, "   ");
            Assert.False(ProcessPolicy.FromEnvironment().IsRestricted);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProcessPolicy.EnvironmentVariable, original);
        }
    }

    [Fact]
    public void DescribeDenied_NamesTheAllowedAppsAndEnvironmentVariable()
    {
        var policy = new ProcessPolicy(new[] { "TabularEditor3", "notepad" });

        var message = policy.DescribeDenied("Process 'explorer'");

        Assert.Contains("Process 'explorer'", message);
        Assert.Contains("TabularEditor3", message);
        Assert.Contains("notepad", message);
        Assert.Contains(ProcessPolicy.EnvironmentVariable, message);
    }
}

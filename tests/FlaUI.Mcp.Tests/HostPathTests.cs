using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// HostPath checks the paths a host reads from its config (CamConfig, DemoConfig, RunConfig) before any is used:
/// a local, fully qualified path, with environment variables expanded or refused, and containment in a root.
/// </summary>
public sealed class HostPathTests
{
    [Fact]
    public void ALocalAbsolutePathIsReturnedInFull()
    {
        Assert.Equal(@"C:\work\demo\probe.tscproj", HostPath.Validate(@"  C:/work/demo/probe.tscproj ", "project"));
    }

    [Theory]
    [InlineData(@"", "is required")]
    [InlineData(@"work\probe.txt", "fully qualified")]
    [InlineData(@"\work\probe.txt", "fully qualified")]
    [InlineData(@"~\work\probe.txt", "~")]
    [InlineData(@"/mnt/c/work/probe.txt", "Windows path")]
    [InlineData(@"/home/me/probe.txt", "Windows path")]
    [InlineData(@"\\server\share\probe.txt", "UNC")]
    [InlineData(@"//server/share/probe.txt", "UNC")]
    [InlineData(@"\\?\C:\work\probe.txt", "UNC")]
    [InlineData(@"C:\work\..\other\probe.txt", ". or ..")]
    [InlineData(@"C:\work\.\probe.txt", ". or ..")]
    [InlineData(@"C:\work\pro|be.txt", "not valid")]
    [InlineData(@"C:\work\probe:stream", "not valid")]
    [InlineData(@"$HOME\probe.txt", "environment")]
    [InlineData(@"C:\${WORK}\probe.txt", "environment")]
    public void AnUnusablePathIsRefusedByName(string path, string reason)
    {
        var error = Assert.Throws<ArgumentException>(() => HostPath.Validate(path, "outputRoot"));

        Assert.StartsWith("outputRoot ", error.Message);
        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADefinedEnvironmentVariableIsExpandedAndAnUndefinedOneRefused()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        Environment.SetEnvironmentVariable("FLAUI_HOSTPATH_TEST_ROOT", root + "work");
        try
        {
            Assert.Equal(
                Path.Combine(root, "work", "probe.txt"),
                HostPath.Validate(@"%FLAUI_HOSTPATH_TEST_ROOT%\probe.txt", "path")
            );
            var error = Assert.Throws<ArgumentException>(() =>
                HostPath.Validate(@"%FLAUI_HOSTPATH_UNDEFINED_VAR%\probe.txt", "path")
            );
            Assert.Contains("FLAUI_HOSTPATH_UNDEFINED_VAR", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLAUI_HOSTPATH_TEST_ROOT", null);
        }
    }

    [Theory]
    [InlineData(@"C:\work", @"C:\work\demo\probe.txt", true)]
    [InlineData(@"C:\work\", @"C:\work\demo", true)]
    [InlineData(@"C:\work", @"C:\work", true)]
    [InlineData(@"C:\Work", @"c:\work\probe.txt", true)]
    [InlineData(@"C:\work", @"C:\work2\probe.txt", false)]
    [InlineData(@"C:\work", @"C:\work\..\other\probe.txt", false)]
    [InlineData(@"C:\work", @"D:\work\probe.txt", false)]
    [InlineData(@"C:\work", @"C:\", false)]
    public void WithinHoldsOnlyForTheRootAndWhatIsUnderIt(string root, string path, bool within)
    {
        Assert.Equal(within, HostPath.Within(root, path));
    }
}

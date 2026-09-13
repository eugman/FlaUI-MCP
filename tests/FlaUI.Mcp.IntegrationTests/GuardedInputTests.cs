using PlaywrightWindows.Mcp.Core;
namespace FlaUI.Mcp.IntegrationTests;

[Collection("TestApps")]
public class GuardedInputTests(TestAppFixture fixture)
{
    [Fact]
    public void FocusStealStopsExistingInputLease()
    {
        var target = fixture.Session.GetInputTarget(fixture.WpfHandle);
        using var input = new GuardedInput(target);
        fixture.Session.FocusWindow(fixture.WinFormsHandle);
        var sent = false;
        Assert.Throws<InvalidOperationException>(() => input.Send(() => sent = true));
        Assert.False(sent);
    }
    [Fact]
    public void SecondPhysicalInputLeaseTimesOutWithoutInput()
    {
        var target = fixture.Session.GetInputTarget(fixture.WpfHandle);
        using var first = new GuardedInput(target);
        Assert.Throws<TimeoutException>(() => new GuardedInput(target));
    }
}

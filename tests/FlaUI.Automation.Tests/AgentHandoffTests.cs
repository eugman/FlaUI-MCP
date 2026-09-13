using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class AgentHandoffTests
{
    [Fact]
    public async Task ExplicitGradeReleases() => await AgentHandoff.Wait(() => "grade", TimeSpan.FromSeconds(1));

    [Theory]
    [InlineData("abort")]
    [InlineData("unexpected")]
    public async Task NonGradeCannotPass(string value)
        => await Assert.ThrowsAsync<InvalidOperationException>(() => AgentHandoff.Wait(() => value, TimeSpan.FromSeconds(1)));

    [Fact]
    public async Task MissingReleaseExpires()
        => await Assert.ThrowsAsync<TimeoutException>(() => AgentHandoff.Wait(() => null, TimeSpan.Zero));
}

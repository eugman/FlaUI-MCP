using Xunit;

namespace FlaUI.Automation.Tests;

public sealed class BacklogRecipeTests
{
    [Fact]
    public void EveryWaveOneCheckpointIsAnOpenBacklogItem()
    {
        var items = BacklogRecipes.Items.ToArray();
        Assert.Equal(items.Length, items.Distinct().Count());
        foreach (var id in items)
        {
            Assert.True(Backlog.IsItemId(id), id);
            var item = Backlog.Find(id);
            Assert.NotEqual("deferred", item.Status);
        }
    }

    [Fact]
    public void BacklogConfigRunsTheWaveOneRecipes()
    {
        var config = RunConfig.Read(Path.Combine(Path.GetDirectoryName(Backlog.DefaultPath())!, "backlog.config.json"));
        Assert.All(config.Scenarios, id => Assert.StartsWith("backlog-", id));
        Assert.Equal(config.Scenarios.Length, RecipeCatalog.Select(config.Scenarios).Length);
    }
}

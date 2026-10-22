using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Small Stage B guarantees: fact hints without annotations, and a refusal after a focus change.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class StageBMinorTests
{
    private sealed class Unannotated : ToolBase
    {
        public override string Name => "unannotated";
        public override string Description => "no annotations";
        public override object InputSchema => new { type = "object" };

        protected override bool Observation => true;

        protected override IReadOnlyList<string> MutatingWhenArgument => ["park"];

        public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) => Task.FromResult(TextResult("x"));
    }

    [Fact]
    public void FactHintsAreKeptWhenAToolDeclaresNoAnnotations()
    {
        var annotations = new Unannotated().Facts.Annotations;

        Assert.NotNull(annotations);
        Assert.True(annotations.ObservationHint);
        Assert.Equal(["park"], annotations.MutatingWhenArgument);
        Assert.Null(annotations.ReadOnlyHint);
    }

    [Fact]
    public void ARefusalAfterTheGuardActivatedTheTargetSaysInputWentOut()
    {
        // Activation changes the desktop and is counted; the refusal that follows must not claim nothing went out.
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, FocusLandsOn = 777 };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => new GuardedInput(FakeDesktop.Target));

        Assert.True(error.Refusal.ActionDispatched);
        Assert.True(error.ActionDispatched);
    }
}

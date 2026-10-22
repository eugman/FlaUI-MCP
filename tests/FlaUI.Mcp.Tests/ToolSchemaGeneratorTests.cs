using System.Text.Json;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// ToolSchema.For writes what the hand-written schemas did, key for key, so tools/list stays byte-identical when a
/// tool moves to an argument record: type, then enum, minimum, maximum, default, items, then description; nested
/// records as objects; a named member for a schema the record cannot express.
/// </summary>
public sealed class ToolSchemaGeneratorTests
{
    private static string Json<T>() => JsonSerializer.Serialize(ToolSchema.For<T>(), McpProtocol.JsonOptions);

    /// <summary>JSON written with single quotes, for readability.</summary>
    private static string Q(string json) => json.Replace('\'', '"');

    internal sealed record Point([property: ToolDoc("Across.")] int X, int Y);

    internal sealed record Rich(
        [property: ToolEnum("maximize", "restore")] string State,
        [property: ToolRange(1, Maximum = 5000, Default = 2000)] int? Limit = null,
        [property: ToolRange(0), ToolDoc("Pause.")] int? Ms = null,
        [property: ToolDoc("Refs.")] string[]? Locate = null,
        [property: ToolDoc("Where.")] Point? At = null,
        [property: ToolSchemaFrom("Fixed")] object? Odd = null
    )
    {
        public static object Fixed => new { description = "Kept as written.", type = "array" };
    }

    [Fact]
    public void EachKindOfPropertyIsWrittenInTheHandWrittenKeyOrder()
    {
        Assert.Equal(
            Q(
                "{'type':'object','additionalProperties':false,'properties':{"
                    + "'state':{'type':'string','enum':['maximize','restore']},"
                    + "'limit':{'type':'integer','minimum':1,'maximum':5000,'default':2000},"
                    + "'ms':{'type':'integer','minimum':0,'description':'Pause.'},"
                    + "'locate':{'type':'array','items':{'type':'string'},'description':'Refs.'},"
                    + "'at':{'type':'object','description':'Where.','properties':{'x':{'type':'integer','description':'Across.'},'y':{'type':'integer'}},'required':['x','y']},"
                    + "'odd':{'description':'Kept as written.','type':'array'}},'required':['state']}"
            ),
            Json<Rich>()
        );
    }

    internal sealed record Members
    {
        [ToolDoc("First, optional.")]
        public string? Handle { get; init; }

        [ToolDoc("Second, required.")]
        public required string Ref { get; init; }

        public bool? Physical { get; init; }
    }

    [Fact]
    public void ARequiredMemberIsRequiredWhereverItIsDeclared()
    {
        // A required argument after optional ones, which a positional record cannot declare.
        Assert.Equal(
            Q(
                "{'type':'object','additionalProperties':false,'properties':{"
                    + "'handle':{'type':'string','description':'First, optional.'},"
                    + "'ref':{'type':'string','description':'Second, required.'},"
                    + "'physical':{'type':'boolean'}},'required':['ref']}"
            ),
            Json<Members>()
        );
    }

    internal sealed record Collections
    {
        public List<string>? Names { get; init; }

        public Dictionary<string, string>? Labels { get; init; }

        public object? Anything { get; init; }
    }

    [Fact]
    public void ACollectionOrPlainObjectIsNotExpandedIntoItsMembers()
    {
        // List<T>'s Capacity and Count, or a dictionary's Keys, are not arguments.
        Assert.Equal(
            Q(
                "{'type':'object','additionalProperties':false,'properties':{"
                    + "'names':{'type':'array','items':{'type':'string'}},"
                    + "'labels':{'type':'object'},"
                    + "'anything':{'type':'object'}}}"
            ),
            Json<Collections>()
        );
    }

    internal sealed record Node
    {
        public Node? Next { get; init; }
    }

    [Fact]
    public void ASelfReferencingRecordIsRefusedByName()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ToolSchema.For<Node>());

        Assert.Contains("Node", error.Message);
        Assert.Contains("refers to itself", error.Message);
    }

    internal sealed record NoneRequired(bool? Flag = null);

    [ToolSchemaRequiredList]
    internal sealed record NoneRequiredButListed(bool? Flag = null);

    [Fact]
    public void AnEmptyRequiredListIsLeftOutUnlessTheRecordAsksForIt()
    {
        Assert.Equal(
            Q("{'type':'object','additionalProperties':false,'properties':{'flag':{'type':'boolean'}}}"),
            Json<NoneRequired>()
        );
        Assert.EndsWith(Q("},'required':[]}"), Json<NoneRequiredButListed>());
        Assert.Equal(Q("{'type':'object','additionalProperties':false,'properties':{}}"), Json<NoArguments>());
    }
}

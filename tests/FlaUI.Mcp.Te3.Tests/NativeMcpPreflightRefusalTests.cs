using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlaUI.Mcp.Te3;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Refusals before and after the identity call; a fake native MCP, no desktop.</summary>
public sealed class NativeMcpPreflightRefusalTests
{
    private const string WorkspaceDescription =
        "Identifies this Tabular Editor instance and the semantic model it currently has open: model name, how the model is loaded (file/folder/connected), unsaved-changes state and compatibility level. Call this first to confirm which instance and model you are talking to.";

    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","serverInfo":{"name":"TE3","version":"3.27.1"}}}""";

    private const string ReadOnlyIdentity =
        """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"model_identity","annotations":{"readOnlyHint":true}}]}}""";

    private sealed class ScriptedHandler(params string[] bodies) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        bodies[Math.Min(Calls++, bodies.Length - 1)],
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }

    private static NativeMcpPreflight.Request Request(string identityTool = "model_identity") =>
        new(
            new("http://localhost:42100/mcp"),
            identityTool,
            "model.name",
            "scratch-model",
            CompatibleServer: "TE3/3.27.1"
        );

    private static string Listing(JsonObject tool) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["result"] = new JsonObject { ["tools"] = new JsonArray(tool) },
        }.ToJsonString();

    private static JsonObject WorkspaceTool() =>
        new()
        {
            ["name"] = "get_workspace_info",
            ["description"] = WorkspaceDescription,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["required"] = new JsonArray(),
            },
        };

    private static async Task<(InvalidOperationException Error, int Calls)> Refusal(
        string listing,
        string identityTool = "model_identity",
        string identity = """{"jsonrpc":"2.0","id":3,"result":{"model":{"name":"scratch-model"}}}"""
    )
    {
        var handler = new ScriptedHandler(Initialize, "", listing, identity);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(Request(identityTool))
        );
        return (error, handler.Calls);
    }

    [Fact]
    public async Task AnUnlistedIdentityToolIsRefusedWithoutACall()
    {
        var (error, calls) = await Refusal(ReadOnlyIdentity, identityTool: "other_identity");

        Assert.Contains("unavailable", error.Message);
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("annotations")]
    [InlineData("schemaType")]
    [InlineData("properties")]
    [InlineData("required")]
    public async Task AWorkspaceToolThatDiffersFromTheAllowlistIsNeverCalled(string change)
    {
        // Each one is the exact TE3 3.27.1 tool with one field changed.
        var tool = WorkspaceTool();
        var schema = tool["inputSchema"]!.AsObject();
        switch (change)
        {
            case "annotations":
                tool["annotations"] = new JsonObject { ["readOnlyHint"] = false };
                break;
            case "schemaType":
                schema["type"] = "string";
                break;
            case "properties":
                schema["properties"] = new JsonObject { ["model"] = new JsonObject { ["type"] = "string" } };
                break;
            default:
                schema.Remove("required");
                break;
        }

        var (error, calls) = await Refusal(Listing(tool), identityTool: "get_workspace_info");

        Assert.Contains("allowlist", error.Message);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task TheExactWorkspaceToolIsAccepted()
    {
        var handler = new ScriptedHandler(
            Initialize,
            "",
            Listing(WorkspaceTool()),
            """{"jsonrpc":"2.0","id":3,"result":{"model":{"name":"scratch-model"}}}"""
        );

        var result = await new NativeMcpPreflight(new HttpClient(handler)).Inspect(Request("get_workspace_info"));

        Assert.Equal("scratch-model", result.ModelIdentity);
    }

    [Fact]
    public async Task AnIdentityToolErrorIsRefused()
    {
        var (error, calls) = await Refusal(
            ReadOnlyIdentity,
            identity: """{"jsonrpc":"2.0","id":3,"result":{"isError":true,"model":{"name":"scratch-model"}}}"""
        );

        Assert.Contains("returned an error", error.Message);
        Assert.Equal(4, calls);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":3,"result":{"model":{"name":""}}}""")]
    // An image block is never read as the identity, even when its text carries one.
    [InlineData(
        """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"image","text":"{\"model\":{\"name\":\"scratch-model\"}}"}]}}"""
    )]
    public async Task AnIdentityResponseWithoutThePropertyIsRefused(string identity)
    {
        var (error, _) = await Refusal(ReadOnlyIdentity, identity: identity);

        Assert.Contains("does not contain model.name", error.Message);
    }

    [Theory]
    [InlineData("", "model.name", "scratch-model")]
    [InlineData("model_identity", " ", "scratch-model")]
    [InlineData("model_identity", "model.name", "")]
    public async Task AMissingIdentityFieldIsRefusedBeforeAnyRequest(string tool, string property, string expected)
    {
        var handler = new ScriptedHandler(Initialize);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(
                new(new("http://localhost:42100/mcp"), tool, property, expected)
            )
        );

        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("endpoint", "not a url", "absolute URL")]
    [InlineData("tokenEnv", "BAD-NAME", "environment variable name")]
    [InlineData("tokenEnv", "", "environment variable name")]
    [InlineData("tokenEnv", "FLAUI_TE3_TEST_TOKEN_THAT_IS_NEVER_SET", "is not set")]
    public async Task TheToolRefusesBadEndpointOrTokenArgumentsBeforeAnyRequest(
        string name,
        string value,
        string refusal
    )
    {
        var handler = new ScriptedHandler(Initialize);
        var arguments = new JsonObject
        {
            ["endpoint"] = "http://127.0.0.1:42100/",
            ["identityTool"] = "model_identity",
            ["identityProperty"] = "model.name",
            ["expectedIdentity"] = "scratch-model",
        };
        arguments[name] = value;

        var result = await new Te3NativePreflightTool(new HttpClient(handler)).ExecuteAsync(
            JsonSerializer.SerializeToElement(arguments)
        );

        Assert.True(result.IsError);
        Assert.Contains(refusal, result.Content[0].Text);
        Assert.Equal(0, handler.Calls);
    }
}

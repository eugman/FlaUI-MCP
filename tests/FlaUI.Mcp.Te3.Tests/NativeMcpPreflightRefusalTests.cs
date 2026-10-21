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

    private const string AllowlistedServer = "TE3/3.27.1";

    private static NativeMcpPreflight.Request Request(
        string identityTool = "model_identity",
        string? compatibleServer = AllowlistedServer
    ) =>
        new(
            new("http://localhost:42100/mcp"),
            identityTool,
            "model.name",
            "scratch-model",
            CompatibleServer: compatibleServer
        );

    private static string Listing(params JsonNode[] tools) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["result"] = new JsonObject { ["tools"] = new JsonArray(tools) },
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
        string identity = """{"jsonrpc":"2.0","id":3,"result":{"model":{"name":"scratch-model"}}}""",
        string initialize = Initialize,
        string? compatibleServer = AllowlistedServer
    )
    {
        var handler = new ScriptedHandler(initialize, "", listing, identity);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(Request(identityTool, compatibleServer))
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
    [InlineData("noProperties")]
    [InlineData("requiredNotArray")]
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
            case "noProperties":
                schema.Remove("properties");
                break;
            case "requiredNotArray":
                schema["required"] = "model";
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
    [InlineData("""{"jsonrpc":"2.0","id":3,"result":{"model":{"name":42}}}""")]
    // Only a single text block is read; two are ambiguous.
    [InlineData(
        """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"{\"model\":{\"name\":\"scratch-model\"}}"},{"type":"text","text":"{}"}]}}"""
    )]
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

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(
                new(new("http://localhost:42100/mcp"), tool, property, expected)
            )
        );

        Assert.Contains("Identity tool, response property and expected identity are required", error.Message);

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

    [Fact]
    public async Task TheWorkspaceExceptionNeedsTheAllowlistedServerVersion()
    {
        // The caller names the allowlisted server, but the server reports another version.
        var (error, calls) = await Refusal(
            Listing(WorkspaceTool()),
            identityTool: "get_workspace_info",
            initialize: Initialize.Replace("3.27.1", "3.28.0")
        );

        Assert.Contains("applies only to a server reporting \"TE3/3.27.1\"", error.Message);
        Assert.Contains("lacks readOnlyHint", error.Message);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task TheWorkspaceExceptionNeedsTheCallerToNameTheServer()
    {
        var (error, calls) = await Refusal(
            Listing(WorkspaceTool()),
            identityTool: "get_workspace_info",
            compatibleServer: null
        );

        Assert.Contains("also requires compatibleServer \"TE3/3.27.1\"", error.Message);
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":2,"result":{}}""", "did not return a tools list")]
    [InlineData("""{"jsonrpc":"2.0","id":2,"result":{"tools":{}}}""", "did not return a tools list")]
    [InlineData(
        """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"model_identity","annotations":{"readOnlyHint":true}},{"description":"no name"}]}}""",
        "listed a tool without a name"
    )]
    public async Task AMalformedToolListIsRefusedBeforeAnyCall(string listing, string refusal)
    {
        var (error, calls) = await Refusal(listing);

        Assert.Contains(refusal, error.Message);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task AServerWithoutANameIsRefused()
    {
        var (error, _) = await Refusal(
            ReadOnlyIdentity,
            initialize: """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","serverInfo":{}}}"""
        );

        Assert.Contains("did not report server identity", error.Message);
    }

    [Fact]
    public async Task AnEndpointWithAFragmentIsRefusedBeforeAnyRequest()
    {
        var handler = new ScriptedHandler(Initialize);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(
                new(new("http://localhost:42100/mcp#elsewhere"), "model_identity", "model.name", "scratch-model")
            )
        );

        Assert.Contains("explicit local HTTP loopback", error.Message);
        Assert.Equal(0, handler.Calls);
    }

    private static JsonObject ToolArguments() =>
        new()
        {
            ["endpoint"] = "http://127.0.0.1:42100/",
            ["identityTool"] = "get_workspace_info",
            ["identityProperty"] = "model.name",
            ["expectedIdentity"] = "scratch-model",
            ["compatibleServer"] = AllowlistedServer,
        };

    private static ScriptedHandler WorkspaceServer() =>
        new(
            Initialize,
            "",
            Listing(WorkspaceTool()),
            """{"jsonrpc":"2.0","id":3,"result":{"model":{"name":"scratch-model"}}}"""
        );

    [Fact]
    public async Task TheToolPassesTheNamedServerToTheWorkspaceException()
    {
        var result = await new Te3NativePreflightTool(new HttpClient(WorkspaceServer())).ExecuteAsync(
            JsonSerializer.SerializeToElement(ToolArguments())
        );

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("scratch-model", result.Content[0].Text);
    }

    [Fact]
    public async Task TheToolRefusesANonStringIdentityTool()
    {
        var arguments = ToolArguments();
        arguments["identityTool"] = 42;

        var result = await new Te3NativePreflightTool(new HttpClient(WorkspaceServer())).ExecuteAsync(
            JsonSerializer.SerializeToElement(arguments)
        );

        Assert.True(result.IsError);
        Assert.Contains("identityTool is required", result.Content[0].Text);
    }

    [Fact]
    public async Task AStoppedOperationStopsThePreflight()
    {
        var context = new FlaUI.Mcp.Core.OperationContext();
        context.Stop.Cancel();
        FlaUI.Mcp.Core.OperationContext.Current.Value = context;
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new Te3NativePreflightTool(new HttpClient(WorkspaceServer())).ExecuteAsync(
                    JsonSerializer.SerializeToElement(ToolArguments())
                )
            );
        }
        finally
        {
            FlaUI.Mcp.Core.OperationContext.Current.Value = null;
        }
    }
}

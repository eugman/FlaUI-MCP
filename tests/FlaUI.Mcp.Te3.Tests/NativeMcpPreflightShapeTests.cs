using System.Net;
using System.Text;
using System.Text.Json;
using FlaUI.Mcp.Te3;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Malformed native MCP responses and the HTTP client the tool builds.</summary>
public sealed class NativeMcpPreflightShapeTests
{
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","serverInfo":{"name":"TE3"}}}""";

    private sealed class ScriptedHandler(params string[] bodies) : HttpMessageHandler
    {
        private int step;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        bodies[Math.Min(step++, bodies.Length - 1)],
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25"}}""", "{}")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","serverInfo":{}}}""", "{}")]
    [InlineData(Initialize, """{"jsonrpc":"2.0","id":2,"result":{}}""")]
    [InlineData(Initialize, """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"description":"no name"}]}}""")]
    public async Task AMissingServerInfoOrToolsFieldIsAToolErrorNotACrash(string initialize, string tools)
    {
        var tool = new Te3NativePreflightTool(new HttpClient(new ScriptedHandler(initialize, "", tools)));
        var args = JsonSerializer.SerializeToElement(
            new
            {
                endpoint = "http://127.0.0.1:42100/",
                identityTool = "model_identity",
                identityProperty = "model.name",
                expectedIdentity = "scratch-model",
            }
        );

        var result = await tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    [Fact]
    public void TheToolClientNeverRoutesLoopbackBearerRequestsThroughAProxy()
    {
        using var handler = Te3NativePreflightTool.CreateHandler();
        Assert.False(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
    }
}

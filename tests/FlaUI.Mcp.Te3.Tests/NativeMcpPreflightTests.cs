using System.Net;
using System.Text;
using FlaUI.Mcp.Te3;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

public sealed class NativeMcpPreflightTests
{
    private sealed class FakeHandler(bool readOnly = true) : HttpMessageHandler
    {
        private int step;
        public List<string> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Calls.Add(body);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(
                step++ switch
                {
                    0 =>
                        """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","serverInfo":{"name":"TE3"}}}""",
                    1 => "",
                    2 => readOnly
                        ? """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"model_identity","annotations":{"readOnlyHint":true}}]}}"""
                        : """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"model_identity"}]}}""",
                    _ => """{"jsonrpc":"2.0","id":3,"result":{"model":{"name":"scratch-model"}}}""",
                },
                Encoding.UTF8,
                "application/json"
            );
            return response;
        }
    }

    [Fact]
    public async Task ProbesReadOnlyIdentityWithoutAnyMutationTool()
    {
        var handler = new FakeHandler();
        var client = new HttpClient(handler);
        var preflight = new NativeMcpPreflight(client);
        var result = await preflight.Inspect(
            new(new("http://127.0.0.1:42100/"), "model_identity", "model.name", "scratch-model")
        );

        Assert.Equal("scratch-model", result.ModelIdentity);
        Assert.Equal("2025-11-25", result.ProtocolVersion);
        Assert.Equal(4, handler.Calls.Count);
        Assert.DoesNotContain(handler.Calls, call => call.Contains("run_script", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesNonLocalEndpointBeforeAnyRequest()
    {
        var handler = new FakeHandler();
        var preflight = new NativeMcpPreflight(new HttpClient(handler));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            preflight.Inspect(new(new("http://example.com:42100/"), "model_identity", "model.name", "scratch-model"))
        );
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task UnannotatedIdentityToolIsNeverCalled()
    {
        var handler = new FakeHandler(readOnly: false);
        var preflight = new NativeMcpPreflight(new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preflight.Inspect(new(new("http://localhost:42100/"), "model_identity", "model.name", "scratch-model"))
        );
        Assert.Equal(3, handler.Calls.Count);
    }

    [Fact]
    public async Task WrongModelFailsClosed()
    {
        var handler = new FakeHandler();
        var preflight = new NativeMcpPreflight(new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preflight.Inspect(new(new("http://localhost:42100/"), "model_identity", "model.name", "other-model"))
        );
    }

    [Fact]
    public async Task ToolRouteUsesFakeNativeMcpWithoutDesktop()
    {
        var handler = new FakeHandler();
        var tool = new Te3NativePreflightTool(new HttpClient(handler));
        var args = System.Text.Json.JsonSerializer.SerializeToElement(
            new
            {
                endpoint = "http://127.0.0.1:42100/",
                identityTool = "model_identity",
                identityProperty = "model.name",
                expectedIdentity = "scratch-model",
            }
        );
        var result = await tool.ExecuteAsync(args);
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(4, handler.Calls.Count);
    }

    private sealed class WorkspaceHandler(bool wrongDescription = false, bool structured = false) : HttpMessageHandler
    {
        private int step;
        public int Calls => step;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            const string description =
                "Identifies this Tabular Editor instance and the semantic model it currently has open: model name, how the model is loaded (file/folder/connected), unsaved-changes state and compatibility level. Call this first to confirm which instance and model you are talking to.";
            var body = step++ switch
            {
                0 =>
                    """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","serverInfo":{"name":"TE3"}}}""",
                1 => "",
                2 => System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        id = 2,
                        result = new
                        {
                            tools = new[]
                            {
                                new
                                {
                                    name = "get_workspace_info",
                                    description = wrongDescription ? "unknown" : description,
                                    inputSchema = new
                                    {
                                        type = "object",
                                        properties = new { },
                                        required = Array.Empty<string>(),
                                    },
                                },
                            },
                        },
                    }
                ),
                _ => structured
                    ? """{"jsonrpc":"2.0","id":3,"result":{"structuredContent":{"model":{"name":"scratch-model"}}}}"""
                    : """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"{\"model\":{\"name\":\"scratch-model\"}}"}]}}""",
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownUnannotatedWorkspaceIdentityAcceptsJsonResultWrappers(bool structured)
    {
        var handler = new WorkspaceHandler(structured: structured);
        var result = await new NativeMcpPreflight(new HttpClient(handler)).Inspect(
            new(new("http://localhost:42100/mcp"), "get_workspace_info", "model.name", "scratch-model")
        );
        Assert.Equal("scratch-model", result.ModelIdentity);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task ChangedUnannotatedWorkspaceDescriptionIsNotAllowed()
    {
        var handler = new WorkspaceHandler(wrongDescription: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(
                new(new("http://localhost:42100/mcp"), "get_workspace_info", "model.name", "scratch-model")
            )
        );
        Assert.Equal(3, handler.Calls);
    }
}

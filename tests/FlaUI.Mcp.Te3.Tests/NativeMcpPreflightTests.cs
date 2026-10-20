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

    private const string Te3ServerInfo = """{"name":"TE3","version":"3.27.1"}""";

    private sealed class WorkspaceHandler(
        bool wrongDescription = false,
        bool structured = false,
        string serverInfo = Te3ServerInfo
    ) : HttpMessageHandler
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
                    $$$"""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","serverInfo":{{{serverInfo}}}}}""",
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
            Workspace(compatibleServer: "TE3/3.27.1")
        );
        Assert.Equal("scratch-model", result.ModelIdentity);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task ChangedUnannotatedWorkspaceDescriptionIsNotAllowed()
    {
        var handler = new WorkspaceHandler(wrongDescription: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(Workspace(compatibleServer: "TE3/3.27.1"))
        );
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("TE3/3.28.0")]
    [InlineData("Other/3.27.1")]
    public async Task UnannotatedWorkspaceToolIsNeverCalledWithoutTheObservedServerIdentity(string? compatibleServer)
    {
        var handler = new WorkspaceHandler();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(Workspace(compatibleServer))
        );
        Assert.Contains("TE3/3.27.1", error.Message);
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData("""{"name":"TE3"}""", "TE3/")]
    [InlineData("""{"name":"TE3","version":""}""", "TE3/")]
    [InlineData("""{"name":"Other","version":"9.9"}""", "Other/9.9")]
    [InlineData("""{"name":"TE3","version":"3.28.0"}""", "TE3/3.28.0")]
    [InlineData("""{"name":"Other","version":"3.27.1"}""", "Other/3.27.1")]
    public async Task EchoingTheReportedServerBackIsNotEnoughOutsideTheAllowlist(string serverInfo, string echoed)
    {
        var handler = new WorkspaceHandler(serverInfo: serverInfo);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(Workspace(compatibleServer: echoed))
        );
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task TheRefusalNamesOnlyTheAllowlistedServerNotTheReportedOne()
    {
        var handler = new WorkspaceHandler(serverInfo: """{"name":"Other","version":"9.9"}""");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(Workspace(compatibleServer: null))
        );
        Assert.DoesNotContain("Other/9.9", error.Message);
    }

    [Theory]
    [InlineData("http://[::1]:42100/mcp")]
    [InlineData("http://127.0.0.1:42100/mcp")]
    [InlineData("http://localhost:42100/mcp")]
    public async Task LoopbackEndpointsIncludingIpv6AreAccepted(string endpoint)
    {
        var handler = new FakeHandler();
        var result = await new NativeMcpPreflight(new HttpClient(handler)).Inspect(
            new(new(endpoint), "model_identity", "model.name", "scratch-model")
        );
        Assert.Equal("scratch-model", result.ModelIdentity);
    }

    [Theory]
    [InlineData("http://localhost.example.com:42100/mcp")]
    [InlineData("http://127.0.0.1.nip.io:42100/mcp")]
    [InlineData("http://user:secret@localhost:42100/mcp")]
    [InlineData("https://localhost:42100/mcp")]
    [InlineData("http://[::2]:42100/mcp")]
    public async Task LookalikeOrCredentialedEndpointsAreRefusedBeforeAnyRequest(string endpoint)
    {
        var handler = new FakeHandler();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new NativeMcpPreflight(new HttpClient(handler)).Inspect(
                new(new(endpoint), "model_identity", "model.name", "scratch-model")
            )
        );
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task OneOverallDeadlineBoundsTheWholeInspection()
    {
        var handler = new SilentHandler();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            new NativeMcpPreflight(new HttpClient(handler), TimeSpan.FromMilliseconds(200)).Inspect(
                new(new("http://localhost:42100/mcp"), "model_identity", "model.name", "scratch-model")
            )
        );
        Assert.Contains("overall", error.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(NativeMcpPreflight.DefaultOverallTimeout < TimeSpan.FromSeconds(30));
    }

    private static NativeMcpPreflight.Request Workspace(string? compatibleServer) =>
        new(
            new("http://localhost:42100/mcp"),
            "get_workspace_info",
            "model.name",
            "scratch-model",
            CompatibleServer: compatibleServer
        );

    private sealed class SilentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using FlaUI.Mcp.Te3;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>What the probe session sends and which responses it accepts; a recording fake, no network.</summary>
public sealed class NativeMcpProbeSessionTests
{
    private const string Ok = """{"jsonrpc":"2.0","id":1,"result":{}}""";

    private sealed class Recorder(Func<int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return reply(Requests.Count - 1);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static NativeMcpProbeSession Session(Recorder recorder, string? bearer = null) =>
        new(new HttpClient(recorder), new("http://localhost:42100/mcp"), bearer);

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    [Fact]
    public async Task EveryRequestAcceptsJsonAndEventStreamAndCarriesTheBearerToken()
    {
        var recorder = new Recorder(step => Json(Ok.Replace("\"id\":1", $"\"id\":{step + 1}")));
        var session = Session(recorder, "secret");

        await session.Send("initialize", new { }, CancellationToken.None);
        await session.NotifyInitialized(CancellationToken.None);

        Assert.All(
            recorder.Requests,
            request =>
            {
                var accepted = request.Headers.Accept.Select(type => type.MediaType).ToArray();
                Assert.Contains("application/json", accepted);
                Assert.Contains("text/event-stream", accepted);
                Assert.Equal("Bearer secret", request.Headers.Authorization?.ToString());
            }
        );
        using var call = JsonDocument.Parse(recorder.Bodies[0]);
        Assert.Equal("2.0", call.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal("initialize", call.RootElement.GetProperty("method").GetString());
        Assert.Equal("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", recorder.Bodies[1]);
    }

    [Fact]
    public async Task AnEmptyBearerTokenSendsNoAuthorization()
    {
        var recorder = new Recorder(_ => Json(Ok));

        await Session(recorder, "").Send("initialize", new { }, CancellationToken.None);

        Assert.Null(recorder.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task TheFirstSessionIdIsSentOnEveryLaterRequest()
    {
        var recorder = new Recorder(step =>
        {
            var response = Json(Ok.Replace("\"id\":1", $"\"id\":{step + 1}"));
            if (step == 0)
            {
                response.Headers.Add("Mcp-Session-Id", "s1");
            }

            return response;
        });
        var session = Session(recorder);

        for (var call = 0; call < 3; call++)
        {
            await session.Send("tools/list", new { }, CancellationToken.None);
        }

        Assert.Equal([null, "s1", "s1"], recorder.Requests.Select(request => Header(request, "Mcp-Session-Id")));
    }

    [Theory]
    [InlineData(300)]
    [InlineData(399)]
    public async Task EveryRedirectStatusIsRefused(int status)
    {
        var recorder = new Recorder(_ => Json(Ok, (HttpStatusCode)status));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Session(recorder).Send("initialize", new { }, CancellationToken.None)
        );

        Assert.Contains("redirected", error.Message);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    public async Task AnErrorStatusFailsEvenWithAMatchingBody(int status)
    {
        var recorder = new Recorder(_ => Json(Ok, (HttpStatusCode)status));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Session(recorder).Send("initialize", new { }, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ARejectedNotificationFails()
    {
        var recorder = new Recorder(_ => Json("", HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Session(recorder).NotifyInitialized(CancellationToken.None)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("2025 11 25")]
    [InlineData("2025-11-25\r\nX-Injected: 1")]
    public void AnUnsafeProtocolVersionIsRefused(string version)
    {
        var session = Session(new Recorder(_ => Json(Ok)));

        var error = Assert.Throws<InvalidOperationException>(() => session.SetProtocolVersion(version));

        Assert.Contains("invalid protocol version", error.Message);
    }

    [Fact]
    public async Task AJsonRpcErrorIsReportedAsTheServersError()
    {
        var recorder = new Recorder(_ =>
            Json("""{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"no such method"}}""")
        );

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Session(recorder).Send("initialize", new { }, CancellationToken.None)
        );

        Assert.Contains("Native MCP error", error.Message);
        Assert.Contains("no such method", error.Message);
    }

    [Fact]
    public async Task AResponseWithNeitherResultNorErrorIsRefused()
    {
        var recorder = new Recorder(_ => Json("""{"jsonrpc":"2.0","id":1}"""));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Session(recorder).Send("initialize", new { }, CancellationToken.None)
        );

        Assert.Contains("neither result nor error", error.Message);
    }
}

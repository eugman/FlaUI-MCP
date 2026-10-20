using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FlaUI.Mcp.Te3;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Event-stream parsing bounds, response matching, and the production client's redirect policy.</summary>
public sealed class NativeMcpTransportBoundsTests
{
    private const string Matching = """{"jsonrpc":"2.0","id":1,"result":{"ok":true}}""";

    private sealed class SseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)))
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") },
                    },
                }
            );
    }

    private static Task<JsonElement> SendOnce(string sseBody)
    {
        var session = new NativeMcpProbeSession(
            new HttpClient(new SseHandler(sseBody)),
            new("http://localhost:42100/mcp"),
            null
        );
        return session.Send("tools/list", new { }, CancellationToken.None);
    }

    [Fact]
    public async Task CrlfEventStreamIsParsed()
    {
        var result = await SendOnce("data: " + Matching + "\r\n\r\n");
        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"ping"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1.5,"result":{"ok":false}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":10000000000,"result":{"ok":false}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":"1","result":{"ok":false}}""")]
    public async Task ServerRequestsAndNonMatchingIdsAreSkipped(string first)
    {
        var result = await SendOnce("data: " + first + "\n\ndata: " + Matching + "\n\n");
        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task ResponseWithoutResultOrErrorIsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SendOnce("data: " + """{"jsonrpc":"2.0","id":1}""" + "\n\n")
        );
    }

    [Fact]
    public async Task OneEventOfManyShortLinesIsBoundedAsAWhole()
    {
        // Each line is under the 64K line limit; together they exceed the 256K response limit.
        var line = "data: " + new string('x', 60_000) + "\n";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SendOnce(string.Concat(Enumerable.Repeat(line, 5)) + "\n")
        );
        Assert.Contains("size limit", error.Message);
    }

    [Fact]
    public async Task ManyValidEventsAreBoundedInTotal()
    {
        var padding = new string('x', 60_000);
        var notification =
            "data: "
            + "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"p\":\""
            + padding
            + "\"}}\n\n";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SendOnce(string.Concat(Enumerable.Repeat(notification, 5)))
        );
        Assert.Contains("size limit", error.Message);
    }

    [Fact]
    public async Task ProductionClientRefusesRedirectAndNeverFollowsIt()
    {
        // A real loopback socket and Te3NativePreflightTool.Create(): proves the shipped HttpClient does not
        // follow a redirect, not only that the probe rejects a 3xx returned by a fake handler.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connections = 0;
        using var stop = new CancellationTokenSource();
        var server = Task.Run(() =>
            AnswerWithRedirects(listener, port, () => Interlocked.Increment(ref connections), stop.Token)
        );

        var args = JsonSerializer.SerializeToElement(
            new
            {
                endpoint = $"http://127.0.0.1:{port}/mcp",
                identityTool = "model_identity",
                identityProperty = "model.name",
                expectedIdentity = "scratch-model",
            }
        );
        var result = await Te3NativePreflightTool.Create().ExecuteAsync(args);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await stop.CancelAsync();
        listener.Stop();
        await Task.WhenAny(server, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.True(result.IsError);
        Assert.Contains("redirected", result.Content.Single().Text);
        Assert.Equal(1, Volatile.Read(ref connections));
    }

    private static async Task AnswerWithRedirects(
        TcpListener listener,
        int port,
        Action accepted,
        CancellationToken stop
    )
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop);
                accepted();
                var stream = client.GetStream();
                await ReadHeaders(stream, stop);
                var reply =
                    "HTTP/1.1 302 Found\r\n"
                    + $"Location: http://127.0.0.1:{port}/elsewhere\r\n"
                    + "Content-Length: 0\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), stop);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // The listener was stopped at the end of the test.
        }
    }

    private static async Task ReadHeaders(NetworkStream stream, CancellationToken stop)
    {
        var buffer = new byte[16_384];
        var read = 0;
        while (read < buffer.Length && !Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n"))
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), stop);
            if (count == 0)
            {
                return;
            }

            read += count;
        }
    }
}

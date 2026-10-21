using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FlaUI.Mcp.Te3;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

public sealed class NativeMcpProbeTransportTests
{
    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        private int count;
        public List<string?> Versions { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Versions.Add(request.Headers.TryGetValues("MCP-Protocol-Version", out var values) ? values.Single() : null);
            return Task.FromResult(reply(request, count++));
        }
    }

    private sealed class OpenTailStream(string prefix) : Stream
    {
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(prefix);
        private int position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (position == bytes.Length)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            }

            var count = Math.Min(buffer.Length, bytes.Length - position);
            bytes.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return count;
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ReadsMatchingResponseFromOpenSseAndSendsNegotiatedVersion()
    {
        const string sse =
            ": heartbeat\n\nevent: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{}}\n\nevent: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":2,\ndata: \"result\":{\"tools\":[{\"name\":\"model_identity\",\"annotations\":{\"readOnlyHint\":true}}]}}\n\n";
        var handler = new Handler(
            (_, step) =>
                step switch
                {
                    0 => Json(
                        """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","serverInfo":{"name":"TE3"}}}"""
                    ),
                    1 => new HttpResponseMessage(HttpStatusCode.Accepted),
                    2 => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new OpenTailStream(sse))
                        {
                            Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") },
                        },
                    },
                    _ => Json("""{"jsonrpc":"2.0","id":3,"result":{"model":{"name":"scratch-model"}}}"""),
                }
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await new NativeMcpPreflight(new HttpClient(handler)).Inspect(
            new(new("http://127.0.0.1:42100/mcp"), "model_identity", "model.name", "scratch-model"),
            timeout.Token
        );
        Assert.Equal("scratch-model", result.ModelIdentity);
        Assert.Equal([null, "2025-11-25", "2025-11-25", "2025-11-25"], handler.Versions);
    }

    private static NativeMcpProbeSession SseSession(string eventData) =>
        new(
            new HttpClient(
                new Handler(
                    (_, _) =>
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(eventData)))
                            {
                                Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") },
                            },
                        }
                )
            ),
            new("http://localhost:42100/mcp"),
            null
        );

    [Fact]
    public async Task RejectsMalformedSse() =>
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() =>
            SseSession("data: {bad}\n\n").Send("tools/list", new { }, CancellationToken.None)
        );

    [Fact]
    public async Task RejectsAValidJsonLineOverTheSizeLimit()
    {
        // Valid JSON-RPC for this request, so only the 64K line limit can reject it.
        var line = "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"pad\":\"" + new string('x', 70_000) + "\"}}\n\n";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SseSession(line).Send("tools/list", new { }, CancellationToken.None)
        );
        Assert.Contains("size limit", error.Message);
    }

    [Fact]
    public async Task RejectsRedirectWithoutFollowingLocation()
    {
        var handler = new Handler(
            (_, _) =>
                new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://example.com/mcp") },
                }
        );
        var session = new NativeMcpProbeSession(new HttpClient(handler), new("http://127.0.0.1:42100/mcp"), null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.Send("initialize", new { }, CancellationToken.None)
        );
        Assert.Single(handler.Versions);
    }

    [Fact]
    public async Task OpenStreamWithoutMatchingIdTimesOutWithinBound()
    {
        var handler = new Handler(
            (_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(
                        new OpenTailStream("data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\"}\n\n")
                    )
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") },
                    },
                }
        );
        var session = new NativeMcpProbeSession(
            new HttpClient(handler),
            new("http://localhost:42100/mcp"),
            null,
            TimeSpan.FromMilliseconds(100)
        );
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            session.Send("tools/list", new { }, CancellationToken.None)
        );
        Assert.Contains("response deadline expired", error.Message);
    }
}

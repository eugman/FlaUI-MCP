using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FlaUI.Mcp.Te3;

internal sealed class NativeMcpProbeSession(
    HttpClient client,
    Uri endpoint,
    string? bearerToken,
    TimeSpan? responseTimeout = null
)
{
    private const int MaxResponseChars = 262_144;
    private const int MaxLineChars = 65_536;
    private string? sessionId;
    private string? protocolVersion;
    private int nextId = 1;

    public void SetProtocolVersion(string version)
    {
        if (
            string.IsNullOrWhiteSpace(version)
            || version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '.'))
        )
        {
            throw new InvalidOperationException("Native MCP returned an invalid protocol version.");
        }

        protocolVersion = version;
    }

    public async Task<JsonElement> Send(string method, object parameters, CancellationToken token)
    {
        using var deadline = Deadline(token);
        try
        {
            var id = nextId++;
            using var message = NewMessage(
                JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        id,
                        method,
                        @params = parameters,
                    }
                )
            );
            using var response = await client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token
            );
            Check(response);
            sessionId ??= response.Headers.TryGetValues("Mcp-Session-Id", out var values)
                ? values.SingleOrDefault()
                : null;
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            return await ReadResult(stream, response.Content.Headers.ContentType?.MediaType, id, deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Native MCP response deadline expired.");
        }
    }

    public async Task NotifyInitialized(CancellationToken token)
    {
        using var deadline = Deadline(token);
        try
        {
            using var message = NewMessage("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            using var response = await client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token
            );
            Check(response);
            // A notification has no result. Do not await a stream that may remain open.
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Native MCP notification deadline expired.");
        }
    }

    private CancellationTokenSource Deadline(CancellationToken token)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(responseTimeout ?? TimeSpan.FromSeconds(10));
        return deadline;
    }

    private HttpRequestMessage NewMessage(string body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (bearerToken is { Length: > 0 })
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (sessionId != null)
        {
            message.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        }

        if (protocolVersion != null)
        {
            message.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
        }

        message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return message;
    }

    private void Check(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri is { } actual && actual != endpoint)
        {
            throw new InvalidOperationException("Native MCP response came from a different endpoint.");
        }

        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new InvalidOperationException("Native MCP redirected; endpoint must stay on explicit loopback URL.");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Native TE3 MCP access token was rejected.");
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> ReadResult(Stream stream, string? mediaType, int id, CancellationToken token)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true
        );
        return mediaType switch
        {
            "text/event-stream" => await ReadSse(reader, id, token),
            "application/json" => await ReadJson(reader, id, token),
            _ => throw new InvalidOperationException("Native MCP response has an unsupported content type."),
        };
    }

    private static async Task<JsonElement> ReadSse(StreamReader reader, int id, CancellationToken token)
    {
        var total = 0;
        var data = new StringBuilder();
        while (true)
        {
            var line =
                await ReadLine(reader, total, token)
                ?? throw new InvalidOperationException("Native MCP stream ended without a matching response.");
            total += line.Length + 1;
            if (line.Length == 0 && data.Length > 0)
            {
                if (TryResult(data.ToString(), id, out var result))
                {
                    return result;
                }

                data.Clear();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                AppendData(data, line);
            }
            // Ignore event/id/retry fields and comments; only data creates a JSON-RPC event.
        }
    }

    private static void AppendData(StringBuilder data, string line)
    {
        if (data.Length > 0)
        {
            data.Append('\n');
        }

        data.Append(line.AsSpan(5).TrimStart());
        if (data.Length > MaxResponseChars)
        {
            throw new InvalidOperationException("Native MCP event exceeded response size limit.");
        }
    }

    private static async Task<JsonElement> ReadJson(StreamReader reader, int id, CancellationToken token)
    {
        var json = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0)
            {
                break;
            }

            if (json.Length + count > MaxResponseChars)
            {
                throw new InvalidOperationException("Native MCP response exceeded size limit.");
            }

            json.Append(buffer, 0, count);
        }

        return TryResult(json.ToString(), id, out var matching)
            ? matching
            : throw new InvalidOperationException("Native MCP JSON response ID did not match its request.");
    }

    private static async Task<string?> ReadLine(StreamReader reader, int refTotal, CancellationToken token)
    {
        var line = new StringBuilder();
        var one = new char[1];
        while (true)
        {
            var count = await reader.ReadAsync(one.AsMemory(), token);
            if (count == 0)
            {
                return line.Length == 0 ? null : line.ToString();
            }

            if (one[0] == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            line.Append(one[0]);
            if (line.Length > MaxLineChars || refTotal + line.Length > MaxResponseChars)
            {
                throw new InvalidOperationException("Native MCP stream exceeded response size limit.");
            }
        }
    }

    private static bool TryResult(string body, int id, out JsonElement result)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (
            !root.TryGetProperty("id", out var responseId)
            || responseId.ValueKind != JsonValueKind.Number
            || responseId.GetInt32() != id
        )
        {
            result = default;
            return false;
        }

        if (root.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException("Native MCP error: " + error);
        }

        result = root.GetProperty("result").Clone();
        return true;
    }
}

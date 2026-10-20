using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FlaUI.Mcp.Te3;

internal sealed class NativeMcpProbeSession(HttpClient client, Uri endpoint, string? bearerToken)
{
    private string? sessionId;
    private int nextId = 1;

    public async Task<JsonElement> Send(string method, object parameters, CancellationToken token)
    {
        using var message = NewMessage(
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = nextId,
                    method,
                    @params = parameters,
                }
            )
        );
        var id = nextId++;
        using var response = await client.SendAsync(message, token);
        Check(response);
        sessionId ??= response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.SingleOrDefault() : null;
        return ReadResult(await response.Content.ReadAsStringAsync(token), id);
    }

    public async Task NotifyInitialized(CancellationToken token)
    {
        using var message = NewMessage("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        using var response = await client.SendAsync(message, token);
        Check(response);
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

        message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return message;
    }

    private static void Check(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Native TE3 MCP access token was rejected.");
        }

        response.EnsureSuccessStatusCode();
    }

    private static JsonElement ReadResult(string body, int id)
    {
        if (body.StartsWith("event:", StringComparison.Ordinal) || body.StartsWith("data:", StringComparison.Ordinal))
        {
            body =
                body.Split('\n')
                    .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                    .Select(line => line[5..].Trim())
                    .FirstOrDefault(data => data.StartsWith("{", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Native MCP stream has no JSON response.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.GetProperty("id").GetInt32() != id)
        {
            throw new InvalidOperationException("Native MCP response ID does not match its request.");
        }

        if (root.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException("Native MCP error: " + error);
        }

        return root.GetProperty("result").Clone();
    }
}

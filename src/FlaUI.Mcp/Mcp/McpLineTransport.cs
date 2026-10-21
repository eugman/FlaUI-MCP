using System.Text;
using System.Text.Json;

namespace FlaUI.Mcp;

/// <summary>JSON-RPC over newline-delimited text: one request per input line, one whole response per output line.</summary>
internal sealed class McpLineTransport(McpServer server)
{
    internal const int MaxRequestsInFlight = 32;

    public async Task RunConsoleAsync(CancellationToken cancellationToken)
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout) { AutoFlush = true };

        // Redirect stderr for logging (MCP servers should not write to stdout except JSON-RPC)
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        await RunAsync(reader, writer, cancellationToken);
    }

    public async Task RunAsync(TextReader reader, TextWriter writer, CancellationToken cancellationToken)
    {
        using var output = new ResponseWriter(writer);
        var requests = new List<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                await Dispatch(line, requests, output);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing request: {ex.Message}");
            }
        }

        await Task.WhenAll(requests);
    }

    private async Task Respond(JsonRpcRequest request, ResponseWriter output)
    {
        try
        {
            var response = await server.HandleRequestAsync(request);
            if (response != null)
            {
                await output.WriteAsync(response);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
        }
    }

    private async Task Dispatch(string line, List<Task> requests, ResponseWriter output)
    {
        var request = Parse(line);
        if (request == null)
        {
            await output.WriteAsync(Refuse(RecoverId(line), -32700, "Parse error; no action dispatched."));
            return;
        }

        // Notifications, liveness and status never dispatch input, so they answer at once and are never
        // refused for capacity: a broker reconciling a busy server must still reach them.
        if (request.Id == null || request.Method is "ping" or "flaui/operations/status")
        {
            await Respond(request, output);
            return;
        }

        requests.RemoveAll(t => t.IsCompleted);
        if (requests.Count >= MaxRequestsInFlight)
        {
            await output.WriteAsync(Refuse(request.Id, -32000, "Request capacity reached; no action dispatched."));
            return;
        }

        requests.Add(Respond(request, output));
    }

    private static JsonRpcRequest? Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonRpcRequest>(line, McpProtocol.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonElement NullId = JsonDocument.Parse("null").RootElement.Clone();

    /// <summary>A request that never ran: answer it and record it so operations/status reports notDispatched.</summary>
    private JsonRpcResponse Refuse(JsonElement? id, int code, string message)
    {
        if (id is { ValueKind: JsonValueKind.Number or JsonValueKind.String } key)
        {
            server.RecordRefusedRequest(key.GetRawText());
        }

        return new()
        {
            Id = id ?? NullId,
            Error = new JsonRpcError
            {
                Code = code,
                Message = message,
                Data = new { notDispatched = true },
            },
        };
    }

    /// <summary>The top-level id of a line that is not valid JSON-RPC, when it precedes the damage.</summary>
    internal static JsonElement? RecoverId(string line)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(line));
        try
        {
            while (reader.Read())
            {
                if (
                    reader.TokenType == JsonTokenType.PropertyName
                    && reader.CurrentDepth == 1
                    && reader.ValueTextEquals("id")
                )
                {
                    return reader.Read() ? JsonElement.ParseValue(ref reader) : null;
                }
            }
        }
        catch (JsonException)
        {
            // The damage came before an id.
        }

        return null;
    }

    /// <summary>Writes whole response lines; concurrent requests never interleave their output.</summary>
    private sealed class ResponseWriter(TextWriter writer) : IDisposable
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        public async Task WriteAsync(JsonRpcResponse response)
        {
            var line = JsonSerializer.Serialize(response, McpProtocol.JsonOptions);
            await gate.WaitAsync();
            try
            {
                await writer.WriteLineAsync(line);
            }
            finally
            {
                gate.Release();
            }
        }

        public void Dispose() => gate.Dispose();
    }
}

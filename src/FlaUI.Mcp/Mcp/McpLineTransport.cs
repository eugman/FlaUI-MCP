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
                var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, McpProtocol.JsonOptions);
                if (request == null)
                {
                    continue;
                }

                if (request.Id == null)
                {
                    await Respond(request, output);
                    continue;
                }

                requests.RemoveAll(t => t.IsCompleted);
                if (requests.Count >= MaxRequestsInFlight)
                {
                    await output.WriteAsync(CapacityError(request.Id.Value));
                    continue;
                }

                requests.Add(Respond(request, output));
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

    private static JsonRpcResponse CapacityError(JsonElement id) =>
        new()
        {
            Id = id,
            Error = new JsonRpcError { Code = -32000, Message = "Request capacity reached; no action dispatched." },
        };

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

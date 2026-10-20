using System.Collections.Concurrent;
using System.Text.Json;

namespace FlaUI.Mcp;

/// <summary>
/// MCP Server that handles JSON-RPC over stdio
/// </summary>
public class McpServer
{
    private readonly ToolRegistry _toolRegistry;
    private readonly string _serverName;
    private readonly bool _concurrentRequests;
    private readonly SemaphoreSlim serialRequests = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> cancellations = new();
    private readonly ConcurrentDictionary<string, byte> notDispatched = new();
    private readonly ConcurrentQueue<string> cancelledBeforeDispatch = new();

    public McpServer(ToolRegistry toolRegistry, string serverName = "flaui-mcp", bool concurrentRequests = false)
    {
        _toolRegistry = toolRegistry;
        _serverName = serverName;
        _concurrentRequests = concurrentRequests;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout) { AutoFlush = true };
        using var outputGate = new SemaphoreSlim(1, 1);
        var requests = new List<Task>();
        async Task Respond(JsonRpcRequest request)
        {
            try
            {
                var response = await HandleRequestAsync(request);
                if (response == null)
                {
                    return;
                }

                await outputGate.WaitAsync();
                try
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, McpProtocol.JsonOptions));
                }
                finally
                {
                    outputGate.Release();
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
            }
        }

        // Redirect stderr for logging (MCP servers should not write to stdout except JSON-RPC)
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

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
                    await Respond(request);
                }
                else
                {
                    requests.RemoveAll(t => t.IsCompleted);
                    if (requests.Count >= 32)
                    {
                        await outputGate.WaitAsync(cancellationToken);
                        try
                        {
                            var busy = new JsonRpcResponse
                            {
                                Id = request.Id,
                                Error = new JsonRpcError
                                {
                                    Code = -32000,
                                    Message = "Request capacity reached; no action dispatched.",
                                },
                            };
                            await writer.WriteLineAsync(JsonSerializer.Serialize(busy, McpProtocol.JsonOptions));
                        }
                        finally
                        {
                            outputGate.Release();
                        }

                        continue;
                    }

                    requests.Add(Respond(request));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing request: {ex.Message}");
            }
        }

        await Task.WhenAll(requests);
    }

    internal async Task<JsonRpcResponse?> HandleRequestAsync(JsonRpcRequest request)
    {
        if (request.Id == null)
        {
            CancelNotification(request);
            return null;
        }

        try
        {
            object? result = request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "ping" => new { },
                "flaui/operations/status" => RequestStatus(request),
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolCallAsync(request),
                _ => throw new Exception($"Unknown method: {request.Method}"),
            };

            if (result == null)
            {
                return null; // Notification, no response
            }

            return new JsonRpcResponse { Id = request.Id, Result = result };
        }
        catch (Exception ex)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = -32603, Message = ex.Message },
            };
        }
    }

    private McpInitializeResult HandleInitialize(JsonRpcRequest request)
    {
        return new McpInitializeResult
        {
            ProtocolVersion = RequestedVersion(request),
            Capabilities = new McpCapabilities
            {
                Tools = new ToolsCapability { ListChanged = false },
                Experimental = new Dictionary<string, object> { ["flauiOperationStatus"] = new { version = 1 } },
            },
            ServerInfo = new McpServerInfo { Name = _serverName, Version = "0.1.0" },
        };
    }

    private McpToolsListResult HandleToolsList()
    {
        return new McpToolsListResult { Tools = _toolRegistry.GetToolDefinitions() };
    }

    private async Task<McpToolResult?> HandleToolCallAsync(JsonRpcRequest request)
    {
        if (request.Params == null)
        {
            return ErrorResult("Missing params");
        }

        var callParams = JsonSerializer.Deserialize<McpToolCallParams>(
            request.Params.Value.GetRawText(),
            McpProtocol.JsonOptions
        );

        if (callParams == null)
        {
            return ErrorResult("Invalid tool call params");
        }

        var key = request.Id!.Value.GetRawText();
        using var stop = new CancellationTokenSource();
        if (!cancellations.TryAdd(key, stop))
        {
            return ErrorResult("Duplicate in-flight request ID; no action dispatched.");
        }

        notDispatched.TryRemove(key, out _);
        try
        {
            if (!_concurrentRequests)
            {
                await serialRequests.WaitAsync(stop.Token);
            }

            Task? stranded = null;
            try
            {
                var result = await _toolRegistry.ExecuteToolAsync(
                    callParams.Name,
                    callParams.Arguments,
                    stop.Token,
                    key,
                    settled => stranded = settled
                );
                return stop.IsCancellationRequested ? null : result;
            }
            finally
            {
                if (!_concurrentRequests)
                {
                    ReleaseSerialSlot(stranded);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            notDispatched[key] = 1;
            cancelledBeforeDispatch.Enqueue(key);
            while (cancelledBeforeDispatch.Count > 128 && cancelledBeforeDispatch.TryDequeue(out var old))
            {
                notDispatched.TryRemove(old, out _);
            }

            return null;
        }
        finally
        {
            cancellations.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// A cancelled tool may still be sending input. Keep tool calls serialized until it returns or its
    /// timeout expires; only then may the next queued call run.
    /// </summary>
    private void ReleaseSerialSlot(Task? stranded)
    {
        if (stranded == null)
        {
            serialRequests.Release();
            return;
        }

        _ = stranded.ContinueWith(_ => serialRequests.Release(), TaskScheduler.Default);
    }

    private object RequestStatus(JsonRpcRequest request)
    {
        var key =
            request.Params is { ValueKind: JsonValueKind.Object } parameters
            && parameters.TryGetProperty("requestKey", out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        return new
        {
            requestKey = key,
            notDispatched = key != null && notDispatched.ContainsKey(key),
            operations = _toolRegistry.Operations.Status(pending: _toolRegistry.Pending, requestKey: key),
        };
    }

    private static string RequestedVersion(JsonRpcRequest request)
    {
        if (
            request.Params is { ValueKind: JsonValueKind.Object } parameters
            && parameters.TryGetProperty("protocolVersion", out var version)
            && version.ValueKind == JsonValueKind.String
            && version.GetString() == "2025-03-26"
        )
        {
            return "2025-03-26";
        }

        return "2024-11-05";
    }

    private void CancelNotification(JsonRpcRequest request)
    {
        if (
            request.Method != "notifications/cancelled"
            || request.Params is not { ValueKind: JsonValueKind.Object } parameters
            || !parameters.TryGetProperty("requestId", out var id)
        )
        {
            return;
        }

        if (cancellations.TryGetValue(id.GetRawText(), out var stop))
        {
            try
            {
                stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Completion won the cancellation race. Never replay the request.
            }
        }
    }

    private static McpToolResult ErrorResult(string message) =>
        new()
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = message },
            },
            IsError = true,
        };
}

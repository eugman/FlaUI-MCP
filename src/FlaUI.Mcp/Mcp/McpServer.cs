using System.Text.Json;

namespace PlaywrightWindows.Mcp;

/// <summary>
/// MCP Server that handles JSON-RPC over stdio
/// </summary>
public class McpServer
{
    private readonly ToolRegistry _toolRegistry;
    private readonly IReadOnlyDictionary<string, McpTextResource>? _resources;

    public McpServer(ToolRegistry toolRegistry, IReadOnlyDictionary<string, McpTextResource>? resources = null)
    {
        _toolRegistry = toolRegistry;
        _resources = resources;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout) { AutoFlush = true };

        // Redirect stderr for logging (MCP servers should not write to stdout except JSON-RPC)
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, McpProtocol.JsonOptions);
                if (request == null) continue;

                var response = await HandleRequestAsync(request);
                if (response != null)
                {
                    var responseJson = JsonSerializer.Serialize(response, McpProtocol.JsonOptions);
                    await writer.WriteLineAsync(responseJson);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing request: {ex.Message}");
            }
        }
    }

    internal async Task<JsonRpcResponse?> HandleRequestAsync(JsonRpcRequest request)
    {
        try
        {
            object? result = request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "notifications/initialized" => null, // No response for notifications
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolCallAsync(request),
                "resources/list" when _resources != null => new { resources = _resources.Values.Select(r => new { r.Uri, r.Name, r.MimeType }) },
                "resources/templates/list" when _resources != null => new { resourceTemplates = Array.Empty<object>() },
                "resources/read" when _resources != null => ReadResource(request),
                _ => throw new Exception($"Unknown method: {request.Method}")
            };

            if (result == null) return null; // Notification, no response

            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = -32603,
                    Message = ex.Message
                }
            };
        }
    }

    private McpInitializeResult HandleInitialize(JsonRpcRequest request)
    {
        return new McpInitializeResult
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new McpCapabilities
            {
                Tools = new ToolsCapability { ListChanged = false }
                , Resources = _resources == null ? null : new { }
            },
            ServerInfo = new McpServerInfo
            {
                Name = "playwright-windows",
                Version = "0.1.0"
            }
        };
    }

    private object ReadResource(JsonRpcRequest request)
    {
        var uri = request.Params?.GetProperty("uri").GetString();
        if (uri == null || !_resources!.TryGetValue(uri, out var resource))
            throw new ArgumentException("Unknown resource URI; use resources/list. Filesystem paths are not accepted.");
        return new { contents = new[] { new { resource.Uri, resource.MimeType, resource.Text } } };
    }

    private McpToolsListResult HandleToolsList()
    {
        return new McpToolsListResult
        {
            Tools = _toolRegistry.GetToolDefinitions()
        };
    }

    private async Task<McpToolResult> HandleToolCallAsync(JsonRpcRequest request)
    {
        if (request.Params == null)
        {
            return ErrorResult("Missing params");
        }

        var callParams = JsonSerializer.Deserialize<McpToolCallParams>(
            request.Params.Value.GetRawText(), 
            McpProtocol.JsonOptions);

        if (callParams == null)
        {
            return ErrorResult("Invalid tool call params");
        }

        return await _toolRegistry.ExecuteToolAsync(callParams.Name, callParams.Arguments);
    }

    private static McpToolResult ErrorResult(string message) => new()
    {
        Content = new List<McpContent>
        {
            new() { Type = "text", Text = message }
        },
        IsError = true
    };
}

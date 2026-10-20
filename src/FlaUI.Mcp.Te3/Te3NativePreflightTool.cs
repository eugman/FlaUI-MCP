using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Te3;

/// <summary>Read-only native MCP connection and model identity preflight; no desktop attachment.</summary>
public sealed class Te3NativePreflightTool(HttpClient client) : ToolBase
{
    public const string ToolName = "te3_native_preflight";

    public static Te3NativePreflightTool Create() => new(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });

    public override string Name => ToolName;
    public override string Description =>
        "Connect once to an explicit local TE3 native MCP endpoint, list tools, and call only a declared read-only identity tool. "
        + "Requires expected model identity; returns a mismatch error and never retries or modifies a model. "
        + "tokenEnv names an environment variable containing a bearer token; token contents are never returned.";

    public override object InputSchema =>
        new
        {
            type = "object",
            properties = new
            {
                endpoint = new { type = "string", description = "Explicit http://127.0.0.1 or localhost TE3 MCP URL." },
                identityTool = new
                {
                    type = "string",
                    description = "Advertised read-only tool returning model identity.",
                },
                identityProperty = new
                {
                    type = "string",
                    description = "Dot-separated JSON property in identity tool result.",
                },
                expectedIdentity = new { type = "string", description = "Exact model identity to require." },
                tokenEnv = new
                {
                    type = "string",
                    description = "Optional environment variable name for bearer token.",
                },
            },
            required = new[] { "endpoint", "identityTool", "identityProperty", "expectedIdentity" },
            additionalProperties = false,
        };

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        try
        {
            var a = arguments ?? throw new ArgumentException("Preflight arguments are required.");
            static string Required(JsonElement a, string name) =>
                a.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? throw new ArgumentException(name + " is required.")
                    : throw new ArgumentException(name + " is required.");
            if (!Uri.TryCreate(Required(a, "endpoint"), UriKind.Absolute, out var endpoint))
            {
                throw new ArgumentException("endpoint must be an absolute URL.");
            }

            var tokenEnv =
                a.TryGetProperty("tokenEnv", out var variable) && variable.ValueKind == JsonValueKind.String
                    ? variable.GetString()
                    : null;
            string? bearerToken = null;
            if (tokenEnv != null)
            {
                if (tokenEnv.Length == 0 || tokenEnv.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
                {
                    throw new ArgumentException("tokenEnv must be an environment variable name.");
                }

                bearerToken =
                    Environment.GetEnvironmentVariable(tokenEnv) ?? throw new ArgumentException("tokenEnv is not set.");
            }

            var result = await new NativeMcpPreflight(client).Inspect(
                new(
                    endpoint,
                    Required(a, "identityTool"),
                    Required(a, "identityProperty"),
                    Required(a, "expectedIdentity"),
                    bearerToken
                )
            );
            return TextResult(JsonSerializer.Serialize(result, McpProtocol.JsonOptions));
        }
        catch (ArgumentException error)
        {
            return ErrorResult($"{ToolName}: {error.Message}");
        }
        catch (InvalidOperationException error)
        {
            return ErrorResult($"{ToolName}: {error.Message}");
        }
        catch (HttpRequestException error)
        {
            return ErrorResult($"{ToolName}: {error.Message}");
        }
        catch (TaskCanceledException error)
        {
            return ErrorResult($"{ToolName}: {error.Message}");
        }
        catch (JsonException error)
        {
            return ErrorResult($"{ToolName}: {error.Message}");
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FlaUI.Mcp.Te3;

/// <summary>Read-only probe of an already running TE3 native MCP server. Never starts TE3 or changes a model.</summary>
public sealed class NativeMcpPreflight(HttpClient client)
{
    public sealed record Request(
        Uri Endpoint,
        string IdentityTool,
        string IdentityProperty,
        string ExpectedIdentity,
        string? BearerToken = null
    );

    public sealed record Result(string ProtocolVersion, string ServerName, string ModelIdentity, string[] Tools);

    public async Task<Result> Inspect(Request request, CancellationToken token = default)
    {
        Validate(request);
        var session = new NativeMcpProbeSession(client, request.Endpoint, request.BearerToken);

        var initialization = await session.Send(
            "initialize",
            new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new { name = "flaui-te3-preflight", version = "1.0" },
            },
            token
        );
        var version =
            initialization.GetProperty("protocolVersion").GetString()
            ?? throw new InvalidOperationException("Native MCP did not report a protocol version.");
        var server =
            initialization.GetProperty("serverInfo").GetProperty("name").GetString()
            ?? throw new InvalidOperationException("Native MCP did not report server identity.");

        session.SetProtocolVersion(version);
        await session.NotifyInitialized(token);

        var definitions = (await session.Send("tools/list", new { }, token))
            .GetProperty("tools")
            .EnumerateArray()
            .ToArray();
        var tools = definitions.Select(tool => tool.GetProperty("name").GetString()!).ToArray();
        var identityTool = definitions.SingleOrDefault(tool =>
            tool.GetProperty("name").GetString() == request.IdentityTool
        );
        if (identityTool.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                "Native MCP identity tool is unavailable; reconnect after checking grants and model."
            );
        }

        if (!IsReadOnlyIdentityTool(identityTool))
        {
            throw new InvalidOperationException(
                "Native MCP identity tool is not declared read-only or on the exact TE3 workspace compatibility allowlist."
            );
        }

        var identityResult = await session.Send(
            "tools/call",
            new { name = request.IdentityTool, arguments = new { } },
            token
        );
        if (identityResult.TryGetProperty("isError", out var error) && error.GetBoolean())
        {
            throw new InvalidOperationException("Native MCP identity tool returned an error.");
        }

        var identity = Property(identityResult, request.IdentityProperty);
        if (!string.Equals(identity, request.ExpectedIdentity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Native MCP model identity does not match the expected model.");
        }

        return new(version, server, identity, tools);
    }

    // TE3 3.27.1 advertises no tool annotations. This exact, empty-argument workspace
    // identity tool is the only compatibility exception; every other tool requires readOnlyHint=true.
    private const string WorkspaceDescription =
        "Identifies this Tabular Editor instance and the semantic model it currently has open: model name, how the model is loaded (file/folder/connected), unsaved-changes state and compatibility level. Call this first to confirm which instance and model you are talking to.";

    private static bool IsReadOnlyIdentityTool(JsonElement tool)
    {
        if (tool.TryGetProperty("annotations", out var annotations))
        {
            return annotations.TryGetProperty("readOnlyHint", out var hint) && hint.ValueKind == JsonValueKind.True;
        }

        if (
            tool.GetProperty("name").GetString() != "get_workspace_info"
            || !tool.TryGetProperty("description", out var description)
            || description.GetString() != WorkspaceDescription
        )
        {
            return false;
        }

        if (
            !tool.TryGetProperty("inputSchema", out var schema)
            || !schema.TryGetProperty("type", out var type)
            || type.GetString() != "object"
        )
        {
            return false;
        }

        if (
            !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object
            || properties.EnumerateObject().Any()
        )
        {
            return false;
        }

        return schema.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.Array
            && !required.EnumerateArray().Any();
    }

    private static string Property(JsonElement result, string path)
    {
        if (TryProperty(result, path, out var value))
        {
            return value;
        }

        if (result.TryGetProperty("structuredContent", out var structured) && TryProperty(structured, path, out value))
        {
            return value;
        }

        if (result.TryGetProperty("content", out var content) && TryTextProperty(content, path, out value))
        {
            return value;
        }

        throw new InvalidOperationException("Native MCP identity response does not contain " + path);
    }

    private static bool TryTextProperty(JsonElement content, string path, out string value)
    {
        value = "";
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() != 1)
        {
            return false;
        }

        var block = content[0];
        if (
            !block.TryGetProperty("type", out var type)
            || type.GetString() != "text"
            || !block.TryGetProperty("text", out var text)
        )
        {
            return false;
        }

        using var payload = JsonDocument.Parse(text.GetString() ?? "");
        return TryProperty(payload.RootElement, path, out value);
    }

    private static bool TryProperty(JsonElement element, string path, out string value)
    {
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element))
            {
                value = "";
                return false;
            }
        }

        value = element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";
        return value.Length > 0;
    }

    private static void Validate(Request request)
    {
        if (
            request.Endpoint.Scheme != Uri.UriSchemeHttp
            || request.Endpoint.Host is not ("127.0.0.1" or "localhost" or "::1")
            || request.Endpoint.UserInfo.Length > 0
            || request.Endpoint.Fragment.Length > 0
        )
        {
            throw new ArgumentException("Native MCP endpoint must be explicit local HTTP loopback.");
        }

        if (
            string.IsNullOrWhiteSpace(request.IdentityTool)
            || string.IsNullOrWhiteSpace(request.IdentityProperty)
            || string.IsNullOrWhiteSpace(request.ExpectedIdentity)
        )
        {
            throw new ArgumentException("Identity tool, response property and expected identity are required.");
        }
    }
}

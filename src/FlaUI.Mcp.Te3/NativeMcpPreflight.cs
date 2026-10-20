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

        if (
            !identityTool.TryGetProperty("annotations", out var annotations)
            || !annotations.TryGetProperty("readOnlyHint", out var readOnly)
            || readOnly.ValueKind != JsonValueKind.True
        )
        {
            throw new InvalidOperationException("Native MCP identity tool is not declared read-only.");
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

    private static string Property(JsonElement element, string path)
    {
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element))
            {
                throw new InvalidOperationException("Native MCP identity response does not contain " + path);
            }
        }

        return element.GetString() ?? throw new InvalidOperationException("Native MCP identity is not text.");
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

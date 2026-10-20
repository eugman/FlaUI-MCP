using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FlaUI.Mcp.Te3;

/// <summary>Read-only probe of an already running TE3 native MCP server. Never starts TE3 or changes a model.</summary>
public sealed class NativeMcpPreflight(HttpClient client, TimeSpan? overallTimeout = null)
{
    /// <summary>Whole inspection budget (four requests). Stays under the 30 s tool timeout, so a slow server
    /// is reported as an expired preflight deadline rather than as an unknown, still-running tool call.</summary>
    public static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromSeconds(25);

    /// <param name="CompatibleServer">
    /// "name/version" the caller observed in serverInfo. Required, as a second factor, before the unannotated
    /// TE3 3.27.1 workspace identity tool may be called; it must equal the hard-coded allowlisted server, and the
    /// server must report that same identity. Any other server or build is refused.
    /// </param>
    public sealed record Request(
        Uri Endpoint,
        string IdentityTool,
        string IdentityProperty,
        string ExpectedIdentity,
        string? BearerToken = null,
        string? CompatibleServer = null
    );

    public sealed record Result(
        string ProtocolVersion,
        string ServerName,
        string ModelIdentity,
        string[] Tools,
        string? ServerVersion = null
    );

    public async Task<Result> Inspect(Request request, CancellationToken token = default)
    {
        Validate(request);
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(token);
        overall.CancelAfter(overallTimeout ?? DefaultOverallTimeout);
        try
        {
            return await InspectWithin(request, overall.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Native MCP preflight overall deadline expired.");
        }
    }

    private async Task<Result> InspectWithin(Request request, CancellationToken token)
    {
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
        var serverInfo = initialization.GetProperty("serverInfo");
        var server =
            serverInfo.GetProperty("name").GetString()
            ?? throw new InvalidOperationException("Native MCP did not report server identity.");
        var serverVersion =
            serverInfo.TryGetProperty("version", out var reported) && reported.ValueKind == JsonValueKind.String
                ? reported.GetString()
                : null;

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

        if (!IsDeclaredReadOnly(identityTool))
        {
            RequireWorkspaceCompatibility(identityTool, request.CompatibleServer, server, serverVersion);
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

        return new(version, server, identity, tools, serverVersion);
    }

    // TE3 3.27.1 advertises no tool annotations. This exact, empty-argument workspace
    // identity tool is the only compatibility exception; every other tool requires readOnlyHint=true.
    private const string WorkspaceServerName = "TE3";
    private const string WorkspaceServerVersion = "3.27.1";
    private const string WorkspaceServer = WorkspaceServerName + "/" + WorkspaceServerVersion;

    private const string WorkspaceDescription =
        "Identifies this Tabular Editor instance and the semantic model it currently has open: model name, how the model is loaded (file/folder/connected), unsaved-changes state and compatibility level. Call this first to confirm which instance and model you are talking to.";

    private static bool IsDeclaredReadOnly(JsonElement tool) =>
        tool.TryGetProperty("annotations", out var annotations)
        && annotations.TryGetProperty("readOnlyHint", out var hint)
        && hint.ValueKind == JsonValueKind.True;

    /// <summary>
    /// The only unannotated tool ever called: TE3 3.27.1's exact workspace tool, only when the server reports
    /// the allowlisted name and version (a missing version never matches) and the caller named that same server.
    /// </summary>
    private static void RequireWorkspaceCompatibility(
        JsonElement tool,
        string? compatibleServer,
        string server,
        string? serverVersion
    )
    {
        if (!IsKnownWorkspaceTool(tool))
        {
            throw new InvalidOperationException(
                "Native MCP identity tool is not declared read-only or on the exact TE3 workspace compatibility allowlist."
            );
        }

        if (
            !string.Equals(server, WorkspaceServerName, StringComparison.Ordinal)
            || !string.Equals(serverVersion, WorkspaceServerVersion, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                "Native MCP identity tool lacks readOnlyHint, and the workspace exception applies only to a server "
                    + $"reporting \"{WorkspaceServer}\"."
            );
        }

        if (!string.Equals(compatibleServer, WorkspaceServer, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Native MCP identity tool lacks readOnlyHint. The TE3 3.27.1 workspace exception also requires "
                    + $"compatibleServer \"{WorkspaceServer}\", as observed in serverInfo."
            );
        }
    }

    private static bool IsKnownWorkspaceTool(JsonElement tool)
    {
        if (tool.TryGetProperty("annotations", out _))
        {
            return false;
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
            || request.Endpoint.Host is not ("127.0.0.1" or "localhost" or "[::1]")
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

using System.Text.Json;

namespace PlaywrightWindows.Mcp.Core;

public sealed record ProcessIdentity(int ProcessId, long StartedTicks);

/// <summary>Validates every target before coordination; delegates make this testable without UIA.</summary>
public sealed class TargetValidator(Func<string, ProcessIdentity> window, Func<string, string> referenceOwner)
{
    public ProcessIdentity? Resolve(JsonElement? arguments)
    {
        if (arguments is not { } args) return null;
        var identities = new List<ProcessIdentity>();
        Visit(args, identities);
        if (identities.Distinct().Count() > 1) throw new ArgumentException("All targets must belong to the same process identity.");
        return identities.FirstOrDefault();
    }

    private void Visit(JsonElement args, List<ProcessIdentity> identities)
    {
        if (args.ValueKind != JsonValueKind.Object) throw new ArgumentException("Arguments must be an object.");
        string? Read(string key)
        {
            if (!args.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new ArgumentException($"Invalid {key}.");
            return value.GetString();
        }
        var handle = Read("handle");
        var reference = Read("ref");
        if (handle != null) identities.Add(window(handle));
        if (reference != null)
        {
            var owner = referenceOwner(reference);
            if (handle != null && owner != handle) throw new ArgumentException("Handle/ref ownership mismatch.");
            identities.Add(window(owner));
        }
        if (args.TryGetProperty("actions", out var actions))
        {
            if (actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() > 100) throw new ArgumentException("Batch requires at most 100 actions.");
            foreach (var action in actions.EnumerateArray()) Visit(action, identities);
        }
    }
}

/// <summary>Never starts a mutation after cancellation or a failed identity check.</summary>
public static class MutationGuard
{
    public static void Execute(Action validateIdentity, Action mutation)
    {
        OperationContext.Check();
        validateIdentity();
        OperationContext.Check();
        mutation();
    }
}

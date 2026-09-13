using System.Security.Cryptography;
using System.Text.Json;

public sealed record SlotRegistration(string Server, string Database, string Owner, string BaselinePath,
    string BaselineHash, bool Dirty = true, string? RecipeHash = null, string? PendingOperation = null);

public interface IFixtureSlotBackend
{
    Task Reset(SlotRegistration slot);
    Task Verify(SlotRegistration slot);
    Task Remove(SlotRegistration slot);
}

/// <summary>Fixed owned fixtures. Caller holds the runner lock across server work.</summary>
public sealed class FixtureSlots(string registryPath, IFixtureSlotBackend backend)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static readonly IReadOnlyList<string> Allowed = Array.AsReadOnly(new[] { "fla_te3_small", "fla_te3_docs", "fla_te3_adventureworks" });
    public static void Validate(SlotRegistration slot)
    {
        if (!Allowed.Contains(slot.Database) || string.IsNullOrWhiteSpace(slot.Server) || slot.Server.IndexOfAny(['=', ';', '\r', '\n']) >= 0 ||
            !Guid.TryParseExact(slot.Owner, "N", out _) || !Path.IsPathFullyQualified(slot.BaselinePath))
            throw new InvalidOperationException("Invalid fixed-slot identity or baseline path");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(slot.BaselinePath)));
        if (!hash.Equals(slot.BaselineHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture baseline checksum mismatch");
    }
    public SlotRegistration[] Status()
    {
        var slots = File.Exists(registryPath)
            ? JsonSerializer.Deserialize<SlotRegistration[]>(File.ReadAllText(registryPath)) ?? throw new InvalidOperationException("Invalid slot registry") : [];
        if (slots.Length > Allowed.Count || slots.Select(s => s.Database).Distinct().Count() != slots.Length ||
            slots.Any(s => !Allowed.Contains(s.Database))) throw new InvalidOperationException("Only explicitly allowlisted fixed slots are allowed");
        return slots;
    }
    private void Save(SlotRegistration slot, bool remove = false)
    {
        var entries = Status().Where(s => s.Database != slot.Database).ToList();
        if (!remove) entries.Add(slot);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(registryPath))!);
        AtomicJournal.Write(registryPath, entries, Json);
    }
    public async Task Reset(SlotRegistration requested, bool replaceBaseline = false)
    {
        Validate(requested);
        var previous = Status().SingleOrDefault(s => s.Database == requested.Database);
        if (previous != null && (previous.Server != requested.Server || previous.Owner != requested.Owner))
            throw new InvalidOperationException("Slot ownership conflict; registry cannot be rebound");
        if (!replaceBaseline && previous != null && (previous.BaselineHash != requested.BaselineHash || previous.BaselinePath != requested.BaselinePath || previous.RecipeHash != requested.RecipeHash))
            throw new InvalidOperationException("Fixture source changed; use fixture-reset to replace the owned baseline");
        if (previous?.PendingOperation == "remove") throw new InvalidOperationException("Slot removal is pending; retry fixture-remove, never recreate via reset");
        var dirty = requested with { Dirty = true, PendingOperation = null };
        Save(dirty);
        await backend.Reset(dirty);
        await backend.Verify(dirty);
        Save(dirty with { Dirty = false, PendingOperation = null });
    }
    public void MarkDirty(string database)
    {
        var slot = Status().Single(s => s.Database == database);
        if (slot.PendingOperation == "remove") throw new InvalidOperationException("Slot removal is pending");
        Save(slot with { Dirty = true, PendingOperation = null });
    }
    public async Task Remove(string database)
    {
        var slot = Status().Single(s => s.Database == database);
        Validate(slot);
        var removing = slot with { Dirty = true, PendingOperation = "remove" };
        Save(removing);
        await backend.Remove(removing);
        Save(removing, remove: true);
    }
}

public static class FixtureBaseline
{
    /// <summary>Retain immutable reset input outside prunable run artifacts.</summary>
    public static string Retain(string source, string directory)
    {
        var bytes = File.ReadAllBytes(source);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(Path.GetFullPath(directory), hash + ".bim");
        if (!File.Exists(destination))
        {
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) { stream.Write(bytes); stream.Flush(true); }
                File.Move(temporary, destination, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))) != hash)
            throw new InvalidOperationException("Retained baseline checksum mismatch");
        return destination;
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace FlaUI.Automation.Tests;
public sealed class SettingsAndFixtureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fla_settings_fixture_" + Guid.NewGuid().ToString("N"));
    public SettingsAndFixtureTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    [Fact] public void NormalizationAndRestorePreserveOriginalBytesAndAbsence()
    {
        var preferences = Path.Combine(root, "UiPreferences.json");
        File.WriteAllText(preferences, """{"Skin":"user","MainWindowState":"Normal"}""");
        var original = File.ReadAllBytes(preferences);
        var lease = new SettingsLease(root, Path.Combine(root, "backup"));
        lease.Normalize(true);
        using (var document = JsonDocument.Parse(File.ReadAllText(preferences)))
            Assert.Equal("Maximized", document.RootElement.GetProperty("MainWindowState").GetString());
        File.WriteAllText(Path.Combine(root, "RecentFiles.json"), "new");
        lease.Restore();
        Assert.Equal(original, File.ReadAllBytes(preferences));
        Assert.False(File.Exists(Path.Combine(root, "RecentFiles.json")));
    }
    [Fact] public void TamperedBackupCannotOverwritePreferences()
    {
        File.WriteAllText(Path.Combine(root, "UiPreferences.json"), "{}");
        var lease = new SettingsLease(root, Path.Combine(root, "backup"));
        File.WriteAllText(Path.Combine(root, "backup", "UiPreferences.json"), "tampered");
        Assert.ThrowsAny<Exception>(lease.Restore);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(root, "UiPreferences.json")));
    }
    [Fact] public async Task ExplicitResetCanReplaceBaselineButCannotChangeOwner()
    {
        var baseline = Path.Combine(root, "fixture.bim"); File.WriteAllText(baseline, """{"model":{"tables":[]}}""");
        var slot = new SlotRegistration("localhost", "fla_te3_small", Guid.NewGuid().ToString("N"), baseline, Hash(baseline), RecipeHash: "one");
        var backend = new FakeBackend();
        var slots = new FixtureSlots(Path.Combine(root, "slots.json"), backend);
        await slots.Reset(slot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => slots.Reset(slot with { RecipeHash = "two" }));
        await slots.Reset(slot with { RecipeHash = "two" }, replaceBaseline: true);
        Assert.Equal("two", Assert.Single(slots.Status()).RecipeHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => slots.Reset(slot with { Owner = Guid.NewGuid().ToString("N") }, replaceBaseline: true));
        Assert.Equal(2, backend.Resets);
    }
    [Fact] public async Task FailedResetRetainsOwnershipForRecovery()
    {
        var baseline = Path.Combine(root, "fixture.bim"); File.WriteAllText(baseline, "{}");
        var slot = new SlotRegistration("localhost", "fla_te3_small", Guid.NewGuid().ToString("N"), baseline, Hash(baseline));
        var slots = new FixtureSlots(Path.Combine(root, "slots.json"), new FakeBackend { Fail = true });
        await Assert.ThrowsAsync<IOException>(() => slots.Reset(slot));
        Assert.Equal(slot.Owner, Assert.Single(slots.Status()).Owner);
        Assert.True(Assert.Single(slots.Status()).Dirty);
    }
    [Fact] public void SourceBackedBaselineIsRejectedBeforeServerWork()
    {
        var baseline = Path.Combine(root, "fixture.bim");
        File.WriteAllText(baseline, """{"model":{"dataSources":[{}],"tables":[]}}""");
        var slot = new SlotRegistration("localhost", "fla_te3_small", Guid.NewGuid().ToString("N"), baseline, Hash(baseline));
        Assert.Throws<InvalidOperationException>(() => CalculatedFixtureBackend.BuildScript(slot, "reset"));
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    [Fact]
    public async Task InterruptedRemovalPersistsIntentAndCannotBeReset()
    {
        var baseline = Path.Combine(root, "fixture.bim");
        File.WriteAllText(baseline, "{}");
        var slot = new SlotRegistration("localhost", "fla_te3_small", Guid.NewGuid().ToString("N"), baseline, Hash(baseline));
        var registry = Path.Combine(root, "slots.json");
        var backend = new FakeBackend();
        var slots = new FixtureSlots(registry, backend);
        await slots.Reset(slot);
        backend.OnRemove = removing =>
        {
            // Simulate successful drop followed by a lost response/process interruption.
            Assert.Equal("remove", Assert.Single(new FixtureSlots(registry, backend).Status()).PendingOperation);
            Assert.Equal(slot.Owner, removing.Owner);
            throw new IOException("response lost after drop");
        };
        await Assert.ThrowsAsync<IOException>(() => slots.Remove(slot.Database));
        var resumed = new FixtureSlots(registry, backend);
        Assert.True(Assert.Single(resumed.Status()).Dirty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resumed.Reset(slot));
        Assert.Throws<InvalidOperationException>(() => resumed.MarkDirty(slot.Database));
        Assert.Equal(1, backend.Resets);
        backend.OnRemove = _ => { };
        await resumed.Remove(slot.Database);
        Assert.Empty(resumed.Status());
    }

    private sealed class FakeBackend : IFixtureSlotBackend
    {
        public int Resets; public bool Fail;
        public Task Reset(SlotRegistration slot) { Resets++; if (Fail) throw new IOException("injected"); return Task.CompletedTask; }
        public Task Verify(SlotRegistration slot) => Task.CompletedTask;
        public Action<SlotRegistration>? OnRemove;
        public Task Remove(SlotRegistration slot) { OnRemove?.Invoke(slot); return Task.CompletedTask; }
    }
}

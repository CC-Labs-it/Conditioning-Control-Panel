using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace CCP.Core.Tests;

public sealed class MemoryStoreWipeConcurrencyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-wipe-tests", Guid.NewGuid().ToString("N"));
    private string PathToMemory => Path.Combine(_dir, "companion", "memory.json");

    [Fact]
    public async Task Wipe_cancels_a_pending_debounce_and_later_shutdown_saves()
    {
        using var store = new MemoryStore(PathToMemory, chatMemoryEnabled: () => true);
        store.UpdateProfileSignal(MemoryStore.KeyFirstSeen, "2025-11-02");
        store.AddFact("private", MemoryFactKind.Event);
        store.SaveNow();

        store.Wipe();
        Assert.False(File.Exists(PathToMemory));
        Assert.Equal("2025-11-02", store.Profile[MemoryStore.KeyFirstSeen]);
        await Task.Delay(MemoryStore.SaveDebounce + TimeSpan.FromMilliseconds(700));
        Assert.False(File.Exists(PathToMemory));
        store.SaveNow();
        Assert.False(File.Exists(PathToMemory));
        store.Dispose();
        Assert.False(File.Exists(PathToMemory));
    }

    [Fact]
    public async Task A_save_queued_before_wipe_cannot_restore_the_old_snapshot()
    {
        using var serialized = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        using var store = new MemoryStore(PathToMemory, chatMemoryEnabled: () => { serialized.Set(); return true; });
        store.AddFact("private before wipe", MemoryFactKind.Event);
        store.SaveNow();
        serialized.Reset();
        var writeLock = typeof(MemoryStore).GetField("_writeLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        Task save;
        lock (writeLock)
        {
            save = Task.Run(() => { started.Set(); store.SaveNow(); });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            // The old implementation serializes outside the write lock. Give it time to reach
            // that point; the fixed implementation waits for the lock before it can serialize.
            serialized.Wait(TimeSpan.FromSeconds(1));
            store.Wipe();
        }
        await save.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(PathToMemory));
        Assert.Empty(store.GetFacts());
    }

    [Fact]
    public async Task Wipe_waits_for_an_in_progress_file_write()
    {
        using var store = new MemoryStore(PathToMemory, chatMemoryEnabled: () => true);
        using var started = new ManualResetEventSlim();
        store.AddFact("private", MemoryFactKind.Event);
        store.SaveNow();
        var writeLock = typeof(MemoryStore).GetField("_writeLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        Task wipe;
        lock (writeLock)
        {
            wipe = Task.Run(() => { started.Set(); store.Wipe(); });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(wipe.Wait(TimeSpan.FromMilliseconds(500)));
        }
        await wipe.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(PathToMemory));
    }

    [Fact]
    public async Task A_new_debounced_change_after_wipe_is_saved()
    {
        using var store = new MemoryStore(PathToMemory, chatMemoryEnabled: () => true);
        store.AddFact("before", MemoryFactKind.Event);
        store.Wipe();
        store.AddFact("after", MemoryFactKind.Event);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!File.Exists(PathToMemory) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(File.Exists(PathToMemory));
        using var reloaded = new MemoryStore(PathToMemory, chatMemoryEnabled: () => true);
        Assert.Equal("after", Assert.Single(reloaded.GetFacts()).Text);
    }

    [Fact]
    public void A_no_op_name_removal_does_not_recreate_a_wiped_file()
    {
        using var store = new MemoryStore(PathToMemory, chatMemoryEnabled: () => true);
        store.AddFact("before", MemoryFactKind.Event);
        store.Wipe();
        store.SetAutomaticPreferredName(null);
        store.SaveNow();
        Assert.False(File.Exists(PathToMemory));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("relationship")]
    [InlineData("usage")]
    [InlineData("fact")]
    public void A_new_mutation_after_wipe_persists(string kind)
    {
        using var store = new MemoryStore(PathToMemory, chatMemoryEnabled: () => true);
        store.AddFact("before", MemoryFactKind.Event);
        store.SaveNow();
        store.Wipe();
        switch (kind)
        {
            case "profile": store.UpdateProfileSignal(MemoryStore.KeyLevel, 42L); break;
            case "relationship": store.NoteChatTurn("bambi"); break;
            case "usage": store.NoteFeatureUsed("flash"); break;
            case "fact": store.AddFact("after", MemoryFactKind.Event); break;
        }
        store.SaveNow();
        using var reloaded = new MemoryStore(PathToMemory, chatMemoryEnabled: () => true);
        Assert.True(File.Exists(PathToMemory));
        switch (kind)
        {
            case "profile": Assert.Equal(42L, reloaded.Profile[MemoryStore.KeyLevel]); break;
            case "relationship": Assert.Equal(1, reloaded.Relationships["bambi"].ChatTurnsTotal); break;
            case "usage": Assert.Equal(1, reloaded.FeatureUsage["flash"]); break;
            case "fact": Assert.Equal("after", Assert.Single(reloaded.GetFacts()).Text); break;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }
}

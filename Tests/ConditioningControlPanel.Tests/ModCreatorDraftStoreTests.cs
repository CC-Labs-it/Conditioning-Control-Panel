using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1667: the Mod Creator draft. Every test runs in its own temp folder, never the real user data.</summary>
public sealed class ModCreatorDraftStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-modcreator-draft-tests", Guid.NewGuid().ToString("N"));
    private string Volatile => Path.Combine(_dir, "volatile");
    private string Kept => Path.Combine(_dir, "kept");
    private static readonly DateTime T0 = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    public ModCreatorDraftStoreTests() { Directory.CreateDirectory(Volatile); Directory.CreateDirectory(Kept); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private ModCreatorDraftStore NewStore() => new(Path.Combine(_dir, "store"), Volatile);

    private static ModCreatorDraft Draft(DateTime savedAt) => new()
    {
        SavedAtUtc = savedAt,
        Manifest = new ModManifest { Name = "VIP", Author = "someone",
            TextReplacements = new Dictionary<string, string> { ["good girl"] = "good pet" } },
    };

    private static string MakeFile(string root, string name, string text)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void A_draft_round_trips_keeping_stable_paths_and_copying_temp_ones()
    {
        var store = NewStore();
        var stable = MakeFile(Kept, "logo.png", "logo");
        var temp = MakeFile(Volatile, "bubble.png", "art");
        var voice = MakeFile(Volatile, "line1.mp3", "voice");
        var draft = Draft(T0);
        draft.Images["logo.png"] = stable;
        draft.Images["bubble.png"] = temp;
        draft.VoiceLines.Add(voice);
        store.Save(draft);
        File.Delete(temp);
        File.Delete(voice);

        var back = NewStore().Load()!;
        Assert.Equal("VIP", back.Manifest!.Name);
        Assert.Equal("good pet", back.Manifest.TextReplacements!["good girl"]);
        Assert.Equal(T0, back.SavedAtUtc.ToUniversalTime());
        Assert.Equal(stable, back.Images["logo.png"]);
        Assert.StartsWith(store.Root, back.Images["bubble.png"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("art", File.ReadAllText(back.Images["bubble.png"]));
        Assert.Equal("voice", File.ReadAllText(back.VoiceLines[0]));
        Assert.False(store.IsVolatile(Path.Combine(Volatile + "-sibling", "a.png")));
    }

    [Fact]
    public void A_draft_is_offered_only_when_newer_than_the_last_export_and_export_deletes_it()
    {
        var store = NewStore();
        Assert.Null(store.Offer());
        store.Save(Draft(T0));
        Assert.NotNull(store.Offer());

        store.MarkExported(T0.AddMinutes(5));
        Assert.False(File.Exists(store.DraftPath));
        Assert.Null(store.Offer());
        Assert.Equal(T0.AddMinutes(5), store.LastExportUtc());

        store.Save(Draft(T0.AddMinutes(1))); // older than the export
        Assert.Null(store.Offer());
        store.Save(Draft(T0.AddMinutes(10)));
        Assert.NotNull(store.Offer());
    }

    [Fact]
    public void Copied_files_go_on_discard_or_on_an_open_that_finds_no_draft()
    {
        var store = NewStore();
        var draft = Draft(T0);
        draft.Audio["sounds/bubble_pop.mp3"] = MakeFile(Volatile, "pop.mp3", "pop");
        store.Save(draft);
        var copy = store.Load()!.Audio["sounds/bubble_pop.mp3"];
        var keepSide = store.NewSideDirName();
        var oldSide = store.NewSideDirName();
        Directory.CreateDirectory(Path.Combine(store.Root, keepSide));
        Directory.CreateDirectory(Path.Combine(store.Root, oldSide));

        store.DropSideDirsExcept(keepSide);
        Assert.True(Directory.Exists(Path.Combine(store.Root, keepSide)));
        Assert.False(Directory.Exists(Path.Combine(store.Root, oldSide)));

        store.PruneIfNoDraft();
        Assert.True(File.Exists(copy));
        store.MarkExported(T0.AddMinutes(1));
        Assert.True(File.Exists(copy)); // the open form may still show it
        store.PruneIfNoDraft();
        Assert.False(File.Exists(copy));
        Assert.False(Directory.Exists(Path.Combine(store.Root, keepSide)));

        store.Save(Draft(T0.AddMinutes(2)));
        store.Discard();
        Assert.Null(store.Load());
    }
}

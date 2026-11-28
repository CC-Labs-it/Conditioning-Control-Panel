using System;
using System.IO;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Review round on the Mod Creator draft (#5587). Store tests run in their own temp folder, never the real
/// user data.</summary>
public sealed class ModCreatorDraftReviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-modcreator-draft-tests", Guid.NewGuid().ToString("N"));
    private string Volatile => Path.Combine(_dir, "volatile");
    private static readonly DateTime T0 = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    public ModCreatorDraftReviewTests() => Directory.CreateDirectory(Volatile);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private ModCreatorDraftStore NewStore() => new(Path.Combine(_dir, "store"), Volatile);

    private static ModCreatorDraft Draft(DateTime savedAt) => new() { SavedAtUtc = savedAt, Manifest = new ModManifest { Name = "VIP" } };

    private string MakeFile(string folder, string name, string text)
    {
        var dir = Path.Combine(Volatile, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    // ── Finding 4: copies key on the whole source path ──

    [Fact]
    public void A_second_mods_same_named_same_length_file_gets_its_own_copy()
    {
        var store = NewStore();
        var a = Draft(T0);
        a.Audio["sounds/bubble_pop.mp3"] = MakeFile("modA", "pop.mp3", "AAAA");
        a.VoiceLines.Add(MakeFile("modA", "line.mp3", "voice-A"));
        store.Save(a);
        var b = Draft(T0.AddMinutes(1));
        b.Audio["sounds/bubble_pop.mp3"] = MakeFile("modB", "pop.mp3", "BBBB");
        b.VoiceLines.Add(MakeFile("modB", "line.mp3", "voice-B"));
        store.Save(b);

        var back = store.Load()!;
        Assert.Equal("BBBB", File.ReadAllText(back.Audio["sounds/bubble_pop.mp3"]));
        Assert.Equal("voice-B", File.ReadAllText(back.VoiceLines[0]));
    }

    [Fact]
    public void Two_same_named_voice_lines_in_one_draft_keep_both_sounds()
    {
        var store = NewStore();
        var d = Draft(T0);
        d.VoiceLines.Add(MakeFile("one", "good.mp3", "first"));
        d.VoiceLines.Add(MakeFile("two", "good.mp3", "other"));
        store.Save(d);
        var back = store.Load()!;
        Assert.Equal("first", File.ReadAllText(back.VoiceLines[0]));
        Assert.Equal("other", File.ReadAllText(back.VoiceLines[1]));
    }

    [Fact]
    public void A_file_rewritten_in_place_with_the_same_length_is_copied_again()
    {
        var store = NewStore();
        var src = MakeFile("mod", "pop.mp3", "AAAA");
        File.SetLastWriteTimeUtc(src, T0);
        var d = Draft(T0);
        d.Audio["k"] = src;
        store.Save(d);

        File.WriteAllText(src, "ZZZZ");
        File.SetLastWriteTimeUtc(src, T0.AddHours(1));
        var d2 = Draft(T0.AddMinutes(1));
        d2.Audio["k"] = src;
        store.Save(d2);
        Assert.Equal("ZZZZ", File.ReadAllText(store.Load()!.Audio["k"]));
    }

    // ── Finding 5: a failed side copy never drops the previous side folder ──

    [Fact]
    public void A_side_copy_that_failed_keeps_the_previous_folder()
    {
        var store = NewStore();
        var previous = store.NewSideDirName();
        var written = store.NewSideDirName();
        Directory.CreateDirectory(Path.Combine(store.Root, previous, "resources", "emotes", "set1"));
        Directory.CreateDirectory(Path.Combine(store.Root, written, "resources"));

        var kept = store.SettleSideDir(written, complete: false, previous);
        store.DropSideDirsExcept(kept);
        Assert.Equal(previous, kept);
        Assert.True(Directory.Exists(Path.Combine(store.Root, previous, "resources", "emotes", "set1")));
        Assert.False(Directory.Exists(Path.Combine(store.Root, written)));
    }

    [Fact]
    public void A_complete_side_copy_or_one_with_no_previous_folder_keeps_the_new_one()
    {
        var store = NewStore();
        var previous = store.NewSideDirName();
        var written = store.NewSideDirName();
        Directory.CreateDirectory(Path.Combine(store.Root, previous));
        Directory.CreateDirectory(Path.Combine(store.Root, written));
        Assert.Equal(written, store.SettleSideDir(written, complete: true, previous));
        Assert.Equal(written, store.SettleSideDir(written, complete: false, null));
        Assert.Equal(written, store.SettleSideDir(written, complete: false, "side-gone"));
    }

    [Fact]
    public void Discard_keeps_the_side_folder_the_open_form_still_reads()
    {
        var store = NewStore();
        store.Save(Draft(T0));
        var reading = store.NewSideDirName();
        var stale = store.NewSideDirName();
        Directory.CreateDirectory(Path.Combine(store.Root, reading));
        Directory.CreateDirectory(Path.Combine(store.Root, stale));
        store.Discard(keepSideDir: reading);
        Assert.True(Directory.Exists(Path.Combine(store.Root, reading)));
        Assert.False(Directory.Exists(Path.Combine(store.Root, stale)));
    }

    [Fact]
    public void A_late_autosave_never_writes_over_a_newer_save()
    {
        var store = NewStore();
        var newer = Draft(T0.AddMinutes(5));
        newer.SideDir = "side-new";
        store.Save(newer);
        store.Save(Draft(T0)); // an older autosave landing late
        Assert.Equal("side-new", store.Load()!.SideDir);
    }

    // ── Findings 1 and 2: an offered draft is never overwritten; a failed restore keeps the offer ──

    [Fact]
    public void While_a_draft_is_offered_nothing_is_saved()
    {
        var gate = new ModCreatorDraftGate();
        gate.Offered();
        gate.Edited();
        Assert.False(gate.MaySave(closing: false));
        Assert.False(gate.MaySave(closing: true));

        gate.Settled();
        Assert.True(gate.MaySave(closing: false));
        Assert.True(gate.MaySave(closing: true));
    }

    [Fact]
    public void No_edit_no_save()
    {
        var gate = new ModCreatorDraftGate();
        Assert.False(gate.MaySave(closing: false));
        Assert.False(gate.MaySave(closing: true));
        gate.Edited();
        gate.Saved();
        Assert.False(gate.MaySave(closing: false));
        Assert.True(gate.MaySave(closing: true)); // the close save adds the side panels
    }

    [Fact]
    public void A_failed_restore_keeps_the_offer_and_a_half_filled_form_is_never_saved()
    {
        var putBack = new ModCreatorDraftGate();
        putBack.Offered();
        putBack.RestoreFailed(formPutBack: true);
        putBack.Edited();
        Assert.True(putBack.Pending);
        Assert.False(putBack.MaySave(closing: true));
        putBack.Settled(); // discarded later: the put-back form saves again
        Assert.True(putBack.MaySave(closing: true));

        var broken = new ModCreatorDraftGate();
        broken.Offered();
        broken.RestoreFailed(formPutBack: false);
        broken.Settled();
        broken.Edited();
        Assert.False(broken.MaySave(closing: false));
        Assert.False(broken.MaySave(closing: true));
    }

    [Fact]
    public void Only_real_value_changes_count_as_edits()
    {
        Sta(() =>
        {
            Assert.True(ModCreatorEditSignal.IsEdit(TextBoxBase.TextChangedEvent, new TextBox()));
            Assert.False(ModCreatorEditSignal.IsEdit(TextBoxBase.TextChangedEvent, new TextBox { IsReadOnly = true }));

            Assert.True(ModCreatorEditSignal.IsEdit(ToggleButton.CheckedEvent, new CheckBox()));
            Assert.True(ModCreatorEditSignal.IsEdit(ToggleButton.UncheckedEvent, new RadioButton()));
            // A ComboBox opening its dropdown and an Expander header are plain ToggleButtons.
            Assert.False(ModCreatorEditSignal.IsEdit(ToggleButton.CheckedEvent, new ToggleButton()));

            Assert.True(ModCreatorEditSignal.IsEdit(Selector.SelectionChangedEvent, new ComboBox(), 1));
            Assert.False(ModCreatorEditSignal.IsEdit(Selector.SelectionChangedEvent, new ComboBox(), 0));
            Assert.False(ModCreatorEditSignal.IsEdit(Selector.SelectionChangedEvent, new ListBox(), 1));

            Assert.True(ModCreatorEditSignal.IsEdit(ButtonBase.ClickEvent, new Button { Content = "Attach mp3" }));
            Assert.False(ModCreatorEditSignal.IsEdit(ButtonBase.ClickEvent, new RepeatButton())); // scrollbar track
            Assert.False(ModCreatorEditSignal.IsEdit(ButtonBase.ClickEvent, new Button { Content = "▶" })); // preview
            Assert.False(ModCreatorEditSignal.IsEdit(ButtonBase.ClickEvent, new Button { Content = "⏹" }));
            var export = new Button { Content = "Export list" };
            ModCreatorEditSignal.MarkPassive(export);
            Assert.False(ModCreatorEditSignal.IsEdit(ButtonBase.ClickEvent, export));
        });
    }

    private static void Sta(Action body)
    {
        Exception? err = null;
        var th = new Thread(() => { try { body(); } catch (Exception e) { err = e; } });
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        th.Join();
        if (err != null) throw new Exception(err.Message, err);
    }
}

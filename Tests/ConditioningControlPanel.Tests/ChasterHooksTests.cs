using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The hooks between CCP's events and the price table: which Lockdown tripwires count as trying
/// to leave (the emergency exit never does), which quest row a quest books, and a guard that
/// every row on the page is actually wired to something. A row with no call site is a switch
/// that does nothing, and nothing else would notice.
/// </summary>
public class ChasterHooksTests
{
    [Theory]
    [InlineData(EscapeKinds.Close, true)]
    [InlineData(EscapeKinds.Stop, true)]
    [InlineData(EscapeKinds.WrongPhrase, true)]
    [InlineData(EscapeKinds.EmergencyExit, false)]
    [InlineData(EscapeKinds.Minimize, false)]
    [InlineData(EscapeKinds.SystemKey, false)]
    [InlineData(EscapeKinds.Settings, false)]
    [InlineData(EscapeKinds.Starve, false)]
    [InlineData(null, false)]
    public void Only_really_trying_to_leave_costs_and_the_emergency_exit_never_does(string? kind, bool costs)
    {
        Assert.Equal(costs, ChasterHooks.EscapeCosts(kind));
    }

    [Fact]
    public void A_weekly_quest_books_the_weekly_row()
    {
        Assert.Equal("quest", ChasterHooks.QuestRow(QuestType.Daily));
        Assert.Equal("quest_weekly", ChasterHooks.QuestRow(QuestType.Weekly));
    }

    [Fact]
    public void A_card_pays_for_every_typo_on_it()
    {
        var on = new HashSet<string> { "typo" };

        Assert.Equal(270, TabPrices.Resolve("typo", on, 9));
        Assert.Equal(0, TabPrices.Resolve("typo", on, 0));
    }

    /// <summary>Rows that exist on the page and are not wired yet. This list only ever shrinks.
    /// The slot's melt (and the jackpot wipe) book off the server's own tape through the Back Room
    /// bridge's <c>landed</c> frame (CONTRACT 10.24). What is left has no moment to hook: Breakout
    /// (ball, wall) is not in the desktop build at all, Racing Thoughts has no crash and no padlock
    /// pickup (its no-lose contract), and the Rabbit Hole has no lock bubble.</summary>
    private static readonly string[] OwedRows = { "bubbles", "ball", "wall", "padlock", "crash" };

    [Fact]
    public void Every_row_on_the_page_is_wired_to_something_or_listed_as_owed()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        // Every product root: the service's own bookings live in CCP.Core since chaster-service-core.
        var source = string.Join("\n", SourceRoots.EnumerateProductSources("*.cs").Select(File.ReadAllText));

        // QuestRow picks its row with a ternary, so inside the hooks file the bare id counts.
        var hooks = File.ReadAllText(Path.Combine(app, "Services", "Chaster", "ChasterHooks.cs"));

        // "misses" is booked by the service itself, on the day the player comes back.
        Assert.Contains("CircesMisses.Charges(", source);

        var unwired = TabPrices.All.Select(p => p.Id)
            .Where(id => id != CircesMisses.EventId)
            // The deeper rows are the service's own verdicts on a day (TabDayEnd).
            .Where(id => !TabDayEnd.ServiceRows.Contains(id))
            .Where(id => !source.Contains("Note(\"" + id + "\"", StringComparison.Ordinal)
                      && !source.Contains("NoteSeconds(\"" + id + "\"", StringComparison.Ordinal)
                      && !source.Contains("NoteAt(\"" + id + "\"", StringComparison.Ordinal)
                      && !hooks.Contains("\"" + id + "\"", StringComparison.Ordinal))
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(OwedRows.OrderBy(id => id), unwired);
    }

    [Fact]
    public void One_failed_video_attention_check_books_the_attention_row_once()
    {
        // Bug hunt 2026-10-22 (main 7e14c2870): the video's end calls TrackAttentionCheckFailed AND
        // TrackVideoAttentionCheckFailed for one failed check, and both booked "attention", so one
        // failure put +10:00 on the lock (+12:30 with heat on) instead of +5:00.
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var video = File.ReadAllText(Path.Combine(app, "Services", "Video", "VideoService.cs"));
        Assert.Contains("TrackAttentionCheckFailed();", video);
        Assert.Contains("TrackVideoAttentionCheckFailed();", video);

        var achievements = File.ReadAllText(Path.Combine(app, "Services", "Progression", "AchievementService.cs"));
        Assert.Contains("App.Chaster?.Note(\"attention\")", Body(achievements, "public void TrackAttentionCheckFailed()"));
        Assert.DoesNotContain("App.Chaster", Body(achievements, "public void TrackVideoAttentionCheckFailed()"));

        // The Avalonia head books it in its attention provider: once.
        var ava = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "App.axaml.cs"));
        var at = ava.IndexOf("CoreProgression.TrackAttentionCheckProvider", StringComparison.Ordinal);
        Assert.True(at > 0, "attention provider not found");
        var provider = ava.Substring(at, ava.IndexOf("};", at, StringComparison.Ordinal) - at);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(provider, "ChasterNote\\(\"attention\"\\)"));
    }

    [Theory]
    [InlineData("MainWindow/MainWindow.RemoteControl.cs", "internal bool TriggerPanicFromRemote()")]
    [InlineData("MainWindow/MainWindow.xaml.cs", "private void HandlePanicKeyPress()")]
    [InlineData("MainWindow/MainWindow.xaml.cs", "private void RunEmergencyPanicTeardown()")]
    [InlineData("Services/RemoteControlService.cs", "private void StopAllRemoteEffects(RemoteEndCause cause)")]
    public void Every_panic_path_opens_the_chaster_safety_hold(string file, string signature)
    {
        // Bug hunt 2026-10-22 (main 7e14c2870): the voice panic ("red", "safe word", "make it stop")
        // runs TriggerPanicFromRemote, which never opened the hold, so prices kept booking for the ten
        // minutes the panic key would have held them. The off-thread fallback skipped it too. Port 7.2
        // fix wave: a controller's trigger_panic (StopAllRemoteEffects with force) opens it as well.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", file));
        Assert.Contains("App.Chaster?.NoteSafetyExit()", Body(source, signature));
    }

    // The text of one method: from its signature to the next member at the same indent.
    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " not found");
        var from = start + signature.Length;
        var next = new[] { "\n        private ", "\n        internal ", "\n        public ", "\n    private ", "\n    internal ", "\n    public " }
            .Select(m => source.IndexOf(m, from, StringComparison.Ordinal))
            .Where(i => i > 0)
            .DefaultIfEmpty(source.Length)
            .Min();
        return source[from..next];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    [Theory]
    [InlineData("melt", "melt")]
    [InlineData("emi3", CircesTab.JackpotEventId)]
    [InlineData("gif3same", null)]
    [InlineData("none", null)]
    [InlineData(null, null)]
    public void Only_the_melt_line_and_the_jackpot_line_touch_the_tab(string? line, string? row)
    {
        Assert.Equal(row, ChasterHooks.SlotLineRow(line));
    }
}

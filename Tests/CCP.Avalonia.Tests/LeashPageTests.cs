using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LeashPageTests
{
    private static void Run(bool punished, Action<MainShellWindow, LauncherWindow, LeashService> body) =>
        LeashGateCardTests.Run(punished, body);

    private static Button Find(Control page, string tag) =>
        page.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag as string == tag);

    [Fact]
    public void LiveLeashShowsTheOwnCardAndCutReturnsToTheEmptyState() =>
        Run(true, (shell, _, svc) =>
        {
            shell.ShowTab("leash");
            var page = shell.Named<LeashTabView>("LeashTab")!;
            Assert.False(page.ShowingEmpty);
            var words = page.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains(ConditioningControlPanel.Localization.Loc.GetF("leash_self_pending", 1), words);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.GetF("leash_self_pardons", 1), words);
            LeashGateCardTests.Click(Find(page, "leash-cut"));
            Assert.Null(svc.Snapshot.Me);
            Assert.True(page.ShowingEmpty);
        });

    [Fact]
    public void OpenPageRebindsWhenServiceIsBuiltReplacedOrDropped() => Run(false, (shell, _, original) =>
    {
        LeashHead.Service = null;
        shell.Show();
        shell.ShowTab("leash");
        Dispatcher.UIThread.RunJobs();
        var page = shell.Named<LeashTabView>("LeashTab")!;
        Assert.True(page.ShowingEmpty);
        var next = new LeashService(new Wire(), () => "u_me", cutSafety: () => { });
        next.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        LeashHead.Service = next;
        Assert.False(page.ShowingEmpty);
        original.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        Assert.False(page.ShowingEmpty);
        LeashHead.Service = original;
        next.ApplyBlock(null);
        Assert.False(page.ShowingEmpty); // Old service is no longer subscribed.
        shell.Hide();
        LeashHead.Service = null;
        Assert.True(page.ShowingEmpty); // The selected page stays current while the shell is hidden.
        shell.Show();
    });

    [Fact]
    public void OwnCardShowsTheBoundHoldToCutKey() => Run(true, (shell, _, _) =>
    {
        shell.ShowTab("leash");
        var card = shell.Named<LeashTabView>("LeashTab")!.SelfCard!;
        var hint = card.GetLogicalDescendants().OfType<TextBlock>().Single(t => (string?)t.Tag == "leash-self-hold");
        Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("leash_self_hold_hint",
            LeashHoldToCut.KeyLabel(CoreSettings.Current.PanicKey)), hint.Text);
    });

    [Fact]
    public void OwnCardShowsNewestFourteenStickerRecords() => Run(true, (shell, _, svc) =>
    {
        shell.ShowTab("leash");
        var card = shell.Named<LeashTabView>("LeashTab")!.SelfCard!;
        var me = svc.Snapshot.Me!;
        var stickers = Enumerable.Range(0, 16)
            .Select(i => new Sticker("star", new LeashPerson(i.ToString(), "Sender" + i, null), DateTimeOffset.UtcNow))
            .ToArray();
        card.Update(me with { Stickers = stickers });
        var records = card.GetLogicalDescendants().OfType<TextBlock>().Where(t => (string?)t.Tag == "leash-sticker").ToArray();
        Assert.Equal(14, records.Length);
        Assert.Contains("Sender15", records[0].Text);
        Assert.Contains("Sender2", records[13].Text);
        Assert.Contains(card.GetLogicalDescendants().OfType<TextBlock>(), t => (string?)t.Tag == "leash-shelf" && t.Text!.Contains("16"));
        card.Update(me with { Stickers = Array.Empty<Sticker>() });
        Assert.DoesNotContain(card.GetLogicalDescendants().OfType<TextBlock>(), t => (string?)t.Tag == "leash-shelf");
    });

    private sealed class Wire : ILeashApi
    {
        public readonly List<(string Op, JObject Body)> Calls = new();
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls.Add((op, (JObject)body.DeepClone()));
            return Task.FromResult<JObject?>(new JObject { ["ok"] = true });
        }
    }

    [Fact]
    public void SettingsButtonsSendCoreGrammarAndShowingAgainRebindsToTheCurrentService() =>
        Run(true, (shell, _, svc) =>
        {
            var wire = new Wire();
            var replacement = new LeashService(wire, () => "u_me", cutSafety: () => { });
            replacement.ApplyBlock(LeashHeadTests.BlockWithPunishment());
            shell.ShowTab("leash");
            var page = shell.Named<LeashTabView>("LeashTab")!;
            Assert.False(page.ShowingEmpty);
            shell.ShowTab("friends");
            svc.ApplyBlock(new JObject { ["me"] = null });
            Assert.NotNull(page.SelfCard); // Hidden page unsubscribed; it redraws on next show.
            LeashHead.Service = replacement;
            shell.ShowTab("leash");
            Assert.False(page.ShowingEmpty);
            LeashGateCardTests.Click(Find(page, "leash-level:0"));
            Assert.Equal("soft", (string?)wire.Calls.Last().Body["intensity"]);
            LeashGateCardTests.Click(Find(page, "leash-dnd:3"));
            Assert.Equal("today", (string?)wire.Calls.Last().Body["dnd"]);
            Assert.NotNull(wire.Calls.Last().Body["dnd_until"]);
            var cap = page.GetLogicalDescendants().OfType<LeashCapSlider>().Single();
            cap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageUp });
            Assert.Equal(cap.Value, (int?)wire.Calls.Last().Body["video_max"]);
            Assert.All(wire.Calls, call => Assert.Equal("settings", call.Op));
        });

    private sealed class Runner : ILeashTaskRunner
    {
        public int Starts;
        public bool IsRunning => RunningAid != null;
        public string? RunningAid { get; private set; }
        public string? RunningPid => null;
        public bool Start(Punishment p) => false;
        public void Cancel() => RunningAid = null;
        public bool StartAssignmentWatch(Assignment a) { Starts++; RunningAid = a.Aid; return true; }
        public event Action<string, int, int>? Progress { add { } remove { } }
        public event Action<string>? Completed { add { } remove { } }
    }

    [Fact]
    public void OnScreenAssignmentIsSeenAndWatchUsesTheExistingPanicRegisteredRunner() =>
        Run(true, (shell, _, _) =>
        {
            var seen = new List<string>();
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
            var block = LeashHeadTests.BlockWithPunishment();
            const string aid = "abcdef0123456789";
            block["me"]!["assignment"] = new JObject
            {
                ["aid"] = aid, ["kind"] = "video", ["size"] = 1, ["day"] = DateTime.Today.ToString("yyyy-MM-dd"),
                ["status"] = "open", ["at"] = DateTimeOffset.UtcNow.ToString("o"),
                ["watch"] = new JObject { ["kind"] = "catalogue", ["id"] = "sample", ["title"] = "Sample" },
            };
            service.ApplyBlock(block);
            Assert.NotNull(service.Snapshot.Me!.Assignment);
            LeashHead.Service = service;
            var previous = LeashTaskHost.Runner;
            var runner = new Runner();
            LeashTaskHost.Runner = runner;
            try
            {
                shell.Show(); shell.ShowTab("leash"); Dispatcher.UIThread.RunJobs();
                var page = shell.Named<LeashTabView>("LeashTab")!;
                Assert.Equal(new[] { aid }, seen);
                LeashGateCardTests.Click(Find(page, "leash-self-watch"));
                Assert.Equal(1, runner.Starts);
                Assert.Equal(aid, runner.RunningAid);
                Assert.False(Find(page, "leash-self-watch").IsEnabled);
                Assert.Equal(new[] { aid }, seen); // Repaint never duplicates the seen receipt.
                ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(s => s.Id == "leash-task").Stop(shell);
                Assert.False(runner.IsRunning);
            }
            finally { LeashTaskHost.Runner = previous; }
        });

    [Fact]
    public void VideoCapKeyboardUsesOneAndFiveMinuteStepsAndClampsAtTheBounds() =>
        Run(false, (_, _, _) =>
        {
            var cap = new LeashCapSlider("cap", 89, "test");
            var committed = new List<int>();
            cap.Committed += committed.Add;
            foreach (var key in new[] { Key.Right, Key.PageUp, Key.PageDown, Key.Left })
                cap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            Assert.Equal(new[] { 90, 90, 85, 84 }, committed);
            cap.Value = 1;
            cap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
            Assert.Equal(1, cap.Value);
        });
    [Fact]
    public void VideoCapMouseRequiresLeftButtonAndCommitsWhenReleased() => AvaloniaTestDispatcher.Run(() =>
    {
        LeashGateCardTests.EnsureHeadless();
        var cap = new LeashCapSlider("cap", 30, "test");
        var committed = new List<int>();
        cap.Committed += committed.Add;
        var window = new Window { Width = 400, Height = 160, Content = cap };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var track = cap.Children.OfType<Canvas>().Single();
            var point = track.TranslatePoint(new Point(track.Bounds.Width - 8, 11), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Assert.Equal(30, cap.Value);
            Assert.Empty(committed);
            window.MouseDown(point, MouseButton.Left);
            Assert.Equal(90, cap.Value);
            Assert.Empty(committed);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(new[] { 90 }, committed);
        }
        finally { window.Close(); }
    });

}

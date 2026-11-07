using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
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
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            LeashGateCardTests.Run(punished, body);
        });

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

    private sealed class Wire : ILeashApi
    {
        public readonly List<(string Op, JObject Body)> Calls = new();
        public string AnswerStatus = "declined";
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls.Add((op, (JObject)body.DeepClone()));
            return Task.FromResult<JObject?>(new JObject { ["ok"] = true, ["status"] = op == "answer" ? AnswerStatus : null });
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

    [Fact]
    public void IncomingOfferAppearsCanBeDeclinedAndIsSeenOnlyOnTheVisiblePage() =>
        Run(false, (shell, _, _) =>
        {
            var wire = new Wire();
            var seen = new List<string>();
            var service = new LeashService(wire, () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
            const string id = "abcdef0123456789";
            var block = new JObject { ["me"] = null, ["holding"] = new JArray(), ["events"] = new JArray(),
                ["offers"] = new JArray { new JObject { ["id"] = id, ["from"] = new JObject { ["id"] = "u_vex", ["name"] = "Vex" }, ["at"] = DateTimeOffset.UtcNow.ToString("o") } } };
            LeashHead.Service = service;
            service.ApplyBlock(block);
            Assert.Empty(seen);
            shell.Show(); shell.ShowTab("leash"); Dispatcher.UIThread.RunJobs();
            var page = shell.Named<LeashTabView>("LeashTab")!;
            Assert.False(page.ShowingEmpty);
            Assert.Equal(new[] { id }, seen);
            service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { id }, seen);
            LeashGateCardTests.Click(Find(page, "leash-offer-look"));
            var ask = Assert.IsType<Window>(shell.OfferSurfaces!.AskWindow);
            Assert.Empty(page.GetLogicalDescendants().OfType<LeashOfferCard>());
            LeashGateCardTests.Click(Find(ask, "leash-ask-no"));
            Assert.Equal("answer", wire.Calls.Last().Op);
            Assert.Equal(false, (bool?)wire.Calls.Last().Body["accept"]);
            shell.ShowTab("friends");
            block["offers"]![0]!["id"] = "0123456789abcdef";
            service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            Assert.Single(seen);
            shell.ShowTab("leash"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { id, "0123456789abcdef" }, seen);
        });

    [Fact]
    public void LookReplacesTheOpenAskAndAReorderedOfferKeepsItsReadClock() =>
        Run(false, (shell, _, _) =>
        {
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { });
            LeashHead.Service = service;
            shell.Show(); shell.ShowTab("leash"); shell.CheckLeashGate(); Dispatcher.UIThread.RunJobs();
            var at = DateTimeOffset.UtcNow.ToString("o");
            var block = new JObject { ["me"] = null, ["holding"] = new JArray(), ["events"] = new JArray(),
                ["offers"] = new JArray {
                    new JObject { ["id"] = "abcdef0123456789", ["from"] = new JObject { ["id"] = "u_a", ["name"] = "A" }, ["at"] = at },
                    new JObject { ["id"] = "0123456789abcdef", ["from"] = new JObject { ["id"] = "u_b", ["name"] = "B" }, ["at"] = at } } };
            service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            var auto = Assert.IsType<Window>(shell.OfferSurfaces!.AskWindow);
            Assert.Equal("leash-ask:u_a", auto.GetLogicalDescendants().OfType<LeashOfferCard>().Single().Tag);
            var page = shell.Named<LeashTabView>("LeashTab")!;
            var row = page.GetLogicalDescendants().OfType<Border>().Single(b => b.Tag as string == "leash-offer-row:u_b");
            LeashGateCardTests.Click(Find(row, "leash-offer-look"));
            var ask = Assert.IsType<Window>(shell.OfferSurfaces.AskWindow);
            Assert.NotSame(auto, ask); Assert.False(auto.IsVisible);
            ask.UpdateLayout();
            var card = ask.GetLogicalDescendants().OfType<LeashOfferCard>().Single();
            Assert.Equal("leash-ask:u_b", card.Tag); Assert.True(card.ReadClockRunning);
            block["offers"] = new JArray(((JArray)block["offers"]!).Reverse().Select(o => o.DeepClone()));
            service.ApplyBlock(block); shell.CheckLeashGate(); Dispatcher.UIThread.RunJobs();
            Assert.Same(ask, shell.OfferSurfaces.AskWindow); Assert.True(card.ReadClockRunning);
            for (int i = 0; i < 25; i++) card.AdvanceReadClock();
            Assert.True(Find(ask, "leash-ask-yes").IsEnabled);
        });

    [Fact]
    public void OffersQueueWhileHiddenAndOpenOverAnotherTabWhenTheShellReturns() =>
        Run(false, (shell, _, _) =>
        {
            var seen = new List<string>(); var wire = new Wire();
            var service = new LeashService(wire, () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
            LeashHead.Service = service; shell.CheckLeashGate();
            shell.Hide();
            var at = DateTimeOffset.UtcNow.ToString("o");
            var block = new JObject { ["me"] = null, ["holding"] = new JArray(), ["events"] = new JArray(),
                ["offers"] = new JArray {
                    new JObject { ["id"] = "abcdef0123456789", ["from"] = new JObject { ["id"] = "u_a", ["name"] = "A" }, ["at"] = at },
                    new JObject { ["id"] = "0123456789abcdef", ["from"] = new JObject { ["id"] = "u_b", ["name"] = "B" }, ["at"] = at } } };
            service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            Assert.Empty(seen); Assert.Null(shell.OfferSurfaces!.AskWindow);
            shell.Show(); shell.ShowTab("friends"); shell.CheckLeashGate(); Dispatcher.UIThread.RunJobs();
            var first = Assert.IsType<Window>(shell.OfferSurfaces.AskWindow);
            Assert.Equal("leash-auto-ask", first.Tag); Assert.Equal(new[] { "abcdef0123456789" }, seen);
            first.UpdateLayout();
            var card = first.GetLogicalDescendants().OfType<LeashOfferCard>().Single();
            for (int i = 0; i < 25; i++) card.AdvanceReadClock();
            wire.AnswerStatus = "on";
            LeashGateCardTests.Click(Find(first, "leash-ask-yes"));
            var snap = Assert.IsType<Window>(shell.OfferSurfaces.AskWindow);
            Assert.Equal("leash-snap", snap.Tag); Assert.Single(seen);
            LeashGateCardTests.Click(Find(snap, "leash-snap-ok"));
            var second = Assert.IsType<Window>(shell.OfferSurfaces.AskWindow);
            Assert.NotSame(first, second); Assert.Equal(new[] { "abcdef0123456789", "0123456789abcdef" }, seen);
            second.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            block["offers"] = new JArray(((JArray)block["offers"]!).Reverse().Select(o => o.DeepClone()));
            service.ApplyBlock(block); shell.CheckLeashGate(); Dispatcher.UIThread.RunJobs();
            Assert.Null(shell.OfferSurfaces.AskWindow); Assert.Equal(2, seen.Count);
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
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
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

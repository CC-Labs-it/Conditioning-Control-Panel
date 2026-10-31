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

    [Fact]
    public void LiveShelfShowsNewestFourteenWithTotalAndSenderAndDisappearsWhenEmpty() =>
        Run(true, (shell, _, svc) =>
        {
            shell.ShowTab("leash");
            var page = shell.Named<LeashTabView>("LeashTab")!;
            Assert.DoesNotContain(page.GetLogicalDescendants().OfType<Control>(), c => c.Tag as string == "leash-shelf");
            var block = LeashHeadTests.BlockWithPunishment();
            var stickers = new JArray();
            for (var i = 0; i < 16; i++) stickers.Add(new JObject
            {
                ["sticker"] = LeashGrammar.Stickers[i % 5],
                ["from"] = new JObject { ["id"] = "sender" + i, ["name"] = "Sender " + i },
                ["at"] = DateTimeOffset.UtcNow.AddMinutes(i).ToString("o"),
            });
            block["me"]!["stickers"] = stickers;
            svc.ApplyBlock(block);
            Assert.Equal(16, svc.Snapshot.Me!.Stickers.Count);
            var shelf = page.GetLogicalDescendants().OfType<StackPanel>().Single(c => c.Tag as string == "leash-shelf");
            Assert.Contains(shelf.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == ConditioningControlPanel.Localization.Loc.GetF("leash_self_shelf", 16));
            var discs = shelf.GetLogicalDescendants().OfType<Grid>().Where(c => (c.Tag as string)?.StartsWith("leash-sticker:") == true).ToArray();
            Assert.Equal(14, discs.Length);
            for (var i = 0; i < discs.Length; i++)
            {
                Assert.Equal("leash-sticker:" + LeashGrammar.Stickers[(15 - i) % 5], discs[i].Tag);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("leash_sticker_from", "Sender " + (15 - i)), ToolTip.GetTip(discs[i]));
            }
            svc.ApplyBlock(LeashHeadTests.BlockWithPunishment());
            Assert.DoesNotContain(page.GetLogicalDescendants().OfType<Control>(), c => c.Tag as string == "leash-shelf");
        });

    [Theory]
    [InlineData("good", "GOOD", "#FFFFCF6B")]
    [InlineData("star", "★", "#FFFFCF6B")]
    [InlineData("pet", "PET", "#FFB99CFF")]
    [InlineData("heart", "♥", "#FFFF5FB4")]
    [InlineData("wow", "WOW", "#FF5FFFD0")]
    [InlineData("unknown", "UNKN", "#FFB99CFF")]
    [InlineData("x", "X", "#FFB99CFF")]
    public void ShelfUsesWpfDiscPaletteWordsSizingAndTilt(string id, string word, string color) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            var me = new MyLeash(new LeashPerson("h", "Holder", null), LeashIntensity.Standard,
                DateTimeOffset.UtcNow, 1, null, LeashRemoteMode.Ask, Array.Empty<Punishment>(), null, 0,
                new[] { new Sticker(id, new LeashPerson("h", "Holder", null), DateTimeOffset.UtcNow),
                    new Sticker(id, new LeashPerson("h", "Holder", null), DateTimeOffset.UtcNow) });
            var card = new LeashSelfCard(me, () => null);
            var discs = card.GetLogicalDescendants().OfType<Grid>().Where(c => c.Tag as string == "leash-sticker:" + id).ToArray();
            Assert.Equal(2, discs.Length);
            for (var i = 0; i < discs.Length; i++)
            {
                Assert.Equal(26, discs[i].Width);
                Assert.Equal(26, discs[i].Height);
                Assert.Equal(((i * 37) % 21) - 10, Assert.IsType<global::Avalonia.Media.RotateTransform>(discs[i].RenderTransform).Angle);
                Assert.Equal(global::Avalonia.Media.Color.Parse(color), Assert.IsType<global::Avalonia.Media.SolidColorBrush>(discs[i].Children.OfType<Border>().Single().Background).Color);
                var text = discs[i].Children.OfType<TextBlock>().Single();
                Assert.Equal(word, text.Text);
                Assert.Equal(word.Length > 1 ? 26 * 0.26 : 26 * 0.5, text.FontSize);
            }
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

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
using Avalonia.Media;
using Avalonia.Controls.Shapes;
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

    [Fact]
    public void OwnCardLevelGuideOpensFromThePageAndSurvivesLevelChanges() =>
        Run(true, (shell, _, svc) =>
        {
            var wasOpen = LeashLevelHelp.Open;
            LeashLevelHelp.Open = false;
            try
            {
                shell.ShowTab("leash");
                var page = shell.Named<LeashTabView>("LeashTab")!;
                LeashLevelHelp Guide() => Assert.Single(page.GetLogicalDescendants().OfType<LeashLevelHelp>());
                var guide = Guide();
                Assert.False(guide.TableShown);
                foreach (var level in LeashLevelGuide.Levels)
                    Assert.Contains(guide.GetLogicalDescendants().OfType<TextBlock>(),
                        t => t.Text == LeashLevelGuide.Summary(level, ConditioningControlPanel.Localization.Loc.Get));
                LeashGateCardTests.Click(Find(page, "leash-guide-toggle"));
                Assert.True(guide.TableShown);
                Assert.Contains(guide.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.Tag as string == "leash-guide-limits");
                LeashGateCardTests.Click(Find(page, "leash-level:0"));
                guide = Guide();
                Assert.True(guide.TableShown);
                Assert.Contains(guide.GetLogicalDescendants().OfType<Control>(),
                    c => c.Tag as string == "leash-guide-line:soft:on");
                LeashGateCardTests.Click(Find(page, "leash-guide-toggle"));
                Assert.False(guide.TableShown);
            }
            finally { LeashLevelHelp.Open = wasOpen; }
        });

    [Fact]
    public void VisiblePageRebindsWhenServiceArrivesAndIsDropped() =>
        Run(true, (shell, _, svc) =>
        {
            LeashHead.Service = null;
            shell.Show(); shell.ShowTab("leash");
            var page = shell.Named<LeashTabView>("LeashTab")!;
            Assert.True(page.ShowingEmpty);
            LeashHead.Service = svc;
            Assert.False(page.ShowingEmpty);
            LeashHead.Service = null;
            Assert.True(page.ShowingEmpty);
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OwnCardShowsHoldHintAndLatestFourteenStickerRecords(bool hint) =>
        Run(true, (shell, _, svc) =>
        {
            shell.ShowTab("leash");
            var page = shell.Named<LeashTabView>("LeashTab")!;
            var me = svc.Snapshot.Me!;
            var ids = new[] { "good", "star", "pet", "heart", "wow", "unknown", "x" };
            page.SelfCard!.Update(me with { Stickers = Enumerable.Range(0, 16)
                .Select(i => new Sticker(ids[i % ids.Length], new LeashPerson("from-" + i, "Sender " + i, null), DateTimeOffset.UtcNow)).ToArray() });
            var words = page.GetLogicalDescendants().OfType<TextBlock>().ToArray();
            if (hint)
                Assert.Contains(words, t => t.Text == ConditioningControlPanel.Localization.Loc.GetF("leash_self_hold_hint",
                    LeashHoldToCut.KeyLabel(ConditioningControlPanel.CoreSettings.Current.PanicKey)));
            else
            {
                var wrap = Assert.Single(page.GetLogicalDescendants().OfType<WrapPanel>());
                var discs = wrap.Children.Cast<Grid>().ToArray();
                Assert.Equal(14, discs.Length);
                for (var i = 0; i < discs.Length; i++)
                {
                    var source = 15 - i;
                    var id = ids[source % ids.Length];
                    var (word, color) = id switch
                    {
                        "good" => ("GOOD", "#FFCF6B"), "star" => ("★", "#FFCF6B"),
                        "pet" => ("PET", "#B99CFF"), "heart" => ("♥", "#FF5FB4"),
                        "wow" => ("WOW", "#5FFFD0"), "unknown" => ("UNKN", "#B99CFF"),
                        _ => ("X", "#B99CFF"),
                    };
                    var disc = discs[i];
                    Assert.Equal("leash-sticker:" + id, disc.Tag);
                    Assert.Equal(26, disc.Width); Assert.Equal(26, disc.Height);
                    Assert.Equal(new Thickness(0, 0, 5, 5), disc.Margin);
                    Assert.Equal(((i * 37) % 21) - 10, Assert.IsType<RotateTransform>(disc.RenderTransform).Angle);
                    Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("leash_sticker_from", "Sender " + source), ToolTip.GetTip(disc));
                    var circle = Assert.IsType<Ellipse>(disc.Children[0]);
                    Assert.Equal(Color.Parse(color), Assert.IsAssignableFrom<ISolidColorBrush>(circle.Fill).Color);
                    var glow = Assert.IsType<DropShadowEffect>(circle.Effect);
                    Assert.Equal(Color.Parse(color), glow.Color);
                    Assert.Equal(6, glow.BlurRadius); Assert.Equal(0.35, glow.Opacity);
                    var face = Assert.IsType<TextBlock>(disc.Children[1]);
                    Assert.Equal(word, face.Text);
                    Assert.Equal(Color.Parse("#1B1026"), Assert.IsAssignableFrom<ISolidColorBrush>(face.Foreground).Color);
                    Assert.Equal(FontWeight.Bold, face.FontWeight);
                    Assert.Equal(word.Length > 1 ? 26 * 0.26 : 26 * 0.5, face.FontSize);
                }
                wrap.Measure(new Size(70, double.PositiveInfinity));
                wrap.Arrange(new Rect(0, 0, 70, wrap.DesiredSize.Height));
                Assert.True(discs[2].Bounds.Y > discs[0].Bounds.Y);
                Assert.DoesNotContain(words, t => t.Text?.Contains("Sender ") == true);

            }
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

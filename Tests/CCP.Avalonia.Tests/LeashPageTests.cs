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
using IconKind = FluentIcons.Common.Symbol;
using IconVariant = FluentIcons.Common.IconVariant;
using ConditioningControlPanel.Avalonia.Controls;
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
            LeashGateCardTests.Run(punished, (shell, launcher, svc) =>
            {
                try { body(shell, launcher, svc); }
                // Unhook the shell's pop-up queue first, so closing the overlay does not open the next waiting ask.
                finally { shell.OfferSurfaces?.Dispose(); LeashAskWindow.CloseCurrent(); }
            });
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
                    if (id is "star" or "heart")
                    {
                        var icon = Assert.IsType<IconGlyph>(disc.Children[1]);
                        Assert.Equal(id == "star" ? IconKind.Star : IconKind.Heart, icon.Kind);
                        Assert.Equal(IconVariant.Filled, icon.Variant);
                        Assert.Equal(13, icon.Size);
                        Assert.Equal(Color.Parse("#1B1026"), Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground).Color);
                        continue;
                    }
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
            var block = Block(("u_vex", "Vex", id));
            LeashHead.Service = service;
            service.ApplyBlock(block);
            Assert.Empty(seen);
            shell.Show(); shell.ShowTab("leash"); Dispatcher.UIThread.RunJobs();
            // The gate tick's pop-up bind, now rather than racing the 2 s timer: it pops this offer, Look replaces it.
            shell.OfferSurfaces!.Rebind();
            var page = shell.Named<LeashTabView>("LeashTab")!;
            Assert.False(page.ShowingEmpty);
            Assert.Equal(new[] { id }, seen);
            service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { id }, seen);
            LeashGateCardTests.Click(Find(page, "leash-offer-look"));
            var ask = LeashAskWindow.Current!;
            LeashGateCardTests.Click(Find(ask, "leash-ask-no"));
            Assert.Equal("answer", wire.Calls.Last().Op);
            Assert.Equal(false, (bool?)wire.Calls.Last().Body["accept"]);
            Assert.Null(LeashAskWindow.Current);
            shell.ShowTab("friends");
            block["offers"]![0]!["id"] = "0123456789abcdef";
            service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            Assert.Single(seen);
            shell.ShowTab("leash"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { id, "0123456789abcdef" }, seen);
        });

    private static JObject Block(params (string From, string Name, string Id)[] offers) => new()
    {
        ["me"] = null, ["holding"] = new JArray(), ["events"] = new JArray(),
        ["offers"] = new JArray(offers.Select(o => new JObject { ["id"] = o.Id,
            ["from"] = new JObject { ["id"] = o.From, ["name"] = o.Name }, ["at"] = DateTimeOffset.UtcNow.ToString("o") })),
    };

    // WPF LeashSurfaces.OnSnapshot/TryShowWaitingAsk (:104-124) and Open (:232): one ask at a time, Look replaces it.
    [Fact]
    public void ANewOfferPopsUpAsTheOneAskAndLookReplacesIt() =>
        Run(false, (shell, _, _) =>
        {
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { });
            LeashHead.Service = service;
            shell.Show(); shell.ShowTab("leash"); shell.CheckLeashGate(); Dispatcher.UIThread.RunJobs();
            var block = Block(("u_a", "A", "abcdef0123456789"), ("u_b", "B", "0123456789abcdef"));
            var (oldPlay, oldArt, oldMuted) = (ConditioningControlPanel.CoreAudio.PlayOneShotProvider,
                ConditioningControlPanel.CoreModArt.AudioOverridePathProvider, ConditioningControlPanel.CoreAudio.IsOutputSuppressedProvider);
            var cue = System.IO.Path.GetTempFileName();
            var played = new List<string>();
            try
            {
                ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => played.Add(tag);
                ConditioningControlPanel.CoreModArt.AudioOverridePathProvider = _ => cue;
                ConditioningControlPanel.CoreAudio.IsOutputSuppressedProvider = () => false;
                ConditioningControlPanel.CoreSettings.Current.MasterVolume = 50;
                service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                (ConditioningControlPanel.CoreAudio.PlayOneShotProvider, ConditioningControlPanel.CoreModArt.AudioOverridePathProvider,
                    ConditioningControlPanel.CoreAudio.IsOutputSuppressedProvider) = (oldPlay, oldArt, oldMuted);
                System.IO.File.Delete(cue);
            }
            Assert.Equal("leash-ask", Assert.Single(played)); // WPF LeashSurfaces.cs:213 LeashFx.Ask().
            var auto = LeashAskWindow.Current!;
            Assert.Equal("leash-ask:u_a", auto.Card.Tag);
            var page = shell.Named<LeashTabView>("LeashTab")!;
            var row = page.GetLogicalDescendants().OfType<Border>().Single(b => b.Tag as string == "leash-offer-row:u_b");
            LeashGateCardTests.Click(Find(row, "leash-offer-look"));
            var ask = LeashAskWindow.Current!;
            Assert.NotSame(auto, ask); Assert.False(auto.IsVisible);
            Assert.Equal("leash-ask:u_b", ask.Card.Tag);
            block["offers"] = new JArray(((JArray)block["offers"]!).Reverse().Select(o => o.DeepClone()));
            service.ApplyBlock(block); shell.CheckLeashGate(); Dispatcher.UIThread.RunJobs();
            Assert.Same(ask, LeashAskWindow.Current);
        });

    [Fact]
    public void OffersQueueWhileHiddenAndTheSnapHoldsTheNextAskUntilItCloses() =>
        Run(false, (shell, _, _) =>
        {
            var seen = new List<string>(); var wire = new Wire();
            var service = new LeashService(wire, () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
            LeashHead.Service = service; shell.CheckLeashGate();
            ConditioningControlPanel.CoreSettings.Current.LeashIntroSeenLeashed = true;
            shell.Hide();
            var block = Block(("u_a", "A", "abcdef0123456789"), ("u_b", "B", "0123456789abcdef"));
            service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            Assert.Empty(seen); Assert.Null(LeashAskWindow.Current);
            shell.Show(); shell.ShowTab("friends"); shell.CheckLeashGate(); Dispatcher.UIThread.RunJobs();
            var first = LeashAskWindow.Current!;
            Assert.Equal("leash-ask:u_a", first.Card.Tag); Assert.Equal(new[] { "abcdef0123456789" }, seen);
            wire.AnswerStatus = "on";
            LeashGateCardTests.Click(first.Card.PutItOnButton);
            Assert.False(first.IsVisible);
            Assert.Null(LeashAskWindow.Current);
            var snap = Assert.IsAssignableFrom<Window>(LeashAskWindow.Snap);
            Assert.Single(seen);
            LeashGateCardTests.Click(Find(snap, "leash-snap-ok"));
            var second = LeashAskWindow.Current!;
            Assert.Equal("leash-ask:u_b", second.Card.Tag);
            Assert.Equal(new[] { "abcdef0123456789", "0123456789abcdef" }, seen);
            second.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            Assert.Null(LeashAskWindow.Current);
            block["offers"] = new JArray(((JArray)block["offers"]!).Reverse().Select(o => o.DeepClone()));
            service.ApplyBlock(block); shell.CheckLeashGate(); Dispatcher.UIThread.RunJobs();
            Assert.Null(LeashAskWindow.Current); Assert.Equal(2, seen.Count);
        });

    private static Window? SnapWindow() => LeashAskWindow.Snap;

    [Fact]
    public void AcceptedAnswerShowsRecipientSnap() => Run(false, (shell, _, _) =>
    {
        var wire = new Wire { AnswerStatus = "on" };
        var service = new LeashService(wire, () => "u_me", cutSafety: () => { });
        LeashHead.Service = service;
        ConditioningControlPanel.CoreSettings.Current.LeashIntroSeenLeashed = true;
        shell.Show(); shell.CheckLeashGate();
        service.ApplyBlock(Block(("u_a", "A", "abcdef0123456789"))); Dispatcher.UIThread.RunJobs();
        LeashGateCardTests.Click(LeashAskWindow.Current!.Card.PutItOnButton);
        var snap = Assert.IsAssignableFrom<Window>(SnapWindow());
        Assert.Contains(snap.GetLogicalDescendants().OfType<TextBlock>(), t =>
            t.Text == ConditioningControlPanel.Localization.Loc.GetF("leash_snap_holder", "A"));
    });

    [Fact]
    public void AcceptedEventShowsHolderSnap() => Run(false, (shell, _, _) =>
    {
        var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { });
        LeashHead.Service = service;
        shell.Show(); shell.CheckLeashGate();
        var block = Block();
        block["events"] = new JArray(new JObject { ["id"] = "abcdef0123456789", ["kind"] = "answered", ["accepted"] = true,
            ["from"] = new JObject { ["id"] = "u_a", ["name"] = "A" }, ["at"] = DateTimeOffset.UtcNow.ToString("o") });
        service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
        var snap = Assert.IsAssignableFrom<Window>(SnapWindow());
        Assert.Contains(snap.GetLogicalDescendants().OfType<TextBlock>(), t =>
            t.Text == ConditioningControlPanel.Localization.Loc.GetF("leash_snap_leashed", "A"));
    });

    // WPF LeashSurfaces.OnEvent (:145-147) and NoteCut (:267-272): the Ended a cut pressed here brings back is swallowed once.
    [Fact]
    public void TheEndedEventOfASelfCutIsSwallowedOnce() =>
        Run(false, (shell, _, _) =>
        {
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { });
            LeashHead.Service = service;
            shell.Show(); shell.CheckLeashGate();
            var (oldToasts, oldSelfCut) = (global::ConditioningControlPanel.Avalonia.App.Notifications, LeashOfferSurfaces.SelfCut);
            var host = new StackPanel();
            var ended = ConditioningControlPanel.Localization.Loc.GetF("leash_evt_ended", "A");
            int Ended(string id)
            {
                var block = Block();
                block["events"] = new JArray(new JObject { ["id"] = id, ["kind"] = "ended",
                    ["from"] = new JObject { ["id"] = "u_a", ["name"] = "A" }, ["at"] = DateTimeOffset.UtcNow.ToString("o") });
                service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
                return host.GetLogicalDescendants().OfType<TextBlock>().Count(t => t.Text == ended);
            }
            try
            {
                global::ConditioningControlPanel.Avalonia.App.Notifications = new();
                global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(host);
                shell.CutLeashAsync().GetAwaiter().GetResult();
                Assert.Equal(0, Ended("abcdef0123456789"));
                Assert.Equal(1, Ended("0123456789abcdef"));
            }
            finally
            {
                foreach (var toast in host.Children.OfType<Border>()) (toast.Tag as DispatcherTimer)?.Stop();
                (global::ConditioningControlPanel.Avalonia.App.Notifications, LeashOfferSurfaces.SelfCut) = (oldToasts, oldSelfCut);
            }
        });

    [Fact]
    public void HiddenReceiptWaitsForThePanel() =>
        Run(false, (shell, _, _) =>
        {
            var seen = new List<string>();
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
            LeashHead.Service = service;
            shell.Show(); shell.CheckLeashGate(); shell.Hide();
            var block = Block();
            block["events"] = new JArray(new JObject {
                ["id"] = "abcdef0123456789", ["kind"] = "tug",
                ["from"] = new JObject { ["id"] = "u_a", ["name"] = "A" }, ["at"] = DateTimeOffset.UtcNow.ToString("o") });
            service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            Assert.Empty(seen);
            shell.Show(); shell.CheckLeashGate(); Dispatcher.UIThread.RunJobs();
            Assert.Contains("abcdef0123456789", seen);
        });

    [Fact]
    public void TugWobblesTheLauncherAndReportsSeenOnlyWhenVisibleWithMotion() =>
        Run(false, (shell, launcher, _) =>
        {
            var seen = new List<string>();
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
            LeashHead.Service = service;
            shell.Hide(); launcher.Show(); shell.CheckLeashGate();
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            ConditioningControlPanel.CoreSettings.Current.GentleVisuals = false;
            var target = Assert.IsAssignableFrom<Control>(launcher.Content);
            var prior = new ScaleTransform(0.9, 0.9);
            target.RenderTransform = prior;
            void Tug(string id)
            {
                var block = Block();
                block["events"] = new JArray(new JObject {
                    ["id"] = id, ["kind"] = "tug", ["from"] = new JObject { ["id"] = "u_a", ["name"] = "A" },
                    ["at"] = DateTimeOffset.UtcNow.ToString("o") });
                service.ApplyBlock(block); Dispatcher.UIThread.RunJobs();
            }
            Tug("abcdef0123456789");
            var group = Assert.IsType<TransformGroup>(target.RenderTransform);
            Assert.Same(prior, group.Children[0]);
            shell.OfferSurfaces!.StepTug(156);
            Assert.Equal(-9.6, Assert.IsType<TranslateTransform>(group.Children[2]).X, 5);
            Assert.Equal(-1.76, Assert.IsType<RotateTransform>(group.Children[1]).Angle, 5);
            shell.OfferSurfaces.StepTug(416);
            Assert.Equal(-0.30618, Assert.IsType<TranslateTransform>(group.Children[2]).X, 4);
            shell.OfferSurfaces.StepTug(520);
            Assert.Same(prior, target.RenderTransform);
            Assert.Contains("abcdef0123456789", seen);
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            Tug("0123456789abcdef");
            Assert.DoesNotContain("0123456789abcdef", seen);
            launcher.Hide(); shell.Show(); shell.CheckLeashGate();
            Assert.Contains("0123456789abcdef", seen);
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Reduced;
            var panelTarget = Assert.IsAssignableFrom<Control>(shell.Content);
            var panelPrior = panelTarget.RenderTransform;
            Tug("1111111111111111");
            var panelGroup = Assert.IsType<TransformGroup>(panelTarget.RenderTransform);
            shell.OfferSurfaces!.StepTug(156);
            Assert.Equal(-4.8, Assert.IsType<TranslateTransform>(panelGroup.Children[2]).X, 5);
            Assert.Contains("1111111111111111", seen);
            shell.OfferSurfaces.StepTug(520);
            Assert.Same(panelPrior, panelTarget.RenderTransform);
            shell.Hide(); launcher.Hide(); shell.CheckLeashGate();
            Tug("2222222222222222");
            Assert.DoesNotContain("2222222222222222", seen);
            shell.Show(); shell.CheckLeashGate();
            Assert.Contains("2222222222222222", seen);
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

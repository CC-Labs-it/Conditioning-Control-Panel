using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Controls/Leash/LeashSurfaces.cs OnEvent/ShowSnap/NoteCut on this head: the shell's one leash
/// binding (MainShellWindow.LeashAsks) and the snap in the ask's overlay slot.</summary>
public sealed class LeashEventsTests
{
    private sealed class Wire : ILeashApi
    {
        public string AnswerStatus = "on";
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default) =>
            Task.FromResult<JObject?>(new JObject { ["ok"] = true, ["status"] = op == "answer" ? AnswerStatus : null });
    }

    private static JObject Block(params (string From, string Name, string Id)[] offers) => new()
    {
        ["me"] = null, ["holding"] = new JArray(), ["events"] = new JArray(),
        ["offers"] = new JArray(offers.Select(o => new JObject { ["id"] = o.Id,
            ["from"] = new JObject { ["id"] = o.From, ["name"] = o.Name }, ["at"] = DateTimeOffset.UtcNow.ToString("o") })),
    };

    private static JObject Event(string id, string kind, bool? accepted = null)
    {
        var e = new JObject { ["id"] = id, ["kind"] = kind, ["from"] = new JObject { ["id"] = "u_a", ["name"] = "A" },
            ["at"] = DateTimeOffset.UtcNow.ToString("o") };
        if (accepted is bool a) e["accepted"] = a;
        var block = Block();
        block["events"] = new JArray(e);
        return block;
    }

    private static T Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().Single(c => c.Tag as string == tag);

    private static string[] Words(Control root) => root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    // WPF LeashSurfaces.ShowAsk (:205-209) and CloseOverlay (:249-254): an accept swaps the ask for the snap in the
    // one overlay slot; the next waiting ask pops up only once the snap closes.
    [Fact]
    public void AcceptingSwapsTheAskForTheSnapAndTheNextAskWaitsForIt() =>
        LeashGateCardTests.Run(false, (shell, _, _) =>
        {
            var seen = new List<string>();
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
            LeashHead.Service = service;
            CoreSettings.Current.LeashIntroSeenLeashed = true;
            shell.Show(); shell.ShowTab("friends"); shell.RebindLeashAsks(); Dispatcher.UIThread.RunJobs();
            service.ApplyBlock(Block(("u_a", "A", "abcdef0123456789"), ("u_b", "B", "0123456789abcdef"))); Dispatcher.UIThread.RunJobs();
            var first = LeashAskWindow.Current!;
            Assert.Equal("leash-ask:u_a", first.Card.Tag);
            LeashGateCardTests.Click(first.Card.PutItOnButton);
            Assert.False(first.IsVisible);
            var snap = LeashAskWindow.Current!;
            var card = Assert.IsType<LeashSnapCard>(snap.Body);
            Assert.Equal("A", card.Holder.Name);
            Assert.Contains(Loc.GetF("leash_snap_holder", "A"), Words(card));
            Assert.Equal(new[] { "abcdef0123456789" }, seen);
            LeashGateCardTests.Click(Tagged<Button>(card, "leash-snap-ok"));
            Assert.False(snap.IsVisible);
            Assert.Equal("leash-ask:u_b", LeashAskWindow.Current!.Card.Tag);
            Assert.Equal(new[] { "abcdef0123456789", "0123456789abcdef" }, seen);
        });

    // WPF LeashSurfaces.OnEvent (:133-136): the holder sees the snap, holder (me) left, the friend right; no toast.
    [Fact]
    public void AnAcceptedAnswerOpensTheHolderSnap() =>
        LeashGateCardTests.Run(false, (shell, _, _) =>
        {
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { });
            LeashHead.Service = service;
            shell.Show(); shell.RebindLeashAsks();
            service.ApplyBlock(Event("abcdef0123456789", "answered", accepted: true)); Dispatcher.UIThread.RunJobs();
            var card = Assert.IsType<LeashSnapCard>(LeashAskWindow.Current!.Body);
            Assert.Equal("A", card.Leashed.Name);
            Assert.Contains(Loc.GetF("leash_snap_leashed", "A"), Words(card));
        });

    // WPF LeashSurfaces.NoteCut (:267-272) and OnEvent (:145-147): the Ended a cut pressed here brings back is
    // swallowed once; the next one toasts.
    [Fact]
    public void TheEndedEventOfASelfCutIsSwallowedOnce() =>
        LeashGateCardTests.Run(false, (shell, _, _) =>
        {
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { });
            LeashHead.Service = service;
            shell.Show(); shell.RebindLeashAsks();
            var oldToasts = global::ConditioningControlPanel.Avalonia.App.Notifications;
            var host = new StackPanel();
            var ended = Loc.GetF("leash_evt_ended", "A");
            int Ended(string id)
            {
                service.ApplyBlock(Event(id, "ended")); Dispatcher.UIThread.RunJobs();
                return Words(host).Count(t => t == ended);
            }
            try
            {
                global::ConditioningControlPanel.Avalonia.App.Notifications = new();
                global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(host);
                shell.CutLeashAsync().GetAwaiter().GetResult();
                Assert.True(LeashHead.SelfCut);
                Assert.Equal(0, Ended("abcdef0123456789"));
                Assert.False(LeashHead.SelfCut);
                Assert.Equal(1, Ended("0123456789abcdef"));
            }
            finally
            {
                foreach (var toast in host.Children.OfType<Border>()) (toast.Tag as DispatcherTimer)?.Stop();
                global::ConditioningControlPanel.Avalonia.App.Notifications = oldToasts;
            }
        });

    // WPF LeashUiRules.EventNotice + FlushHeldNotices (:79-100): a receipt item that landed with nothing on screen
    // is reported seen only when the panel returns.
    [Fact]
    public void AReceiptThatLandedHiddenIsSeenWhenThePanelReturns() =>
        LeashGateCardTests.Run(false, (shell, _, _) =>
        {
            var seen = new List<string>();
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
            LeashHead.Service = service;
            shell.RebindLeashAsks();
            Assert.False(shell.IsVisible);
            service.ApplyBlock(Event("abcdef0123456789", "tug")); Dispatcher.UIThread.RunJobs();
            Assert.Empty(seen);
            shell.Show(); shell.RebindLeashAsks(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "abcdef0123456789" }, seen);
        });

    // WPF MainWindow.Leash.cs:442-450 and LeashFx.Tug (:89-115): the launcher wobbles and the tug counts as seen
    // there only with motion on; the prior transform comes back after 520 ms.
    [Fact]
    public void ATugWobblesTheLauncherOnlyWithMotionOn() =>
        LeashGateCardTests.Run(false, (shell, launcher, _) =>
        {
            var seen = new List<string>();
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
            LeashHead.Service = service;
            var s = CoreSettings.Current;
            (s.MotionLevel, s.GentleVisuals) = (MotionLevel.Full, false);
            launcher.Show(); shell.RebindLeashAsks();
            var target = Assert.IsAssignableFrom<Control>(launcher.Content);
            var prior = new ScaleTransform(0.9, 0.9);
            target.RenderTransform = prior;
            service.ApplyBlock(Event("abcdef0123456789", "tug")); Dispatcher.UIThread.RunJobs();
            Assert.Contains("abcdef0123456789", seen);
            var group = Assert.IsType<TransformGroup>(target.RenderTransform);
            Assert.Same(prior, group.Children[0]);
            shell.StepLeashTug(156);
            Assert.Equal(-9.6, Assert.IsType<TranslateTransform>(group.Children[2]).X, 5);
            Assert.Equal(-1.76, Assert.IsType<RotateTransform>(group.Children[1]).Angle, 5);
            shell.StepLeashTug(520);
            Assert.Same(prior, target.RenderTransform);
            Assert.False(shell.LeashTugRunning);
            s.MotionLevel = MotionLevel.Off;
            service.ApplyBlock(Event("0123456789abcdef", "tug")); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("0123456789abcdef", seen);
            Assert.Same(prior, target.RenderTransform);
        });

    // WPF LeashSurfaces.cs:213 (LeashFx.Ask) and LeashSnapCard.Play (LeashFx.Snap).
    [Fact]
    public void TheAskAndTheSnapPlayTheirCues() =>
        LeashGateCardTests.Run(false, (shell, _, _) =>
        {
            var service = new LeashService(new Wire(), () => "u_me", cutSafety: () => { });
            LeashHead.Service = service;
            CoreSettings.Current.LeashIntroSeenLeashed = true;
            CoreSettings.Current.MasterVolume = 50;
            var (oldPlay, oldArt, oldMuted) = (CoreAudio.PlayOneShotProvider, CoreModArt.AudioOverridePathProvider, CoreAudio.IsOutputSuppressedProvider);
            var cue = System.IO.Path.GetTempFileName();
            var played = new List<string>();
            try
            {
                CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => played.Add(tag);
                CoreModArt.AudioOverridePathProvider = _ => cue;
                CoreAudio.IsOutputSuppressedProvider = () => false;
                Thread.Sleep(LeashSfxRules.MinGap + TimeSpan.FromMilliseconds(20));   // clear an earlier test's cue gap
                shell.Show(); shell.RebindLeashAsks();
                service.ApplyBlock(Block(("u_a", "A", "abcdef0123456789"))); Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "leash-ask" }, played);
                Thread.Sleep(LeashSfxRules.MinGap + TimeSpan.FromMilliseconds(20));
                LeashGateCardTests.Click(LeashAskWindow.Current!.Card.PutItOnButton);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "leash-ask", "leash-snap" }, played);
            }
            finally
            {
                (CoreAudio.PlayOneShotProvider, CoreModArt.AudioOverridePathProvider, CoreAudio.IsOutputSuppressedProvider) = (oldPlay, oldArt, oldMuted);
                System.IO.File.Delete(cue);
            }
        });

    // WPF LeashSnapCard.Play (:116-146): chain on the Thud curve, title after 300 ms, 22 sparks where the chain lands;
    // Motion Off plays nothing; Gentle visuals hold the title's rise to 300 ms.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSnapPlaysOnTheSharedBeatAndMotionOffShowsItStill(bool gentle) =>
        LeashGateCardTests.Run(false, (shell, _, _) =>
        {
            var s = CoreSettings.Current;
            (s.MotionLevel, s.GentleVisuals) = (MotionLevel.Full, gentle);
            var w = LeashAskWindow.OpenSnap(new LeashPerson("u_a", "A", null), new LeashPerson("me", "Me", null), shell)!;
            var card = w.SnapCard!;
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.MotionActive);
            var title = Tagged<TextBlock>(card, "leash-snap-title");
            card.Advance(100);
            Assert.Equal(0, title.Opacity);
            card.Advance(400);
            Assert.True(title.Opacity > 0);
            Assert.Equal(!gentle, title.Opacity == 1);
            var sparks = Tagged<Canvas>(card, "leash-snap-sparks");
            Assert.NotEmpty(sparks.Children);   // 22 at Full, 11 at Reduced (Gentle or OS animations off)
            card.Advance(2000);
            Assert.Empty(sparks.Children);
            Assert.Equal(1, title.Opacity);
            card.Stop();
            LeashAskWindow.CloseCurrent();
            s.MotionLevel = MotionLevel.Off;
            var still = LeashAskWindow.OpenSnap(new LeashPerson("u_a", "A", null), new LeashPerson("me", "Me", null), shell)!;
            Dispatcher.UIThread.RunJobs();
            Assert.False(still.SnapCard!.MotionActive);
            Assert.Equal(1, Tagged<TextBlock>(still.SnapCard, "leash-snap-title").Opacity);
        });
}

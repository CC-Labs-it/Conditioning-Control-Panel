using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using Runner = ConditioningControlPanel.Controls.Leash.ILeashTaskRunner;

namespace CCP.Avalonia.Tests;

public sealed class LeashCardsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly LeashPerson Person = new("friend", "Friend", null);
    private static MyLeash Me(Assignment? task = null) => new(Person, LeashIntensity.Standard, Now, 2, null, LeashRemoteMode.Ask,
        Array.Empty<Punishment>(), task, 1, new[] { new Sticker("star", Person, Now) });
    private static Assignment TaskVideo => new("a1", AssignKind.Video, 1, new("ht", "123", null), DateTime.Today.ToString("yyyyMMdd"), AssignStatus.Open, Now);
    private static HeldLeash Held(LeashIntensity level = LeashIntensity.Standard, bool linked = false) => new(Person, true, level, Now, 2, null,
        new("20261023", 30, 2, 3, 5, linked, null, linked ? 900 : null, false, Now),
        new[] { new WeekDay("20261023", WeekMark.Today) }, Array.Empty<Punishment>(), null, 0);
    private sealed class Service : ILeashService
    {
        public bool Available => true;
        public LeashSnapshot Snapshot { get; private set; } = LeashSnapshot.Empty;
        public readonly List<string> Calls = new(), Seen = new();
        public LeashSendStatus Reply = LeashSendStatus.Sent;
        public bool ReleaseOk = true;
        public LeashWatch? Watch;
        public bool ReceiptsSupported { get; set; }
        public event Action<LeashSnapshot>? SnapshotChanged;
        public event Action? ReceiptsChanged;
        public event Action<LeashEvent>? EventArrived { add { } remove { } }
        public Punishment? GateDue => null;
        public void Push(MyLeash? me, params HeldLeash[] held) { Snapshot = new(me, held, Array.Empty<LeashOffer>()); SnapshotChanged?.Invoke(Snapshot); }
        public void Receipts() => ReceiptsChanged?.Invoke();
        public void NoteShown(string? id) { if (id != null) Seen.Add(id); }
        public IReadOnlyList<LeashSentItem> SentTo(string id) => new[] { new LeashSentItem("l1", LeashItemKind.Tug, id, null, LeashStep.Seen, Now, Now) };
        private Task<LeashSendResult> Send(string call, LeashWatch? watch = null) { Calls.Add(call); Watch = watch; return System.Threading.Tasks.Task.FromResult(new LeashSendResult(Reply)); }
        public Task<LeashSendResult> OfferAsync(string id) => Send("offer");
        public Task<LeashSendResult> AssignAsync(string id, AssignKind k, int size, LeashWatch? watch = null) => Send($"assign:{k}:{size}", watch);
        public Task<LeashSendResult> PunishAsync(string id, PunishKind k, int size, LeashWatch? watch = null) => Send($"punish:{k}:{size}", watch);
        public Task<LeashSendResult> RewardAsync(string id, RewardKind k, string? token = null, int? size = null) => Send($"reward:{k}:{token}:{size}");
        public Task<LeashSendResult> TugAsync(string id) => Send("tug");
        public Task<bool> ReleaseAsync(string id) { Calls.Add("release"); return System.Threading.Tasks.Task.FromResult(ReleaseOk); }
        public Task CutAsync() { Calls.Add("cut"); Push(null); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetIntensityAsync(LeashIntensity i) { Calls.Add("level:" + i); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetDndAsync(LeashDnd d) { Calls.Add("dnd:" + d); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetVideoMaxAsync(int n) { Calls.Add("cap:" + n); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetRemoteModeAsync(LeashRemoteMode m) => System.Threading.Tasks.Task.CompletedTask;
        public Task CompleteAsync(string id) => System.Threading.Tasks.Task.CompletedTask;
        public Task<bool> PardonAsync(string id) => System.Threading.Tasks.Task.FromResult(false);
        public Task<LeashAnswerResult> AnswerAsync(string id, bool yes, LeashIntensity i) => System.Threading.Tasks.Task.FromResult(LeashAnswerResult.Done);
    }
    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null) AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        body();
    });
    private static T Find<T>(Control root, string tag) where T : Control => root.GetLogicalDescendants().OfType<T>().Single(c => c.Tag as string == tag);
    private static void Click(Control root, string tag)
    {
        var b = Find<Button>(root, tag);
        Assert.True(b.IsEffectivelyEnabled);
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
    [Theory]
    [InlineData(true, "leash-cut", "leash_release_done")]
    [InlineData(false, "leash-denied", "leash_release_failed")]
    public void ReleaseFeedbackMatchesReply(bool ok, string cue, string message) => Run(() =>
    {
        var audio = ConditioningControlPanel.CoreAudio.PlayOneShotProvider;
        var suppressed = ConditioningControlPanel.CoreAudio.IsOutputSuppressedProvider;
        var art = ConditioningControlPanel.CoreModArt.AudioOverridePathProvider;
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var volume = settings.MasterVolume;
        var notifications = global::ConditioningControlPanel.Avalonia.App.Notifications;
        var hostField = notifications.GetType().GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var oldHost = hostField.GetValue(notifications);
        var host = new StackPanel();
        var path = System.IO.Path.GetTempFileName();
        var sounds = new List<string>();
        try
        {
            notifications.AttachHost(host);
            settings.MasterVolume = 100;
            ConditioningControlPanel.CoreAudio.IsOutputSuppressedProvider = () => false;
            ConditioningControlPanel.CoreModArt.AudioOverridePathProvider = _ => path;
            ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => sounds.Add(tag);
            System.Threading.Thread.Sleep(200);
            var svc = new Service { ReleaseOk = ok };
            var card = new LeashHolderCard(Held(), () => svc);
            var release = (MenuItem)Find<Button>(card, "leash-holder-more").ContextMenu!.Items[1]!;
            release.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            LeashCutConfirmWindow.Current!.YesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { cue }, sounds);
            Assert.Contains(host.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.GetF(message, Person.Name));
        }
        finally
        {
            hostField.SetValue(notifications, oldHost);
            settings.MasterVolume = volume;
            ConditioningControlPanel.CoreAudio.PlayOneShotProvider = audio;
            ConditioningControlPanel.CoreAudio.IsOutputSuppressedProvider = suppressed;
            ConditioningControlPanel.CoreModArt.AudioOverridePathProvider = art;
            System.IO.File.Delete(path);
        }
    });
    [Fact]
    public void ReplaySoundUsesWpfSnapCue() => Run(() =>
    {
        var (audio, suppressed, art) = (ConditioningControlPanel.CoreAudio.PlayOneShotProvider,
            ConditioningControlPanel.CoreAudio.IsOutputSuppressedProvider, ConditioningControlPanel.CoreModArt.AudioOverridePathProvider);
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var volume = settings.MasterVolume;
        var path = System.IO.Path.GetTempFileName();
        string? cue = null;
        try
        {
            settings.MasterVolume = 100;
            ConditioningControlPanel.CoreAudio.IsOutputSuppressedProvider = () => false;
            ConditioningControlPanel.CoreModArt.AudioOverridePathProvider = name => { cue = name; return path; };
            var sounds = new List<string>();
            ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (_, gain, tag, _, _) => { Assert.Equal(0.28f, gain); sounds.Add(tag); };
            System.Threading.Thread.Sleep(200);
            LeashSound.Snap();
            Assert.Equal("chaos/collar_save.mp3", cue);
            Assert.Equal(new[] { "leash-snap" }, sounds);
        }
        finally
        {
            settings.MasterVolume = volume;
            ConditioningControlPanel.CoreAudio.PlayOneShotProvider = audio;
            ConditioningControlPanel.CoreAudio.IsOutputSuppressedProvider = suppressed;
            ConditioningControlPanel.CoreModArt.AudioOverridePathProvider = art;
            System.IO.File.Delete(path);
            System.Threading.Thread.Sleep(200); // Let the shared sound gap expire before the next test.
        }
    });

    [Fact]
    public void HolderMenuStaysInHeaderAcrossSheetsAndHasReplayAndTimedHelp() => Run(() =>
    {
        var card = new LeashHolderCard(Held(), () => null);
        Click(card, "leash-btn:assign");
        var header = Find<Grid>(card, "leash-holder-header");
        var more = Find<Button>(header, "leash-holder-more");
        Assert.NotNull(Find<StackPanel>(card, "leash-sheet:assign"));
        var menu = more.ContextMenu!;
        Assert.Equal(2, menu.Items.Count);
        var replay = Assert.IsType<MenuItem>(menu.Items[0]);
        Assert.Equal(Loc.Get("leash_menu_replay"), replay.Header);
        var release = Assert.IsType<MenuItem>(menu.Items[1]);
        Assert.Equal(250, ToolTip.GetShowDelay(release));
        Assert.Equal(-1, ToolTip.GetBetweenShowDelay(release));
        Assert.Equal(TimeSpan.FromSeconds(60), card.ReleaseHelpTimer!.Interval);
        Assert.IsType<LeashHelpSlip>(ToolTip.GetTip(release));
    });

    [Fact]
    public void HolderButtonOpensMenuAndReplayShowsCurrentFriend() => Run(() =>
    {
        var card = new LeashHolderCard(Held(), () => null);
        var host = new Window { Content = card };
        try
        {
            host.Show();
            Click(card, "leash-btn:assign");
            card.Update(Held() with { Who = Person with { Name = "Updated friend" } });
            var more = Find<Button>(card, "leash-holder-more");
            Click(card, "leash-holder-more");
            var menu = more.ContextMenu!;
            Assert.True(menu.IsOpen);
            ((MenuItem)menu.Items[0]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var snap = Assert.IsType<Window>(card.SnapWindow);
            Assert.Equal(Loc.GetF("leash_snap_leashed", "Updated friend"), Find<TextBlock>(snap, "leash-snap-leashed").Text);
            Click(snap, "leash-snap-ok");
            Assert.Null(card.SnapWindow);
        }
        finally { card.SnapWindow?.Close(); host.Close(); }
    });

    [Theory]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Off, 0)]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Reduced, 11)]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Full, 22)]
    public void SnapMotionShowsChainImpactAndSparksOrStaysStill(ConditioningControlPanel.Models.MotionLevel motion, int count) => Run(() =>
    {
        var card = new LeashSnapCard(new("me", "Holder", null), Person, motion);
        try
        {
            var chain = Find<Canvas>(card, "leash-chain");
            var scale = Assert.IsType<global::Avalonia.Media.ScaleTransform>(chain.RenderTransform);
            var title = Find<TextBlock>(card, "leash-snap-title");
            var sparks = Find<Canvas>(card, "leash-snap-sparks");
            var face = Find<Grid>(card, "leash-snap-avatar:friend");
            var impact = Assert.IsType<global::Avalonia.Media.ScaleTransform>(face.RenderTransform);
            Assert.Equal(18, chain.Children.Count);
            card.Play();
            Assert.Equal(motion == ConditioningControlPanel.Models.MotionLevel.Off ? 1 : 0, scale.ScaleX);
            Assert.Equal(motion == ConditioningControlPanel.Models.MotionLevel.Off ? 1 : 0, title.Opacity);
            card.Advance(170);
            if (motion != ConditioningControlPanel.Models.MotionLevel.Off)
                Assert.True(Math.Abs(scale.ScaleX) > .001 && Math.Abs(scale.ScaleX - 1) > .001);
            card.Advance(340);
            Assert.Equal(1, scale.ScaleX, 5);
            Assert.Equal(1, title.Opacity);
            Assert.Equal(count, sparks.Children.Count);
            Assert.Equal(1 + .18 * (count / 22.0), impact.ScaleX, 5);
            card.Advance(1200);
            Assert.Empty(sparks.Children);
            Assert.Equal(1, impact.ScaleX, 5);
        }
        finally { card.Stop(); System.Threading.Thread.Sleep(200); }
    });

    [Fact]
    public void SnapImpactSettlesAfterLanding() => Run(() =>
    {
        var card = new LeashSnapCard(new("me", "Holder", null), Person, ConditioningControlPanel.Models.MotionLevel.Full);
        var face = Find<Grid>(card, "leash-snap-avatar:friend");
        var scale = Assert.IsType<global::Avalonia.Media.ScaleTransform>(face.RenderTransform);
        card.Advance(340);
        Assert.Equal(1.18, scale.ScaleX, 5);
        card.Advance(680);
        Assert.Equal(1, scale.ScaleX, 5);
    });

    [Fact]
    public void SnapSparksAppearAndExpire() => Run(() =>
    {
        var card = new LeashSnapCard(new("me", "Holder", null), Person, ConditioningControlPanel.Models.MotionLevel.Full);
        var sparks = Find<Canvas>(card, "leash-snap-sparks");
        card.Advance(340);
        Assert.Equal(22, sparks.Children.Count);
        card.Advance(1200);
        Assert.Empty(sparks.Children);
    });

    [Fact]
    public void SnapTagSwingRespectsPerformanceMode() => Run(() =>
    {
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var (mode, auto, motion, gentle) = (settings.PerformanceMode, settings.AutoPerformanceMode, settings.MotionLevel, settings.GentleVisuals);
        var (probe, stamp) = (UiMotionPreference.OsProbe, UiMotionPreference.CacheStamp);
        var cachedField = typeof(UiMotionPreference).GetField("_cached", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var cached = cachedField.GetValue(null);
        try
        {
            UiMotionPreference.OsProbe = () => true; UiMotionPreference.CacheStamp = long.MinValue;
            settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full; settings.GentleVisuals = false; settings.AutoPerformanceMode = false;
            settings.PerformanceMode = true;
            var still = new LeashSnapCard(new("me", "Holder", null), Person, settings.MotionLevel);
            still.Advance(400);
            Assert.Equal(0, Assert.IsType<global::Avalonia.Media.RotateTransform>(Find<Viewbox>(still, "leash-tag").RenderTransform).Angle);
            settings.PerformanceMode = false;
            var moving = new LeashSnapCard(new("me", "Holder", null), Person, settings.MotionLevel);
            moving.Advance(400);
            Assert.NotEqual(0, Assert.IsType<global::Avalonia.Media.RotateTransform>(Find<Viewbox>(moving, "leash-tag").RenderTransform).Angle);
        }
        finally
        {
            (settings.PerformanceMode, settings.AutoPerformanceMode, settings.MotionLevel, settings.GentleVisuals) = (mode, auto, motion, gentle);
            cachedField.SetValue(null, cached);
            (UiMotionPreference.OsProbe, UiMotionPreference.CacheStamp) = (probe, stamp);
        }
    });

    [Fact]
    public void SnapPhotoUsesProxyAndKeepsInitialsOnFailure() => Run(() =>
    {
        var old = LeashSnapCard.FetchPhoto;
        string? requested = null;
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(TestFile())!, "../.."));
        var image = System.IO.File.ReadAllBytes(System.IO.Path.Combine(root, "packaging/linux/share/icons/hicolor/128x128/apps/io.github.CodeBambi.ConditioningControlPanel.png"));
        try
        {
            LeashSnapCard.FetchPhoto = url => { requested = url; return Task.FromResult(image); };
            var card = new LeashSnapCard(new("me", "Holder", null), Person with { AvatarUrl = "/avatar.png" }, ConditioningControlPanel.Models.MotionLevel.Off);
            var host = Find<Grid>(card, "leash-snap-avatar:friend");
            Assert.Equal("https://codebambi-proxy.vercel.app/avatar.png", requested);
            var disc = Assert.IsType<Border>(host.Children[0]);
            Assert.IsType<global::Avalonia.Media.ImageBrush>(disc.Background);
            Assert.Null(disc.Child);
            LeashSnapCard.FetchPhoto = _ => Task.FromException<byte[]>(new Exception());
            var fallbackCard = new LeashSnapCard(new("me", "Holder", null), Person with { AvatarUrl = "https://example.test/avatar" }, ConditioningControlPanel.Models.MotionLevel.Off);
            var fallback = Find<Grid>(fallbackCard, "leash-snap-avatar:friend");
            Assert.IsType<TextBlock>(((Border)fallback.Children[0]).Child);
        }
        finally { LeashSnapCard.FetchPhoto = old; }
    });
    private static string TestFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void ReplayingAnotherHolderClosesTheFirstSnap() => Run(() =>
    {
        var first = new LeashHolderCard(Held(), () => null);
        var second = new LeashHolderCard(Held() with { Who = Person with { Id = "second", Name = "Second" } }, () => null);
        var host = new Window { Content = new StackPanel { Children = { first, second } } };
        try
        {
            host.Show();
            ((MenuItem)Find<Button>(first, "leash-holder-more").ContextMenu!.Items[0]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var old = Assert.IsType<Window>(first.SnapWindow);
            ((MenuItem)Find<Button>(second, "leash-holder-more").ContextMenu!.Items[0]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var current = Assert.IsType<Window>(second.SnapWindow);
            Assert.NotSame(old, current);
            Assert.False(old.IsVisible);
            Assert.Same(current, first.SnapWindow);
            Assert.Equal(Loc.GetF("leash_snap_leashed", "Second"), Find<TextBlock>(current, "leash-snap-leashed").Text);
        }
        finally { second.SnapWindow?.Close(); host.Close(); }
    });

    [Fact]
    public void ReleaseHelpClosesWhenItsTimerExpires() => Run(() =>
    {
        var card = new LeashHolderCard(Held(), () => null);
        var host = new Window { Content = card };
        try
        {
            host.Show();
            Click(card, "leash-holder-more");
            var release = (MenuItem)Find<Button>(card, "leash-holder-more").ContextMenu!.Items[1]!;
            card.ReleaseHelpTimer!.Interval = TimeSpan.FromMilliseconds(20);
            ToolTip.SetIsOpen(release, true);
            Assert.True(ToolTip.GetIsOpen(release));
            System.Threading.Thread.Sleep(80);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ToolTip.GetIsOpen(release));
        }
        finally { host.Close(); }
    });

    [Fact]
    public void PreviewIsReadOnlyAndHasNoInventedWeek() => Run(() =>
    {
        var svc = new Service(); var card = new LeashSelfCard(Me(), () => svc);
        Click(card, "leash-preview-toggle");
        var preview = Find<LeashHolderCard>(card, "leash-preview");
        Assert.Empty(preview.GetLogicalDescendants().OfType<Button>());
        Assert.Empty(Find<WrapPanel>(preview, "leash-week").Children);
        Assert.Empty(svc.Calls);
        Click(card, "leash-preview-toggle");
        Assert.DoesNotContain(card.GetLogicalDescendants().OfType<LeashHolderCard>(), _ => true);
    });
    [Fact]
    public void HolderShowsReportTaskAndOnlyTheLastSevenWeekMarks() => Run(() =>
    {
        var held = Held() with { Assignment = TaskVideo,
            Week = Enumerable.Range(1, 9).Select(i => new WeekDay("202610" + i.ToString("00"), WeekMark.Did)).ToArray() };
        var card = new LeashHolderCard(held, () => null);
        Assert.Equal("30", Find<TextBlock>(card, "leash-ring-figure").Text);
        Assert.Equal(360, Find<global::Avalonia.Controls.Shapes.Arc>(card, "leash-ring-arc").SweepAngle);
        Assert.Contains(Find<Border>(card, "leash-fig-quests").GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "2/3");
        Assert.Equal(7, Find<WrapPanel>(card, "leash-week").Children.Count);
        Assert.Equal(Loc.GetF("leash_task_open", Loc.Get("leash_assign_video_n")), Find<TextBlock>(card, "leash-holder-task").Text);
        Assert.DoesNotContain(card.GetLogicalDescendants().OfType<Border>(), t => t.Tag as string == "leash-fig-lock");
        card.Update(Held(linked: true));
        Assert.NotNull(Find<Border>(card, "leash-fig-lock"));
    });
    [Fact]
    public void MissingReportKeepsAllThreeFiguresUnknownAndTheRingEmpty() => Run(() =>
    {
        var card = new LeashHolderCard(Held() with { Report = null }, () => null);
        var figures = Find<global::Avalonia.Controls.Primitives.UniformGrid>(card, "leash-figures");
        Assert.Equal(3, figures.Columns); Assert.Equal(3, figures.Children.Count);
        foreach (var tag in new[] { "leash-fig-minutes", "leash-fig-quests", "leash-fig-streak" })
            Assert.Contains(Find<Border>(card, tag).GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "-");
        Assert.DoesNotContain(figures.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.GetF("leash_fig_streak", 0));
        Assert.Empty(figures.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Arc>());
    });
    [Theory]
    [InlineData(AssignKind.Minutes, AssignStatus.Open, 60, 180)]
    [InlineData(AssignKind.Minutes, AssignStatus.Open, 120, 90)]
    [InlineData(AssignKind.Minutes, AssignStatus.Open, 15, 360)]
    [InlineData(AssignKind.Minutes, AssignStatus.Done, 60, 360)]
    [InlineData(AssignKind.Minutes, AssignStatus.Missed, 60, 360)]
    [InlineData(AssignKind.Quests, AssignStatus.Open, 60, 360)]
    public void MinutesRingUsesOnlyAnOpenMinutesAssignmentGoal(AssignKind kind, AssignStatus status, int goal, double sweep) => Run(() =>
    {
        var card = new LeashHolderCard(Held() with { Assignment = TaskVideo with { Kind = kind, Status = status, Size = goal } }, () => null);
        var arc = Find<global::Avalonia.Controls.Shapes.Arc>(card, "leash-ring-arc");
        Assert.Equal(-90, arc.StartAngle); Assert.Equal(sweep, arc.SweepAngle);
        Assert.Equal("30", Find<TextBlock>(card, "leash-ring-figure").Text);
    });
    [Theory]
    [InlineData(LeashIntensity.Soft, false, 2)]
    [InlineData(LeashIntensity.Standard, false, 5)]
    [InlineData(LeashIntensity.Strict, true, 6)]
    public void PunishRowsFollowIntensityAndChaster_AndPresetReachesTheService(LeashIntensity level, bool linked, int kinds) => Run(() =>
    {
        var svc = new Service(); var card = new LeashHolderCard(Held(level, linked), () => svc);
        Click(card, "leash-btn:punish");
        var count = card.GetLogicalDescendants().OfType<Button>().Select(b => b.Tag as string).Where(t => t?.StartsWith("leash-punish:") == true).Select(t => t!.Split(':')[1]).Distinct().Count();
        Assert.Equal(kinds - (level == LeashIntensity.Soft ? 0 : 1), count); // Video uses its picker instead of size chips.
        Click(card, "leash-punish:lines:5"); Assert.Equal("punish:Lines:5", svc.Calls.Single());
        Assert.DoesNotContain(card.GetLogicalDescendants().OfType<StackPanel>(), p => p.Tag as string == "leash-sheet:punish");
        Assert.Equal(Loc.GetF("leash_done_punish", Person.Name), Find<TextBlock>(card, "leash-holder-result").Text);
    });
    [Fact]
    public void QuietStillAllowsRewards_DailyCapHidesPunishments_AndFailuresKeepTheSheet() => Run(() =>
    {
        var svc = new Service { Reply = LeashSendStatus.Dnd };
        var held = Held(); var card = new LeashHolderCard(held with { DndUntil = Now.AddHours(1) }, () => svc);
        foreach (var id in new[] { "assign", "punish", "tug" }) Assert.False(Find<Button>(card, "leash-btn:" + id).IsEnabled);
        Assert.Equal(Loc.GetF("leash_holder_dnd", Person.Name, ConditioningControlPanel.Controls.Leash.LeashUiRules.DndUntilText(Now.AddHours(1))),
            Find<TextBlock>(card, "leash-holder-dnd").Text);
        Click(card, "leash-btn:reward"); Click(card, "leash-reward:pardon");
        Assert.NotNull(Find<StackPanel>(card, "leash-sheet:reward"));
        Assert.Equal(Loc.Get("leash_res_dnd"), Find<TextBlock>(card, "leash-holder-result").Text);
        card.Update(held with { PunishedToday = 3 }); Click(card, "leash-btn:punish");
        Assert.DoesNotContain(card.GetLogicalDescendants().OfType<Button>(), b => (b.Tag as string)?.StartsWith("leash-punish:") == true);
    });
    [Fact]
    public void AssignRewardVideoAndReleaseButtonsSendTheWpfGrammar() => Run(() =>
    {
        var svc = new Service(); var card = new LeashHolderCard(Held(LeashIntensity.Strict, true) with { VideoMax = 12 }, () => svc);
        Click(card, "leash-btn:assign"); Click(card, "leash-assign:minutes:30");
        Click(card, "leash-btn:assign"); Click(card, "leash-assign:quests:2");
        foreach (var tag in new[] { "sticker:star", "praise:proud", "pardon", "credit:900" })
        { Click(card, "leash-btn:reward"); Click(card, "leash-reward:" + tag); }
        Click(card, "leash-btn:punish");
        var cap = Find<LeashCapSlider>(card, "leash-video-cap");
        Assert.Equal(12, cap.Ceiling); Assert.Equal(12, cap.Value);
        Assert.Equal(Loc.Get("leash_video_max_hint"), ToolTip.GetTip(cap));
        cap.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.PageDown });
        Assert.Equal(7, cap.Value);
        Find<TextBox>(card, "leash-ht-box").Text = "12x34";
        Assert.Equal("1234", Find<TextBox>(card, "leash-ht-box").Text);
        Click(card, "leash-video-send"); Assert.Equal("punish:Video:7", svc.Calls.Last()); Assert.Equal("1234", svc.Watch!.Id);
        var menu = Find<Button>(card, "leash-holder-more").ContextMenu!;
        var release = Assert.IsType<MenuItem>(menu.Items[1]);
        Assert.Equal("leash-release", release.Tag);
        Assert.IsType<LeashHelpSlip>(ToolTip.GetTip(release));
        release.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("punish:Video:7", svc.Calls.Last());   // WPF asks first
        LeashCutConfirmWindow.Current!.NoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain("release", svc.Calls);
        release.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var confirm = LeashCutConfirmWindow.Current!;
        Assert.Equal(Loc.Get("leash_release_confirm_yes"), ((TextBlock)confirm.YesButton.Content!).Text);
        confirm.YesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Assert.Equal("release", svc.Calls.Last()); Assert.Single(svc.Calls, c => c == "release"); Assert.Null(LeashCutConfirmWindow.Current);
        Assert.Contains("assign:Minutes:30", svc.Calls); Assert.Contains("assign:Quests:2", svc.Calls);
        Assert.Contains("reward:Sticker:star:", svc.Calls); Assert.Contains("reward:Praise:proud:", svc.Calls);
        Assert.Contains("reward:Pardon::", svc.Calls); Assert.Contains("reward:Credit::900", svc.Calls);
    });
    [Theory]
    [InlineData(LeashSendStatus.Sent, true)]
    [InlineData(LeashSendStatus.Queued, true)]
    [InlineData(LeashSendStatus.Dnd, false)]
    [InlineData(LeashSendStatus.Failed, false)]
    [InlineData(LeashSendStatus.TooFast, false)]
    public void SendResultUsesMintForSuccessAndGoldForRefusal(LeashSendStatus status, bool good) => Run(() =>
    {
        var svc = new Service { Reply = status }; var card = new LeashHolderCard(Held(), () => svc);
        Click(card, "leash-btn:reward"); Click(card, "leash-reward:pardon");
        Assert.Equal("reward:Pardon::", Assert.Single(svc.Calls));
        Assert.Same(good ? FriendsDrawer.Mint : FriendsDrawer.Gold, Find<TextBlock>(card, "leash-holder-result").Foreground);
    });
    [Fact]
    public void PageKeepsHeldCardsAndSheetsAcrossPollsAndReceipts_AndFoldsOnHide() => Run(() =>
    {
        var svc = new Service(); var page = new LeashTabView(() => svc);
        using var cleanup = new WindowCleanup(new Window { Content = page });
        page.OnShown(); Assert.True(page.ShowingEmpty);
        svc.Push(null, Held()); Assert.False(page.ShowingEmpty);
        var card = Find<LeashHolderCard>(page, "leash-holder-card:friend");
        Click(card, "leash-btn:reward"); svc.Push(null, Held());
        svc.ReceiptsSupported = true; svc.Receipts();
        Assert.Same(card, Find<LeashHolderCard>(page, "leash-holder-card:friend"));
        Assert.NotNull(Find<StackPanel>(card, "leash-sheet:reward"));
        Assert.NotNull(Find<LeashSentList>(card, "leash-sent"));
        page.IsVisible = false;
        Assert.DoesNotContain(card.GetLogicalDescendants().OfType<StackPanel>(), p => p.Tag as string == "leash-sheet:reward");
        svc.Push(null); Assert.False(page.ShowingEmpty);
        page.IsVisible = true; page.OnShown(); Assert.True(page.ShowingEmpty);
    });
    [Fact]
    public void TugThrottleIsPerFriend_AndReceiptsUseHonestSentWording() => Run(() =>
    {
        var svc = new Service { ReceiptsSupported = true };
        var card = new LeashHolderCard(Held() with { Who = Person with { Id = Guid.NewGuid().ToString() } }, () => svc);
        Click(card, "leash-btn:tug"); Assert.Equal(Loc.Get("leash_res_sent"), Find<TextBlock>(card, "leash-holder-result").Text);
        Click(card, "leash-btn:tug"); Assert.Single(svc.Calls);
        Assert.Equal(Loc.Get("leash_res_too_fast"), Find<TextBlock>(card, "leash-holder-result").Text);
        // WPF LeashHolderCard.cs:722: a punishment always names the friend, even with receipts on.
        Click(card, "leash-btn:punish"); Click(card, "leash-punish:lines:5");
        Assert.Equal(Loc.GetF("leash_done_punish", Person.Name), Find<TextBlock>(card, "leash-holder-result").Text);
        svc.Reply = LeashSendStatus.Queued; Click(card, "leash-btn:punish"); Click(card, "leash-punish:lines:5");
        Assert.Equal(Loc.GetF("leash_done_punish_queued", Person.Name), Find<TextBlock>(card, "leash-holder-result").Text);
    });
    private sealed class WindowCleanup : IDisposable
    {
        private readonly Window _window;
        internal WindowCleanup(Window window) { _window = window; window.Show(); Dispatcher.UIThread.RunJobs(); }
        public void Dispose() => _window.Close();
    }
}

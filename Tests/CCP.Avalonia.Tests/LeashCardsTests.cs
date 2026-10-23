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
        public Task<bool> ReleaseAsync(string id) { Calls.Add("release"); return System.Threading.Tasks.Task.FromResult(true); }
        public Task CutAsync() { Calls.Add("cut"); Push(null); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetIntensityAsync(LeashIntensity i) { Calls.Add("level:" + i); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetDndAsync(LeashDnd d) { Calls.Add("dnd:" + d); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetVideoMaxAsync(int n) { Calls.Add("cap:" + n); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetRemoteModeAsync(LeashRemoteMode m) => System.Threading.Tasks.Task.CompletedTask;
        public Task CompleteAsync(string id) => System.Threading.Tasks.Task.CompletedTask;
        public Task<bool> PardonAsync(string id) => System.Threading.Tasks.Task.FromResult(false);
        public Task<LeashAnswerResult> AnswerAsync(string id, bool yes, LeashIntensity i) => System.Threading.Tasks.Task.FromResult(LeashAnswerResult.Done);
    }
    private sealed class WatchRunner : Runner
    {
        public string? RunningAid { get; private set; }
        public bool IsRunning => RunningAid != null;
        public string? RunningPid => null;
        public bool StartAssignmentWatch(Assignment a) { RunningAid = a.Aid; return true; }
        public bool Start(Punishment p) => false;
        public void Cancel() => RunningAid = null;
        public event Action<string, int, int>? Progress { add { } remove { } }
        public event Action<string>? Completed { add { } remove { } }
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
    [Fact]
    public void PageTracksLiveCardsAndEmptyState_AndCutCancelsTheTaskImmediately() => Run(() =>
    {
        var svc = new Service();
        var page = new LeashTabView(() => svc);
        using var cleanup = new WindowCleanup(new Window { Content = page });
        page.OnShown();
        Assert.True(page.ShowingEmpty);
        svc.Push(Me(), Held());
        Assert.False(page.ShowingEmpty);
        Assert.IsType<LeashSelfCard>(page.Section.Children[0]);
        Assert.IsType<LeashHolderCard>(page.Section.Children[1]);
        var runner = new WatchRunner(); runner.StartAssignmentWatch(TaskVideo);
        var old = LeashTaskHost.Runner; LeashTaskHost.Runner = runner;
        try { Click(page, "leash-cut"); Assert.False(runner.IsRunning); Assert.Contains("cut", svc.Calls); Assert.True(page.ShowingEmpty); }
        finally { LeashTaskHost.Runner = old; }
    });
    [Fact]
    public void SelfControlsSetOnlyThePlayersLevelQuietTimeAndCap_AndPreviewHasNoActions() => Run(() =>
    {
        var svc = new Service(); var card = new LeashSelfCard(Me(), () => svc);
        Click(card, "leash-level:soft"); Click(card, "leash-dnd:today");
        Find<NumericUpDown>(card, "leash-video-max").Value = 47;
        Assert.Equal(new[] { "level:Soft", "dnd:Today", "cap:47" }, svc.Calls);
        Assert.Single(Find<WrapPanel>(card, "leash-shelf").Children);
        Click(card, "leash-preview-toggle");
        Assert.Empty(Find<LeashHolderCard>(card, "leash-preview").GetLogicalDescendants().OfType<Button>());
    });
    [Fact]
    public void WatchButtonStartsAnOpenVideoAssignment_AndDoesNotOfferCompletedWatches() => Run(() =>
    {
        var old = LeashTaskHost.Runner; var runner = new WatchRunner(); LeashTaskHost.Runner = runner;
        try
        {
            var card = new LeashSelfCard(Me(TaskVideo), () => null);
            Click(card, "leash-self-watch"); Assert.Equal("a1", runner.RunningAid);
            Assert.False(Find<Button>(card, "leash-self-watch").IsEnabled);
            card.Update(Me(TaskVideo with { Status = AssignStatus.Done }));
            Assert.DoesNotContain(card.GetLogicalDescendants().OfType<Button>(), b => b.Tag as string == "leash-self-watch");
        }
        finally { LeashTaskHost.Runner = old; }
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
        Assert.Equal(12, Find<NumericUpDown>(card, "leash-video-cap").Maximum);
        Find<TextBox>(card, "leash-ht-box").Text = "12x34";
        Assert.Equal("1234", Find<TextBox>(card, "leash-ht-box").Text);
        Click(card, "leash-video-send"); Assert.Equal("punish:Video:12", svc.Calls.Last()); Assert.Equal("1234", svc.Watch!.Id);
        Click(card, "leash-release"); Assert.Equal("release", svc.Calls.Last());
        Assert.Contains("assign:Minutes:30", svc.Calls); Assert.Contains("assign:Quests:2", svc.Calls);
        Assert.Contains("reward:Sticker:star:", svc.Calls); Assert.Contains("reward:Praise:proud:", svc.Calls);
        Assert.Contains("reward:Pardon::", svc.Calls); Assert.Contains("reward:Credit::900", svc.Calls);
    });
    [Fact]
    public void PollAndReceiptUpdatesKeepTheOpenSheet_OnlyVisibleAssignmentsAreSeen() => Run(() =>
    {
        var svc = new Service(); svc.Push(Me(TaskVideo), Held());
        var page = new LeashTabView(() => svc);
        using var cleanup = new WindowCleanup(new Window { Content = page });
        page.OnShown(); Assert.Contains("a1", svc.Seen);
        var card = page.Section.Children.OfType<LeashHolderCard>().Single(); Click(card, "leash-btn:reward");
        svc.ReceiptsSupported = true; svc.Receipts();
        Assert.Same(card, page.Section.Children.OfType<LeashHolderCard>().Single());
        Assert.NotNull(Find<StackPanel>(card, "leash-sheet:reward")); Assert.NotNull(Find<LeashSentList>(card, "leash-sent"));
        page.IsVisible = false; svc.Seen.Clear(); svc.Push(Me(TaskVideo with { Aid = "a2" }), Held()); Assert.Empty(svc.Seen);
        page.IsVisible = true; page.OnShown(); Assert.Contains("a2", svc.Seen);
        page.FoldForPanic(); Assert.DoesNotContain(card.GetLogicalDescendants().OfType<StackPanel>(), p => p.Tag as string == "leash-sheet:reward");
        Assert.Contains(PanicSurfaces.All, s => s.Id == "leash-cards");
    });
    [Fact]
    public void TugThrottleIsPerFriend_AndReceiptsUseHonestSentWording() => Run(() =>
    {
        var svc = new Service { ReceiptsSupported = true };
        var card = new LeashHolderCard(Held() with { Who = Person with { Id = Guid.NewGuid().ToString() } }, () => svc);
        Click(card, "leash-btn:tug"); Assert.Equal(Loc.Get("leash_res_sent"), Find<TextBlock>(card, "leash-holder-result").Text);
        Click(card, "leash-btn:tug"); Assert.Single(svc.Calls);
        Assert.Equal(Loc.Get("leash_res_too_fast"), Find<TextBlock>(card, "leash-holder-result").Text);
    });
    [Fact]
    public void FriendsDrawerHostsTheSameCards_WhileTheFriendsPageLeavesThemOnLeash() => Run(() =>
    {
        var old = LeashHead.Service;
        var svc = new Service(); svc.Push(Me(), Held()); LeashHead.Service = svc;
        try
        {
            var drawer = new FriendsDrawer(); drawer.LeashSection!.Refresh();
            Assert.IsType<LeashSelfCard>(drawer.LeashSection.Children[0]);
            Assert.Null(new FriendsDrawer(null, asPage: true).LeashSection);
            drawer.OnClosed(); svc.Push(null); // folded drawer is unsubscribed until reopened
            Assert.Equal(2, drawer.LeashSection.Children.Count);
        }
        finally { LeashHead.Service = old; }
    });
    private sealed class WindowCleanup : IDisposable
    {
        private readonly Window _window;
        internal WindowCleanup(Window window) { _window = window; window.Show(); Dispatcher.UIThread.RunJobs(); }
        public void Dispose() => _window.Close();
    }
}

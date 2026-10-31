using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class LeashHolderTests
{
    private sealed class SteppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        private readonly List<(DateTimeOffset Due, TimerCallback Cb)> _due = new();
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        { var t = (_now + due, cb); _due.Add(t); return new Handle(() => _due.Remove(t)); }
        public void Step(TimeSpan by)
        {
            _now += by;
            foreach (var t in _due.Where(t => t.Due <= _now).ToArray()) { _due.Remove(t); t.Cb(null); }
        }
        private sealed class Handle(Action drop) : ITimer
        {
            public bool Change(TimeSpan d, TimeSpan p) => false;
            public void Dispose() => drop();
            public ValueTask DisposeAsync() { drop(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Service : ILeashService
    {
        public bool Available => true;
        public LeashSnapshot Snapshot { get; private set; } = LeashSnapshot.Empty;
        public event Action<LeashSnapshot>? SnapshotChanged;
        public event Action? ReceiptsChanged;
        public event Action<LeashEvent>? EventArrived { add { } remove { } }
        public Punishment? GateDue => null;
        public bool ReceiptsSupported { get; set; }
        public IReadOnlyList<LeashSentItem> Items = Array.Empty<LeashSentItem>();
        public IReadOnlyList<LeashSentItem> SentTo(string id) => Items;
        public LeashSendResult Reply = new(LeashSendStatus.Sent);
        public bool ThrowSend;
        public readonly List<(string Op, object? Kind, object? Value, LeashWatch? Watch)> Calls = new();
        public void Publish(params HeldLeash[] held) { Snapshot = new(null, held, Array.Empty<LeashOffer>()); SnapshotChanged?.Invoke(Snapshot); }
        public void Receipt(LeashSentItem item) { Items = new[] { item }; ReceiptsChanged?.Invoke(); }
        private Task<LeashSendResult> Send(string op, object? kind = null, object? value = null, LeashWatch? watch = null)
        { if (ThrowSend) throw new InvalidOperationException("offline"); Calls.Add((op, kind, value, watch)); return Task.FromResult(Reply); }
        public Task<LeashSendResult> OfferAsync(string id) => Send("offer");
        public Task<LeashSendResult> AssignAsync(string id, AssignKind kind, int size, LeashWatch? watch = null) => Send("assign", kind, size, watch);
        public Task<LeashSendResult> PunishAsync(string id, PunishKind kind, int size, LeashWatch? watch = null) => Send("punish", kind, size, watch);
        public Task<LeashSendResult> RewardAsync(string id, RewardKind kind, string? token = null, int? size = null) => Send("reward", kind, token ?? (object?)size);
        public Task<LeashSendResult> TugAsync(string id) => Send("tug");
        public Task<bool> ReleaseAsync(string id) { Calls.Add(("release", id, null, null)); Publish(); return Task.FromResult(true); }
        public Task<LeashAnswerResult> AnswerAsync(string id, bool accept, LeashIntensity intensity) => throw new NotSupportedException();
        public Task CutAsync() => Task.CompletedTask; public Task SetIntensityAsync(LeashIntensity i) => Task.CompletedTask;
        public Task SetDndAsync(LeashDnd d) => Task.CompletedTask;
        public Task SetRemoteModeAsync(LeashRemoteMode m) => Task.CompletedTask;
        public Task SetVideoMaxAsync(int m) => Task.CompletedTask;
        public Task CompleteAsync(string id) => Task.CompletedTask; public Task<bool> PardonAsync(string id) => Task.FromResult(false);
    }
    private static HeldLeash Held() => new(new LeashPerson(Guid.NewGuid().ToString("N"), "Juno", null), true,
        LeashIntensity.Standard, DateTimeOffset.UtcNow.AddDays(-2), 3, null, null,
        Array.Empty<WeekDay>(), Array.Empty<Punishment>(), null, 0);
    private static T Tagged<T>(Control root, string tag) where T : Control => root.GetLogicalDescendants().OfType<T>().Single(c => c.Tag as string == tag);
    private static IEnumerable<string> Tags(Control root) => root.GetLogicalDescendants().OfType<Control>().Select(c => c.Tag as string ?? "");
    private static void Click(Control root, string tag) => LeashGateCardTests.Click(Tagged<Button>(root, tag));
    private static void Run(Action<LeashTabView, Service, HeldLeash> action) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new Service(); var held = Held(); service.Publish(held);
        var page = new LeashTabView(() => service);
        var window = new Window { Content = page, Width = 660, Height = 750 };
        try { window.Show(); page.OnShown(); Dispatcher.UIThread.RunJobs(); action(page, service, held); }
        finally { window.Close(); }
    });
    [Fact]
    public void HoldingAloneReachesTheCardAndPollsKeepItsOpenSheet() => Run((page, svc, held) =>
    {
        Assert.False(page.ShowingEmpty);
        var card = Assert.Single(page.HolderCards.Values);
        Click(card, "leash-btn:assign");
        svc.Publish(held with { Day = 4 });
        Assert.Same(card, Assert.Single(page.HolderCards.Values));
        Assert.Equal("assign", card.OpenSheet);
        Assert.Contains(card.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.GetF("leash_holder_day", 4));
        svc.Publish(); Assert.True(page.ShowingEmpty); Assert.Empty(page.HolderCards.Values);
    });
    [Fact]
    public void PresetButtonsSendAllThreeKindsAndOnlyGoodRepliesCloseTheSheet() => Run((page, svc, _) =>
    {
        var card = Assert.Single(page.HolderCards.Values);
        Click(card, "leash-btn:assign"); Click(card, "leash-assign:minutes:15");
        Assert.Equal(("assign", (object?)AssignKind.Minutes, (object?)15, (LeashWatch?)null), svc.Calls.Last());
        Assert.Null(card.OpenSheet);
        Click(card, "leash-btn:reward"); Click(card, "leash-reward:sticker:star");
        Assert.Equal(RewardKind.Sticker, svc.Calls.Last().Kind); Assert.Equal("star", svc.Calls.Last().Value);
        Click(card, "leash-btn:reward"); Click(card, "leash-reward:praise:proud");
        Assert.Equal(RewardKind.Praise, svc.Calls.Last().Kind);
        Click(card, "leash-btn:reward"); Click(card, "leash-reward:pardon"); Assert.Equal(RewardKind.Pardon, svc.Calls.Last().Kind);
        svc.Reply = new(LeashSendStatus.Dnd, DateTimeOffset.UtcNow.AddHours(1));
        Click(card, "leash-btn:punish"); Click(card, "leash-punish:lines:3");
        Assert.Equal("punish", card.OpenSheet); Assert.Contains("Juno", card.ResultText);
        svc.Reply = new(LeashSendStatus.Queued);
        Click(card, "leash-punish:lines:3");
        Assert.Null(card.OpenSheet); Assert.Equal(Loc.GetF("leash_done_punish_queued", "Juno"), card.ResultText);
    });
    [Fact]
    public void QuietIntensityDailyCapAndChasterLinkControlTheAvailableActions() => Run((page, svc, held) =>
    {
        svc.Publish(held with { Intensity = LeashIntensity.Soft, DndUntil = DateTimeOffset.UtcNow.AddHours(1) });
        var card = Assert.Single(page.HolderCards.Values);
        foreach (var id in new[] { "assign", "punish", "tug" }) Assert.False(Tagged<Button>(card, "leash-btn:" + id).IsEnabled);
        Click(card, "leash-btn:reward"); Assert.DoesNotContain("leash-reward:credit:900", Tags(card));
        svc.Publish(held with { Intensity = LeashIntensity.Soft });
        Click(card, "leash-btn:punish");
        Assert.Contains("leash-punish:lines:3", Tags(card));
        Assert.DoesNotContain(Tags(card), t => t.StartsWith("leash-punish:bubbles:") || t.StartsWith("leash-punish:chaster:"));
        svc.Publish(held with { PunishedToday = 3 });
        Assert.DoesNotContain(Tags(card), t => t.StartsWith("leash-punish:"));
        var report = new DayReport("2026-10-23", 12, 1, 3, 2, true, 3600, 900, false, DateTimeOffset.UtcNow);
        svc.Publish(held with { Intensity = LeashIntensity.Strict, Report = report });
        Assert.Contains("leash-punish:chaster:900", Tags(card));
        Click(card, "leash-btn:reward"); Assert.Equal("-15m", ((TextBlock)Tagged<Button>(card, "leash-reward:credit:900").Content!).Text); Assert.Equal("-30m", ((TextBlock)Tagged<Button>(card, "leash-reward:credit:1800").Content!).Text); Click(card, "leash-reward:credit:900");
        Assert.Equal(RewardKind.Credit, svc.Calls.Last().Kind); Assert.Equal(900, svc.Calls.Last().Value);
    });
    [Fact]
    public void VideoPickerNormalisesIdsAndLimitsPunishmentDurationToTheirCap() => Run((page, svc, held) =>
    {
        var old = FriendsInviteCodes.CatalogueWatches;
        FriendsInviteCodes.CatalogueWatches = () => new[] { ("sample", "Sample") };
        try
        {
            svc.Publish(held with { VideoMax = 7 });
            var card = Assert.Single(page.HolderCards.Values);
            Click(card, "leash-btn:punish"); Click(card, "leash-punish:video:1");
            var cap = Tagged<LeashCapSlider>(card, "leash-video-cap");
            cap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageUp });
            Assert.Equal(7, cap.Value);
            var box = Tagged<TextBox>(card, "leash-ht-box"); box.Text = "x12-34"; Dispatcher.UIThread.RunJobs();
            Assert.Equal("1234", box.Text); Click(card, "leash-video-send");
            Assert.Equal(7, svc.Calls.Last().Value); Assert.Equal(new LeashWatch("ht", "1234", null), svc.Calls.Last().Watch);
            Click(card, "leash-btn:assign"); Click(card, "leash-assign:video:1"); Click(card, "leash-catalogue:sample");
            Assert.Equal(AssignKind.Video, svc.Calls.Last().Kind); Assert.Equal(new LeashWatch("catalogue", "sample", "Sample"), svc.Calls.Last().Watch);
        }
        finally { FriendsInviteCodes.CatalogueWatches = old; }
    });
    [Fact]
    public void ReceiptsRepaintInPlaceAndHiddenPagesStopListening() => Run((page, svc, held) =>
    {
        svc.ReceiptsSupported = true;
        var at = DateTimeOffset.UtcNow;
        var item = new LeashSentItem("k", LeashItemKind.Tug, held.Who.Id, "0123456789abcdef", LeashStep.Seen, at, at);
        var card = Assert.Single(page.HolderCards.Values);
        Click(card, "leash-btn:reward"); Click(card, "leash-reward:pardon"); Assert.Equal(Loc.Get("leash_res_sent"), card.ResultText); Click(card, "leash-btn:reward"); svc.Receipt(item);
        Assert.Contains("leash-sent-row:tug:seen", Tags(card)); Assert.Equal("reward", card.OpenSheet);
        Assert.Equal(3, Tags(card).Count(t => t.StartsWith("leash-step-dot:") && t.EndsWith(":on")));
        page.IsVisible = false; svc.Receipt(item with { Step = LeashStep.Done });
        Assert.Contains("leash-sent-row:tug:seen", Tags(card));
        page.IsVisible = true; page.OnShown(); Assert.Contains("leash-sent-row:tug:done", Tags(card));
        svc.ReceiptsSupported = false; svc.Publish(held); Assert.DoesNotContain("leash-sent", Tags(card));
    });
    [Fact]
    public void TugThrottlesBeforeTheService() => Run((page, svc, held) =>
    {
        var card = Assert.Single(page.HolderCards.Values);
        Click(card, "leash-btn:tug"); Click(card, "leash-btn:tug");
        Assert.Single(svc.Calls); Assert.Equal(Loc.Get(LeashUiRules.ResultKey(LeashSendStatus.TooFast)), card.ResultText);
    });
    [Fact]
    public void ServiceErrorsKeepTheSheetOpenAndWordTheFailure() => Run((page, svc, held) =>
    {
        var card = Assert.Single(page.HolderCards.Values);
        svc.ThrowSend = true;
        Click(card, "leash-btn:assign"); Click(card, "leash-assign:quests:1");
        Assert.Equal("assign", card.OpenSheet); Assert.Equal(Loc.Get(LeashUiRules.ResultKey(LeashSendStatus.Failed)), card.ResultText);
        var off = new LeashHolderCard(held, () => null);
        Click(off, "leash-btn:reward"); Click(off, "leash-reward:pardon");
        Assert.Equal("reward", off.OpenSheet); Assert.Equal(Loc.Get(LeashUiRules.ResultKey(LeashSendStatus.Off)), off.ResultText);
    });
    [Fact]
    public Task SendFeedbackExpiresAfterThreeSeconds() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var clock = new SteppedClock();
        var svc = new Service(); var card = new LeashHolderCard(Held(), () => svc) { Clock = clock };
        var window = new Window { Content = card };
        try
        {
            window.Show();
            await card.SendRewardAsync(RewardKind.Pardon, null, null);
            Assert.NotNull(card.ResultText);
            clock.Step(TimeSpan.FromMilliseconds(2900)); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(card.ResultText);
            clock.Step(TimeSpan.FromMilliseconds(200)); Dispatcher.UIThread.RunJobs();
            Assert.Null(card.ResultText);
        }
        finally { window.Close(); }
    });
    [Fact]
    public void ReportFiguresAssignmentAndTheLastSevenDaysComeFromTheSnapshot() => Run((page, svc, held) =>
    {
        var at = DateTimeOffset.UtcNow;
        var report = new DayReport("2026-10-23", 12, 1, 3, 2, true, 3600, 900, false, at);
        var week = Enumerable.Range(0, 9).Select(i => new WeekDay(at.AddDays(i - 8).ToString("yyyy-MM-dd"), WeekMark.Did)).ToArray();
        var task = new Assignment("aid", AssignKind.Quests, 3, null, "2026-10-23", AssignStatus.Done, at);
        svc.Publish(held with { Report = report, Week = week, Assignment = task });
        var card = Assert.Single(page.HolderCards.Values);
        Assert.Equal(7, Tagged<UniformGrid>(card, "leash-week").Children.Count);
        var words = card.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Contains("12", words); Assert.Contains("1/3", words);
        Assert.Contains(LeashUiRules.LockLeft(3600), words);
        Assert.Contains(Loc.GetF("leash_fig_tab", LeashUiRules.Tab(900)), words);
        Assert.Equal(Loc.GetF("leash_task_done", Loc.GetF("leash_assign_quests_n", 3)), Tagged<TextBlock>(card, "leash-holder-task").Text);
        svc.Publish(held with { Report = report with { ChasterLinked = false } });
        Assert.DoesNotContain("leash-fig-lock", Tags(card)); Assert.Contains("leash-fig-streak", Tags(card));
    });
    [Fact]
    public void MinutesRingTracksOpenGoalsCompletionAndMissingReports() => Run((page, svc, held) =>
    {
        var report = new DayReport("2026-10-23", 12, 1, 3, 2, false, null, null, false, DateTimeOffset.UtcNow);
        var task = new Assignment("aid", AssignKind.Minutes, 15, null, report.Day, AssignStatus.Open, report.At);
        void Check(Assignment? assignment, int minutes, double sweep)
        {
            svc.Publish(held with { Report = report with { Minutes = minutes }, Assignment = assignment });
            var ring = Tagged<Grid>(Assert.Single(page.HolderCards.Values), "leash-ring");
            Assert.Equal(sweep, Assert.Single(ring.Children.OfType<Arc>()).SweepAngle, 6);
            Assert.Equal(minutes.ToString(), Tagged<TextBlock>(ring, "leash-ring-figure").Text);
        }
        Check(null, 12, 144); Check(task, 12, 288); Check(task with { Size = 60 }, 12, 72);
        Check(task, 20, 360); Check(task with { Status = AssignStatus.Done }, 12, 144);
        Check(task with { Kind = AssignKind.Quests }, 12, 144);
        svc.Publish(held); var empty = Tagged<Grid>(Assert.Single(page.HolderCards.Values), "leash-ring");
        Assert.Empty(empty.Children.OfType<Arc>()); Assert.Equal("-", Tagged<TextBlock>(empty, "leash-ring-figure").Text);
    });
    [Theory]
    [InlineData(WeekMark.Did, "#FF5FFFD0")]
    [InlineData(WeekMark.Punished, "#FFFF5F7A")]
    [InlineData(WeekMark.Today, "#00FFFFFF")]
    [InlineData(WeekMark.Idle, "#FF4A3D61")]
    public void WeekMarkersDistinguishEveryState(WeekMark mark, string fill) => Run((page, svc, held) =>
    {
        svc.Publish(held with { Week = new[] { new WeekDay("20261023", mark) } });
        var cell = Tagged<StackPanel>(Assert.Single(page.HolderCards.Values), "leash-week:" + mark);
        var dot = Assert.Single(cell.Children.OfType<Ellipse>());
        Assert.Equal(Color.Parse(fill), Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);
        Assert.Equal("F", Assert.Single(cell.Children.OfType<TextBlock>()).Text);
        if (mark == WeekMark.Today) { Assert.Same(FriendsDrawer.Gold, dot.Stroke); Assert.Equal(2, dot.StrokeThickness); Assert.Equal(new[] { 1.6, 1.2 }, dot.StrokeDashArray); }
        else { Assert.Null(dot.Stroke); Assert.True(dot.StrokeDashArray == null || dot.StrokeDashArray.Count == 0); }
    });
}

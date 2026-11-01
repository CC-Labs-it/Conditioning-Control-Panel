using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class OnlineMeterTests
{
    private static readonly DateTime SixPm = new(2026, 10, 20, 18, 0, 0);
    private static readonly LandingWorld Quiet = new(false, false, false, true, false, false), Session = new(false, false, false, true, false, false, SessionRunning: true);
    [Fact]
    public void PillAndCardFollowSourceConsentAndTheDaysShare() => Run((shell, s) =>
    {
        FriendsLanding.World = () => Session;   // the spent notice waits; this test is about the pill and card
        var tab = shell.Named<AssetsTabView>("AssetsTab")!;
        void Refresh() { shell.RefreshOnlineMeterPill(); tab.RefreshOnlineMeter(); }
        ScrolllerAllowance.Charge(ScrolllerAllowance.LimitBytes * 42 / 100); Refresh();
        var (pill, pillText) = (shell.Named<Border>("OnlineMeterPill")!, shell.Named<TextBlock>("TxtOnlineMeterPill")!);
        Assert.True(pill.IsVisible); Assert.Equal(string.Format(Loc.Get("online_pill"), ScrolllerAllowance.Percent), pillText.Text);
        Assert.True(tab.OnlineMeterCard.IsVisible); Assert.Equal(string.Format(Loc.Get("online_meter_pct"), ScrolllerAllowance.Percent), tab.TxtOnlineMeterPct.Text);
        Assert.Equal(ScrolllerAllowance.Percent, shell.OnlinePillRing!.Percent); Assert.Equal(OnlineMeterTone.Pink, shell.OnlinePillRing.Tone);
        Assert.True(tab.BtnOnlineMeterPlans.IsVisible);
        var inlines = tab.TxtOnlineMeterLine.Inlines!;
        Assert.Same(tab.TxtOnlineMeterExplanation, inlines[0]);
        Assert.Same(tab.BtnOnlineMeterPlans, inlines.OfType<global::Avalonia.Controls.Documents.InlineUIContainer>().Single().Child);
        var link = tab.BtnOnlineMeterPlans; var label = Assert.IsType<TextBlock>(link.Content);
        Assert.Equal(global::Avalonia.Media.FontWeight.SemiBold, label.FontWeight); Assert.True(label.TextDecorations is null || label.TextDecorations.Count == 0);
        Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), link.Cursor?.ToString());
        Assert.True(Application.Current!.TryFindResource("PinkBrush", Application.Current.ActualThemeVariant, out var pink)); Assert.Same(pink, label.Foreground);
        link.Transitions = null; link.ApplyTemplate(); Dispatcher.UIThread.RunJobs();
        var presenter = link.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
        foreach (var state in new[] { ":pointerover", ":pressed" })
        {
            ((global::Avalonia.Controls.IPseudoClasses)link.Classes).Set(state, true); Dispatcher.UIThread.RunJobs();
            Assert.True(presenter.Background is null || presenter.Background is global::Avalonia.Media.ISolidColorBrush b && b.Color == global::Avalonia.Media.Colors.Transparent, state);
            Assert.Equal(default(Thickness), presenter.BorderThickness); Assert.True(label.TextDecorations is null || label.TextDecorations.Count == 0);
            Assert.Equal(Matrix.Identity, link.RenderTransform?.Value ?? Matrix.Identity);
            ((global::Avalonia.Controls.IPseudoClasses)link.Classes).Set(state, false); Dispatcher.UIThread.RunJobs();
        }
        tab.BtnOnlineMeterPlans.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.NotNull(global::ConditioningControlPanel.Avalonia.Views.Dialogs.VaultGateDialog.Open);
        global::ConditioningControlPanel.Avalonia.Views.Dialogs.VaultGateDialog.Open!.Close();
        ScrolllerAllowance.Charge(ScrolllerAllowance.LimitBytes * 40 / 100);
        shell.RefreshOnlineMeterPill();
        Assert.Equal(OnlineMeterTone.Amber, shell.OnlinePillRing.Tone);
        (s.RemoteMediaConsented, s.FypOnlineConsented) = (false, false); Refresh();
        Assert.False(pill.IsVisible); Assert.False(tab.OnlineMeterCard.IsVisible);
        (s.RemoteMediaConsented, s.MediaSource) = (true, "local"); Refresh();
        Assert.False(pill.IsVisible);
        s.MediaSource = "mixed"; ScrolllerAllowance.Charge(ScrolllerAllowance.LimitBytes);
        Dispatcher.UIThread.RunJobs(); Refresh();
        Assert.True(pill.IsVisible); Assert.Equal(Loc.Get("online_pill_done"), pillText.Text);
        Assert.Equal(string.Format(Loc.Get("online_meter_spent"), string.Format(Loc.Get("online_meter_hours"), 6)),
            tab.TxtOnlineMeterExplanation.Text!.Split("   ")[0]);
        Assert.Equal(OnlineMeterTone.Spent, shell.OnlinePillRing!.Tone);
        shell.OpenOnlineMeterHover();
        Assert.True(shell.OnlineMeterHover!.IsOpen);
        Assert.Contains(shell.OnlineMeterHover.Child!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("online_see_plans"));
        var body = shell.OnlineMeterHover.Child!.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Loc.Get("online_hover_body"));
        var press = new PointerPressedEventArgs(body, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), shell, new Point(400, 400), 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 2);
        body.RaiseEvent(press); Assert.True(press.Handled);
        shell.ShowTab("dashboard");
        Dispatcher.UIThread.RunJobs();
        pill.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.IsVisible); Assert.False(shell.OnlineMeterHover.IsOpen);
        shell.OpenOnlineMeterHover();
        LocalizationManager.Instance.SetLanguage("de");
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.OnlineMeterHover.IsOpen); Assert.Equal(Loc.Get("online_pill_done"), pillText.Text);
        LocalizationManager.Instance.SetLanguage("en"); ScrolllerAllowance.PlanProvider = () => ScrolllerPlan.Prime;
        tab.RefreshOnlineMeter();
        Assert.False(tab.BtnOnlineMeterPlans.IsVisible);
    });
    [Fact]
    public void SpentNoticeWaitsOutAHoldThenOffersPlans() => Run((shell, s) =>
    {
        var host = shell.Named<Panel>("NotificationHost")!;
        bool Toast() => host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.Get("online_toast_spent"));
        FriendsLanding.World = () => Session;
        ScrolllerAllowance.Charge(ScrolllerAllowance.LimitBytes);   // SpentReached, on the real event path
        Dispatcher.UIThread.RunJobs();
        Assert.False(Toast());
        FriendsLanding.World = () => Quiet; shell.ReleaseOnlineSpentNotice(); // what the 3 s wait timer runs
        Dispatcher.UIThread.RunJobs();
        Assert.True(Toast());
        var count = host.Children.Count;
        shell.ReleaseOnlineSpentNotice();
        Assert.Equal(count, host.Children.Count);
        Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("online_see_plans"));
    });
    [Fact]
    public void RingDrawsAnArcThenAGreyCircle() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        var ring = new global::ConditioningControlPanel.Avalonia.Controls.OnlineMeterRing { Width = 36, Height = 36 };
        ring.Measure(new Size(36, 36));
        ring.Arrange(new Rect(0, 0, 36, 36));
        byte[] Frame(int percent, OnlineMeterTone tone)
        {
            ring.Percent = percent; ring.Tone = tone;
            using var frame = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(36, 36));
            frame.Render(ring);
            var pixels = new byte[36 * 36 * 4];
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { frame.CopyPixels(new PixelRect(0, 0, 36, 36), pin.AddrOfPinnedObject(), pixels.Length, 36 * 4); }
            finally { pin.Free(); }
            return pixels;
        }
        var (empty, pink) = (Frame(0, OnlineMeterTone.Pink), Frame(42, OnlineMeterTone.Pink));
        var (amber, spent) = (Frame(82, OnlineMeterTone.Amber), Frame(100, OnlineMeterTone.Spent));
        Assert.False(empty.SequenceEqual(pink)); Assert.False(pink.SequenceEqual(amber));
        Assert.False(amber.SequenceEqual(spent));
        Assert.True(spent.SequenceEqual(Frame(0, OnlineMeterTone.Spent)));
        Assert.Contains(spent.Where((_, i) => i % 4 == 3), a => a > 200);
    });
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisplayedNoticeFoldsOnHoldOrPausesWhileHovered(bool hold) => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        var service = new global::ConditioningControlPanel.Avalonia.Helpers.NotificationService();
        service.RemoveLater = (remove, _) => remove();
        var host = new StackPanel();
        var now = SixPm;
        var queue = new OnlineSpentNoticeQueue();
        bool holding = false;
        using var toast = new global::ConditioningControlPanel.Avalonia.Helpers.OnlineSpentToast(service, host,
            () => holding, () => queue.Arm(now), "spent", null, null);
        var beats = global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Running;
        Assert.Contains(beats, b => ReferenceEquals(b.Owner, toast.Border));
        toast.Tick(4_000);
        if (hold)
        {
            holding = true; toast.Tick(50);
            Assert.Empty(host.Children);
            Assert.False(queue.TryRelease(now, true));
            Assert.True(queue.TryRelease(now, false));
            Assert.False(queue.TryRelease(now, false));
        }
        else
        {
            void Hover(bool on) => toast.Border.RaiseEvent(new PointerEventArgs(
                on ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent, toast.Border,
                null!, null!, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Hover(true);
            toast.Tick(20_000);
            Assert.Single(host.Children);
            Hover(false);
            toast.Tick(5_999);
            Assert.Single(host.Children);
            toast.Tick(1);
            Assert.Empty(host.Children);
            Assert.False(queue.Pending);
        }
        Assert.DoesNotContain(beats, b => ReferenceEquals(b.Owner, toast.Border));
    });
    [Fact]
    public void MinimizedNoticeFoldsDuringHoldAndReturnsOnRestore() => Run((shell, _) =>
    {
        var host = shell.Named<Panel>("NotificationHost")!;
        FriendsLanding.World = () => Quiet;
        ScrolllerAllowance.Charge(ScrolllerAllowance.LimitBytes);
        Dispatcher.UIThread.RunJobs();
        var (original, beats) = (host.Children.Last(), global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Running);
        var beat = beats.Single(b => ReferenceEquals(b.Owner, original));
        Assert.True(shell.OnlineSpentPolling);
        shell.WindowState = WindowState.Minimized;
        Assert.False(beat.IsTicking);
        FriendsLanding.World = () => Session;
        shell.ReleaseOnlineSpentNotice(); // The existing three-second queue poll, even while the beat is parked.
        Assert.DoesNotContain(original, host.Children);
        FriendsLanding.World = () => Quiet;
        shell.ReleaseOnlineSpentNotice();
        var replacement = host.Children.Last();
        Assert.NotSame(original, replacement);
        Assert.True(shell.OnlineSpentPolling);
        ((SteppedTime)global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time).Advance(TimeSpan.FromSeconds(20));
        shell.WindowState = WindowState.Normal;
        Assert.Contains(beats, b => ReferenceEquals(b.Owner, replacement) && b.IsTicking);
        global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
        Assert.Contains(replacement, host.Children.Where(c => c.IsHitTestVisible));
    }, stepBeatTime: true);
    [Fact]
    public void HelperRestoresBeatClockBeforeReturning() => AvaloniaTestDispatcher.Run(() =>
    {
        var previous = global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time;
        Run((_, _) => Assert.IsType<SteppedTime>(global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time), stepBeatTime: true);
        Assert.Same(previous, global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time);
    });
    private static void Platform()
    {
        if (Application.Current is null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static void Run(Action<MainShellWindow, AppSettings> body, bool stepBeatTime = false) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldTime = global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time;
        Platform();
        LocalizationManager.Instance.SetLanguage("en");
        var oldSettings = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var svc = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => svc;
        var s = svc.Current;
        (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = ("online", true, false);
        var saved = (ScrolllerAllowance.PlanProvider, ScrolllerAllowance.Now, ScrolllerAllowance.AccountProvider,
            ScrolllerAllowance.LedgerPath, ScrolllerAllowance.LegacyLedgerPath, FriendsLanding.World);
        ScrolllerAllowance.ResetForTests(); (ScrolllerAllowance.PlanProvider, ScrolllerAllowance.Now) = (() => ScrolllerPlan.Free, () => SixPm);
        (ScrolllerAllowance.AccountProvider, ScrolllerAllowance.LedgerPath, ScrolllerAllowance.LegacyLedgerPath) = (() => null, () => null, () => null);
        MainShellWindow? shell = null;
        try
        {
            if (stepBeatTime) global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = new SteppedTime();
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            body(shell, s);
        }
        finally
        {
            if (shell != null)
            {
                foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
                shell.Close();
            }
            Dispatcher.UIThread.RunJobs();
            ScrolllerAllowance.ResetForTests();
            (ScrolllerAllowance.PlanProvider, ScrolllerAllowance.Now, ScrolllerAllowance.AccountProvider,
                ScrolllerAllowance.LedgerPath, ScrolllerAllowance.LegacyLedgerPath, FriendsLanding.World) = saved;
            svc.SealForReset();
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldSettings;
            global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = oldTime;
        }
    });
}

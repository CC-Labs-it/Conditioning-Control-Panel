using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using E = ConditioningControlPanel.Services.BrainDrainKeepClear.BandEntry;
namespace CCP.Avalonia.Tests;
public sealed class BrainDrainIntegrationTests
{
    private static Task Run(Func<Window, Task> test, bool supported = true) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var previous = global::ConditioningControlPanel.Avalonia.App.Notifications;
        global::ConditioningControlPanel.Avalonia.App.Notifications = new();
        Window? owner = null;
        try { owner = Setup(supported); await test(owner); }
        finally
        {
            try { BrainDrainOverlay.CloseAll(); owner?.Close(); }
            finally { global::ConditioningControlPanel.Avalonia.App.Notifications = previous; }
        }
    });
    private static Window Setup(bool supported = true)
    {
        // First setup owns process-wide fonts, including later card and panic tests.
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        BrainDrainOverlay.CloseAll();
        BrainDrainOverlay.LastNoticeUtc = DateTime.MinValue;
        ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => { };
        var notices = new StackPanel();
        global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(notices);
        notices.Children.Clear();
        var owner = new Window { Content = notices, Tag = notices }; owner.Show();
        CoreSession.IsEngineRunningProvider = () => true; var settings = CoreSettings.Current; settings.BrainDrainEnabled = true; settings.BrainDrainBlurStrength = 100; settings.DualMonitorEnabled = true; settings.BrainDrainKeepPicturesClear = false;
        BrainDrainOverlay.Prepare = _ => supported; BrainDrainOverlay.Capture = (_, _) => { };
        BrainDrainOverlay.Alpha = (w, a) => w.Opacity = a;
        BrainDrainOverlay.Screens = _ => new[] {
            (new PixelRect(0, 0, 800, 600), 1.0, true),
            (new PixelRect(800, 0, 800, 600), 2.0, false) };
        var handles = new Dictionary<Window, IntPtr>();
        BrainDrainOverlay.Handle = w => { if (!handles.ContainsKey(w)) handles[w] = new(handles.Count + 1); return handles[w]; };
        BrainDrainOverlay.OwnWindows = () => new[] { owner }; owner.Topmost = true;
        BrainDrainOverlay.ReadBand = ws => ws.Reverse().Select(w => new E(BrainDrainOverlay.Handle(w), true, false)).ToArray();
        BrainDrainOverlay.Raise = _ => { }; BrainDrainOverlay.Below = (_, _) => { }; return owner;
    }
    [Fact]
    public Task LiveToggleAnd500msTickSinkEveryMonitorAndOffRaisesAgain() => Run(async owner =>
    {
        var moves = new List<(Window, Window)>(); int raises = 0;
        BrainDrainOverlay.Below = (w, a) => moves.Add((w, a));
        BrainDrainOverlay.Raise = _ => raises++;
        BrainDrainOverlay.Start(owner);
        Assert.Equal(2, BrainDrainOverlay.Active.Count);
        Assert.True(BrainDrainOverlay.Polling);
        Assert.Equal(400, BrainDrainOverlay.Active[1].Width);
        Assert.Empty(moves);
        CoreSettings.Current.BrainDrainKeepPicturesClear = true; // immediate, no timer pump
        Assert.Equal(2, moves.Count);
        Assert.Same(owner, moves[0].Item2);
        Assert.Same(moves[0].Item1, moves[1].Item2);
        moves.Clear();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (moves.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(30);
        Assert.Equal(2, moves.Count); // actual dispatcher timer reclaims a drifted band
        int before = raises;
        CoreSettings.Current.BrainDrainKeepPicturesClear = false;
        Assert.Equal(before + 2, raises);
    });
    [Fact]
    public Task StrengthZeroSettingsReplacementAndOwnerCloseFollowTheLiveRun() => Run(owner =>
    {
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        service.Current.BrainDrainEnabled = true; service.Current.BrainDrainBlurStrength = 1;
        BrainDrainOverlay.Start(owner);
        Assert.Equal(26 / 255.0, BrainDrainOverlay.Active[0].Opacity);
        service.Current.BrainDrainBlurStrength = 0;
        Assert.Empty(BrainDrainOverlay.Active);
        service.RestoreFrom(new() { BrainDrainEnabled = true, BrainDrainBlurStrength = 100 });
        Assert.NotEmpty(BrainDrainOverlay.Active);
        Assert.Equal(217 / 255.0, BrainDrainOverlay.Active[0].Opacity);
        owner.Close();
        Assert.Empty(BrainDrainOverlay.Active); Assert.False(BrainDrainOverlay.Polling);
        service.Current.BrainDrainBlurStrength = 90;
        Assert.Empty(BrainDrainOverlay.Active); return Task.CompletedTask;
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StopAndRegisteredPanicCloseHostsAndRetirePendingRefreshes(bool panic) => Run(owner =>
    {
        BrainDrainOverlay.Start(owner); Assert.NotEmpty(BrainDrainOverlay.Active);
        if (panic) PanicSurfaces.All.Single(x => x.Id == "brain-drain").Stop(null);
        else global::ConditioningControlPanel.Avalonia.App.StopDesktopOverlays(false);
        Assert.Empty(BrainDrainOverlay.Active); Assert.False(BrainDrainOverlay.Polling);
        CoreSettings.Current.BrainDrainKeepPicturesClear = true;
        BrainDrainOverlay.Reconcile();
        Assert.Empty(BrainDrainOverlay.Active);
        CoreSession.IsEngineRunningProvider = () => false;
        BrainDrainOverlay.Start(owner); Assert.False(BrainDrainOverlay.Polling); return Task.CompletedTask;
    });
    [Fact]
    public Task RefusedCompositorLeavesNoHostAndDoesNotRetryEveryTick() => Run(owner =>
    {
        int tries = 0;
        BrainDrainOverlay.Prepare = _ => { tries++; return false; };
        BrainDrainOverlay.Start(owner); BrainDrainOverlay.Reconcile();
        Assert.Empty(BrainDrainOverlay.Active); Assert.Equal(1, tries); return Task.CompletedTask;
    }, supported: false);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CaptureAffinityFollowsCreationToggleAndReplacement(bool allow) => Run(owner =>
    {
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        service.Current.BrainDrainEnabled = true; service.Current.BrainDrainBlurStrength = 50; service.Current.AllowOverlayCapture = allow;
        var calls = new List<(uint Flag, bool Visible)>(); BrainDrainOverlay.Capture = (w, flag) => calls.Add((flag, w.IsVisible));
        BrainDrainOverlay.Start(owner);
        Assert.NotEmpty(calls); Assert.All(calls, x => Assert.Equal(allow ? 0u : 0x11u, x.Flag));
        Assert.Contains(calls, x => !x.Visible); Assert.Contains(calls, x => x.Visible);
        calls.Clear(); service.Current.AllowOverlayCapture = !allow;
        Assert.NotEmpty(calls); Assert.All(calls, x => Assert.Equal(allow ? 0x11u : 0u, x.Flag));
        calls.Clear(); service.RestoreFrom(new() { BrainDrainEnabled = true, BrainDrainBlurStrength = 50, AllowOverlayCapture = allow });
        Assert.NotEmpty(calls); Assert.All(calls, x => Assert.Equal(allow ? 0u : 0x11u, x.Flag)); return Task.CompletedTask;
    });
    // WPF BrainDrainFeatureControl.xaml.cs:346-355 and OverlayService.cs:3749-3758.
    [Fact]
    public Task CaptureControlUpdatesRunningHostsAndSaves() => Run(owner =>
    {
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        service.Current.BrainDrainEnabled = true; service.Current.BrainDrainBlurStrength = 50;
        service.Current.AllowOverlayCapture = true;
        var card = new BrainDrainFeatureControl(); owner.Content = card;
        var check = card.FindControl<CheckBox>("ChkAllowCapture")!;
        Assert.True(check.IsChecked); Assert.False(service.HasPendingSave);
        var calls = new List<uint>(); BrainDrainOverlay.Capture = (_, flag) => calls.Add(flag);
        BrainDrainOverlay.Start(owner); calls.Clear();
        check.IsChecked = false;
        Assert.False(service.Current.AllowOverlayCapture); Assert.True(service.HasPendingSave);
        Assert.NotEmpty(calls); Assert.All(calls, flag => Assert.Equal(0x11u, flag));
        calls.Clear(); check.IsChecked = true;
        Assert.True(service.Current.AllowOverlayCapture);
        Assert.NotEmpty(calls); Assert.All(calls, flag => Assert.Equal(0u, flag));
        return Task.CompletedTask;
    });
    // WPF BrainDrainFeatureControl.xaml.cs:106,207-217: load and external edits repaint the card.
    [Fact]
    public Task CaptureControlTracksExternalEditsAndSettingsReplacement() => Run(owner =>
    {
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        service.Current.AllowOverlayCapture = true;
        var card = new BrainDrainFeatureControl(); owner.Content = card;
        var check = card.FindControl<CheckBox>("ChkAllowCapture")!;
        Assert.True(check.IsChecked); Assert.False(service.HasPendingSave);
        service.Current.AllowOverlayCapture = false;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.False(check.IsChecked);
        var old = service.Current;
        service.RestoreFrom(new() { AllowOverlayCapture = true });
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.True(check.IsChecked);
        old.AllowOverlayCapture = false;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.True(check.IsChecked);
        owner.Content = null;
        service.Current.AllowOverlayCapture = false;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.True(check.IsChecked);
        return Task.CompletedTask;
    });
    // WPF capture affinity is Windows-only: Compositor/OverlayCaptureAffinity.cs:48-55.
    [Fact]
    public Task CaptureControlIsHiddenWhereExclusionIsUnavailable() => Run(owner =>
    {
        var card = new BrainDrainFeatureControl(); owner.Content = card;
        Assert.Equal(OperatingSystem.IsWindows(), card.FindControl<Grid>("CaptureRow")!.IsVisible);
        return Task.CompletedTask;
    });
    [Fact]
    public Task CapturedPixelsArePresentedWithLiveBlur() => Run(async owner =>
    {
        BrainDrainOverlay.Screens = _ => new[] { (new PixelRect(0, 0, 8, 8), 1.0, true) };
        BrainDrainCapturePump? pump = null;
        BrainDrainOverlay.CreatePump = _ => pump = new BrainDrainCapturePump(
            () => new BrainDrainCapturePump.Frame(2, 2, new byte[] { 7, 12, 200, 255, 7, 12, 200, 255, 7, 12, 200, 255, 7, 12, 200, 255 }), TimeSpan.FromSeconds(1));
        BrainDrainOverlay.Start(owner);
        var image = Assert.IsType<Image>(Assert.Single(BrainDrainOverlay.Active).Content);
        for (int i = 0; i < 100 && image.Source == null; i++) await Task.Delay(20);
        var bitmap = Assert.IsType<WriteableBitmap>(image.Source);
        using (var buffer = bitmap.Lock()) Assert.Equal(7, Marshal.ReadByte(buffer.Address));
        Assert.Null(image.Effect);
        Assert.Equal(14.0 / 12, pump!.Sigma, 5);
        CoreSettings.Current.BrainDrainBlurStrength = 50;
        Assert.Equal(7.0 / 12, pump.Sigma, 5);
    });

    [Fact]
    public Task EachCapturedFrameInvalidatesAfterCopyAndReturnsItsBuffer() => Run(async owner =>
    {
        var pending = new ConcurrentQueue<BrainDrainCapturePump.Frame>();
        var pool = new BrainDrainCapturePumpTests.TrackingPool();
        var painted = new List<byte>();
        BrainDrainOverlay.Screens = _ => new[] { (new PixelRect(0, 0, 8, 8), 1.0, true) };
        BrainDrainOverlay.CreatePump = _ => new BrainDrainCapturePump(() => pending.TryDequeue(out var f) ? f : null, TimeSpan.FromMilliseconds(10));
        BrainDrainOverlay.InvalidateFrame = image => {
            using var buffer = Assert.IsType<WriteableBitmap>(image.Source).Lock();
            painted.Add(Marshal.ReadByte(buffer.Address)); image.InvalidateVisual();
        };
        BrainDrainOverlay.Start(owner);
        var image = Assert.IsType<Image>(Assert.Single(BrainDrainOverlay.Active).Content);
        void Queue(byte color) { var pixels = pool.Rent(16); Array.Fill(pixels, color); pending.Enqueue(new(2, 2, pixels, pool)); }
        Queue(7);
        for (int i = 0; i < 100 && pool.Returns < 1; i++) await Task.Delay(20);
        Assert.Equal(new byte[] { 7 }, painted); Assert.Equal(1, pool.Returns);
        var bitmap = image.Source;
        Queue(42);
        for (int i = 0; i < 100 && pool.Returns < 2; i++) await Task.Delay(20);
        Assert.Same(bitmap, image.Source); Assert.Equal(new byte[] { 7, 42 }, painted); Assert.Equal(2, pool.Returns);
    });

    [Fact]
    public Task CapturedBlurUsesEachMonitorScaleAndTracksDpiChanges() => Run(owner =>
    {
        var screens = new[] {
            (new PixelRect(0, 0, 800, 600), 1.0, true),
            (new PixelRect(800, 0, 800, 600), 1.5, false),
            (new PixelRect(1600, 0, 800, 600), 2.0, false),
            (new PixelRect(2400, 0, 800, 600), 0.0, false) };
        BrainDrainOverlay.Screens = _ => screens;
        var pumps = new List<BrainDrainCapturePump>();
        BrainDrainOverlay.CreatePump = _ => { var pump = new BrainDrainCapturePump(() => null, TimeSpan.FromSeconds(1)); pumps.Add(pump); return pump; };
        BrainDrainOverlay.Start(owner);
        Assert.Equal(4, BrainDrainOverlay.Active.Count);
        double Radius(int index) => pumps[pumps.Count - screens.Length + index].Sigma * 12 / (screens[index].Item2 > 0 ? screens[index].Item2 : 1);
        Assert.Equal(14, Radius(0), 5);
        Assert.Equal(14 / 1.5, Radius(1), 5);
        Assert.Equal(7, Radius(2), 5);
        Assert.Equal(14, Radius(3), 5); // Invalid scaling uses the same 1x fallback as host sizing.
        CoreSettings.Current.BrainDrainBlurStrength = 50;
        Assert.Equal(7 / 1.5, Radius(1), 5);
        var old = BrainDrainOverlay.Active[1];
        screens[1] = (screens[1].Item1, 1.25, false);
        BrainDrainOverlay.Reconcile();
        Assert.False(old.IsVisible);
        Assert.NotSame(old, BrainDrainOverlay.Active[1]);
        Assert.Equal(7 / 1.25, Radius(1), 5);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("stop")]
    [InlineData("panic")]
    [InlineData("owner")]
    [InlineData("monitor")]
    public Task BlockedCaptureDoesNotHoldLifecycleOrPublishAfterwards(string action) => Run(async owner =>
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var pumps = new List<BrainDrainCapturePump>();
        var bounds = new PixelRect(0, 0, 8, 8);
        BrainDrainOverlay.Screens = _ => new[] { (bounds, 1.0, true) };
        BrainDrainOverlay.CreatePump = _ => {
            if (pumps.Count > 0)
            {
                var replacement = new BrainDrainCapturePump(() => null, TimeSpan.FromMilliseconds(33));
                pumps.Add(replacement); return replacement;
            }
            var pump = new BrainDrainCapturePump(() => {
                entered.Set(); release.Wait(); finished.Set();
                return new BrainDrainCapturePump.Frame(2, 2, new byte[16]);
            }, TimeSpan.FromMilliseconds(33));
            pumps.Add(pump); return pump;
        };
        try
        {
            BrainDrainOverlay.Start(owner);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var oldHost = Assert.Single(BrainDrainOverlay.Active);
            var oldImage = Assert.IsType<Image>(oldHost.Content);
            bool dispatched = false;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => dispatched = true);
            for (int i = 0; i < 100 && !dispatched; i++) await Task.Delay(10);
            Assert.True(dispatched);
            var elapsed = Stopwatch.StartNew();
            switch (action)
            {
                case "stop": global::ConditioningControlPanel.Avalonia.App.StopDesktopOverlays(false); break;
                case "panic": PanicSurfaces.All.Single(x => x.Id == "brain-drain").Stop(null); break;
                case "owner": owner.Close(); break;
                default: bounds = new PixelRect(0, 0, 12, 12); BrainDrainOverlay.Reconcile(); break;
            }
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
            Assert.False(oldHost.IsVisible);
            Assert.Null(oldImage.Source);
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
            await Task.Delay(250);
            Assert.Null(pumps[0].Take());
            Assert.Null(oldImage.Source);
            if (action == "monitor") Assert.NotSame(oldHost, Assert.Single(BrainDrainOverlay.Active));
            else Assert.Empty(BrainDrainOverlay.Active);
        }
        finally { BrainDrainOverlay.CloseAll(); release.Set(); }
    });

    [Fact]
    public Task SameOriginResolutionAndDpiChangesRecreateFullMonitorHosts() => Run(owner =>
    {
        var bounds = new PixelRect(0, 0, 800, 600); double scale = 1;
        BrainDrainOverlay.Screens = _ => new[] { (bounds, scale, true) };
        BrainDrainOverlay.Start(owner);
        foreach (var next in new[] { (new PixelRect(0, 0, 1000, 800), 1.0), (new PixelRect(0, 0, 1000, 800), 2.0), (new PixelRect(0, 0, 2000, 1600), 4.0) })
        {
            var old = Assert.Single(BrainDrainOverlay.Active); (bounds, scale) = next; BrainDrainOverlay.Reconcile();
            var host = Assert.Single(BrainDrainOverlay.Active); Assert.NotSame(old, host); Assert.False(old.IsVisible);
            Assert.Equal(bounds.Position, host.Position); Assert.Equal(bounds.Width / scale, host.Width); Assert.Equal(bounds.Height / scale, host.Height);
        } return Task.CompletedTask;
    });
    [Fact]
    public Task ZeroFramesAndRefusedPreparationShareNoticeCooldown() => Run(async owner =>
    {
        var notices = ((StackPanel)owner.Tag!).Children;
        BrainDrainOverlay.Screens = _ => new[] { (new PixelRect(0, 0, 8, 8), 1.0, true) };
        BrainDrainOverlay.CreatePump = _ => new BrainDrainCapturePump(() => null, TimeSpan.FromMilliseconds(20));
        BrainDrainOverlay.Start(owner);
        for (int i = 0; i < 250 && notices.Count == 0; i++) await Task.Delay(20);
        Assert.Single(notices);
        BrainDrainOverlay.CloseAll();
        BrainDrainOverlay.Prepare = _ => false;
        BrainDrainOverlay.Start(owner);
        BrainDrainOverlay.Reconcile();
        Assert.Single(notices);
        Assert.Empty(BrainDrainOverlay.Active);
    });

    [Fact]
    public Task RefusedPreparationRaisesNotice() => Run(owner =>
    {
        var notices = ((StackPanel)owner.Tag!).Children;
        BrainDrainOverlay.Prepare = _ => false;
        BrainDrainOverlay.Start(owner); BrainDrainOverlay.Reconcile();
        Assert.Single(notices);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FirstFrameDisarmsFailureNotice() => Run(async owner =>
    {
        var notices = ((StackPanel)owner.Tag!).Children;
        BrainDrainOverlay.Screens = _ => new[] { (new PixelRect(0, 0, 8, 8), 1.0, true) };
        BrainDrainOverlay.CreatePump = _ => new BrainDrainCapturePump(
            () => new BrainDrainCapturePump.Frame(2, 2, new byte[16]), TimeSpan.FromMilliseconds(20));
        BrainDrainOverlay.Start(owner);
        await Task.Delay(3500);
        Assert.Empty(notices);
    });

    [Fact]
    public Task NoScreensShowsOneTwelveSecondWarningAndRecovers() => Run(owner =>
    {
        var notices = ((StackPanel)owner.Tag!).Children;
        BrainDrainOverlay.Screens = _ => Array.Empty<(PixelRect, double, bool)>();
        BrainDrainOverlay.Start(owner);
        BrainDrainOverlay.Reconcile();
        var toast = Assert.IsType<Border>(Assert.Single(notices));
        Assert.Empty(BrainDrainOverlay.Active);
        Assert.Equal(Loc.Get("brain_drain_capture_failed"), Assert.IsType<TextBlock>(Assert.IsType<Grid>(toast.Child).Children[0]).Text);
        Assert.Equal(Color.Parse("#FFB347"), Assert.IsType<SolidColorBrush>(toast.BorderBrush).Color);
        Assert.Equal(TimeSpan.FromSeconds(12), Assert.IsType<DispatcherTimer>(toast.Tag).Interval);
        BrainDrainOverlay.Screens = _ => new[] { (new PixelRect(0, 0, 8, 8), 1.0, true) };
        BrainDrainOverlay.Reconcile();
        Assert.Single(BrainDrainOverlay.Active);
        Assert.Single(notices);
        return Task.CompletedTask;
    });

    [Fact]
    public async Task FixtureRestoresNotificationHostAndQueuedStateEvenOnFailure()
    {
        var original = global::ConditioningControlPanel.Avalonia.App.Notifications;
        var previous = new ConditioningControlPanel.Avalonia.Helpers.NotificationService();
        var host = new StackPanel();
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() => {
                global::ConditioningControlPanel.Avalonia.App.Notifications = previous;
                previous.Show("queued"); return Task.CompletedTask;
            });
            await Run(owner => { BrainDrainOverlay.Prepare = _ => false; BrainDrainOverlay.Start(owner); return Task.CompletedTask; });
            await AvaloniaTestDispatcher.RunAsync(() => {
                Assert.Same(previous, global::ConditioningControlPanel.Avalonia.App.Notifications);
                previous.AttachHost(host);
                Assert.Single(host.Children);
                return Task.CompletedTask;
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(_ => throw new InvalidOperationException()));
            await AvaloniaTestDispatcher.RunAsync(() => {
                Assert.Same(previous, global::ConditioningControlPanel.Avalonia.App.Notifications);
                previous.Show("after"); Assert.Equal(2, host.Children.Count);
                return Task.CompletedTask;
            });
        }
        finally { global::ConditioningControlPanel.Avalonia.App.Notifications = original; }
    }

}

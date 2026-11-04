using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
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
        var owner = Setup(supported); try { await test(owner); }
        finally { BrainDrainOverlay.CloseAll(); owner.Close(); }
    });
    private static Window Setup(bool supported = true)
    {
        // First setup owns process-wide fonts, including later card and panic tests.
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        BrainDrainOverlay.CloseAll(); var owner = new Window(); owner.Show();
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
        Assert.Equal(OperatingSystem.IsWindows(), card.FindControl<Border>("CaptureDivider")!.IsVisible);
        return Task.CompletedTask;
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
}

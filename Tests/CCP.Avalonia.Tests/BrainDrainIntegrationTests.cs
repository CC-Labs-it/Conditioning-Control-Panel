using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using E = ConditioningControlPanel.Services.BrainDrainKeepClear.BandEntry;

namespace CCP.Avalonia.Tests;

public sealed class BrainDrainIntegrationTests
{
    private static Window Setup(bool supported = true)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        BrainDrainOverlay.CloseAll();
        var owner = new Window(); owner.Show();
        CoreSession.IsEngineRunningProvider = () => true;
        CoreSettings.Current.BrainDrainEnabled = true;
        CoreSettings.Current.BrainDrainBlurStrength = 100;
        CoreSettings.Current.DualMonitorEnabled = true;
        CoreSettings.Current.BrainDrainKeepPicturesClear = false;
        BrainDrainOverlay.Prepare = _ => supported;
        BrainDrainOverlay.Capture = (_, _) => { };
        BrainDrainOverlay.Alpha = (w, a) => w.Opacity = a;
        BrainDrainOverlay.Screens = _ => new[] {
            (new PixelRect(0, 0, 800, 600), 1.0, true),
            (new PixelRect(800, 0, 800, 600), 2.0, false) };
        var handles = new Dictionary<Window, IntPtr>();
        BrainDrainOverlay.Handle = w => { if (!handles.ContainsKey(w)) handles[w] = new(handles.Count + 1); return handles[w]; };
        BrainDrainOverlay.OwnWindows = () => new[] { owner };
        owner.Topmost = true;
        BrainDrainOverlay.ReadBand = ws => ws.Reverse().Select(w => new E(BrainDrainOverlay.Handle(w), true, false)).ToArray();
        BrainDrainOverlay.Raise = _ => { };
        BrainDrainOverlay.Below = (_, _) => { };
        return owner;
    }

    [Fact]
    public Task LiveToggleAnd500msTickSinkEveryMonitorAndOffRaisesAgain() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var owner = Setup(); var moves = new List<(Window, Window)>(); int raises = 0;
        BrainDrainOverlay.Below = (w, a) => moves.Add((w, a));
        BrainDrainOverlay.Raise = _ => raises++;
        try
        {
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
        }
        finally { BrainDrainOverlay.CloseAll(); owner.Close(); }
    });

    [Fact]
    public Task StrengthZeroSettingsReplacementAndOwnerCloseFollowTheLiveRun() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var owner = Setup(); var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        service.Current.BrainDrainEnabled = true; service.Current.BrainDrainBlurStrength = 1;
        try
        {
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
            Assert.Empty(BrainDrainOverlay.Active);
        }
        finally { BrainDrainOverlay.CloseAll(); owner.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StopAndRegisteredPanicCloseHostsAndRetirePendingRefreshes(bool panic) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var owner = Setup();
        try
        {
            BrainDrainOverlay.Start(owner); Assert.NotEmpty(BrainDrainOverlay.Active);
            if (panic) PanicSurfaces.All.Single(x => x.Id == "brain-drain").Stop(null);
            else global::ConditioningControlPanel.Avalonia.App.StopDesktopOverlays(false);
            Assert.Empty(BrainDrainOverlay.Active); Assert.False(BrainDrainOverlay.Polling);
            CoreSettings.Current.BrainDrainKeepPicturesClear = true;
            BrainDrainOverlay.Reconcile();
            Assert.Empty(BrainDrainOverlay.Active);
            CoreSession.IsEngineRunningProvider = () => false;
            BrainDrainOverlay.Start(owner); Assert.False(BrainDrainOverlay.Polling);
        }
        finally { BrainDrainOverlay.CloseAll(); owner.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RefusedCompositorLeavesNoHostAndDoesNotRetryEveryTick() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var owner = Setup(false); int tries = 0;
        BrainDrainOverlay.Prepare = _ => { tries++; return false; };
        try
        {
            BrainDrainOverlay.Start(owner); BrainDrainOverlay.Reconcile();
            Assert.Empty(BrainDrainOverlay.Active); Assert.Equal(1, tries);
        }
        finally { BrainDrainOverlay.CloseAll(); owner.Close(); }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CaptureAffinityFollowsCreationToggleAndReplacement(bool allow) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var owner = Setup(); var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        service.Current.BrainDrainEnabled = true; service.Current.BrainDrainBlurStrength = 50; service.Current.AllowOverlayCapture = allow;
        var calls = new List<(uint Flag, bool Visible)>(); BrainDrainOverlay.Capture = (w, flag) => calls.Add((flag, w.IsVisible));
        try
        {
            BrainDrainOverlay.Start(owner);
            Assert.NotEmpty(calls); Assert.All(calls, x => Assert.Equal(allow ? 0u : 0x11u, x.Flag));
            Assert.Contains(calls, x => !x.Visible); Assert.Contains(calls, x => x.Visible);
            calls.Clear(); service.Current.AllowOverlayCapture = !allow;
            Assert.NotEmpty(calls); Assert.All(calls, x => Assert.Equal(allow ? 0x11u : 0u, x.Flag));
            calls.Clear(); service.RestoreFrom(new() { BrainDrainEnabled = true, BrainDrainBlurStrength = 50, AllowOverlayCapture = allow });
            Assert.NotEmpty(calls); Assert.All(calls, x => Assert.Equal(allow ? 0u : 0x11u, x.Flag));
        }
        finally { BrainDrainOverlay.CloseAll(); owner.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task SameOriginResolutionAndDpiChangesRecreateFullMonitorHosts() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var owner = Setup(); var bounds = new PixelRect(0, 0, 800, 600); double scale = 1;
        BrainDrainOverlay.Screens = _ => new[] { (bounds, scale, true) };
        try
        {
            BrainDrainOverlay.Start(owner);
            foreach (var next in new[] { (new PixelRect(0, 0, 1000, 800), 1.0), (new PixelRect(0, 0, 1000, 800), 2.0), (new PixelRect(0, 0, 2000, 1600), 4.0) })
            {
                var old = Assert.Single(BrainDrainOverlay.Active); (bounds, scale) = next; BrainDrainOverlay.Reconcile();
                var host = Assert.Single(BrainDrainOverlay.Active); Assert.NotSame(old, host); Assert.False(old.IsVisible);
                Assert.Equal(bounds.Position, host.Position); Assert.Equal(bounds.Width / scale, host.Width); Assert.Equal(bounds.Height / scale, host.Height);
            }
        }
        finally { BrainDrainOverlay.CloseAll(); owner.Close(); }
        return Task.CompletedTask;
    });
}

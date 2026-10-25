using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StatusPulseLifecycleTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long GetTimestamp() => Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    [Fact]
    public Task StatusGlowBreathesBehindAnOpaqueDotAndParksOnEveryGate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldTime = BeatLoop.Time;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = MotionLevel.Full;
        settings.Current.PerformanceMode = false;
        // The Awareness tab re-drives its pulse from the master switch whenever it is shown
        // (WPF SyncAwarenessTabUI -> UpdateAwarenessStatusIndicator(masterOn)), so the master must be on.
        settings.Current.KeywordTriggersEnabled = true;
        var clock = new Clock();
        BeatLoop.Time = clock;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.Activate(); shell.ShowTab("awareness");
            Dispatcher.UIThread.RunJobs();
            shell.SetAwarenessStatusPulse(true);
            var tab = shell.Named<Control>("AwarenessTab")!;
            var dot = tab.FindControl<Control>("AwarenessStatusDot")!;
            Assert.Null(dot.Effect); // never an animated Effect in the main shell
            var glow = tab.FindControl<Border>("AwarenessStatusGlow")!;
            Assert.True(glow.IsVisible);
            Assert.Equal(1, dot.Opacity);
            Assert.Equal(0.22, glow.Opacity, 3);
            Assert.Equal(1, glow.BoxShadow.Count);
            Assert.Equal(10, glow.BoxShadow[0].Blur);
            var pulse = Assert.Single(BeatLoop.Running, l => ReferenceEquals(l.Owner, glow));
            clock.Ticks += TimeSpan.FromSeconds(1.4).Ticks;
            BeatLoop.StepAll();
            Assert.Equal(0.585, glow.Opacity, 3); // WPF SineEase midpoint
            shell.SetAwarenessStatusPulse(true); // repeated feature refresh must not restart the clock
            Assert.Same(pulse, Assert.Single(BeatLoop.Running, l => ReferenceEquals(l.Owner, glow)));
            Assert.Equal(0.585, glow.Opacity, 3);
            clock.Ticks += TimeSpan.FromSeconds(1.4).Ticks;
            BeatLoop.StepAll();
            Assert.Equal(0.95, glow.Opacity, 3);
            clock.Ticks += TimeSpan.FromSeconds(2.8).Ticks;
            BeatLoop.StepAll();
            Assert.Equal(0.22, glow.Opacity, 3); // auto-reverse returns after a full 5.6 s cycle

            settings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(glow.IsVisible);
            Assert.DoesNotContain(BeatLoop.Running, l => ReferenceEquals(l.Owner, glow));
            settings.Current.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(glow.IsVisible);
            settings.Current.PerformanceMode = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(glow.IsVisible);
            settings.Current.PerformanceMode = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(glow.IsVisible);

            shell.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs();
            Assert.False(glow.IsVisible);
            shell.WindowState = WindowState.Normal; shell.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.True(glow.IsVisible);
            shell.ShowTab("settings"); Dispatcher.UIThread.RunJobs();
            Assert.False(glow.IsVisible);
            shell.ShowTab("awareness"); Dispatcher.UIThread.RunJobs();
            Assert.True(glow.IsVisible);
            shell.SetAwarenessStatusPulse(false);
            Assert.False(glow.IsVisible);
            Assert.Null(dot.Effect);
            Assert.Equal(1, dot.Opacity);
            shell.SetAwarenessStatusPulse(true);
            shell.Close(); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(BeatLoop.Running, l => ReferenceEquals(l.Owner, glow));
        }
        finally
        {
            shell.Close(); BeatLoop.Time = oldTime; CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF seeds an already-connected device's pulse when Haptics arrives. Here the tab's
    /// ctor refresh has no window yet, so the attach repaint (RefreshHapticToys) must light it.</summary>
    [Fact]
    public Task ConnectedHapticsGlowsWhenStudioOpens() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldHaptics = CoreHaptics.Service;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = MotionLevel.Full;
        settings.Current.PerformanceMode = false;
        CoreEntitlement.HasPremiumProvider = () => true;
        var hs = new HapticSettings { Enabled = true };
        hs.EnsureV2Migrated();
        hs.V2.Provider("lovense").Enabled = false;
        hs.V2.Provider("buttplug").Enabled = false;
        hs.V2.Provider("mock").Enabled = true;
        var haptics = new HapticService(hs);
        MainShellWindow? shell = null;
        try
        {
            Assert.True(haptics.ConnectAsync().Result);
            CoreHaptics.Service = haptics;
            shell = new MainShellWindow();
            shell.Show(); shell.Activate(); shell.ShowTab("studio");
            Dispatcher.UIThread.RunJobs();
            var glow = shell.StudioRack!.HapticsPanel.FindControl<Border>("HapticStatusGlow")!;
            Assert.True(glow.IsVisible);
        }
        finally
        {
            shell?.Close();
            haptics.PanicStop();
            haptics.Dispose();
            CoreHaptics.Service = oldHaptics;
            CoreEntitlement.HasPremiumProvider = oldPremium;
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });
}

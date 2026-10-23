using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ShellStartStopLifecycleTests
{
    [Fact]
    public Task RealStartStopDrivesRampAndSchedulerMemory() => Run(shell =>
    {
        var s = CoreSettings.Current;
        s.IntensityRampEnabled = true; s.RampLinkFlashOpacity = true; s.FlashOpacity = 30;
        s.RampDurationMinutes = 10; s.SchedulerMultiplier = 2; s.EndSessionOnRampComplete = true;
        s.SchedulerEnabled = true; s.SchedulerStartTime = "16"; s.SchedulerEndTime = "22";
        var now = new DateTime(2026, 10, 19, 17, 0, 0); shell.EngineNow = () => now;
        Click(shell, "BtnStart");
        Assert.True(CoreEngine.IsRunning); Assert.True(shell.Ramp.IsActive);
        var sentinel = Path.Combine(CorePaths.UserData, "logs", "engine_session.active");
        Assert.True(File.Exists(sentinel));
        now = now.AddMinutes(5); shell.TickEngineRamp(); Assert.Equal(45, s.FlashOpacity);
        Click(shell, "BtnStart");
        Assert.False(CoreEngine.IsRunning); Assert.False(shell.Ramp.IsActive); Assert.Equal(30, s.FlashOpacity);
        Assert.False(File.Exists(sentinel));
        Assert.True(shell.Scheduler.ManuallyStoppedDuringSchedule);
        shell.TickEngineScheduler(); Assert.False(CoreEngine.IsRunning);
        now = now.AddHours(6); shell.TickEngineScheduler();
        now = now.AddDays(1).Date.AddHours(17); shell.TickEngineScheduler(); Assert.True(CoreEngine.IsRunning);
        now = now.AddMinutes(10); shell.TickEngineRamp();
        Assert.False(CoreEngine.IsRunning); Assert.Equal(30, s.FlashOpacity);
    });

    private sealed class LayerPlayer : ConditioningControlPanel.Avalonia.Platform.LayeredAudio.ILayerPlayer
    {
        public int Volume { get; set; }
        public void Dispose() { }
    }

    [Fact]
    public Task AudioOnlyForcesBedAndStopPreservesStandaloneMaster() => Run(shell =>
    {
        var old = ConditioningControlPanel.Avalonia.Platform.LayeredAudio.Instance;
        using var layers = new ConditioningControlPanel.Avalonia.Platform.LayeredAudio(_ => new LayerPlayer());
        var file = Path.GetTempFileName();
        try
        {
            ConditioningControlPanel.Avalonia.Platform.LayeredAudio.Instance = layers;
            var s = CoreSettings.Current; s.AudioOnlySession = true; s.AudioLayersEnabled = false;
            s.AudioLayers = new() { new AudioLayerTrack { Path = file, Enabled = true } };
            shell.StartEngine(); Assert.True(layers.IsPlaying); Assert.False(CoreFlash.IsRunning);
            MainShellWindow.StopEngine(); Assert.False(layers.IsPlaying);
            s.AudioLayersEnabled = true;
            shell.StartEngine(); MainShellWindow.StopEngine(); Assert.True(layers.IsPlaying);
        }
        finally { ConditioningControlPanel.Avalonia.Platform.LayeredAudio.Instance = old; File.Delete(file); }
    });

    [Theory]
    [InlineData(4, 1, 0)]
    [InlineData(5, 0, 1)]
    [InlineData(8, 0, 0)]
    [InlineData(23, 1, 0)]
    public Task StartCreditsWpfTimeOfDayCounters(int hour, int night, int morning) => Run(shell =>
    {
        shell.EngineNow = () => new DateTime(2026, 10, 19, hour, 0, 0);
        var s = CoreSettings.Current; int n = s.NightTimeUsageCount, m = s.EarlyMorningUsageCount;
        shell.StartEngine();
        Assert.Equal(n + night, s.NightTimeUsageCount); Assert.Equal(m + morning, s.EarlyMorningUsageCount);
    });

    [Fact]
    public Task ButtonStopAndPanicBothEarnRelapseOnRestart() => Run(shell =>
    {
        var field = typeof(AvApp).GetField("<Achievements>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        var old = field.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("ccp-shell-relapse-");
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir.FullName, "achievements.json")));
        field.SetValue(null, engine);
        try
        {
            Click(shell, "BtnStart"); Click(shell, "BtnStart");
            Assert.NotNull(engine.Progress.LastPanicPressTime);
            Click(shell, "BtnStart"); Assert.True(engine.Progress.IsUnlocked("relapse"));
            engine.Reset();
            shell.HandlePanicKeyPress(DateTime.Now);
            Assert.NotNull(engine.Progress.LastPanicPressTime);
            Click(shell, "BtnStart"); Assert.True(engine.Progress.IsUnlocked("relapse"));
        }
        finally { field.SetValue(null, old); dir.Delete(true); }
    });

    [Fact]
    public Task EngineArmsConsentedTakeoverAndStopRetainsOnlyItsIndependentOptIn() => Run(shell =>
    {
        var old = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            var s = CoreSettings.Current; s.AutonomyModeEnabled = true; s.AutonomyConsentGiven = false;
            shell.StartEngine(); Assert.False(shell.Autonomy.IsEnabled); MainShellWindow.StopEngine();
            s.AutonomyConsentGiven = true;
            shell.StartEngine(); Assert.True(shell.Autonomy.IsEnabled);
            MainShellWindow.StopEngine(); Assert.True(shell.Autonomy.IsEnabled);
            s.AutonomyConsentGiven = false;
            MainShellWindow.StopEngine(); Assert.False(shell.Autonomy.IsEnabled);
        }
        finally { CoreEntitlement.HasPremiumProvider = old; }
    });

    [Fact]
    public Task HeroUsesWpfRingDipWashHeartbeatAndParksWithMotionOff() => Run(shell =>
    {
        var button = shell.Named<Button>("BtnStart")!;
        shell.PaintStartAmbient(.4725, true);
        Assert.Equal(.7, shell.Named<Border>("StartRingExhale")!.Opacity, 6);
        Assert.IsType<LinearGradientBrush>(button.Background);
        shell.FlashStartIgnition(); shell.PaintStartIgnition(.04);
        Assert.Equal(.97, ((ScaleTransform)button.RenderTransform!).ScaleX, 6);
        Assert.InRange(shell.Named<Border>("StartIgnitionFlash")!.Opacity, 0, .55);
        shell.PaintStartIgnition(.83); Assert.Equal(0, shell.Named<Border>("StartRingBurstB")!.Opacity);
        shell.StartEngine(); shell.PaintStartAmbient(2.4, true);
        Assert.Equal(1.012, ((ScaleTransform)button.RenderTransform!).ScaleY, 6);
        Assert.Equal(0, shell.Named<Border>("StartRingExhale")!.Opacity);
        Assert.Equal(Color.FromRgb(255, 107, 107), Assert.IsType<SolidColorBrush>(button.Background).Color);
        CoreSettings.Current.MotionLevel = MotionLevel.Off; shell.ApplyStartHeroState();
        Assert.False(shell.StartHeroTicking); Assert.Equal(1, ((ScaleTransform)button.RenderTransform!).ScaleY);
    });

    [Fact]
    public Task WallpaperArmingOffersBothRecoveryActionsForEighteenSeconds() => Run(shell =>
    {
        var host = shell.Named<StackPanel>("NotificationHost")!;
        var field = typeof(NotificationService).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var old = field.GetValue(AvApp.Notifications);
        try
        {
            AvApp.Notifications.AttachHost(host);
            // Startup toasts from shell.Show (6 on windows-latest) are not this test's subject.
            foreach (var t in host.Children.OfType<Border>()) (t.Tag as DispatcherTimer)?.Stop();
            host.Children.Clear();
            shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.BambiTakeoverTabView>("BambiTakeoverTab")!
                .FindControl<CheckBox>("ChkWallpaperKeep")!.IsChecked = true;
            var toast = Assert.IsType<Border>(Assert.Single(host.Children));
            Assert.Equal(TimeSpan.FromSeconds(18), Assert.IsType<DispatcherTimer>(toast.Tag).Interval);
            var buttons = toast.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Contains(buttons, b => b.Name == "ToastAction");
            Assert.Contains(buttons, b => b.Name == "ToastSecondaryAction");
        }
        finally
        {
            foreach (var toast in host.Children.OfType<Border>()) (toast.Tag as DispatcherTimer)?.Stop();
            host.Children.Clear(); field.SetValue(AvApp.Notifications, old);
        }
    });

    private static void Click(MainShellWindow shell, string name) =>
        shell.Named<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Task Run(Action<MainShellWindow> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null) ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider; var oldStop = CoreEngine.StoppedHook;
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        var oldSink = ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink;
        ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => { };
        var dir = Directory.CreateTempSubdirectory("ccp-start-stop-");
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        CorePaths.EffectiveAssetsProvider = () => dir.FullName;
        var s = CoreSettings.Current; s.MotionLevel = MotionLevel.Reduced;
        s.FlashEnabled = s.SubliminalEnabled = s.BubblesEnabled = s.BouncingTextEnabled = false;
        s.LockCardEnabled = s.PopQuizEnabled = s.MindWipeEnabled = s.MandatoryVideosEnabled = s.BubbleCountEnabled = false;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs(); CoreEngine.StoppedHook = shell.OnEngineStopped; test(shell);
        }
        finally
        {
            CoreEngine.Stop(); shell.Autonomy.Stop(); shell.Close(); CoreEngine.StoppedHook = oldStop;
            // Toasts raised with no host (achievement unlocks) would replay into the next test's AttachHost.
            ((System.Collections.IList)typeof(NotificationService).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(AvApp.Notifications)!).Clear();
            CoreSettings.ServiceProvider = oldSettings; CorePaths.EffectiveAssetsProvider = oldAssets;
            ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = oldSink; dir.Delete(true);
        }
        return Task.CompletedTask;
    });
}

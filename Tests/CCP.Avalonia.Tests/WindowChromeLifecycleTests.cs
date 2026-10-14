using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class WindowChromeLifecycleTests
{
    private static Task WithShell(bool avatar, Action<MainShellWindow> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        CoreSettings.ServiceProvider = () => Settings;
        var s = Settings.Current;
        s.AvatarEnabled = avatar;
        s.HasAcceptedAgeVerification = true;
        s.Welcomed = true;
        s.AvatarTubeDetached = s.AvatarMuted = false;
        s.MotionLevel = MotionLevel.Off;
        s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
        CoreEntitlement.HasPremiumProvider = () => true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            s.AutonomyModeEnabled = true; // startup intentionally clears the opt-out run flag
            check(shell);
        }
        finally
        {
            if (AvApp.Sessions is { } runner) runner.SessionLog.LogReady -= shell.OnSessionLogReady;
            AvApp.Sessions?.Stop(); AvApp.Sessions = null;
            CoreSession.IsSessionRunningProvider = null;
            CoreEngine.StoppedHook = null; CoreEngine.Stop();
            shell.RequestExit(); shell.Tray?.Dispose(); Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = old; CoreEntitlement.HasPremiumProvider = oldPremium;
        }
        return Task.CompletedTask;
    });

    private static readonly SettingsService Settings = new();

    [Fact]
    public Task ScalingChangeQuiescesSharedOverlaySpawns() => WithShell(false, shell =>
    {
        // Drive the backend callback (Avalonia 12 keeps it protected on ITopLevelImpl).
        var callback = (Action<double>)typeof(global::Avalonia.Platform.ITopLevelImpl)
            .GetProperty("ScalingChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(shell.PlatformImpl)!;
        callback(2);
        Assert.True(ConditioningControlPanel.Services.UI.DisplayChangeCoordinator.SpawnsSuppressed);
    });

    [Fact]
    public Task WindowManagerMaximizeUpdatesChromeGlyph() => WithShell(false, shell =>
    {
        shell.WindowState = WindowState.Maximized;
        Assert.Equal("❐", shell.Named<Button>("BtnMaximize")!.Content);
        shell.WindowState = WindowState.Normal;
        Assert.Equal("☐", shell.Named<Button>("BtnMaximize")!.Content);
    });

    [Fact]
    public Task ActualCloseRestoresSessionBeforePublicClosingAndDoesNotOpenRecap() => WithShell(false, shell =>
    {
        CoreSettings.Current.FlashEnabled = false;
        CoreSettings.Current.FlashFrequency = 123;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        runner.SessionLog.LogReady += shell.OnSessionLogReady;
        runner.Start(new Session { Id = "chrome-exit", Name = "Chrome exit", DurationMinutes = 20,
            Settings = new SessionSettings { FlashEnabled = true, FlashPerHour = 150 } });
        Assert.True(CoreSettings.Current.FlashEnabled);
        Assert.Equal(150, CoreSettings.Current.FlashFrequency);
        bool restored = false;
        shell.Closing += (_, _) => restored = !runner.IsRunning && !CoreSettings.Current.FlashEnabled
            && CoreSettings.Current.FlashFrequency == 123;
        shell.Close(); Dispatcher.UIThread.RunJobs();
        Assert.True(restored);
        Assert.False(runner.IsRunning);
        Assert.Empty(shell.OwnedWindows);
        runner.SessionLog.LogReady -= shell.OnSessionLogReady;
    });

    [Fact]
    public Task TrayHideKeepsSessionAndDoesNotRaiseExitCleanup() => WithShell(false, shell =>
    {
        shell.CreateTray(); shell.TrayHostPresent = () => true;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        runner.Start(new Session { Id = "chrome-tray", Name = "Chrome tray", DurationMinutes = 20 });
        int closing = 0;
        shell.Closing += (_, _) => closing++;
        shell.Close();
        Assert.True(runner.IsRunning, $"session ended on tray close; visible={shell.IsVisible}, tray={shell.Tray is not null}");
        Assert.False(shell.IsVisible);
        Assert.Equal(0, closing);
        shell.ShowFromTray();
        Assert.True(shell.IsVisible);
    });

    [Fact]
    public Task TrayWakeCanExplicitlyUnmuteTemporaryMinimizeMute() => WithShell(true, shell =>
    {
        var tube = Assert.IsType<AvatarTubeWindow>(typeof(MainShellWindow)
            .GetField("_avatarTubeWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell));
        shell.WindowState = WindowState.Minimized;
        Assert.True(tube.IsMuted);
        Assert.False(CoreSettings.Current.AvatarMuted);
        shell.WakeBambiUp();
        Assert.True(tube.IsDetached);
        tube.OnMenuMute();
        Assert.False(tube.IsMuted);
        Assert.False(CoreSettings.Current.AvatarMuted);
        shell.WindowState = WindowState.Normal;
        Assert.False(tube.IsMuted);
        Assert.False(CoreSettings.Current.AvatarMuted);
    });

    [Fact]
    public Task AttachedMinimizeBorrowsTakeoverAndMuteThenRestoreReattaches() => WithShell(true, shell =>
    {
        var tube = Assert.IsType<AvatarTubeWindow>(typeof(MainShellWindow)
            .GetField("_avatarTubeWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell));
        Assert.True(shell.Autonomy.Start(withTimer: false));
        shell.WindowState = WindowState.Minimized;
        Assert.False(shell.Autonomy.IsEnabled);
        Assert.True(tube.IsMuted);
        Assert.False(CoreSettings.Current.AvatarMuted);
        shell.WindowState = WindowState.Normal;
        Assert.True(shell.Autonomy.IsEnabled);
        Assert.False(tube.IsMuted);
        shell.WindowState = WindowState.Maximized;
        Assert.True(tube.IsDetached);
        shell.WindowState = WindowState.Minimized;
        Assert.True(shell.Autonomy.IsEnabled);
        Assert.False(tube.IsMuted);
        shell.WindowState = WindowState.Normal;
        Assert.False(tube.IsDetached);
    });
}

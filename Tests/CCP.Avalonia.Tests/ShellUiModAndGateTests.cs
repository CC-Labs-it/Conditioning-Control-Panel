using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ShellUiModAndGateTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
        CoreSettings.Current.OfflineMode = true;
    }

    private static void RaiseMod() => CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true));
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Fact]
    public void TakeoverGateFollowsPremiumAndFreeDayOnNavigation() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        bool premium = false, free = false;
        CoreEntitlement.HasPremiumProvider = () => premium;
        CoreEntitlement.IsFreeTodayProvider = key => key == "takeover" && free;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Settle();
            var tab = shell.Named<Control>("BambiTakeoverTab")!;
            var locked = tab.FindControl<Control>("AutonomyLocked")!;
            var unlocked = tab.FindControl<Control>("AutonomyUnlocked")!;
            var veil = tab.FindControl<Control>("BambiTakeoverGate")!;
            void Check(bool open)
            {
                shell.ShowTab("bambitakeover"); Settle();
                Assert.Equal(!open, locked.IsVisible);
                Assert.Equal(open, unlocked.IsVisible);
                Assert.Equal(!open, veil.IsVisible);
            }
            Check(false);
            premium = true; Check(true);
            premium = false; free = true; Check(true);
            free = false; Check(false);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public void RunningTakeoverButtonRepaintsAfterModChange() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        string accent = "#123456";
        CoreMods.AccentColorHexProvider = () => accent;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Settle();
            CoreSettings.Current.AutonomyModeEnabled = true;
            CoreSettings.Current.AutonomyConsentGiven = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.True(shell.Autonomy.Start(withTimer: false));
            Settle();
            var button = shell.Named<Control>("BambiTakeoverTab")!.FindControl<Button>("BtnAutonomyStartStop")!;
            RaiseMod(); Settle();
            Assert.Equal(Color.Parse(accent), ColorOf(button.Foreground));
            accent = "#654321"; RaiseMod(); Settle();
            Assert.Equal(Color.Parse(accent), ColorOf(button.Foreground));
            CoreSettings.Current.AutonomyModeEnabled = false; shell.Autonomy.Stop(); RaiseMod(); Settle();
            Assert.Equal(Color.FromRgb(144, 238, 144), ColorOf(button.Foreground));
        }
        finally { shell.Close(); }
    });

    [Fact]
    public void StartThenStopBeforeTheQueuedRepaintLeavesStart() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        CoreEntitlement.HasPremiumProvider = () => true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Settle();
            var s = CoreSettings.Current;
            s.AutonomyConsentGiven = true;
            Assert.True(shell.SetAutonomyEnabled(true));
            Assert.False(shell.SetAutonomyEnabled(false));
            Settle();   // the queued start and stop callbacks run only now
            var button = shell.Named<Control>("BambiTakeoverTab")!.FindControl<Button>("BtnAutonomyStartStop")!;
            Assert.False(s.AutonomyModeEnabled);
            Assert.False(shell.Autonomy.IsEnabled);
            Assert.Equal(Color.FromRgb(144, 238, 144), ColorOf(button.Foreground));
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_start_2"), Assert.IsType<TextBlock>(button.Content).Text);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public void LeaderboardModeButtonsRepaintAfterModChangeWithoutChangingMode() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        string accent = "#123456";
        CoreMods.AccentColorHexProvider = () => accent;
        var view = new LeaderboardTabView();
        var host = new Window { Content = view };
        try
        {
            host.Show(); Settle();
            var monthly = view.FindControl<Button>("BtnLeaderboardMonthly")!;
            var allTime = view.FindControl<Button>("BtnLeaderboardAllTime")!;
            Assert.True(view.IsAllTimeMode);
            Assert.Equal(Color.Parse(accent), ColorOf(monthly.Foreground));
            Assert.Equal(Color.Parse("#FFD700"), ColorOf(allTime.BorderBrush));
            accent = "#654321"; RaiseMod(); Settle();
            Assert.True(view.IsAllTimeMode);
            Assert.Equal(Color.Parse(accent), ColorOf(monthly.Foreground));
            view.SetLeaderboardMode(false).GetAwaiter().GetResult();
            Assert.Equal(Color.Parse(accent), ColorOf(monthly.Background));
            accent = "#236789"; RaiseMod(); Settle();
            Assert.False(view.IsAllTimeMode);
            Assert.Equal(Color.Parse(accent), ColorOf(monthly.Background));
            Assert.Equal(Color.Parse(accent), ColorOf(allTime.Foreground));
            Assert.Equal(Color.Parse(accent), ColorOf(allTime.BorderBrush));
        }
        finally { host.Close(); }
    });
}

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionShellEventsTests
{
    [Fact]
    public void AwardRefreshesVisibleRoomAndSwitchRefreshesEvenWhenHidden() => WithShell((shell, hero) =>
    {
        var s = CoreSettings.Current;
        CompanionCore.AddCompanionXP(7, "test");
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("7 /", hero.XpLabel);
        shell.ShowTab("achievements");
        CompanionCore.AddCompanionXP(2, "test");
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("7 /", hero.XpLabel); // WPF avoids repainting a hidden room on ordinary XP.
        s.CompanionProgressData[2] = new CompanionProgress { Level = 4, CurrentXP = 19 };
        CompanionCore.SwitchCompanion((CompanionId)2);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, hero.Level);
        Assert.StartsWith("19 /", hero.XpLabel);
    });

    [Fact]
    public void DrainRefreshesTheHeaderAndVisibleRoomWithoutChangingPlayerLevel() => WithShell((shell, hero) =>
    {
        var s = CoreSettings.Current;
        s.CompanionPerk = CompanionBonusType.XPDrain;
        s.MotionLevel = MotionLevel.Off; // the header readout snaps (the WPF odometer would count 23 -> 20 over 0.7s)
        s.PlayerXP = 23;
        s.PlayerLevel = 4;
        shell.UpdateLevelDisplay();
        s.ActiveCompanionProgress.CurrentXP = 11;
        CompanionCore.DrainTick(DateTime.UtcNow);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("20 /", shell.FindControl<TextBlock>("TxtXP")!.Text);
        Assert.StartsWith("11 /", hero.XpLabel);
        Assert.Equal(4, s.PlayerLevel);
        shell.ShowTab("achievements");
        CompanionCore.DrainTick(DateTime.UtcNow);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("17 /", shell.FindControl<TextBlock>("TxtXP")!.Text);
    });

    [Fact]
    public void DrainFlashesTheHeroCardXpBar() => WithShell((shell, _) =>
    {
        var s = CoreSettings.Current;
        s.CompanionPerk = CompanionBonusType.XPDrain;
        s.PlayerXP = 23;
        s.ActiveCompanionProgress.CurrentXP = 11;
        var overlay = shell.GetLogicalDescendants().OfType<CompanionHeroCard>().Single()
            .FindControl<Border>("PrgCompanion0FlashOverlay")!;
        var header = shell.FindControl<Border>("XPBarFlashOverlay")!;
        var clock = new SharedDeeperThemeTests.StepClock();
        foreach (var bar in new[] { header, overlay })
        {
            Assert.Equal(0, bar.Opacity);
            bar.SetValue(SharedDeeperThemeTests.StepClock.Property, clock.Instance);
        }
        CompanionCore.DrainTick(DateTime.UtcNow);
        // WPF MainWindow.Companion.cs:116-130: 250 ms EaseOut forward, then auto-reverse.
        clock.Step(0);
        foreach (var (ms, opacity) in new[] { (125, 0.75), (250, 1.0), (375, 0.75), (500, 0.0) })
        {
            clock.Step(ms);
            foreach (var bar in new[] { header, overlay }) Assert.Equal(opacity, bar.Opacity, 5);
        }
    });

    [Fact]
    public void RoomMuteRefreshesTubeMenuAndKeepsTheBubble() => WithShell((shell, hero) =>
    {
        var tube = shell.Tube!;
        tube.GigglePriority("still here", playSound: false, aiGenerated: false);
        var bubble = tube.FindControl<Border>("SpeechBubble")!;
        Assert.True(bubble.IsVisible);
        hero.ToggleMuteCommand.Execute(null);
        Assert.True(tube.IsMuted);
        Assert.Equal("menu_mute_avatar_on", (tube.FindControl<MenuItem>("MenuItemMute")!.Header as IconLabel)?.Key);
        Assert.True(bubble.IsVisible);
        tube.OnMenuMute();
        Assert.False(hero.IsMuted);
    });

    [Fact]
    public void MilestonesNotifyPlayAtWpfVolumeAndStopOnPanicAndClose() => WithShell((shell, hero) =>
    {
        var s = CoreSettings.Current;
        var notices = new List<(string Title, string Body)>();
        var oldSink = OsNotifications.Sink;
        var oldPlayer = CoreAudio.PlayStoppableProvider;
        var played = new List<(string Path, float Volume, string Tag)>();
        var stops = 0;
        OsNotifications.Sink = (title, body) => notices.Add((title, body));
        CoreAudio.PlayStoppableProvider = (path, volume, tag, _, _) =>
        {
            played.Add((path, volume, tag));
            return () => stops++;
        };
        try
        {
            s.MasterVolume = 50;
            s.ActiveCompanionProgress.Level = 8;
            CompanionCore.AddCompanionXP(s.ActiveCompanionProgress.XPForNextLevel, "test");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(9, hero.Level);
            Assert.Empty(notices);
            Assert.Empty(played);
            CompanionCore.AddCompanionXP(s.ActiveCompanionProgress.XPForNextLevel, "test");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Companion level up", Assert.Single(notices).Title);
            Assert.EndsWith(" reached Level 10.", notices[0].Body);
            Assert.Equal("level-up", Assert.Single(played).Tag);
            Assert.Equal(Math.Max(0.01f, (float)Math.Pow(0.5, 1.5) * 0.2625f), played[0].Volume);
            s.ActiveCompanionProgress.Level = CompanionProgress.MaxLevel - 1;
            CompanionCore.AddCompanionXP(s.ActiveCompanionProgress.XPForNextLevel, "test");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Max level", notices[1].Title);
            Assert.Equal(2, played.Count);
            Assert.Equal(1, stops); // the new milestone cuts off the previous sound.
            PanicSurfaces.StopAll("test", shell);
            Assert.Equal(2, stops);
            PanicSurfaces.StopAll("test", shell);
            Assert.Equal(2, stops);
            s.ActiveCompanionProgress.Level = 19;
            CompanionCore.AddCompanionXP(s.ActiveCompanionProgress.XPForNextLevel, "test");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, played.Count);
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, stops);
            s.ActiveCompanionProgress.Level = 9;
            CompanionCore.AddCompanionXP(s.ActiveCompanionProgress.XPForNextLevel, "after-close");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, notices.Count); // static event subscriptions come off at close.
            Assert.Equal(3, played.Count);
        }
        finally { OsNotifications.Sink = oldSink; CoreAudio.PlayStoppableProvider = oldPlayer; }
    });

    [Fact]
    public void WakeShowsDetachedTubeAndTheWpfMorningGreeting() => WithShell((shell, hero) =>
    {
        CoreSettings.Current.AvatarEnabled = false;
        shell.HideAvatarTube();
        shell.WakeBambiUp();
        Dispatcher.UIThread.RunJobs();
        Assert.True(CoreSettings.Current.AvatarEnabled);
        Assert.True(shell.Tube!.IsDetached);
        Assert.Equal("Good morning.", shell.Tube.FindControl<TextBlock>("TxtSpeech")!.Text);
    });

    [Fact]
    public void WorkerAwardsMarshalToUiAndQueuedAwardsDoNotRepaintAfterClose() => WithShell((shell, hero) =>
    {
        System.Threading.Tasks.Task.Run(() => CompanionCore.AddCompanionXP(5, "worker")).GetAwaiter().GetResult();
        Assert.StartsWith("0 /", hero.XpLabel);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("5 /", hero.XpLabel);
        System.Threading.Tasks.Task.Run(() => CompanionCore.AddCompanionXP(2, "queued")).GetAwaiter().GetResult();
        shell.RequestExit();
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("5 /", hero.XpLabel);
    });

    private static void WithShell(Action<MainShellWindow, CompanionHeroCardViewModel> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.AvatarEnabled = true;
        s.AvatarMuted = false;
        s.AvatarTubeDetached = false;
        s.AutonomyResumeOnStartup = false;
        s.ActiveCompanionId = 0;
        s.CompanionPerk = CompanionBonusType.AutonomyBonus;
        s.CompanionProgressData[0] = new CompanionProgress();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var hero = shell.GetLogicalDescendants().OfType<CompanionHeroCard>().Single().ViewModel!;
            body(shell, hero);
        }
        finally
        {
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = oldProvider;
        }
    });
}

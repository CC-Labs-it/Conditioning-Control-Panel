using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class QuestShellCompletionTests
{
    private static void Run(Action<MainShellWindow, QuestService> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldQuests = AvApp.Quests;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.AvatarEnabled) = (true, true, 1, false);
        s.MotionLevel = MotionLevel.Full;
        s.SuppressPerkNotifications = true; // WPF's opt-out does not suppress the in-app burst.
        var dir = Directory.CreateTempSubdirectory("ccp-quest-shell-").FullName;
        using var quests = new QuestService(null, dir);
        quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
        quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
        AvApp.Quests = quests;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.Activate();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("settings");
            shell.UpdateLayout();
            body(shell, quests);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            AvApp.Quests = oldQuests;
            CoreSettings.ServiceProvider = oldSettings;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    });

    [Fact]
    public void CompletionVoiceHonoursOptOutAndBothStopPathsCutIt() => Run((shell, quests) =>
    {
        var oldBuild = ConditioningControlPanel.Services.Flash.FlashVoicePool.Build;
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var file = Path.GetTempFileName();
        int played = 0, stopped = 0;
        try
        {
            ConditioningControlPanel.Services.Flash.FlashVoicePool.Reset();
            ConditioningControlPanel.Services.Flash.FlashVoicePool.Build = _ => new List<string> { file };
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { played++; return () => stopped++; };
            CoreSettings.Current.FlashAudioEnabled = false;
            quests.TrackBubblesPopped(10_000);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, played);
            CoreSettings.Current.SuppressPerkNotifications = false;
            quests.TrackSpiralMinutes(10_000);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, played);
            AvApp.StopDesktopOverlays();
            Assert.Equal(1, stopped);
            for (int i = 0; i < 200; i++) quests.TrackFlashImage();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, played);
            PanicSurfaces.All.Single(s => s.Id == "quest-voice").Stop(shell);
            Assert.Equal(2, stopped);
        }
        finally
        {
            ConditioningControlPanel.Services.Flash.FlashCelebrationVoice.Stop();
            ConditioningControlPanel.Services.Flash.FlashVoicePool.Build = oldBuild;
            ConditioningControlPanel.Services.Flash.FlashVoicePool.Reset();
            CoreAudio.PlayStoppableProvider = oldPlay;
            File.Delete(file);
        }
    });

    private static AmbientFxCanvas[] BurstLayers(Control anchor) =>
        OverlayLayer.GetOverlayLayer(anchor)!.Children.OfType<AmbientFxCanvas>().Where(c => c.Width == 560).ToArray();

    [Fact]
    public void OffTabCompletionBurstsAtTheOwningNavRowsCentre() => Run((shell, quests) =>
    {
        var anchor = shell.NavAnchorForTab("quests")!;
        Assert.Equal("you", CoreNavSection(anchor));
        Assert.Empty(BurstLayers(anchor));
        Assert.False(shell.Named<QuestsTabView>("QuestsTab")!.IsVisible);
        quests.TrackBubblesPopped(QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!.TargetValue);
        Dispatcher.UIThread.RunJobs();
        var layer = Assert.Single(BurstLayers(anchor));
        var expected = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), OverlayLayer.GetOverlayLayer(anchor)!)!.Value;
        Assert.Equal(expected.X, Canvas.GetLeft(layer) + 280, 3);
        Assert.Equal(expected.Y, Canvas.GetTop(layer) + 280, 3);
        Assert.True(shell.Named<QuestsTabView>("QuestsTab")!.FindControl<Border>("QuestCompleteBanner")!.IsVisible);
        Assert.Equal("settings", shell.CurrentTab);
    });

    private static string? CoreNavSection(Button button) => button.Tag as string;

    [Fact]
    public void VisibleBarsKeepTheirBurstAndRoadmapUsesTheNavFallback() => Run((shell, quests) =>
    {
        var anchor = shell.NavAnchorForTab("quests")!;
        shell.ShowTab("quests");
        shell.UpdateLayout();
        var view = shell.Named<QuestsTabView>("QuestsTab")!;
        quests.TrackBubblesPopped(10_000);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, view.Bursts);
        Assert.Single(BurstLayers(anchor));
        view.FindControl<Button>("BtnQuestSubRoadmap")!.RaiseEvent(
            new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        quests.TrackSpiralMinutes(10_000);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, view.Bursts);
        Assert.Equal(2, BurstLayers(anchor).Length);
    });

    [Fact]
    public void MotionOffAndMinimizedWindowsRefuseOffTabBursts() => Run((shell, quests) =>
    {
        var anchor = shell.NavAnchorForTab("quests")!;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        quests.TrackBubblesPopped(10_000);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(BurstLayers(anchor));
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        shell.WindowState = WindowState.Minimized;
        quests.TrackSpiralMinutes(10_000);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, quests.GetDailyQuestsCompletedToday());
        Assert.Empty(BurstLayers(anchor));
    });
}

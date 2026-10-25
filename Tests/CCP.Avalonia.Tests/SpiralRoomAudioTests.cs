using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralRoomAudioTests
{
    [Fact]
    public Task ShellSpiralDoorPlaysFogCueOncePerVisit() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldFuse = global::ConditioningControlPanel.Avalonia.App.DescentCountdown;
        var settings = CoreSettings.Current;
        var oldAt = settings.DescentCeremonyAtUtc;
        var oldWitnessed = settings.DescentFuseMaxPhaseWitnessed;
        var oldOffline = settings.OfflineMode;
        var oldVolume = settings.MasterVolume;
        var oldEnabled = settings.DescentCountdownAudio;
        using var fuse = new DescentCountdownService();
        global::ConditioningControlPanel.Avalonia.App.DescentCountdown = fuse;
        var cues = new List<string>();
        CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => cues.Add(tag);
        settings.OfflineMode = true;
        settings.MasterVolume = 50;
        settings.DescentCountdownAudio = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.Named<SpiralTabView>("SpiralTab")!.SoundResolver = name => name;
            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("spiral");
            shell.ShowTab("spiral");
            Assert.Equal(new[] { "descent-fog-enter" }, cues.FindAll(c => c == "descent-fog-enter"));
            shell.ShowTab("settings");
            shell.ShowTab("spiral");
            Assert.Equal(2, cues.FindAll(c => c == "descent-fog-enter").Count);
        }
        finally
        {
            shell.Close();
            fuse.Dispose();
            CoreAudio.PlayOneShotProvider = oldAudio;
            global::ConditioningControlPanel.Avalonia.App.DescentCountdown = oldFuse;
            settings.MasterVolume = oldVolume;
            settings.DescentCountdownAudio = oldEnabled;
            settings.DescentCeremonyAtUtc = oldAt;
            settings.DescentFuseMaxPhaseWitnessed = oldWitnessed;
            settings.OfflineMode = oldOffline;
            CoreSettings.SaveImmediate();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RepaintIsSilentAndLeavingRearmsFogAndSplash() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var oldEnabled = CoreSettings.Current.DescentCountdownAudio;
        var played = new List<string>();
        CoreSettings.Current.MasterVolume = 50;
        CoreSettings.Current.DescentCountdownAudio = true;
        CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => played.Add(tag);
        var view = new SpiralTabView { SoundResolver = name => name };
        var apply = typeof(SpiralTabView).GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var splash = typeof(SpiralTabView).GetMethod("ShowSplash", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            apply.Invoke(view, new object[] { SpiralRoomState.Fog });
            apply.Invoke(view, new object[] { SpiralRoomState.Fog });
            view.OnTabShown(); // re-evaluation within the entry must not re-arm audio
            splash.Invoke(view, null);
            splash.Invoke(view, null);
            Assert.Equal(new[] { "descent-fog-enter", "descent-spiral-open" }, played);

            view.IsVisible = false;
            view.IsVisible = true;
            apply.Invoke(view, new object[] { SpiralRoomState.Fog });
            splash.Invoke(view, null);
            Assert.Equal(new[] { "descent-fog-enter", "descent-spiral-open", "descent-fog-enter", "descent-spiral-open" }, played);
        }
        finally
        {
            view.IsVisible = false;
            CoreAudio.PlayOneShotProvider = oldAudio;
            CoreSettings.Current.MasterVolume = oldVolume;
            CoreSettings.Current.DescentCountdownAudio = oldEnabled;
        }
        return Task.CompletedTask;
    });
}

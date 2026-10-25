using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using Head = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Offscreen only; the backend is replaced so the test never opens an audio device.</summary>
public sealed class SpiralRoomEntrySfxTests
{
    [Fact]
    public Task EntrySpendsEachCueOnce_RepaintsKeepItSpent_ExitRearms() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.MasterVolume, s.DescentCountdownAudio, s.MotionLevel, s.DescentCeremonyAtUtc,
                   s.DescentMigrationCompleted, s.PendingDescentMigrationChoice);
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldResolver = CoreModArt.AudioOverridePathProvider;
        var oldFuse = Head.DescentCountdown;
        var asset = Path.Combine(TestUserDataProfile.Root, "spiral-entry.mp3");
        var played = new List<string>();
        using var fuse = new DescentCountdownService();
        Window? window = null;
        try
        {
            File.WriteAllBytes(asset, Array.Empty<byte>());
            CoreModArt.AudioOverridePathProvider = _ => asset;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => played.Add(tag);
            Head.DescentCountdown = fuse;
            s.MasterVolume = 50;
            s.DescentCountdownAudio = true;
            s.MotionLevel = MotionLevel.Off;
            s.DescentMigrationCompleted = false;
            s.PendingDescentMigrationChoice = null;
            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            var tab = new SpiralTabView();
            window = new Window { Content = tab, Width = 800, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            tab.OnTabShown(); // the second refresh on the same navigation must not double-whoosh
            Assert.Equal(new[] { "descent-fog-enter" }, played);
            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(played); // a phase repaint is not a new entry

            // Spiral selection still needs the descent service; drive just the existing splash's
            // retry path to pin its entry latch without pretending it is a live map navigation.
            var splash = typeof(SpiralTabView).GetMethod("ShowSplash", BindingFlags.NonPublic | BindingFlags.Instance)!;
            splash.Invoke(tab, null);
            splash.Invoke(tab, null);
            Assert.Equal(new[] { "descent-fog-enter", "descent-spiral-open" }, played);
            tab.IsVisible = false;
            tab.IsVisible = true;
            tab.OnTabShown();
            splash.Invoke(tab, null);
            Assert.Equal(new[] { "descent-fog-enter", "descent-spiral-open", "descent-fog-enter", "descent-spiral-open" }, played);
        }
        finally
        {
            window?.Close();
            CoreAudio.PlayOneShotProvider = oldAudio;
            CoreModArt.AudioOverridePathProvider = oldResolver;
            Head.DescentCountdown = oldFuse;
            (s.MasterVolume, s.DescentCountdownAudio, s.MotionLevel, s.DescentCeremonyAtUtc,
             s.DescentMigrationCompleted, s.PendingDescentMigrationChoice) = old;
            File.Delete(asset);
        }
        return Task.CompletedTask;
    });
}

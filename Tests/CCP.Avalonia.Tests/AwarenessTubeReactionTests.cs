using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>shell-awareness: an observed window change makes the tube say the preset line
/// (WPF AvatarTubeWindow.Reactions.cs OnActivityChanged, AI off), and the cooldown holds the next.</summary>
public sealed class AwarenessTubeReactionTests
{
    [Fact]
    public Task FlashClickSpeaksAndPanicClearsTheBubble() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        PanicAftermath.EndQuiet();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.CustomCompanionPhrases.Add(new CustomCompanionPhrase { Id = "test-click", Category = "FlashClicked", Text = "Click" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            AvatarTubeWindow.TestLive = tube;
            tube.ReactToFlashClick(1);
            Assert.False(tube.HasBubbleUp);
            tube.ReactToFlashClick(0);
            Assert.True(tube.HasBubbleUp);
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.StopAll("test");
            Assert.False(tube.HasBubbleUp);
            tube.ReactToFlashClick(0);
            Assert.False(tube.HasBubbleUp);
        }
        finally
        {
            AvatarTubeWindow.TestLive = null;
            tube.Close();
            PanicAftermath.EndQuiet();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FlashPreAnnouncesAndPanicCancels() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        PanicAftermath.EndQuiet();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.FlashAudioEnabled = true;
        CoreSettings.Current.CustomCompanionPhrases.Add(new CustomCompanionPhrase { Id = "test-pre", Category = "FlashPre", Text = "Incoming" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            AvatarTubeWindow.TestLive = tube;
            var generation = FlashOverlay.Generation;
            Assert.True(FlashOverlay.Announce(generation));
            Assert.False(tube.HasBubbleUp);
            Assert.Equal(1, tube._flashCounter);
            CoreSettings.Current.FlashAudioEnabled = false;
            for (var i = 0; i < 3; i++) Assert.True(FlashOverlay.Announce(generation));
            Assert.False(tube.HasBubbleUp);
            Assert.True(FlashOverlay.Announce(generation));
            Assert.True(tube.HasBubbleUp);
            Assert.Equal(5, tube._flashCounter);
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.StopAll("test");
            Assert.False(tube.HasBubbleUp);
            Assert.False(FlashOverlay.Announce(generation));
            Assert.Equal(5, tube._flashCounter);
        }
        finally
        {
            AvatarTubeWindow.TestLive = null;
            tube.Close();
            PanicAftermath.EndQuiet();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FlashStopCutsAnActivePrePhrase() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        PanicAftermath.EndQuiet();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.FlashAudioEnabled = false;
        CoreSettings.Current.CustomCompanionPhrases.Add(new CustomCompanionPhrase { Id = "test-pre", Category = "FlashPre", Text = "Incoming" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Assert.True(FlashOverlay.Announce(FlashOverlay.Generation));
            Assert.True(tube.HasBubbleUp);
            FlashOverlay.CloseAll(final: false);
            Assert.False(tube.HasBubbleUp);
        }
        finally
        {
            tube.Close();
            PanicAftermath.EndQuiet();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FlashStopPreservesUnrelatedLineAfterPanic() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        PanicAftermath.EndQuiet();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.FlashAudioEnabled = false;
        CoreSettings.Current.CustomCompanionPhrases.Add(new CustomCompanionPhrase { Id = "test-pre", Category = "FlashPre", Text = "Incoming" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Assert.True(FlashOverlay.Announce(FlashOverlay.Generation));
            Assert.True(tube.HasBubbleUp);
            tube.PanicSilence();
            tube.Giggle("Other speech");
            Assert.True(tube.IsSpeaking);
            FlashOverlay.CloseAll(final: false);
            Assert.True(tube.IsSpeaking);
        }
        finally
        {
            tube.Close();
            PanicAftermath.EndQuiet();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FlashStopRemovesQueuedPrePhraseWithoutStoppingOtherSpeech() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        PanicAftermath.EndQuiet();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.FlashAudioEnabled = false;
        CoreSettings.Current.CustomCompanionPhrases.Add(new CustomCompanionPhrase { Id = "test-pre", Category = "FlashPre", Text = "Incoming" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.Giggle("Other speech");
            Assert.True(tube.HasBubbleUp);
            Assert.True(FlashOverlay.Announce(FlashOverlay.Generation));
            Assert.Equal(1, tube.QueuedSpeechCount);
            FlashOverlay.CloseAll(final: false);
            Assert.True(tube.HasBubbleUp);
            Assert.Equal(0, tube.QueuedSpeechCount);
        }
        finally
        {
            tube.Close();
            PanicAftermath.EndQuiet();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AWindowChangeSpeaksOnceThenTheCooldownHolds() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AwarenessReactionCooldownSeconds = 600;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            // App.WindowAwareness is process-global: Show starts it polling the REAL desktop (any focus
            // change - another gate's app - reacts and arms the cooldown), and an earlier test may have
            // armed it already. Only this test's events may drive the tube.
            var awareness = global::ConditioningControlPanel.Avalonia.App.WindowAwareness;
            awareness.Stop();
            typeof(WindowAwarenessService).GetField("_lastReactionTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(awareness, DateTime.MinValue);
            Dispatcher.UIThread.RunJobs();
            tube._startupTime = DateTime.MinValue;   // past WPF's 3 s startup quiet
            var bubble = tube.FindControl<Border>("SpeechBubble")!;
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            bubble.IsVisible = false;
            text.Text = "";

            tube.OnActivityChanged(null, new ActivityChangedEventArgs(
                ActivityCategory.Gaming, ActivityCategory.Unknown, "Steam", "Steam"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(bubble.IsVisible);
            while (tube.TypewriterFrame is { } frame) frame();
            Assert.False(string.IsNullOrWhiteSpace(text.Text));

            bubble.IsVisible = false;
            text.Text = "";
            tube.OnActivityChanged(null, new ActivityChangedEventArgs(
                ActivityCategory.Media, ActivityCategory.Gaming, "YouTube", "YouTube"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(bubble.IsVisible);          // MarkReaction armed the 600 s cooldown
            Assert.Equal("", text.Text);
        }
        finally
        {
            tube.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}

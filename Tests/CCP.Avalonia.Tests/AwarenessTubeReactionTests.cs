using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
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

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubePriorityTypewriterTests
{
    [Fact]
    public async Task PriorityTestsLeaveTheirCallersSettingsUntouched()
    {
        var previous = CoreSettings.ServiceProvider;
        var callerSettings = new SettingsService();
        Func<SettingsService?> provider = () => callerSettings;
        try
        {
            CoreSettings.ServiceProvider = provider;
            callerSettings.Current.AvatarEnabled = false;
            callerSettings.Current.AvatarMuted = false;
            callerSettings.Current.BubbleDurationSeconds = 7;

            await PriorityLineTypesAndKeepsReadingTime(true, 30, 2.3);
            Assert.Same(provider, CoreSettings.ServiceProvider);
            Assert.False(callerSettings.Current.AvatarEnabled);
            Assert.False(callerSettings.Current.AvatarMuted);
            Assert.Equal(7, callerSettings.Current.BubbleDurationSeconds);

            await PriorityTypingRestoresLinksAndPanicCancelsStaleFrames();
            Assert.Same(provider, CoreSettings.ServiceProvider);
            Assert.False(callerSettings.Current.AvatarEnabled);
            Assert.False(callerSettings.Current.AvatarMuted);
            Assert.Equal(7, callerSettings.Current.BubbleDurationSeconds);
        }
        finally { CoreSettings.ServiceProvider = previous; }
    }

    [Theory]
    [InlineData(true, 30, 2.3)]
    [InlineData(false, 75, 2.75)]
    public Task PriorityLineTypesAndKeepsReadingTime(bool ai, int stepMs, double seconds) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        AvatarTubeWindow? tube = null;
        try
        {
            settings.Current.AvatarEnabled = true;
            settings.Current.AvatarMuted = true;
            settings.Current.BubbleDurationSeconds = 2;
            tube = new AvatarTubeWindow(null);
            tube.Show();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.GigglePriority("1234567890", playSound: false, aiGenerated: ai);
            Assert.True(tube.HasBubbleUp);
            Assert.Equal("", text.Text);
            Assert.Equal(TimeSpan.FromMilliseconds(stepMs), tube.TypewriterInterval);
            Assert.Equal(TimeSpan.FromSeconds(seconds), tube.SpeechHideDelay);
            tube.TypewriterFrame!();
            Assert.Equal("1", text.Text);
            while (tube.TypewriterFrame is { } frame) frame();
            Assert.Equal("1234567890", text.Text);
        }
        finally
        {
            try { tube?.Close(); }
            finally { CoreSettings.ServiceProvider = previous; }
        }
        return Task.CompletedTask;
    });

    // WPF Speech.cs:373,587-598: every priority line uses the AI source's reading floor.
    [Theory]
    [InlineData(true, 20.96)]
    [InlineData(false, 23.48)]
    public Task LongPriorityLinesKeepTheReadingFloor(bool ai, double seconds) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        AvatarTubeWindow? tube = null;
        try
        {
            settings.Current.AvatarEnabled = true;
            settings.Current.AvatarMuted = true;
            settings.Current.BubbleDurationSeconds = 2;
            tube = new AvatarTubeWindow(null);
            tube.Show();
            tube.GigglePriority(new string('x', 240), playSound: false, aiGenerated: ai);
            Assert.Equal(TimeSpan.FromSeconds(seconds), tube.SpeechHideDelay);
        }
        finally
        {
            try { tube?.Close(); }
            finally { CoreSettings.ServiceProvider = previous; }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PriorityTypingRestoresLinksAndPanicCancelsStaleFrames() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        AvatarTubeWindow? tube = null;
        try
        {
            settings.Current.AvatarEnabled = true;
            settings.Current.AvatarMuted = true;
            tube = new AvatarTubeWindow(null);
            tube.Show();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.GigglePriority("Watch [clip](https://example.com/video)", playSound: false);
            while (tube.TypewriterFrame is { } frame) frame();
            Assert.Single(text.Inlines!.OfType<InlineUIContainer>());
            tube.GigglePriority("old line", playSound: false);
            var stale = tube.TypewriterFrame!;
            tube.GigglePriority("new line", playSound: false);
            stale();
            Assert.Equal("", text.Text);
            var cancelled = tube.TypewriterFrame!;
            tube.PanicSilence();
            Assert.False(tube.HasBubbleUp);
            Assert.Null(tube.TypewriterFrame);
            cancelled();
            Assert.False(tube.HasBubbleUp);
            Assert.Equal("", text.Text);
        }
        finally
        {
            try { tube?.Close(); }
            finally { CoreSettings.ServiceProvider = previous; }
        }
        return Task.CompletedTask;
    });
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubePriorityTypewriterTests
{
    [Theory]
    [InlineData(true, 30, 2.3)]
    [InlineData(false, 75, 2.75)]
    public Task PriorityLineTypesAndKeepsReadingTime(bool ai, int stepMs, double seconds) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarMuted = true;
        CoreSettings.Current.BubbleDurationSeconds = 2;
        var tube = new AvatarTubeWindow(null);
        try
        {
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
        finally { tube.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PriorityTypingRestoresLinksAndPanicCancelsStaleFrames() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarMuted = true;
        var tube = new AvatarTubeWindow(null);
        try
        {
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
        finally { tube.Close(); }
        return Task.CompletedTask;
    });
}

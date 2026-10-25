using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class StartupDescentSpeechTests
{
    [Fact]
    public void StartupKeepsThePhaseAvailableBeforeTheAvatarExists()
    {
        using var fuse = AvApp.CreateDescentCountdown(() => null);
        Assert.NotNull(fuse.Speaker);
        Assert.False(fuse.Speaker!("not yet"));
    }

    [Fact]
    public Task StartupSpeakerRetriesUntilTheAvatarExistsAndUsesScriptedPrioritySpeech() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        AvatarTubeWindow? live = null;
        using var fuse = AvApp.CreateDescentCountdown(() => live);
        Assert.NotNull(fuse.Speaker);
        Assert.False(fuse.Speaker!("too early")); // Core retries instead of burning this phase.
        var muted = CoreSettings.Current.AvatarMuted;
        var play = CoreAudio.PlayOneShotProvider;
        int sounds = 0;
        CoreSettings.Current.AvatarMuted = false;
        CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => sounds++;
        AvatarTubeWindow? tube = null;
        try
        {
            tube = new AvatarTubeWindow(null);
            tube.Show();
            live = tube;
            tube.GigglePriority("previous AI line", playSound: false);
            var aiField = typeof(AvatarTubeWindow).GetField("_lastAiBubbleUtc",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var aiAt = aiField.GetValue(tube);
            tube.EndSpeechBubble(); // the earned AI gap queues this preset.
            tube.Giggle("waiting preset");
            Assert.Equal(1, tube.QueuedSpeechCount);
            Assert.True(fuse.Speaker("scripted phase"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("scripted phase", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.Equal(aiAt, aiField.GetValue(tube)); // scripted text does not suppress barks as AI.
            Assert.Equal(0, sounds);
            tube.PanicSilence(); // existing panic surface also silences the new caller.
            Assert.False(tube.IsSpeaking);
            live = null;
            Assert.False(fuse.Speaker("after close"));
        }
        finally
        {
            tube?.Close();
            CoreAudio.PlayOneShotProvider = play;
            CoreSettings.Current.AvatarMuted = muted;
        }
        return Task.CompletedTask;
    });
}

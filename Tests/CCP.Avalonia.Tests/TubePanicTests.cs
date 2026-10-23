using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Panic silences the companion tube (WPF MainWindow.xaml.cs:1980-1981 -> AvatarTubeWindow
/// PanicSilence, Speech.cs:1837): her spoken clip is stopped, the thinking/listening animations and the
/// bubble go, through the shell's panic registry entry.</summary>
public sealed class TubePanicTests
{
    [Fact]
    public Task PanicCutsHerLineAndTakesTheBubbleDown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (enabled, volume, muted, voMuted) = (s.AvatarEnabled, s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted);
        var provider = CoreAudio.PlayStoppableProvider;
        var clip = Path.GetTempFileName();
        int started = 0, stopped = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { started++; return () => stopped++; };
        s.AvatarEnabled = true; s.MasterVolume = 80; s.AvatarMuted = false; s.CompanionVoiceLinesMuted = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube;
            Assert.NotNull(tube);
            var panic = PanicSurfaces.All.Single(x => x.Id == "tube-speech");
            var bubble = tube!.FindControl<Border>("SpeechBubble")!;

            // A voiced line mid-play: panic stops the clip and the bubble.
            tube.GigglePriority("hi", phraseAudioPath: clip, barkVoice: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, started);
            Assert.True(tube.HasBubbleUp);
            Assert.True(bubble.IsVisible);
            panic.Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, stopped);
            Assert.False(tube.HasBubbleUp);
            Assert.False(bubble.IsVisible);
            Assert.False(tube.IsSpeaking);

            // Two voiced lines close together: the second cuts the first (WPF Speech.cs:1896), so the
            // panic leaves neither playing.
            var live = new System.Collections.Generic.HashSet<int>();
            var next = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { var id = next++; live.Add(id); return () => live.Remove(id); };
            tube.GigglePriority("one", phraseAudioPath: clip, barkVoice: true);
            tube.GigglePriority("two", phraseAudioPath: clip, barkVoice: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, next);
            panic.Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(live);
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { started++; return () => stopped++; };

            // Waiting on the AI (thinking dots): panic ends the wait too.
            tube.StartThinkingAnimation();
            Assert.True(tube.IsCompanionBusy(0));
            panic.Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.IsCompanionBusy(0));
            Assert.False(tube.HasBubbleUp);

            // The listening bubble: gone, and idle panic is a no-op.
            tube.ShowListeningBubble("listening");
            Dispatcher.UIThread.RunJobs();
            Assert.True(tube.HasBubbleUp);
            panic.Stop(shell);
            panic.Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.HasBubbleUp);
            Assert.False(bubble.IsVisible);
            Assert.Equal(1, stopped);
        }
        finally
        {
            shell.Close();
            CoreAudio.PlayStoppableProvider = provider;
            (s.AvatarEnabled, s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) = (enabled, volume, muted, voMuted);
            File.Delete(clip);
        }
        return Task.CompletedTask;
    });
}

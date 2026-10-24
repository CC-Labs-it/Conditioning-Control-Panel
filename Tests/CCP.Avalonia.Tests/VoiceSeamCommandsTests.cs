using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Voice intents whose seams now exist on this head (WPF AutonomyService.VoiceCommandHost
/// VoiceAction): volume/mute (WPF ApplyVoiceMute / AdjustMasterVolume), session pause/resume (WPF
/// Pause/ResumeSessionFromRemote) and the mind-wipe one-shot. Driven as a wake utterance with the
/// command inline ("hey bambi louder"), the path MainShellWindow.OnWakeWordHeard takes. No Vosk.</summary>
public sealed class VoiceSeamCommandsTests
{
    private static readonly string[] Wake = { "hey bambi" };

    private static void Say(MainShellWindow shell, string command)
    {
        Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi " + command, Wake), command);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task VolumeAndMuteCommandsMoveTheMasterLikeWpf() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var saved = (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted);
        var savedEnum = AudioSettingsBinder.Enumerate;
        AudioSettingsBinder.Enumerate = () => System.Array.Empty<ConditioningControlPanel.Avalonia.Platform.LibVlcAudio.OutputDevice>();
        var host = new global::Avalonia.Controls.Window { Content = new AudioSettingsSection() };
        try
        {
            (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted) = (50, true, false);
            host.Show();   // Settings "Audio" attached: its master slider must follow the voice (WPF Patreon.cs:2161)
            Dispatcher.UIThread.RunJobs();
            var slider = ((AudioSettingsSection)host.Content!).SliderMaster;
            Assert.Equal(50, slider.Value);
            Say(shell, "louder");
            Assert.Equal(65, s.MasterVolume);
            Assert.Equal(65, slider.Value);
            Say(shell, "quieter");
            Assert.Equal(50, s.MasterVolume);

            Say(shell, "mute");
            Assert.Equal((0, false, true), (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted));
            Assert.Equal(0, slider.Value);
            Say(shell, "unmute");   // the remembered level comes back
            Assert.Equal((50, true, false), (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted));

            s.MasterVolume = 95;
            Say(shell, "louder");   // clamped at 100
            Assert.Equal(100, s.MasterVolume);
        }
        finally
        {
            host.Close();
            AudioSettingsBinder.Enumerate = savedEnum;
            (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted) = saved;
        }
    });

    [Fact]
    public Task PauseAndResumeCommandsDriveTheRunningSession() => Run(shell =>
    {
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.StartSession(new Session { Id = "voice_pause", Name = "Voice Pause", DurationMinutes = 1 });
            Assert.True(runner.IsRunning && !runner.IsPaused);

            Say(shell, "pause the session");
            Assert.True(runner.IsPaused);
            Assert.Equal("\u25B6", shell.Named<global::Avalonia.Controls.TextBlock>("TxtPauseIcon")!.Text);

            Say(shell, "resume the session");
            Assert.False(runner.IsPaused);
            Assert.Equal("\u23F8", shell.Named<global::Avalonia.Controls.TextBlock>("TxtPauseIcon")!.Text);
        }
        finally
        {
            runner.Stop();
            CoreEngine.StoppedHook = null;
            CoreSession.IsSessionRunningProvider = null;
            AvApp.Sessions = null;
            CoreEngine.Stop();
        }
    });

    [Fact]
    public Task MindWipeIsInTheGrammarOnlyOnceItsSeamIsSeeded() => Run(shell =>
    {
        var saved = CoreMindWipe.TriggerOnceProvider;
        try
        {
            CoreMindWipe.TriggerOnceProvider = null;
            Assert.DoesNotContain(shell.VoiceCmds.Available, i => i.Name == "wipe_once");
            var fired = 0;
            CoreMindWipe.TriggerOnceProvider = () => fired++;
            Assert.Contains(shell.VoiceCmds.Available, i => i.Name == "wipe_once");
            Say(shell, "wipe my mind");
            Assert.Equal(1, fired);
            Assert.Contains("pause", shell.VoiceCmds.HelpLine());
        }
        finally { CoreMindWipe.TriggerOnceProvider = saved; }
    });

    [Fact]
    public Task MuteCutsHerPlayingClipBeforeTheConfirmation() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var saved = (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted, s.AvatarEnabled, s.CompanionVoiceLinesMuted);
        var audio = CoreAudio.PlayStoppableProvider;
        var clip = System.IO.Path.GetTempFileName();
        try
        {
            (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted, s.AvatarEnabled, s.CompanionVoiceLinesMuted) = (70, true, false, true, false);
            var live = false;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, ended) =>
            {
                live = true;
                return () => { live = false; ended?.Invoke(); };
            };
            shell.SetAvatarEnabled(true);
            shell.Tube!.GigglePriority("playing", phraseAudioPath: clip, barkVoice: true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(live);
            var done = shell.Tube.SpokenAudioDone;
            // Like WPF the action is queued (InvokeAsync); silence the confirmation so only the
            // mute itself can cut the clip.
            var say = shell.VoiceCmds.Host.Say;
            shell.VoiceCmds.Host.Say = (_, _) => { };
            try
            {
                Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi mute", Wake));
                Dispatcher.UIThread.RunJobs();
            }
            finally { shell.VoiceCmds.Host.Say = say; }
            Assert.True(s.AvatarMuted);
            Assert.False(live);
            Assert.True(done.IsCompleted);
            shell.ApplyVoiceMute(true); // repeated mute is harmless and preserves the remembered volume
            shell.ApplyVoiceMute(false);
            Assert.Equal(70, s.MasterVolume);
        }
        finally
        {
            shell.Tube?.Close();
            CoreAudio.PlayStoppableProvider = audio;
            (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted, s.AvatarEnabled, s.CompanionVoiceLinesMuted) = saved;
            System.IO.File.Delete(clip);
        }
    });

    private static Task Run(System.Action<MainShellWindow> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            shell.Show();
            try { body(shell); }
            finally { shell.Close(); }
            return Task.CompletedTask;
        });
}

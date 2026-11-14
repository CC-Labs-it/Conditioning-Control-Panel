using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
/// <summary>Restack b191 (#3797, #4071): the book margin line and the avatar voice hold.</summary>
public sealed class EmiBookAvatarHoldTests
{
    // WPF AvatarTubeWindow.Speech.cs:428-430, EmiDeskService.cs:123,157-195,
    // EmiDeskWindow.Bubble.cs:545-565, and desk-lines.json avatarSpeaking tail.
    [Fact]
    public void PublishedLinesAreAvailableToTheHoldEngine()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "emi", "desk-lines.json")));
        Assert.True(EmiLineEngine.Instance.IsHoldMoment("avatarSpeaking"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AvatarSpeechInterruptsBookAndHoldsUntilBubbleAndVoiceEnd(bool deskMutesAvatar) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var svc = EmiDeskService.Instance;
        var engine = EmiLineEngine.Instance;
        var s = CoreSettings.Current;
        var old = (s.EmiDeskEnabled, s.EmiDeskMuteAvatar, s.EmiDeskMuteDontAsk, s.AvatarEnabled, s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted);
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var clip = Path.GetTempFileName();
        Action? finish = null;
        var tube = new AvatarTubeWindow(null);
        try
        {
            engine.ResetForTests();
            (s.EmiDeskEnabled, s.EmiDeskMuteAvatar, s.EmiDeskMuteDontAsk, s.AvatarEnabled, s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) =
                (true, deskMutesAvatar, deskMutesAvatar, true, 80, false, false);
            CoreAudio.PlayStoppableProvider = (_, _, _, _, done) => { finish = done; return () => { }; };
            svc.ResetMutePrompt();
            await svc.Summon();
            var desk = svc.Window!;
            var summonDeadline = DateTime.UtcNow.AddSeconds(5);
            while (desk.InputLocked && DateTime.UtcNow < summonDeadline)
            {
                await Task.Delay(20);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(desk.InputLocked);
            tube.Show();
            desk.OpenBook(EmiBookCards.All[0].Id);
            Assert.Equal(".", desk.BubbleText);
            Assert.Equal(deskMutesAvatar, svc.AvatarMuted);

            tube.GigglePriority("avatar line", phraseAudioPath: clip, barkVoice: true);
            Assert.Equal("avatarSpeaking", engine.HeldBy);
            Assert.Null(desk.BubbleText);
            Assert.False(desk.ChainLive);
            var timer = (DispatcherTimer)typeof(EmiDeskWindow).GetField("_holdTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(desk)!;
            Assert.True(timer.IsEnabled);
            Assert.True(timer.Interval.TotalMilliseconds > 0);
            var poll = (DispatcherTimer)typeof(EmiDeskService).GetField("_avatarHoldTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(svc)!;
            Assert.True(poll.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(1), poll.Interval);
            svc.CheckAvatarVoiceHold();
            if (!deskMutesAvatar)
            {
                Assert.True(tube.HasBubbleUp);
                Assert.NotNull(finish);
                Assert.Equal("avatarSpeaking", engine.HeldBy);
                tube.Hide();
                svc.CheckAvatarVoiceHold(); // hiding the window does not end its speech
                Assert.Equal("avatarSpeaking", engine.HeldBy);
                tube.Show();
                finish!();
                svc.CheckAvatarVoiceHold(); // finished audio must not release a visible bubble
                Assert.Equal("avatarSpeaking", engine.HeldBy);
                tube.GigglePriority("next line", phraseAudioPath: clip, barkVoice: true);
                tube.PanicSilence(); // bubble gone, audio completion has not arrived yet
                svc.CheckAvatarVoiceHold();
                Assert.Equal("avatarSpeaking", engine.HeldBy);
                finish!();
                Assert.True(tube.SpokenAudioDone.IsCompleted);
            }
            svc.CheckAvatarVoiceHold();
            Assert.Null(engine.HeldBy);
            Assert.True(engine.HoldActive); // the authored 20-second tail
            Assert.Null(engine.Draw("effectFired"));

            // A new margin line cancels the held face timer, as SpeakLine does in WPF.
            desk.Book!.FindControl<Button>("BtnNext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(timer.IsEnabled);
            Assert.Equal(".", desk.BubbleText);
        }
        finally
        {
            tube.Close();
            svc.CheckAvatarVoiceHold();
            svc.Window?.ShutDown();
            svc.ResetMutePrompt();
            engine.ResetForTests();
            CoreAudio.PlayStoppableProvider = oldPlay;
            (s.EmiDeskEnabled, s.EmiDeskMuteAvatar, s.EmiDeskMuteDontAsk, s.AvatarEnabled, s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) = old;
            File.Delete(clip);
        }
    });

    [Fact]
    public Task AvatarSpeechWhileDeskIsAwayArmsAndPollReleasesTheHold() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var svc = EmiDeskService.Instance;
        var engine = EmiLineEngine.Instance;
        var tube = new AvatarTubeWindow(null);
        try
        {
            engine.ResetForTests();
            Assert.False(svc.IsOut);
            tube.Show();
            tube.GigglePriority("away", playSound: false, aiGenerated: false);
            Assert.Equal("avatarSpeaking", engine.HeldBy);
            tube.PanicSilence();
            var until = DateTime.UtcNow.AddSeconds(4);
            while (engine.HeldBy != null && DateTime.UtcNow < until) await Task.Delay(20);
            Assert.Null(engine.HeldBy);
            Assert.True(engine.HoldActive);
        }
        finally
        {
            tube.Close();
            svc.CheckAvatarVoiceHold();
            engine.ResetForTests();
        }
    });

    // WPF AvatarTubeWindow.Speech.cs:2856-2867, 428-430, 469.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ThinkingInterruptsBookEvenWhenDeskMutesAvatar(bool muted) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var svc = EmiDeskService.Instance;
        var engine = EmiLineEngine.Instance;
        var settings = CoreSettings.Current;
        var old = (settings.EmiDeskEnabled, settings.EmiDeskMuteAvatar, settings.EmiDeskMuteDontAsk, settings.AvatarEnabled);
        var tube = new AvatarTubeWindow(null);
        try
        {
            engine.ResetForTests();
            (settings.EmiDeskEnabled, settings.EmiDeskMuteAvatar, settings.EmiDeskMuteDontAsk, settings.AvatarEnabled) =
                (true, muted, muted, true);
            svc.ResetMutePrompt();
            await svc.Summon();
            var desk = svc.Window!;
            var until = DateTime.UtcNow.AddSeconds(5);
            while (desk.InputLocked && DateTime.UtcNow < until)
            {
                await Task.Delay(20);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(desk.InputLocked);
            tube.Show();
            desk.OpenBook(EmiBookCards.All[0].Id);
            Assert.Equal(".", desk.BubbleText);
            Assert.Equal(muted, svc.AvatarMuted);
            tube.StartThinkingAnimation();
            Assert.Equal("avatarSpeaking", engine.HeldBy);
            Assert.Null(desk.BubbleText);
            Assert.False(desk.ChainLive);
            Assert.Equal(!muted, tube.HasBubbleUp);
            if (!muted)
            {
                svc.CheckAvatarVoiceHold();
                Assert.Equal("avatarSpeaking", engine.HeldBy);
            }
            tube.PanicSilence();
            svc.CheckAvatarVoiceHold();
            Assert.Null(engine.HeldBy);
            Assert.True(engine.HoldActive);
        }
        finally
        {
            tube.Close();
            svc.CheckAvatarVoiceHold();
            svc.Window?.ShutDown();
            svc.ResetMutePrompt();
            engine.ResetForTests();
            (settings.EmiDeskEnabled, settings.EmiDeskMuteAvatar, settings.EmiDeskMuteDontAsk, settings.AvatarEnabled) = old;
        }
    });
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VoicedIdleBarkTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static Task WithTube(Func<AvatarTubeWindow, string, List<string>, Task> run) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff);
        var engine = CoreSession.IsEngineRunningProvider;
        var session = CoreSession.IsSessionRunningProvider;
        var audio = CoreAudio.PlayStoppableProvider;
        var mod = CoreMods.ActiveModIdProvider;
        var art = CoreModArt.OverridePathProvider;
        var clip = Path.GetTempFileName();
        var played = new List<string>();
        CoreSession.IsEngineRunningProvider = () => false;
        CoreSession.IsSessionRunningProvider = () => false;
        CoreMods.ActiveModIdProvider = () => "";
        CoreAudio.PlayStoppableProvider = (_, _, channel, _, _) => { played.Add(channel); return () => { }; };
        (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff) = (false, 80, false, false);
        PanicAftermath.EndQuiet();
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            CoreModArt.OverridePathProvider = path => path.StartsWith("sounds/giggle", StringComparison.Ordinal) ? clip : null;
            await run(tube, clip, played);
        }
        finally
        {
            tube.Close(); File.Delete(clip);
            (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff) = old;
            CoreSession.IsEngineRunningProvider = engine; CoreSession.IsSessionRunningProvider = session;
            CoreAudio.PlayStoppableProvider = audio; CoreMods.ActiveModIdProvider = mod; CoreModArt.OverridePathProvider = art;
        }
    });

    [Fact]
    public Task ImmediateVoicedIdleUsesPresetSpeechAndItsTwoSecondGap() => WithTube((tube, clip, played) =>
    {
        var clock = new Clock();
        tube.SpeechClock = clock;
        CoreSession.IsEngineRunningProvider = () => true;
        TimeSpan delay = default;
        tube.SpeechDelayScheduler = (d, _) => { delay = d; return () => { }; };
        BarkHead.Speak(new BarkSpeech("idle voice", clip, false, null, false, true), tube);
        while (tube.TypewriterFrame is { } twf) twf();   // presets type slowly (WPF Speech.cs:559)
        Assert.Equal("idle voice", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        Assert.Equal(new[] { "bark-voice" }, played);
        Assert.Empty(tube.ChatHistory);
        Assert.False(tube.IsCompanionBusy(0));
        tube.EndSpeechBubble();
        tube.Giggle("next");
        Assert.Equal(TimeSpan.FromSeconds(2), delay);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public Task QueuedVoicedIdleKeepsItsGateButDropsItsClipAsWpf(bool bubbleUp, bool optedIn) => WithTube((tube, clip, played) =>
    {
        var clock = new Clock();
        tube.SpeechClock = clock;
        CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff = optedIn;
        Action? fire = null;
        TimeSpan delay = default;
        tube.SpeechDelayScheduler = (d, f) => { delay = d; fire = f; return () => { }; };
        tube.Giggle("foreground");
        while (tube.TypewriterFrame is { } typed) typed();
        if (!bubbleUp) tube.EndSpeechBubble();
        // The fifth preset has a giggle cue, making the queued idle voice gate observable.
        tube.NextPresetGiggleSound(); tube.NextPresetGiggleSound(); tube.NextPresetGiggleSound();
        BarkHead.Speak(new BarkSpeech("queued idle", clip, false, null, false, true), tube);
        while (tube.TypewriterFrame is { } twf) twf();   // presets type slowly (WPF Speech.cs:559)
        Assert.Equal("foreground", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        Assert.Empty(tube.ChatHistory);
        Assert.Empty(played);
        if (bubbleUp) { Assert.Equal(1, tube.QueuedSpeechCount); tube.EndSpeechBubble(); }
        Assert.Equal(TimeSpan.FromSeconds(2), delay);
        Assert.NotNull(fire);
        fire!();
        while (tube.TypewriterFrame is { } twf) twf();   // presets type slowly (WPF Speech.cs:559)
        Assert.Equal("queued idle", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        Assert.Equal(optedIn ? new[] { "giggle" } : Array.Empty<string>(), played);
        Assert.Empty(tube.ChatHistory);
        return Task.CompletedTask;
    });
}

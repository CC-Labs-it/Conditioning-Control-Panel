using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>SubliminalWhisperShow against WPF SubliminalService.TriggerBambiFreeze / PlayBambiReset:
/// duck + whisper first, card 300 ms later, Reset 4-8 s on a passing 90% roll; muted = card at once,
/// no Reset. Uses the shipped neutral freeze/reset clips linked into the output (no mod layer here).</summary>
public sealed class SubliminalWhisperShowTests
{
    // WPF SubliminalService :543 MarkWhisperAudio: the companion never talks over an audible whisper (from #3272).
    [Fact]
    public void AudibleWhisperStartHoldsTheBarkFloor() => With(true, 0.5, _ =>
    {
        var saved = (ConditioningControlPanel.Avalonia.Platform.BarkHead.Engine, CoreSettings.Current.AvatarEnabled);
        try
        {
            CoreSettings.Current.AvatarEnabled = true;
            var rule = new ConditioningControlPanel.Services.Bark.BarkRule
            {
                Id = "whisper-floor", Trigger = "AttentionCheckFail",
                VariantPool = new() { new("look") },
            };
            var engine = new ConditioningControlPanel.Services.Bark.BarkEngine(() =>
                new ConditioningControlPanel.Services.Bark.BarkRuleSet(new[] { rule })) { Speak = _ => { } };
            engine.Start();
            ConditioningControlPanel.Avalonia.Platform.BarkHead.Engine = engine;
            CoreAudio.PlayStoppableProvider = (_, _, _, started, _) =>
            {
                started?.Invoke(TimeSpan.FromSeconds(10));
                return () => { };
            };
            SubliminalWhisperShow.Phrase("reset");
            Assert.True(engine.IsWhisperAudioPlaying);
            Assert.False(engine.Raise("AttentionCheckFail"));
            Assert.True(engine.Raise("AttentionCheckFail", guaranteed: true));
        }
        finally
        {
            SubliminalWhisperShow.StopAll();
            (ConditioningControlPanel.Avalonia.Platform.BarkHead.Engine, CoreSettings.Current.AvatarEnabled) = saved;
        }
    });

    private sealed record Run(List<string> Log, List<(int Ms, Action Then)> Timers);

    private static void With(bool audible, double roll, Action<Run> body)
    {
        var s = CoreSettings.Current;
        var (en, mu, d, lvl) = (s.SubAudioEnabled, s.SubAudioMuted, s.AudioDuckingEnabled, s.DuckingLevel);
        var (draw, after, rollFn) = (SubliminalWhisperShow.Draw, SubliminalWhisperShow.After, SubliminalWhisperShow.Roll);
        var (play, duck, unduck) = (CoreAudio.PlayOneShotProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider);
        var stoppable = CoreAudio.PlayStoppableProvider;
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = SubliminalWhisperShow.Freeze;   // as App.axaml.cs seeds it
        var run = new Run(new(), new());
        (s.SubAudioEnabled, s.SubAudioMuted, s.AudioDuckingEnabled, s.DuckingLevel) = (true, !audible, true, 70);
        SubliminalWhisperShow.Draw = t => run.Log.Add("card " + t);
        SubliminalWhisperShow.After = (ms, then) => run.Timers.Add((ms, then));
        SubliminalWhisperShow.Roll = () => roll;
        CoreAudio.PlayOneShotProvider = (p, v, tag, _, done) => { run.Log.Add($"play {Path.GetFileName(p)} {tag}"); done?.Invoke(); };
        CoreAudio.DuckProvider = n => run.Log.Add("duck " + n);
        CoreAudio.UnduckProvider = _ => run.Log.Add("unduck");
        try { body(run); }
        finally
        {
            (s.SubAudioEnabled, s.SubAudioMuted, s.AudioDuckingEnabled, s.DuckingLevel) = (en, mu, d, lvl);
            (SubliminalWhisperShow.Draw, SubliminalWhisperShow.After, SubliminalWhisperShow.Roll) = (draw, after, rollFn);
            (CoreAudio.PlayOneShotProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider) = (play, duck, unduck);
            CoreSubliminal.BambiFreezeProvider = freeze;
            CoreAudio.PlayStoppableProvider = stoppable;
        }
    }

    // WPF SubliminalService.PlayTriggerAudio: voice only, no duplicate haptic/card/reset.
    [Fact]
    public void Tube_trigger_audio_uses_linked_clip_without_a_card()
    {
        With(audible: true, roll: 0.5, run =>
        {
            SubliminalWhisperShow.PlayTriggerAudio("freeze");
            Assert.Equal(new[] { "duck 70", "play freeze.mp3 whisper" }, run.Log);
            Fire(run, 500);
            Assert.Empty(run.Timers); // fail-proof: routing through Whisper schedules the card
            Assert.DoesNotContain(run.Log, l => l.StartsWith("card"));
        });
        With(audible: false, roll: 0.5, run =>
        {
            SubliminalWhisperShow.PlayTriggerAudio("freeze");
            Assert.Empty(run.Log);
            Assert.Empty(run.Timers);
        });
    }

    private static void Fire(Run run, int ms)
    {
        var i = run.Timers.FindIndex(t => t.Ms == ms);
        Assert.True(i >= 0, $"no {ms} ms timer in [{string.Join(",", run.Timers.ConvertAll(t => t.Ms))}]");
        var t = run.Timers[i];
        run.Timers.RemoveAt(i);
        t.Then();
    }

    [Fact]
    public void Video_reset_waits_for_teardown_is_consumed_once_and_stop_cancels_it()
    {
        With(audible: true, roll: 0.5, run =>
        {
            SubliminalWhisperShow.FreezeForVideo();
            Assert.DoesNotContain(run.Timers, t => t.Ms >= 1000);
            SubliminalWhisperShow.FinishVideoFreeze(cancelled: false);
            Assert.Single(run.Timers, t => t.Ms >= 1000 && t.Ms < 2000);
            SubliminalWhisperShow.FinishVideoFreeze(cancelled: false);
            Assert.Single(run.Timers, t => t.Ms >= 1000);
            var reset = run.Timers.Find(t => t.Ms >= 1000);
            reset.Then();
            Assert.Equal("play reset.mp3 whisper", run.Log[^1]);
            SubliminalWhisperShow.FreezeForVideo();
            SubliminalWhisperShow.FinishVideoFreeze(cancelled: true);
            run.Timers.Clear();
            SubliminalWhisperShow.FinishVideoFreeze(cancelled: false);
            Assert.Empty(run.Timers);
            SubliminalWhisperShow.FreezeForVideo();
            SubliminalWhisperShow.FinishVideoFreeze(cancelled: false);
            var cancelled = run.Timers.FindLast(t => t.Ms >= 1000);
            SubliminalWhisperShow.FinishVideoFreeze(cancelled: true);
            var count = run.Log.Count; cancelled.Then(); Assert.Equal(count, run.Log.Count);
            SubliminalWhisperShow.FreezeForVideo();
            SubliminalWhisperShow.StopAll();
            run.Timers.Clear();
            SubliminalWhisperShow.FinishVideoFreeze(cancelled: false);
            Assert.Empty(run.Timers);
        });
        With(audible: false, roll: 0.5, run =>
        {
            SubliminalWhisperShow.FreezeForVideo();
            SubliminalWhisperShow.FinishVideoFreeze(cancelled: false);
            Assert.Empty(run.Timers);
        });
        With(audible: true, roll: 0.95, run =>
        {
            SubliminalWhisperShow.FreezeForVideo();
            SubliminalWhisperShow.FinishVideoFreeze(cancelled: false);
            Assert.DoesNotContain(run.Timers, t => t.Ms >= 1000);
        });
    }

    [Fact]
    public void Freeze_whispers_then_card_then_reset() => With(audible: true, roll: 0.9, run =>
    {
        CoreSubliminal.TriggerBambiFreeze();
        Assert.Equal(new[] { "duck 70", "play freeze.mp3 whisper" }, run.Log);   // no card yet
        Fire(run, 500);                                                         // unduck after the clip
        Fire(run, 50); Fire(run, 250);                                          // haptic lead, then visual gap
        Assert.Equal(new[] { "duck 70", "play freeze.mp3 whisper", "unduck", "card Freeze" }, run.Log);
        var reset = Assert.Single(run.Timers);
        Assert.InRange(reset.Ms, 4000, 7999);
        Fire(run, reset.Ms);
        Assert.Equal("play reset.mp3 whisper", run.Log[^1]);
        Fire(run, 500);
        Fire(run, 50); Fire(run, 250);
        Assert.Equal("card Reset", run.Log[^1]);
        Assert.Empty(run.Timers);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VideoFreezeDefersResetAndCancellationDropsPendingEffects(bool cancel) => With(true, 0.9, run =>
    {
        MandatoryVideoOverlay.Instance.Freeze();
        Assert.Empty(run.Timers.FindAll(t => t.Ms >= 1000));
        MandatoryVideoOverlay.Instance.FinishFreeze(cancel);
        if (cancel)
        {
            foreach (var timer in run.Timers.ToArray()) timer.Then();
            foreach (var timer in run.Timers.ToArray()) timer.Then();
            Assert.DoesNotContain("card Freeze", run.Log);
            Assert.Empty(run.Timers.FindAll(t => t.Ms >= 1000));
        }
        else
        {
            var reset = Assert.Single(run.Timers.FindAll(t => t.Ms >= 1000));
            Assert.InRange(reset.Ms, 1000, 1999); Fire(run, reset.Ms);
            Assert.Contains("play reset.mp3 whisper", run.Log);
        }
    });

    [Fact]
    public void Ambient_phrase_whispers_for_20_xp_else_card_for_10()
    {
        var xp = new List<double>();
        var prev = CoreProgression.AddXPProvider;
        CoreProgression.AddXPProvider = (a, _) => xp.Add(a);
        try
        {
            With(audible: true, roll: 0.5, run =>
            {
                SubliminalWhisperShow.Phrase("reset");                            // has a neutral clip
                Assert.Equal(new[] { "duck 70", "play reset.mp3 whisper" }, run.Log);
                Fire(run, 50); Fire(run, 250);
                Assert.Equal("card reset", run.Log[^1]);
                SubliminalWhisperShow.Phrase("no clip for this");
                Assert.Equal("card no clip for this", run.Log[^1]);             // no clip: card at once
            });
            Assert.Equal(new[] { 20.0, 10.0 }, xp);
        }
        finally { CoreProgression.AddXPProvider = prev; }
    }

    [Fact]
    public void Freeze_muted_draws_at_once_and_a_failed_roll_skips_reset()
    {
        With(audible: false, roll: 0.5, run =>
        {
            CoreSubliminal.TriggerBambiFreeze();
            Assert.Equal(new[] { "card Freeze" }, run.Log);                      // whispers muted: no audio
            Assert.Empty(run.Timers);                                           // and no Reset (WPF else-branch)
        });
        With(audible: true, roll: 0.95, run =>
        {
            CoreSubliminal.TriggerBambiFreeze();
            Fire(run, 500);
            Fire(run, 50); Fire(run, 250);
            Assert.Empty(run.Timers);                                           // the 10% skip
        });
    }

    /// <summary>WPF PlayWhisperAudio's StopAudio + TearDownSurfaces: a new whisper cuts the previous one,
    /// and the tray's Stop everything silences the playing one and drops the card and Reset behind it.</summary>
    [Fact]
    public async Task New_whisper_cuts_the_last_and_stop_everything_silences_it() => await CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(() =>
    {
        With(audible: true, roll: 0.5, run =>
        {
            CoreAudio.PlayStoppableProvider = (p, v, tag, _, done) =>
            {
                var name = Path.GetFileName(p);
                run.Log.Add("play " + name);
                return () => { run.Log.Add("stop " + name); done?.Invoke(); };
            };
            CoreSubliminal.TriggerBambiFreeze();
            SubliminalWhisperShow.Phrase("reset");
            Assert.Equal(new[] { "duck 70", "play freeze.mp3", "duck 70", "stop freeze.mp3", "play reset.mp3" }, run.Log);

            run.Log.Clear();
            ConditioningControlPanel.Avalonia.App.StopDesktopOverlays(final: false);   // tray: Stop everything
            Assert.Equal(new[] { "stop reset.mp3" }, run.Log);
            foreach (var t in run.Timers.ToArray()) t.Then();                     // every pending delay elapses
            foreach (var t in run.Timers.ToArray()) t.Then();
            Assert.DoesNotContain(run.Log, l => l.StartsWith("card") || l.StartsWith("play"));
        });
        return Task.CompletedTask;
    });

    [Fact]
    public void Custom_card_keeps_wpf_sanitization_opacity_haptic_lead_and_stop_guard() => With(audible: true, roll: 0.5, run =>
    {
        var old = (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, CoreProgression.AddXPProvider,
            SubliminalWhisperShow.DrawWithOpacity);
        var settings = new ConditioningControlPanel.Models.HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        settings.V2.Provider("mock").Enabled = true;
        settings.V2.Provider("buttplug").Enabled = false;
        settings.V2.Provider("lovense").Enabled = false;
        CoreEntitlement.HasPremiumProvider = () => true;
        using var haptics = new HapticService(settings);
        CoreHaptics.Service = haptics;
        Assert.True(haptics.ConnectAsync().GetAwaiter().GetResult());
        var triggered = 0;
        haptics.HapticTriggered += (_, _) => triggered++;
        var xp = new List<double>();
        CoreProgression.AddXPProvider = (amount, _) => xp.Add(amount);
        SubliminalWhisperShow.DrawWithOpacity = (text, opacity) => run.Log.Add($"custom {text} {opacity}");
        try
        {
            Assert.True(haptics.SubliminalAnticipationMs > 0);
            SubliminalWhisperShow.Custom("  <b>Relax</b>  ", 42);
            Assert.Empty(run.Log);                       // silent custom card, no whisper
            Assert.Equal(1, triggered);
            Fire(run, haptics.SubliminalAnticipationMs);
            Assert.Equal("custom Relax 42", Assert.Single(run.Log));
            SubliminalWhisperShow.Custom("queued", 30);
            SubliminalWhisperShow.StopAll();
            Fire(run, haptics.SubliminalAnticipationMs);
            Assert.Single(run.Log);                     // panic during anticipation drops it
            CoreHaptics.Service = null;
            SubliminalWhisperShow.Custom(" ");
            SubliminalWhisperShow.Custom(new string('x', 205), 12);
            Assert.Equal($"custom {new string('x', 200)} 12", run.Log[^1]);
            Assert.Equal(new[] { 10.0, 10.0, 10.0 }, xp);
        }
        finally
        {
            haptics.PanicStop();
            (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, CoreProgression.AddXPProvider,
                SubliminalWhisperShow.DrawWithOpacity) = old;
        }
    });

    [Fact]
    public void Enhancement_custom_subliminal_passes_duration_and_suppresses_haptic() => With(true, 0.5, run =>
    {
        var old = (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, CoreProgression.AddXPProvider,
            SubliminalWhisperShow.DrawWithDuration);
        var settings = new ConditioningControlPanel.Models.HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        settings.V2.Provider("mock").Enabled = true;
        settings.V2.Provider("buttplug").Enabled = false;
        settings.V2.Provider("lovense").Enabled = false;
        CoreEntitlement.HasPremiumProvider = () => true;
        using var haptics = new HapticService(settings);
        CoreHaptics.Service = haptics;
        Assert.True(haptics.ConnectAsync().GetAwaiter().GetResult());
        var triggered = 0;
        haptics.HapticTriggered += (_, _) => triggered++;
        CoreProgression.AddXPProvider = (_, _) => { };
        SubliminalWhisperShow.DrawWithDuration = (text, opacity, duration) => run.Log.Add($"{text}:{duration}");
        try
        {
            using var source = new ConditioningControlPanel.Avalonia.Platform.Deeper.WebHostVideoTimeSource(new ConditioningControlPanel.Avalonia.Views.Controls.WebHost());
            var dispatcher = new ConditioningControlPanel.Avalonia.Platform.Deeper.BrowserEnhancementDispatcher();
            var ctx = new ConditioningControlPanel.Services.Deeper.EnhancementDispatchContext(new ConditioningControlPanel.Models.Deeper.Enhancement(), source, 0, null);
            var effect = new ConditioningControlPanel.Models.Deeper.TriggerEffectAction { EffectType = ConditioningControlPanel.Models.Deeper.EffectTypes.Subliminal, Text = "Relax", DurationMs = 2400, SuppressHaptic = true };
            dispatcher.DispatchAsync(effect, ctx).GetAwaiter().GetResult();
            Assert.Equal("Relax:2400", Assert.Single(run.Log));
            Assert.Equal(0, triggered);
            Assert.Empty(run.Timers);
            effect.SuppressHaptic = false;
            dispatcher.DispatchAsync(effect, ctx).GetAwaiter().GetResult();
            Assert.Equal(1, triggered);
            Fire(run, haptics.SubliminalAnticipationMs);
            Assert.Equal(2, run.Log.Count);
        }
        finally
        {
            haptics.PanicStop();
            (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, CoreProgression.AddXPProvider,
                SubliminalWhisperShow.DrawWithDuration) = old;
        }
    });

    [Fact]
    public void Subliminal_hold_uses_override_and_wpf_minimum()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(2400), SubliminalOverlay.Hold(85, 2400));
        Assert.Equal(TimeSpan.FromMilliseconds(100), SubliminalOverlay.Hold(85, 30));
        Assert.Equal(TimeSpan.FromMilliseconds(1445), SubliminalOverlay.Hold(85, null));
    }

    /// <summary>A Stop that lands while the whisper is starting still silences it and drops its card.</summary>
    [Fact]
    public void Stop_racing_the_whisper_start_still_silences_it() => With(audible: true, roll: 0.5, run =>
    {
        CoreAudio.PlayStoppableProvider = (p, v, tag, _, done) =>
        {
            var name = Path.GetFileName(p);
            run.Log.Add("play " + name);
            SubliminalWhisperShow.StopAll();                                      // Stop lands mid-start
            return () => { run.Log.Add("stop " + name); done?.Invoke(); };
        };
        SubliminalWhisperShow.Phrase("reset");
        foreach (var t in run.Timers.ToArray()) t.Then();
        foreach (var t in run.Timers.ToArray()) t.Then();
        Assert.Contains("stop reset.mp3", run.Log);
        Assert.DoesNotContain(run.Log, l => l.StartsWith("card"));
    });
}

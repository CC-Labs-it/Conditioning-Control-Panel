using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>PossessionAudio moved from the WPF head (possession-core-b2; tests adapted from trunk 98cfa9d86):
/// two synthesised tones, quiet, throttled, behind the Lockdown audio-tics switch, the master volume and the
/// head's output-suppressed probe, armed only while a lockdown runs.</summary>
[Collection("PossessionAudio")]
public sealed class PossessionAudioTests : IDisposable
{
    private readonly (Func<DateTime>, Func<AppSettings?>, Action<string, float, string>, Func<string>) _was =
        (PossessionAudio.UtcNow, PossessionAudio.Settings, PossessionAudio.Play, PossessionAudio.ClipDir);
    private readonly (Action<int>?, Action<long>?, Func<long>?, Func<bool>?) _audioWas =
        (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, CoreAudio.IsOutputSuppressedProvider);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-poss-audio-" + Guid.NewGuid().ToString("N"));
    private readonly List<(string Path, float Volume, string Tag)> _played = new();
    private readonly List<int> _ducks = new();
    private readonly AppSettings _settings = new() { LockdownAudioTics = true, MasterVolume = 50 };
    private DateTime _now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private bool _suppressed;

    public PossessionAudioTests()
    {
        PossessionAudio.Uninstall();
        PossessionAudio.ForgetClips();
        PossessionAudio.UtcNow = () => _now;
        PossessionAudio.Settings = () => _settings;
        PossessionAudio.ClipDir = () => _dir;
        PossessionAudio.Play = (path, volume, tag) => _played.Add((path, volume, tag));
        CoreAudio.DuckProvider = s => _ducks.Add(s);
        CoreAudio.UnduckProvider = _ => { };
        CoreAudio.DuckGenerationProvider = () => 7;
        CoreAudio.IsOutputSuppressedProvider = () => _suppressed;
    }

    public void Dispose()
    {
        PossessionAudio.Uninstall();
        PossessionAudio.Rendering.GetAwaiter().GetResult();
        PossessionAudio.ForgetClips();
        (PossessionAudio.UtcNow, PossessionAudio.Settings, PossessionAudio.Play, PossessionAudio.ClipDir) = _was;
        (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, CoreAudio.IsOutputSuppressedProvider) = _audioWas;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static double PeakDbfs(float[] buf) => 20 * Math.Log10(buf.Max(v => Math.Abs(v)));

    [Fact]
    public void TheTwoCuesAreShortQuietTones_AndRenderTheSameEveryTime()
    {
        var tick = PossessionAudio.SynthTick();
        var stinger = PossessionAudio.SynthStinger();
        Assert.Equal(2205, tick.Length);          // 50 ms
        Assert.Equal(13230, stinger.Length);      // 300 ms
        Assert.Equal(-18.0, PeakDbfs(tick), 2);
        Assert.Equal(-12.0, PeakDbfs(stinger), 2);
        Assert.Equal(0f, tick[0]);                // faded in and out: no click
        Assert.Equal(0f, stinger[^1]);
        Assert.Equal(tick, PossessionAudio.SynthTick());

        var wav = PossessionAudio.WriteWav(tick);
        Assert.Equal(44 + tick.Length * 2, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(44100, BitConverter.ToInt32(wav, 24));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));   // mono
    }

    [Fact]
    public void OnlyABigEffectTicks_AtMostEvery1500ms_AtTheMasterVolume()
    {
        PossessionAudio.OnEffectStarted("nudge", "TxtTitle", isBig: false);
        Assert.Empty(_played);

        PossessionAudio.OnEffectStarted("melt", "CardPips", isBig: true);
        var played = Assert.Single(_played);
        Assert.Equal("possession-tick", played.Tag);
        Assert.Equal(0.5f, played.Volume);
        Assert.StartsWith(_dir, played.Path);
        Assert.True(new FileInfo(played.Path).Length > 128);

        _now = _now.AddMilliseconds(900);
        PossessionAudio.OnEffectStarted("retitle", "TxtTitle", isBig: true);
        Assert.Single(_played);
        _now = _now.AddMilliseconds(700);
        PossessionAudio.OnEffectStarted("retitle", "TxtTitle", isBig: true);
        Assert.Equal(2, _played.Count);
    }

    [Fact]
    public void TheDipAnswersARungAboveSettle_AndTheThirdPullAtATripwire_WithADuck()
    {
        PossessionAudio.OnRungChanged(PossessionRung.Settle);
        Assert.Empty(_played);
        PossessionAudio.OnRungChanged(PossessionRung.Drift);
        Assert.Equal("possession-dip", Assert.Single(_played).Tag);
        Assert.Equal(new[] { 60 }, _ducks);

        _now = _now.AddSeconds(2);
        PossessionAudio.OnRungChanged(PossessionRung.Melt);            // inside the 5 s throttle
        Assert.Single(_played);

        _now = _now.AddSeconds(6);
        PossessionAudio.OnTripwireReacted(new EscapeAttempt(EscapeKinds.Stop, 2, 2, _now));
        Assert.Single(_played);
        PossessionAudio.OnTripwireReacted(new EscapeAttempt(EscapeKinds.Stop, 3, 3, _now));
        Assert.Equal(2, _played.Count);
    }

    [Fact]
    public void TheSwitchOff_TheVolumeAtZero_OrASuppressedOutputIsSilence_AndNoDuck()
    {
        _settings.LockdownAudioTics = false;
        PossessionAudio.OnEffectStarted("melt", null, true);
        PossessionAudio.OnRungChanged(PossessionRung.Collapse);

        _settings.LockdownAudioTics = true;
        _settings.MasterVolume = 0;
        _now = _now.AddSeconds(10);
        PossessionAudio.OnEffectStarted("melt", null, true);
        PossessionAudio.OnRungChanged(PossessionRung.Collapse);

        _settings.MasterVolume = 50;
        _suppressed = true;
        _now = _now.AddSeconds(10);
        PossessionAudio.OnEffectStarted("melt", null, true);
        PossessionAudio.OnRungChanged(PossessionRung.Collapse);

        Assert.Empty(_played);
        Assert.Empty(_ducks);
    }

    [Fact]
    public void ASilencedCueInOneTest_DoesNotThrottleTheNext()
    {
        // Every test starts at the same clock, so a throttle stamp left by the silent test made the next
        // one's first cue look "too soon" (CI order, rgate run 38347835986).
        using (var first = new PossessionAudioTests()) first.TheSwitchOff_TheVolumeAtZero_OrASuppressedOutputIsSilence_AndNoDuck();
        using (var second = new PossessionAudioTests()) second.OnlyABigEffectTicks_AtMostEvery1500ms_AtTheMasterVolume();
        using (var third = new PossessionAudioTests()) third.TheDipAnswersARungAboveSettle_AndTheThirdPullAtATripwire_WithADuck();
    }

    private static int Subscribers(PossessionDirector director, string evt) =>
        (typeof(PossessionDirector).GetField(evt, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(director) as Delegate)?.GetInvocationList().Length ?? 0;

    [Fact]
    public void ALockdownSubscribesTheCuesToTheDirector_AndItsEndUnsubscribesThem()
    {
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, Array.Empty<IPossessionEffect>());
        var events = new[] { "EffectStarted", "RungChanged", "TripwireReacted" };
        PossessionAudio.Install(lockdown, director);
        try
        {
            Assert.All(events, e => Assert.Equal(0, Subscribers(director, e)));
            lockdown.Activate(TimeSpan.FromMinutes(5));
            Assert.All(events, e => Assert.Equal(1, Subscribers(director, e)));
            lockdown.Deactivate();
            Assert.All(events, e => Assert.Equal(0, Subscribers(director, e)));
        }
        finally { PossessionAudio.Uninstall(); }
    }

    [Fact]
    public void WithNoDirector_ALockdownLeavesTheCuesDisarmed()
    {
        using var lockdown = new LockdownService();
        PossessionAudio.Install(lockdown, null);
        try
        {
            lockdown.Activate(TimeSpan.FromMinutes(5));
            Assert.False(PossessionAudio.IsArmed);
            lockdown.Deactivate();
        }
        finally { PossessionAudio.Uninstall(); }
    }
}

[CollectionDefinition("PossessionAudio", DisableParallelization = true)]
public sealed class PossessionAudioCollection { }

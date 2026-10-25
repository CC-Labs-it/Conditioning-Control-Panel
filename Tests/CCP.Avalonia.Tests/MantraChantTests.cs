using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

// No AppBuilder, window, font, native audio or real clock: callbacks advance explicitly.
public sealed class MantraChantTests
{
    private sealed class Wait(Action fire, TimeSpan delay) : IDisposable
    {
        public Action Fire = fire;
        public TimeSpan Delay = delay;
        public bool Cancelled;
        public void Dispose() => Cancelled = true;
    }
    private sealed class Voice : MindWipePlayer.IVoice
    {
        public double Level;
        public bool Stopped;
        public double Volume { set => Level = value; }
        public void Dispose() => Stopped = true;
    }
    private sealed class Rig : IDisposable
    {
        public readonly MantraChantService Chant = new();
        public readonly List<(string Path, Voice Voice, Action End)> Played = new();
        public readonly List<Wait> Waits = new();
        private readonly AppSettings _s = CoreSettings.Current;
        private readonly (bool Enabled, bool Muted, double Volume, int Master, int Gap) _saved;
        public Rig()
        {
            _saved = (_s.MantraChantEnabled, _s.AvatarMuted, _s.MantraChantVolume, _s.MasterVolume, _s.MantraChantGapSeconds);
            (_s.MantraChantEnabled, _s.AvatarMuted, _s.MantraChantVolume, _s.MasterVolume, _s.MantraChantGapSeconds) = (true, false, 50, 80, 7);
            Chant.HasVoiced = () => true;
            Chant.Next = () => new MantraEntry { PromptAudio = "ask", ResponseAudio = "affirm" };
            Chant.Resolve = p => p;
            Chant.After = (a, d) => { var w = new Wait(a, d); Waits.Add(w); return w; };
            Chant.Play = (p, v, end) => { var voice = new Voice { Volume = v }; Played.Add((p, voice, end)); return voice; };
        }
        public void End() { Played.Last().End(); Dispatcher.UIThread.RunJobs(); }
        public void Dispose()
        {
            Chant.Dispose();
            (_s.MantraChantEnabled, _s.AvatarMuted, _s.MantraChantVolume, _s.MasterVolume, _s.MantraChantGapSeconds) = _saved;
        }
    }

    [Fact]
    public void PairPlaysAskThenAffirmationWithAnswerBeatAndUserGap() => AvaloniaTestDispatcher.Run(() =>
    {
        using var r = new Rig();
        r.Chant.Start();
        r.Chant.Start(); // idempotent
        Assert.Single(r.Played);
        Assert.Equal("ask", r.Played[0].Path);
        Assert.Equal(.4, r.Played[0].Voice.Level, 5);
        r.End();
        Assert.Equal(TimeSpan.FromSeconds(2.5), r.Waits.Last().Delay);
        r.Waits.Last().Fire();
        Assert.Equal("affirm", r.Played.Last().Path);
        r.End();
        Assert.Equal(TimeSpan.FromSeconds(7), r.Waits.Last().Delay);
        r.Waits.Last().Fire();
        Assert.Equal(new[] { "ask", "affirm", "ask" }, r.Played.Select(p => p.Path));
    });

    [Theory]
    [InlineData("ask", null)]
    [InlineData(null, "affirm")]
    public void MissingHalfIsSkippedAndGapIsClamped(string? ask, string? affirm) => AvaloniaTestDispatcher.Run(() =>
    {
        using var r = new Rig();
        CoreSettings.Current.MantraChantGapSeconds = 100;
        r.Chant.Next = () => new MantraEntry { PromptAudio = ask, ResponseAudio = affirm };
        r.Chant.Start();
        Assert.Equal(ask ?? affirm, r.Played.Single().Path);
        r.End();
        Assert.Equal(TimeSpan.FromSeconds(60), r.Waits.Last().Delay);
    });

    [Fact]
    public void NoVoicedContentStaysOffAndUnresolvableSetStopsAfterTwelvePicks() => AvaloniaTestDispatcher.Run(() =>
    {
        using var r = new Rig();
        r.Chant.HasVoiced = () => false;
        r.Chant.Start();
        Assert.False(r.Chant.IsRunning);
        Assert.Empty(r.Waits);
        r.Chant.HasVoiced = () => true;
        int picks = 0;
        r.Chant.Next = () => { picks++; return new MantraEntry(); };
        r.Chant.Start();
        Assert.False(r.Chant.IsRunning);
        Assert.Equal(12, picks);
        Assert.Empty(r.Played);
        Assert.All(r.Waits, w => Assert.True(w.Cancelled));
    });

    [Fact]
    public void VolumeIsLiveAndMuteDropsThePairThenResumesAtAnAsk() => AvaloniaTestDispatcher.Run(() =>
    {
        using var r = new Rig();
        r.Chant.Start();
        CoreSettings.Current.MantraChantVolume = 25;
        r.Chant.ApplyVolume();
        Assert.Equal(.2, r.Played.Last().Voice.Level, 5);
        CoreSettings.Current.AvatarMuted = true;
        r.Chant.ApplyVolume();
        Assert.Equal(0, r.Played.Last().Voice.Level);
        r.Chant.OnMuteWatchTick();
        Assert.True(r.Played.Last().Voice.Stopped);
        CoreSettings.Current.AvatarMuted = false;
        r.Chant.OnMuteWatchTick();
        Assert.Equal(new[] { "ask", "ask" }, r.Played.Select(p => p.Path));
        r.End();
        r.Waits.Last().Fire();
        Assert.Equal("affirm", r.Played.Last().Path);
    });

    [Fact]
    public void StartMutedOpensNoDeviceAndUnmuteResumes() => AvaloniaTestDispatcher.Run(() =>
    {
        using var r = new Rig();
        CoreSettings.Current.AvatarMuted = true;
        r.Chant.Start();
        Assert.Empty(r.Played);
        Assert.Equal(TimeSpan.FromSeconds(.5), r.Waits.Single().Delay);
        CoreSettings.Current.AvatarMuted = false;
        r.Waits.Single().Fire();
        Assert.Equal("ask", r.Played.Single().Path);
    });

    [Fact]
    public void StopCancelsTimersAndStaleEndsCannotResumeEvenAfterRestart() => AvaloniaTestDispatcher.Run(() =>
    {
        using var r = new Rig();
        r.Chant.Start();
        var staleEnd = r.Played.Single().End;
        r.End();
        var staleTimers = r.Waits.ToArray();
        // Watchdog teardown may arrive off-thread. It leaves the persisted opt-in intact.
        Task.Run(r.Chant.StopEmergency).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.True(CoreSettings.Current.MantraChantEnabled);
        Assert.All(staleTimers, w => Assert.True(w.Cancelled));
        r.Chant.Start();
        foreach (var w in staleTimers) w.Fire();
        staleEnd();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, r.Played.Count);
        r.Chant.StopAndDisarm();
        Assert.False(CoreSettings.Current.MantraChantEnabled);
        Assert.False(r.Chant.IsRunning);
        Assert.True(r.Played.Last().Voice.Stopped);
        r.Chant.Dispose();
        r.Chant.Start();
        Assert.False(r.Chant.IsRunning);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedBackendWaitsThenContinuesInsteadOfWedging(bool throws) => AvaloniaTestDispatcher.Run(() =>
    {
        using var r = new Rig();
        var play = r.Chant.Play;
        r.Chant.Play = (_, _, _) => throws ? throw new InvalidOperationException("test output unavailable") : null;
        r.Chant.Start();
        Assert.True(r.Chant.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(2.5), r.Waits.Last().Delay);
        r.Chant.Play = play;
        r.Waits.Last().Fire();
        Assert.Equal("affirm", r.Played.Single().Path);
    });

    [Fact]
    public void EmergencyAndDisposeStopActiveAudioWithoutClearingTheOptIn() => AvaloniaTestDispatcher.Run(() =>
    {
        using var r = new Rig();
        r.Chant.Start();
        Task.Run(r.Chant.StopEmergency).GetAwaiter().GetResult();
        Assert.True(r.Played.Last().Voice.Stopped);
        Assert.False(r.Chant.IsRunning);
        Assert.True(CoreSettings.Current.MantraChantEnabled);
        r.Chant.Start();
        r.Chant.Dispose();
        Assert.True(r.Played.Last().Voice.Stopped);
        Assert.False(r.Chant.IsRunning);
        Assert.True(CoreSettings.Current.MantraChantEnabled);
        r.Chant.Start();
        Assert.False(r.Chant.IsRunning);
        Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    public void StartupAndRegisteredPanicDisarmButAutomaticStopDoesNot() => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current;
        var saved = s.MantraChantEnabled;
        try
        {
            s.MantraChantEnabled = true;
            MantraChantService.ClearStaleSwitchOnLaunch();
            Assert.False(s.MantraChantEnabled);
            Assert.False(MantraChantService.Instance.IsRunning);
            s.MantraChantEnabled = true;
            MantraChantService.Instance.StopEmergency();
            Assert.True(s.MantraChantEnabled);
            PanicSurfaces.All.Single(x => x.Id == "mantra-chant").Stop(null);
            Assert.False(s.MantraChantEnabled);
        }
        finally { s.MantraChantEnabled = saved; }
    });

    [Fact]
    public void WatchdogStillStopsMindWipeWhileTheChantIsWedgedInPlay()
    {
        // A UI thread hung inside Play() (LibVLC open) holds the chant's lock; the watchdog's chant step
        // waits on it, so it must come after mind wipe and unduck (review P2).
        var chant = MantraChantService.Instance;
        var s = CoreSettings.Current;
        var hooks = (chant.HasVoiced, chant.Next, chant.Resolve, chant.After, chant.Play);
        var saved = (s.AvatarMuted, s.MasterVolume, s.MantraChantVolume);
        var oldStop = CoreMindWipe.StopProvider;
        using var inPlay = new System.Threading.ManualResetEventSlim();
        using var release = new System.Threading.ManualResetEventSlim();
        using var wiped = new System.Threading.ManualResetEventSlim();
        Task? starter = null, teardown = null;
        try
        {
            (s.AvatarMuted, s.MasterVolume, s.MantraChantVolume) = (false, 80, 50);
            chant.HasVoiced = () => true;
            chant.Next = () => new MantraEntry { PromptAudio = "ask", ResponseAudio = "affirm" };
            chant.Resolve = p => p;
            chant.After = (a, d) => new Wait(a, d);
            chant.Play = (_, _, _) => { inPlay.Set(); release.Wait(TimeSpan.FromSeconds(10)); return null; };
            CoreMindWipe.StopProvider = () => wiped.Set();

            starter = Task.Run(chant.Start);
            Assert.True(inPlay.Wait(TimeSpan.FromSeconds(10)));
            teardown = Task.Run(PanicWatchdog.RunEmergencyTeardown);
            Assert.True(wiped.Wait(TimeSpan.FromSeconds(5)), "mind wipe waited behind the wedged chant");
            release.Set();
            Assert.True(teardown.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(chant.IsRunning);
        }
        finally
        {
            release.Set();
            starter?.Wait(TimeSpan.FromSeconds(10));
            teardown?.Wait(TimeSpan.FromSeconds(10));
            chant.Stop();
            (chant.HasVoiced, chant.Next, chant.Resolve, chant.After, chant.Play) = hooks;
            (s.AvatarMuted, s.MasterVolume, s.MantraChantVolume) = saved;
            CoreMindWipe.StopProvider = oldStop;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MindWipeService behind the Mind Wipe card: tick on (engine running) starts the
/// timed service, a tick plays a clip, Test plays one, the loop silences the ticks and pays Clean
/// Slate at 60 s, untick stops everything, and the engine stop (panic) stops a stopped-service clip.</summary>
public sealed class MindWipeTests
{
    [Fact]
    public void PlayedClipFeedsMemoryButFailedPlaybackDoesNot() => Run((player, voices, clock) =>
    {
        var uses = new List<string>();
        global::ConditioningControlPanel.Avalonia.App.FeatureUsed += uses.Add;
        try
        {
            player.TriggerOnce();
            Assert.Equal(new[] { "mindwipe" }, uses);
            var failed = new MindWipePlayer((path, volume, loop, ended) => null, (path, ended) => null);
            failed.TriggerOnce();
            Assert.Single(uses);
        }
        finally { global::ConditioningControlPanel.Avalonia.App.FeatureUsed -= uses.Add; }
    });

    // Restack b84 (#3811): the bug report's recent sounds name a mind wipe clip, one-shot and loop.
    [Fact]
    public void PlayedClipsReachTheRecentSoundsRing() => Run((player, voices, clock) =>
    {
        player.TriggerOnce();
        player.StartLoop(0.5);
        var noted = global::ConditioningControlPanel.Services.Audio.RecentSounds.Shared.Snapshot()
            .Where(e => e.Player is "mindwipe" or "mindwipe-loop").Select(e => e.Player).ToList();
        Assert.Contains("mindwipe", noted);
        Assert.Contains("mindwipe-loop", noted);
        player.StopLoop();
    });

    private sealed class Voice : LibVlcAudio.IPreparedVoice
    {
        public bool Loop, Disposed;
        public int Plays;
        public double Vol;
        public Action Ended = () => { };
        public double Volume { set => Vol = value; }
        public bool Play() { Plays++; return true; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Clock : TimeProvider
    {
        private long _now;
        private DateTimeOffset _wallOrigin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public TimeZoneInfo Zone = TimeZoneInfo.Utc;
        public override TimeZoneInfo LocalTimeZone => Zone;
        public override DateTimeOffset GetUtcNow() => _wallOrigin.AddMilliseconds(_now);
        public void JumpWallClock(TimeSpan by) => _wallOrigin += by;
        private readonly List<Timer> _timers = new();
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new Timer(this, cb, _now + (long)due.TotalMilliseconds, period == Timeout.InfiniteTimeSpan ? 0 : (long)period.TotalMilliseconds);
            _timers.Add(t);
            return t;
        }
        public void Advance(TimeSpan by)
        {
            var end = _now + (long)by.TotalMilliseconds;
            while (_timers.Where(t => t.Due <= end).OrderBy(t => t.Due).FirstOrDefault() is { } t)
            {
                _now = t.Due;
                if (t.Period > 0) t.Due += t.Period; else _timers.Remove(t);
                t.Cb(null);
            }
            _now = end;
        }
        private sealed class Timer(Clock c, TimerCallback cb, long due, long period) : ITimer
        {
            public TimerCallback Cb = cb; public long Due = due, Period = period;
            public bool Change(TimeSpan d, TimeSpan p) => false;
            public void Dispose() => c._timers.Remove(this);
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

    private static void Run(Action<MindWipePlayer, List<Voice>, Clock> body, double roll = 0.4) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.MindWipeEnabled, s.MindWipeLoop, s.MindWipeAudioPath, s.MindWipeVolume, s.MindWipeFrequency, s.MasterVolume);
            var engine = CoreSession.IsEngineRunningProvider;
            var dir = Directory.CreateTempSubdirectory();
            var clip = Path.Combine(dir.FullName, "wipe.mp3");
            File.WriteAllText(clip, "x");
            (s.MindWipeEnabled, s.MindWipeLoop, s.MindWipeAudioPath, s.MindWipeVolume, s.MindWipeFrequency, s.MasterVolume) = (false, false, clip, 40, 180, 100);
            var voices = new List<Voice>();
            var clock = new Clock();
            var player = new MindWipePlayer((path, vol, loop, ended) =>
            {
                Assert.Equal(clip, path);
                var v = new Voice { Loop = loop, Vol = vol, Ended = ended };
                voices.Add(v);
                return v;
            }, (path, ended) =>
            {
                Assert.Equal(clip, path);
                var v = new Voice { Ended = ended };
                voices.Add(v);
                return v;
            }, clock, roll: () => roll);   // 0.4: under 180/h's 0.5 per tick, every tick fires
            player.Seed();
            try { body(player, voices, clock); }
            finally
            {
                CoreMindWipe.Stop();
                player.UnwatchMaster();
                CoreMindWipe.StartProvider = null; CoreMindWipe.StartSessionProvider = null; CoreMindWipe.StopProvider = null; CoreMindWipe.IsRunningProvider = null;
                CoreMindWipe.TriggerOnceProvider = null; CoreMindWipe.StartLoopProvider = null; CoreMindWipe.StopLoopProvider = null;
                CoreMindWipe.IsLoopingProvider = null; CoreMindWipe.UpdateSettingsProvider = null;
                CoreMindWipe.ReloadClipsProvider = null; CoreMindWipe.ClipCountProvider = null;
                CoreSession.IsEngineRunningProvider = engine;
                (s.MindWipeEnabled, s.MindWipeLoop, s.MindWipeAudioPath, s.MindWipeVolume, s.MindWipeFrequency, s.MasterVolume) = saved;
                dir.Delete(true);
            }
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stop_during_preparation_cancels_a_one_shot(bool timed) => Run((_, voices, clock) =>
    {
        var prepared = new Voice();
        MindWipePlayer? player = null;
        player = new MindWipePlayer((path, volume, loop, ended) => null, (path, ended) =>
        {
            player!.Stop();
            return prepared;
        }, clock, roll: () => 0);
        if (timed) { player.Start(180, 0.4); clock.Advance(TimeSpan.FromSeconds(10)); }
        else player.TriggerOnce();
        Assert.Equal(0, prepared.Plays);
        Assert.True(prepared.Disposed);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Newer_one_shot_wins_when_preparations_finish_in_reverse_order(bool timed) => Run((_, voices, clock) =>
    {
        var older = new Voice();
        var newer = new Voice();
        var preparations = 0;
        MindWipePlayer? player = null;
        player = new MindWipePlayer((path, volume, loop, ended) => null, (path, ended) =>
        {
            if (++preparations != 1) return newer;
            player!.TriggerOnce(); // Finish the newer preparation before returning the older one.
            return older;
        }, clock, roll: () => 0);
        try
        {
            if (timed) { player.Start(180, 0.4); clock.Advance(TimeSpan.FromSeconds(10)); }
            else player.TriggerOnce();
            Assert.Equal(2, preparations);
            Assert.Equal(0, older.Plays);
            Assert.True(older.Disposed);
            Assert.Equal(1, newer.Plays);
            Assert.False(newer.Disposed);
        }
        finally { player.Stop(); }
        Assert.True(newer.Disposed);
    });

    [Fact]
    public void Volume_changed_during_preparation_is_applied_before_play() => Run((_, voices, clock) =>
    {
        var prepared = new Voice();
        MindWipePlayer? player = null;
        player = new MindWipePlayer((path, volume, loop, ended) => null, (path, ended) =>
        {
            player!.UpdateSettings(180, 0.8);
            return prepared;
        }, clock, roll: () => 0);
        try
        {
            player.TriggerOnce();
            Assert.Equal(1, prepared.Plays);
            Assert.Equal(0.8, prepared.Vol, 3);
        }
        finally { player.Stop(); }
    });

    [Fact]
    public void Card_drives_the_service_ticks_loop_clean_slate_and_stop() => Run((player, voices, clock) =>
    {
        CoreSession.IsEngineRunningProvider = () => true;
        double? cleanSlate = null;
        player.CleanSlate = secs => cleanSlate = secs;
        var card = new MindWipeFeatureControl();
        var host = new Window { Content = card };
        host.Show();
        try
        {
            card.BtnTest.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(voices);                                    // Test plays with the service stopped
            Assert.Equal(0.4, voices[0].Vol, 3);                      // at the saved volume

            card.ChkEnable.IsChecked = true;
            Assert.True(player.IsRunning);
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Equal(2, voices.Count);                            // one tick, one clip
            Assert.True(voices[0].Disposed);                          // displacing the previous one

            card.ChkLoop.IsChecked = true;
            Assert.True(player.IsLooping);
            var loop = voices.Last();
            Assert.True(loop.Loop);
            clock.Advance(TimeSpan.FromSeconds(59));
            Assert.Null(cleanSlate);
            Assert.Equal(3, voices.Count);                            // no random clips over the loop
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(60, cleanSlate);

            card.ChkEnable.IsChecked = false;                         // #1304: off stops it all
            Assert.False(player.IsRunning);
            Assert.False(player.IsLooping);
            Assert.True(voices.All(v => v.Disposed));
            clock.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(3, voices.Count);
        }
        finally { host.Close(); }
    });

    [Fact]
    public void Session_mode_escalates_per_five_minute_block_owns_the_presence_and_resumes_plain() => Run((player, voices, clock) =>
    {
        // WPF MindWipeService.StartSession/Timer_Tick (MindWipeService.cs:232-248, 715-718) and the
        // resume through Start (SessionEngine.cs:575-576), with a roll of 0.05 per tick.
        var oldRpc = global::ConditioningControlPanel.Avalonia.App.DiscordRpc;
        var rpc = global::ConditioningControlPanel.Avalonia.App.DiscordRpc = new ConditioningControlPanel.Services.DiscordRichPresenceService();
        try
        {
            CoreMindWipe.StartSession(1, 0.7);
            Assert.True(player.IsRunning);
            Assert.Equal("Deep conditioning", rpc.CurrentState);   // WPF MindWipeService.cs:245
            clock.Advance(TimeSpan.FromSeconds(290));
            Assert.Empty(voices);                                     // block 0: 1/30 per tick
            clock.Advance(TimeSpan.FromSeconds(20));
            Assert.NotEmpty(voices);                                  // block 1: 2/30 per tick
            Assert.Equal(0.7, voices[0].Vol, 3);

            CoreMindWipe.StartSession(9, 0.1);                        // a running service ignores it
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Equal(0.7, voices.Last().Vol, 3);

            CoreMindWipe.Stop();                                      // session pause
            Assert.Equal("Browsing", rpc.CurrentState);
            var played = voices.Count;
            CoreMindWipe.Start(1, 0.7);                               // resume: 1 per hour, no escalation
            clock.Advance(TimeSpan.FromMinutes(30));
            Assert.Equal(played, voices.Count);
        }
        finally
        {
            player.Stop();
            rpc.Dispose();
            global::ConditioningControlPanel.Avalonia.App.DiscordRpc = oldRpc;
        }
    }, roll: 0.05);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Session_escalation_uses_local_wall_time_for_clock_jumps_and_dst(bool daylightSaving, bool forward) => Run((player, voices, clock) =>
    {
        // WPF MindWipeService.cs:238,717: subtract local DateTimes, including DST offset changes.
        if (daylightSaving)
        {
            clock.Zone = TimeZoneInfo.CreateCustomTimeZone("MindWipe-test", TimeSpan.FromHours(-5), "Test", "Standard", "Daylight",
                new[] { TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday)) });
            // Five minutes before the tick that crosses the spring-forward or fall-back boundary.
            var start = forward ? new DateTimeOffset(2026, 3, 8, 6, 54, 50, TimeSpan.Zero)
                                : new DateTimeOffset(2026, 11, 1, 5, 54, 50, TimeSpan.Zero);
            clock.JumpWallClock(start - clock.GetUtcNow());
        }
        CoreMindWipe.StartSession(1, 0.7);
        clock.Advance(TimeSpan.FromMinutes(5));
        if (forward) Assert.Empty(voices); else Assert.NotEmpty(voices);
        var played = voices.Count;
        if (!daylightSaving) clock.JumpWallClock(TimeSpan.FromHours(forward ? 1 : -1));
        clock.Advance(TimeSpan.FromSeconds(10));
        // Forward: the wall clock crosses 65 minutes and raises the probability above 0.4.
        // Backward: negative elapsed time must go quiet, even though monotonic time advanced.
        Assert.Equal(forward ? played + 1 : played, voices.Count);
    }, roll: forward ? 0.4 : 0.05);

    [Fact]
    public void A_loop_that_dies_stops_reporting_looping_and_pays_no_clean_slate() => Run((player, voices, clock) =>
    {
        double? cleanSlate = null;
        player.CleanSlate = secs => cleanSlate = secs;
        player.StartLoop(0.5);
        Assert.True(player.IsLooping);
        voices.Single().Ended();                                      // decode error / device gone
        Assert.False(player.IsLooping);
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(cleanSlate);
    });

    [Fact]
    public void Engine_stop_silences_a_test_clip_and_the_loop() => Run((player, voices, clock) =>
    {
        player.TriggerOnce();
        player.StartLoop(0.5);
        CoreEngine.Stop();                                            // what StopEngine and panic call
        Assert.Equal(2, voices.Count);
        Assert.True(voices.All(v => v.Disposed));
        Assert.False(player.IsLooping);
    });

    /// <summary>restack b41 (#3868): WPF MindWipeService.EffectiveVolume (MindWipeService.cs:120) and its live master
    /// watch (:136-174): the heard level is the feature's own beneath the master dial, and what is sounding follows
    /// the dial while it plays. Master 0 is silence (ccp-bugs #1432).</summary>
    [Fact]
    public void Heard_level_sits_beneath_the_master_dial_and_follows_it_live() => Run((player, voices, clock) =>
    {
        var s = CoreSettings.Current;
        s.MasterVolume = 50;
        player.TriggerOnce();
        Assert.Equal(0.2, voices[0].Vol, 3);          // 40% under a 50% master
        player.StartLoop(0.8);
        var loop = voices.Last();
        Assert.True(loop.Loop);
        Assert.Equal(0.4, loop.Vol, 3);
        s.MasterVolume = 25;                          // moved while both play
        Assert.Equal(0.2, loop.Vol, 3);
        s.MasterVolume = 0;
        Assert.Equal(0, loop.Vol, 3);
        player.UpdateSettings(180, 0.5);              // the feature's own level still applies beneath it
        s.MasterVolume = 100;
        Assert.Equal(0.5, loop.Vol, 3);
        player.UnwatchMaster();
        s.MasterVolume = 10;                          // unwatched: no more live writes
        Assert.Equal(0.5, loop.Vol, 3);
    });

    [Theory]
    [InlineData(100, 0.4, 0.4)]
    [InlineData(50, 0.4, 0.2)]
    [InlineData(0, 1.0, 0)]
    [InlineData(150, 2.0, 1)]
    [InlineData(-5, 0.5, 0)]
    public void EffectiveVolume_is_the_linear_product_clamped(int master, double own, double heard) =>
        Assert.Equal(heard, global::ConditioningControlPanel.Services.MindWipeSchedule.EffectiveVolume(master, own), 6);

    [Fact]
    public void EffectiveVolume_of_NaN_is_silence() =>
        Assert.Equal(0, global::ConditioningControlPanel.Services.MindWipeSchedule.EffectiveVolume(100, double.NaN));
}

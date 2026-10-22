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
    private sealed class Voice : MindWipePlayer.IVoice
    {
        public bool Loop, Disposed;
        public double Vol;
        public Action Ended = () => { };
        public double Volume { set => Vol = value; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Clock : TimeProvider
    {
        private long _now;
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
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.MindWipeEnabled, s.MindWipeLoop, s.MindWipeAudioPath, s.MindWipeVolume, s.MindWipeFrequency);
            var engine = CoreSession.IsEngineRunningProvider;
            var dir = Directory.CreateTempSubdirectory();
            var clip = Path.Combine(dir.FullName, "wipe.mp3");
            File.WriteAllText(clip, "x");
            (s.MindWipeEnabled, s.MindWipeLoop, s.MindWipeAudioPath, s.MindWipeVolume, s.MindWipeFrequency) = (false, false, clip, 40, 180);
            var voices = new List<Voice>();
            var clock = new Clock();
            var player = new MindWipePlayer((path, vol, loop, ended) =>
            {
                Assert.Equal(clip, path);
                var v = new Voice { Loop = loop, Vol = vol, Ended = ended };
                voices.Add(v);
                return v;
            }, clock, roll: () => roll);   // 0.4: under 180/h's 0.5 per tick, every tick fires
            player.Seed();
            try { body(player, voices, clock); }
            finally
            {
                CoreMindWipe.Stop();
                CoreMindWipe.StartProvider = null; CoreMindWipe.StartSessionProvider = null; CoreMindWipe.StopProvider = null; CoreMindWipe.IsRunningProvider = null;
                CoreMindWipe.TriggerOnceProvider = null; CoreMindWipe.StartLoopProvider = null; CoreMindWipe.StopLoopProvider = null;
                CoreMindWipe.IsLoopingProvider = null; CoreMindWipe.UpdateSettingsProvider = null;
                CoreMindWipe.ReloadClipsProvider = null; CoreMindWipe.ClipCountProvider = null;
                CoreSession.IsEngineRunningProvider = engine;
                (s.MindWipeEnabled, s.MindWipeLoop, s.MindWipeAudioPath, s.MindWipeVolume, s.MindWipeFrequency) = saved;
                dir.Delete(true);
            }
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

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
        // WPF MindWipeService.StartSession/Timer_Tick (MindWipeService.cs:188-205, 679-689) and the
        // resume through Start (SessionEngine.cs:562), with a roll of 0.05 per tick.
        var oldRpc = global::ConditioningControlPanel.Avalonia.App.DiscordRpc;
        var rpc = global::ConditioningControlPanel.Avalonia.App.DiscordRpc = new ConditioningControlPanel.Services.DiscordRichPresenceService();
        try
        {
            CoreMindWipe.StartSession(1, 0.7);
            Assert.True(player.IsRunning);
            Assert.Equal("Deep conditioning", rpc.CurrentState);   // WPF MindWipeService.cs:201
            clock.Advance(TimeSpan.FromSeconds(290));
            Assert.Empty(voices);                                     // block 0: 1/30 per tick
            clock.Advance(TimeSpan.FromSeconds(20));
            Assert.NotEmpty(voices);                                  // block 1: 2/30 per tick
            Assert.Equal(0.7, voices[0].Vol, 3);

            CoreMindWipe.StartSession(9, 0.1);                        // a running service ignores it
            Assert.Equal(0.7, voices[0].Vol, 3);

            CoreMindWipe.Stop();                                      // session pause
            Assert.Equal("Browsing", rpc.CurrentState);
            var played = voices.Count;
            CoreMindWipe.Start(1, 0.7);                               // resume: 1 per hour, no escalation
            clock.Advance(TimeSpan.FromMinutes(30));
            Assert.Equal(played, voices.Count);
        }
        finally
        {
            rpc.Dispose();
            global::ConditioningControlPanel.Avalonia.App.DiscordRpc = oldRpc;
        }
    }, roll: 0.05);

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
}

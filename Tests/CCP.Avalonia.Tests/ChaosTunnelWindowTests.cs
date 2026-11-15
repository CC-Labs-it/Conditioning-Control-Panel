using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosTunnelWindowTests
{
    [Fact]
    public async Task Opt_in_opens_below_pinned_chrome_tracks_streak_and_panic_closes()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = CoreSettings.Current;
            var enabled = settings.ChaosTunnelEnabled;
            var pinned = settings.ChaosPinOnTop;
            var canOpen = ChaosTunnelWindow.CanOpen;
            try
            {
                ChaosTunnelWindow.CanOpen = () => true;
                settings.ChaosPinOnTop = true;
                settings.ChaosTunnelEnabled = false;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                Assert.Null(ChaosTunnelWindow.Active);
                ChaosRunHost.ForceShutdown();

                settings.ChaosTunnelEnabled = true;
                settings.ChaosPinOnTop = false;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                Assert.Null(ChaosTunnelWindow.Active);
                ChaosRunHost.ForceShutdown();
                settings.ChaosPinOnTop = true;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                var tunnel = Assert.IsType<ChaosTunnelWindow>(ChaosTunnelWindow.Active);
                Assert.False(tunnel.Topmost);
                Assert.True(ChaosRunHost.Hud!.Topmost);
                var scripts = new List<string>();
                tunnel.Web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
                tunnel.OnMessage("{\"type\":\"ready\"}");
                Assert.Contains(scripts, s => s.Contains("\"type\":\"run-start\""));
                tunnel.OnTimeout();
                Assert.Same(tunnel, ChaosTunnelWindow.Active);
                ChaosRunHost.State!.Combo = 4;
                Assert.Contains(scripts, s => s.Contains("\"type\":\"streak\"") && s.Contains("\"combo\":4"));
                ChaosRunHost.State.ActIndex = 3;
                Assert.Contains(scripts, s => s.Contains("\"type\":\"zone-hint\"") && s.Contains("\"depth\":3"));
                ChaosRunHost.ForceShutdown();
                Assert.Null(ChaosTunnelWindow.Active);

                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                Assert.NotNull(ChaosTunnelWindow.Active);
                ChaosRunHost.RequestStop();
                Assert.Null(ChaosTunnelWindow.Active);
                ChaosRunHost.ForceShutdown();

                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                var stalled = Assert.IsType<ChaosTunnelWindow>(ChaosTunnelWindow.Active);
                stalled.OnTimeout();
                Assert.Null(ChaosTunnelWindow.Active);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosTunnelWindow.CanOpen = canOpen;
                settings.ChaosTunnelEnabled = enabled;
                settings.ChaosPinOnTop = pinned;
            }
            return Task.CompletedTask;
        });
    }
    private sealed class Voice : LibVlcAudio.IPausableVoice
    {
        public double Volume { set { } }
        public readonly List<bool> Pauses = new();
        public bool Disposed;
        public void SetPaused(bool paused) => Pauses.Add(paused);
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task Unpinned_run_bridges_audio_video_and_exit_with_restart_protection()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = CoreSettings.Current;
            var (enabled, pinned) = (settings.ChaosTunnelEnabled, settings.ChaosPinOnTop);
            var (canOpen, sink, sound, ambient, path) = (ChaosTunnelWindow.CanOpen, ChaosTunnelWindow.Sink,
                ChaosTunnelWindow.PlaySound, ChaosTunnelWindow.PlayAmbient, ChaosTunnelWindow.AmbientPath);
            var file = Path.GetTempFileName();
            var voices = new List<Voice>();
            var sounds = new List<(string, float)>();
            var scripts = new List<string>();
            int sinks = 0;
            try
            {
                settings.ChaosTunnelEnabled = true;
                settings.ChaosPinOnTop = false;
                ChaosTunnelWindow.CanOpen = () => true;
                ChaosTunnelWindow.Sink = _ => { sinks++; return true; };
                ChaosTunnelWindow.PlaySound = (name, scale) => sounds.Add((name, scale));
                ChaosTunnelWindow.AmbientPath = () => file;
                ChaosTunnelWindow.PlayAmbient = (_, _) => { var v = new Voice(); voices.Add(v); return v; };
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                var w = Assert.IsType<ChaosTunnelWindow>(ChaosTunnelWindow.Active);
                Assert.False(w.Topmost);
                Assert.False(ChaosRunHost.Hud!.Topmost);
                Assert.Equal(1, sinks);
                w.Web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
                w.Web.OnNavigationCompleted(w.Web.Source);
                Assert.Contains(scripts, s => s.Contains("ccp-tunnel-connect") && s.Contains("invokeCSharpAction"));
                w.OnMessage("{\"type\":\"ready\"}");
                Assert.Single(voices);
                w.OnMessage("{\"type\":\"sfx\",\"name\":\"tunnel_exit\",\"scale\":0.32}");
                Assert.Equal(("tunnel_exit", 0.32f), Assert.Single(sounds));
                ChaosRunHost.TunnelVideo(true);
                ChaosRunHost.OnVideoEndedDuringRun();
                Assert.Equal(new[] { true, false }, voices[0].Pauses);
                Assert.Contains(scripts, s => s.Contains("\"video-playing\"") && s.Contains("\"on\":true"));
                ChaosRunHost.RequestStop();
                Assert.True(voices[0].Disposed);
                Assert.Same(w, ChaosTunnelWindow.Active);
                Assert.Contains(scripts, s => s.Contains("\"run-end\""));
                ChaosTunnelWindow.Open();
                Assert.Same(w, ChaosTunnelWindow.Active);
                Assert.Equal(2, voices.Count);
                w.OnMessage("{\"type\":\"exit-done\"}");
                w.OnExitTimeout();
                Assert.Same(w, ChaosTunnelWindow.Active);
                ChaosTunnelWindow.EndActive();
                w.OnMessage("{\"type\":\"exit-done\"}");
                Assert.Null(ChaosTunnelWindow.Active);
                Assert.True(voices[1].Disposed);
                ChaosTunnelWindow.Open();
                var timed = Assert.IsType<ChaosTunnelWindow>(ChaosTunnelWindow.Active);
                timed.OnMessage("{\"type\":\"ready\"}");
                ChaosTunnelWindow.EndActive();
                timed.OnExitTimeout();
                Assert.Null(ChaosTunnelWindow.Active);
                ChaosTunnelWindow.Open();
                var closed = Assert.IsType<ChaosTunnelWindow>(ChaosTunnelWindow.Active);
                closed.Web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
                ChaosTunnelWindow.CloseActive();
                scripts.Clear();
                closed.Web.OnNavigationCompleted(closed.Web.Source);
                Assert.DoesNotContain(scripts, s => s.Contains("ccp-tunnel-connect"));
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosTunnelWindow.CanOpen = canOpen;
                ChaosTunnelWindow.Sink = sink;
                ChaosTunnelWindow.PlaySound = sound;
                ChaosTunnelWindow.PlayAmbient = ambient;
                ChaosTunnelWindow.AmbientPath = path;
                settings.ChaosTunnelEnabled = enabled;
                settings.ChaosPinOnTop = pinned;
                File.Delete(file);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Win32_sink_uses_bottom_without_activation()
    {
        IntPtr target = IntPtr.Zero, after = IntPtr.Zero;
        uint flags = 0;
        Assert.True(Win32Overlay.Sink(new IntPtr(42), (h, a, x, y, w, hgt, f) =>
        { target = h; after = a; flags = f; return true; }));
        Assert.Equal(new IntPtr(42), target);
        Assert.Equal(new IntPtr(1), after);
        Assert.Equal(0x13u, flags & 0x13u);
    }

    [Fact]
    public async Task Run_video_events_pause_the_tunnel_and_are_unhooked_at_panic()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = CoreSettings.Current;
            var (enabled, pinned, canOpen, previous) = (settings.ChaosTunnelEnabled, settings.ChaosPinOnTop,
                ChaosTunnelWindow.CanOpen, CoreEngine.Video);
            var video = new MandatoryVideoScheduler(null!);
            var started = typeof(MandatoryVideoScheduler).GetField("VideoStarted", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var ended = typeof(MandatoryVideoScheduler).GetField("VideoEnded", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try
            {
                settings.ChaosTunnelEnabled = true; settings.ChaosPinOnTop = true;
                ChaosTunnelWindow.CanOpen = () => true; CoreEngine.Video = video;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                var w = Assert.IsType<ChaosTunnelWindow>(ChaosTunnelWindow.Active);
                var scripts = new List<string>();
                w.Web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
                w.OnMessage("{\"type\":\"ready\"}");
                Assert.IsType<Action>(started.GetValue(video)).Invoke();
                Assert.IsType<Action>(ended.GetValue(video)).Invoke();
                Assert.Contains(scripts, s => s.Contains("\"video-playing\"") && s.Contains("\"on\":true"));
                Assert.Contains(scripts, s => s.Contains("\"video-playing\"") && s.Contains("\"on\":false"));
                ChaosRunHost.ForceShutdown();
                Assert.Null(started.GetValue(video));
                Assert.Null(ended.GetValue(video));
            }
            finally
            {
                ChaosRunHost.ForceShutdown(); CoreEngine.Video = previous;
                ChaosTunnelWindow.CanOpen = canOpen;
                settings.ChaosTunnelEnabled = enabled; settings.ChaosPinOnTop = pinned;
            }
            return Task.CompletedTask;
        });
    }

}

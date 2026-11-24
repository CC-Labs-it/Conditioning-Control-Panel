using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using System.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public class HomeBrowserEnhanceTests
{
    [Fact]
    public void PollReadsVideoAndDetachStopsRequests() => AvaloniaTestDispatcher.Run(() =>
    {
        var scripts = new List<string>();
        var host = new WebHost { ScriptInvoker = script =>
        {
            scripts.Add(script);
            return Task.FromResult<string?>("{\"t\":12.5,\"d\":30,\"paused\":false,\"rx\":0.1,\"ry\":0.2,\"rw\":0.8,\"rh\":0.7}");
        }};
        using var source = new WebHostVideoTimeSource(host);
        double? notified = null;
        source.PlaybackTimeChanged += time => notified = time;
        source.Attach();
        source.PollAsync().GetAwaiter().GetResult();
        Assert.Equal(12.5, notified);
        Assert.Equal(30, source.GetDurationSeconds());
        Assert.True(source.IsPlaying);
        source.Detach();
        source.PollAsync().GetAwaiter().GetResult();
        Assert.Single(scripts);
    });
    [Fact]
    public void PlaybackCommandsUsePageVideo() => AvaloniaTestDispatcher.Run(() =>
    {
        var scripts = new List<string>();
        var host = new WebHost { ScriptInvoker = script =>
        {
            scripts.Add(script);
            return Task.FromResult<string?>(null);
        }};
        using var source = new WebHostVideoTimeSource(host);
        source.Seek(2.5);
        source.Pause();
        source.Play();
        Assert.Contains("currentTime=2.5", scripts[0]);
        Assert.Contains("v.pause()", scripts[1]);
        Assert.Contains("v.play()", scripts[2]);
    });
    [Fact]
    public void HomeMatchesNavigationAndUnbindsOnToggleAndDetach() => AvaloniaTestDispatcher.Run(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.Current.BrowserEnhanceIfPossible;
        CoreSettings.Current.BrowserEnhanceIfPossible = false;
        var shell = new MainShellWindow();
        string? path = null;
        try
        {
            shell.Show();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var checkbox = tab.FindControl<CheckBox>("ToggleEnhanceIfPossible")!;
            var status = tab.FindControl<TextBlock>("TxtEnhanceMatchStatus")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            web.ScriptInvoker = _ => Task.FromResult<string?>(null);
            var library = shell.BrowserEnhancementLibrary!;
            path = Path.Combine(library.LibraryFolder, "home-browser-test-unique.ccpenh.json");
            File.WriteAllText(path, "{\"$schema\":\"ccp-enhancement/v1\",\"version\":1,\"media_type\":\"video\",\"media_source\":\"https://hypnotube.com/watch/ccp-unique\",\"metadata\":{\"name\":\"Home test\",\"creator\":\"test\"}}");
            Assert.True(checkbox.IsEnabled);
            Assert.Equal(Loc.Get("browser_enhance_match_off"), status.Text);
            checkbox.IsChecked = true;
            Assert.True(CoreSettings.Current.BrowserEnhanceIfPossible);
            Assert.Equal(Loc.Get("browser_enhance_match_none"), status.Text);
            const string url = "https://hypnotube.com/watch/ccp-unique";
            Assert.NotNull(library.FindMatch(url, MediaTypes.Video));
            Assert.Same(shell, TopLevel.GetTopLevel(tab));
            web.OnNavigationCompleted(new Uri(url));
            Assert.Equal(url, web.CurrentUrl?.ToString());
            Assert.NotNull(tab.BrowserEnhanceHost.LoadedEnhancement);
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            Assert.True(tab.BrowserEnhanceHost.MatchesCurrentSource(url));
            Assert.Equal(string.Format(Loc.Get("browser_enhance_match_fmt"), "Home test"), status.Text);
            web.OnNavigationCompleted(new Uri("https://example.com/unrelated"));
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
            Assert.Equal(Loc.Get("browser_enhance_match_none"), status.Text);
            web.OnNavigationCompleted(new Uri(url));
            checkbox.IsChecked = false;
            Assert.False(CoreSettings.Current.BrowserEnhanceIfPossible);
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
            Assert.Equal(Loc.Get("browser_enhance_match_off"), status.Text);
            checkbox.IsChecked = true;
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            var original = File.ReadAllText(path);
            var unsupported = new Enhancement { MediaSource = url, Metadata = new EnhancementMetadata { Name = "Unsupported", Creator = "test" } };
            unsupported.Rules.Add(new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 1 }, Action = new TriggerEffectAction { EffectType = EffectTypes.Overlay, OverlayKind = OverlayKinds.BrainDrain } });
            File.WriteAllText(path, EnhancementSerializer.Save(unsupported));
            web.OnNavigationCompleted(new Uri(url));
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
            Assert.Equal(Loc.Get("browser_enhance_unsupported"), status.Text);
            File.WriteAllText(path, original);
            web.OnNavigationCompleted(new Uri(url));
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            shell.Close();
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
        }
        finally
        {
            shell.Close();
            if (path != null) File.Delete(path);
            CoreSettings.Current.BrowserEnhanceIfPossible = previous;
        }
    });
    [Fact]
    public void RuntimeDispatchesPause() => AvaloniaTestDispatcher.Run(() =>
    {
        var scripts = new List<string>();
        var web = new WebHost { ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); } };
        using var source = new WebHostVideoTimeSource(web);
        var dispatcher = new BrowserEnhancementDispatcher();
        dispatcher.DispatchAsync(new PauseAction(), new EnhancementDispatchContext(new Enhancement(), source, 0, null)).GetAwaiter().GetResult();
        Assert.Contains("v.pause()", Assert.Single(scripts));
    });

    [Fact]
    public void EnhancementCleanupStopsPlayingAudio()
    {
        var previous = CoreAudio.PlayStoppableProvider;
        var master = CoreSettings.Current.MasterVolume;
        var path = Path.GetTempFileName();
        var stops = 0;
        var gains = new List<float>();
        try
        {
            CoreSettings.Current.MasterVolume = 40;
            CoreAudio.PlayStoppableProvider = (_, gain, _, _, _) => { gains.Add(gain); return () => stops++; };
            using var source = new WebHostVideoTimeSource(new WebHost());
            var dispatcher = new BrowserEnhancementDispatcher();
            var ctx = new EnhancementDispatchContext(new Enhancement(), source, 0, null);
            dispatcher.DispatchAsync(new PlayAudioAction { Path = path, Volume = 50 }, ctx).GetAwaiter().GetResult();
            dispatcher.DispatchAsync(new PlayAudioAction { Path = path, Volume = 50 }, ctx).GetAwaiter().GetResult();
            Assert.Equal(1, stops); // the second clip replaces the first (WPF AudioService.cs:833)
            Assert.Equal(new[] { CoreAudio.SoundGain(50, 40), CoreAudio.SoundGain(50, 40) }, gains);
            Assert.IsAssignableFrom<IEnhancementRunCleanup>(dispatcher).ResetOverlayBands();
            Assert.Equal(2, stops);
        }
        finally { CoreAudio.PlayStoppableProvider = previous; CoreSettings.Current.MasterVolume = master; File.Delete(path); }
    }

    [Fact]
    public void BubbleBurstReleasesTheFieldItStartedOnStop() => AvaloniaTestDispatcher.Run(() =>
    {
        var (start, stop, calls) = (ConditioningControlPanel.Services.CoreBubbles.StartAction, ConditioningControlPanel.Services.CoreBubbles.StopAction, new List<string>());
        using var cts = new System.Threading.CancellationTokenSource(); using var source = new WebHostVideoTimeSource(new WebHost());
        try
        {
            (ConditioningControlPanel.Services.CoreBubbles.StartAction, ConditioningControlPanel.Services.CoreBubbles.StopAction) = (() => calls.Add("start"), () => calls.Add("stop"));
            var burst = new TriggerEffectAction { EffectType = EffectTypes.Bubble, DurationMs = 60_000 };
            new BrowserEnhancementDispatcher().DispatchAsync(burst, new EnhancementDispatchContext(new Enhancement(), source, 0, null), cts.Token).GetAwaiter().GetResult();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); cts.Cancel(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "start", "stop" }, calls); // WPF RealActionDispatcher.cs:387-391: Stop releases the burst early
        }
        finally { (ConditioningControlPanel.Services.CoreBubbles.StartAction, ConditioningControlPanel.Services.CoreBubbles.StopAction) = (start, stop); }
    });

    [Fact]
    public void AudioDuckingIsReleasedOnCompletionAndCleanup()
    {
        var player = CoreAudio.PlayStoppableProvider;
        var duck = CoreAudio.DuckProvider;
        var unduck = CoreAudio.UnduckProvider;
        var generation = CoreAudio.DuckGenerationProvider;
        var enabled = CoreSettings.Current.AudioDuckingEnabled;
        var level = CoreSettings.Current.DuckingLevel;
        var path = Path.GetTempFileName();
        var ducks = 0;
        var releases = new List<long>();
        Action? finish = null;
        try
        {
            CoreSettings.Current.AudioDuckingEnabled = true;
            CoreSettings.Current.DuckingLevel = 71;
            CoreAudio.DuckProvider = strength => { Assert.Equal(71, strength); ducks++; };
            CoreAudio.UnduckProvider = g => releases.Add(g);
            CoreAudio.DuckGenerationProvider = () => 42;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, done) => { finish = done; return () => { }; };
            using var source = new WebHostVideoTimeSource(new WebHost());
            var ctx = new EnhancementDispatchContext(new Enhancement(), source, 0, null);
            var dispatcher = new BrowserEnhancementDispatcher();
            var action = new PlayAudioAction { Path = path, Volume = 50, DuckOtherAudio = true };
            dispatcher.DispatchAsync(action, ctx).GetAwaiter().GetResult();
            Assert.Equal(1, ducks);
            finish!();
            Assert.Equal(new long[] { 42 }, releases);
            dispatcher.DispatchAsync(action, ctx).GetAwaiter().GetResult();
            dispatcher.ResetOverlayBands();
            Assert.Equal(new long[] { 42, 42 }, releases);
            finish!();
            Assert.Equal(2, releases.Count);
            CoreSettings.Current.AudioDuckingEnabled = false;
            var silentDuck = new BrowserEnhancementDispatcher();
            silentDuck.DispatchAsync(action, ctx).GetAwaiter().GetResult();
            silentDuck.ResetOverlayBands();
            Assert.Equal(2, ducks);
            Assert.Equal(2, releases.Count);
            CoreSettings.Current.AudioDuckingEnabled = true;
            action.DuckOtherAudio = false;
            var unrequested = new BrowserEnhancementDispatcher();
            unrequested.DispatchAsync(action, ctx).GetAwaiter().GetResult();
            unrequested.ResetOverlayBands();
            Assert.Equal(2, ducks);
            Assert.Equal(2, releases.Count);
        }
        finally
        {
            CoreAudio.PlayStoppableProvider = player; CoreAudio.DuckProvider = duck;
            CoreAudio.UnduckProvider = unduck; CoreAudio.DuckGenerationProvider = generation;
            CoreSettings.Current.AudioDuckingEnabled = enabled; CoreSettings.Current.DuckingLevel = level;
            File.Delete(path);
        }
    }

    [Fact]
    public void PlayingClipFollowsMasterVolumeUntilStopped()
    {
        var (live, stoppable, master) = (BrowserEnhancementDispatcher.LiveClipPlayer, CoreAudio.PlayStoppableProvider, CoreSettings.Current.MasterVolume);
        var path = Path.GetTempFileName();
        var voice = new FakeVoice();
        try
        {
            CoreSettings.Current.MasterVolume = 40;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => throw new InvalidOperationException("live path expected");
            BrowserEnhancementDispatcher.LiveClipPlayer = () => (_, gain, _) => { voice.Volume = gain; return voice; };
            using var source = new WebHostVideoTimeSource(new WebHost());
            var dispatcher = new BrowserEnhancementDispatcher();
            dispatcher.DispatchAsync(new PlayAudioAction { Path = path, Volume = 50 }, new EnhancementDispatchContext(new Enhancement(), source, 0, null)).GetAwaiter().GetResult();
            Assert.Equal(CoreAudio.SoundGain(50, 40), voice.Last);
            CoreSettings.Current.MasterVolume = 90;   // WPF AudioService.cs:897
            Assert.Equal(CoreAudio.SoundGain(50, 90), voice.Last);
            dispatcher.ResetOverlayBands();
            Assert.True(voice.Disposed);
            CoreSettings.Current.MasterVolume = 10;
            Assert.Equal(CoreAudio.SoundGain(50, 90), voice.Last);
        }
        finally
        {
            (BrowserEnhancementDispatcher.LiveClipPlayer, CoreAudio.PlayStoppableProvider, CoreSettings.Current.MasterVolume) = (live, stoppable, master);
            File.Delete(path);
        }
    }

    private sealed class FakeVoice : MindWipePlayer.IVoice
    {
        public float Last;
        public bool Disposed;
        public double Volume { set => Last = (float)value; }
        public void Dispose() => Disposed = true;
    }

    // WPF RealActionDispatcher.cs:222-273: pink and spiral bands, ramps and one-shots; brain drain stays unbound.
    [Fact]
    public void OverlayEffectsHoldRampReleaseAndStopOnResetAndPanic() => AvaloniaTestDispatcher.Run(() =>
    {
        ChatThresholdLinksTests.Setup();
        var lifetimeField = typeof(global::Avalonia.Application).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(f => typeof(global::Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime).IsAssignableFrom(f.FieldType));
        var oldLifetime = global::Avalonia.Application.Current!.ApplicationLifetime;
        using var lifetime = new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var (pinkChecks, spiralChecks) = (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests);
        var s = CoreSettings.Current;
        var (pinkOn, spiralOn) = (s.PinkFilterEnabled, s.SpiralEnabled);
        var host = new Window();
        try
        {
            lifetimeField.SetValue(global::Avalonia.Application.Current, lifetime);
            lifetime.MainWindow = host;
            (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests) = (true, true);
            (s.PinkFilterEnabled, s.SpiralEnabled) = (false, false);
            PinkFilterOverlay.Stop(); SpiralOverlay.Stop();
            Enhancement With(string kind) { var e = new Enhancement(); e.Rules.Add(new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 1 }, Action = new TriggerEffectAction { EffectType = EffectTypes.Overlay, OverlayKind = kind } }); return e; }
            Assert.True(BrowserEnhancementDispatcher.Supports(With(OverlayKinds.PinkFilter)));
            Assert.True(BrowserEnhancementDispatcher.Supports(With(OverlayKinds.Spiral)));
            Assert.False(BrowserEnhancementDispatcher.Supports(With(OverlayKinds.BrainDrainMelt)));
            using var source = new WebHostVideoTimeSource(new WebHost());
            var ctx = new EnhancementDispatchContext(new Enhancement(), source, 0, null);
            var dispatcher = new BrowserEnhancementDispatcher();
            void Send(string kind, EffectPhase phase, double opacity, string id = "band") => dispatcher.DispatchAsync(new TriggerEffectAction
                { EffectType = EffectTypes.Overlay, OverlayKind = kind, Phase = phase, Opacity = opacity, EffectId = id, DurationMs = 60_000 }, ctx).GetAwaiter().GetResult();
            Send(OverlayKinds.PinkFilter, EffectPhase.Start, 0.3);
            Assert.Equal(0.3, PinkFilterOverlay.VoiceOpacity);
            Send(OverlayKinds.PinkFilter, EffectPhase.Update, 0.6);
            Assert.Equal(0.6, PinkFilterOverlay.VoiceOpacity);
            Send(OverlayKinds.Spiral, EffectPhase.Stop, 0, "band"); // remembered kind wins (WPF :237-246)
            Assert.Null(PinkFilterOverlay.VoiceOpacity);
            Send(OverlayKinds.Spiral, EffectPhase.OneShot, 0.4, "shot");
            Assert.True(SpiralOverlay.HasTimedPayload);
            Assert.Null(SpiralOverlay.VoiceOpacity);
            Send(OverlayKinds.Spiral, EffectPhase.Start, 0.5, "leak");
            Assert.Equal(0.5, SpiralOverlay.VoiceOpacity);
            dispatcher.ResetOverlayBands(); // engine Stop on unbind: a band whose Stop never came
            Assert.Null(SpiralOverlay.VoiceOpacity);
            Send(OverlayKinds.PinkFilter, EffectPhase.Start, 0.3, "late");
            Assert.Null(PinkFilterOverlay.VoiceOpacity); // a retired run shows nothing
            var live = new BrowserEnhancementDispatcher();
            live.DispatchAsync(new TriggerEffectAction { EffectType = EffectTypes.Overlay, OverlayKind = OverlayKinds.PinkFilter, Phase = EffectPhase.Start, Opacity = 0.7, EffectId = "p" }, ctx).GetAwaiter().GetResult();
            Assert.Equal(0.7, PinkFilterOverlay.VoiceOpacity);
            foreach (var surface in PanicSurfaces.All.Where(x => x.Id is "bubble-host" or "voice-overlays")) surface.Stop(null);
            Assert.Null(PinkFilterOverlay.VoiceOpacity);
            Assert.False(SpiralOverlay.HasTimedPayload);
        }
        finally
        {
            PinkFilterOverlay.Stop(); SpiralOverlay.Stop(); PinkFilterOverlay.CloseAll(); SpiralOverlay.CloseAll();
            (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests) = (pinkChecks, spiralChecks);
            (s.PinkFilterEnabled, s.SpiralEnabled) = (pinkOn, spiralOn);
            lifetimeField.SetValue(global::Avalonia.Application.Current, oldLifetime);
            host.Close();
        }
    });

    [Fact]
    public void WebcamAdapterForwardsUntilDisposed() => AvaloniaTestDispatcher.Run(() =>
    {
        var camera = new BrowserEnhancementWebcam();
        int count = 0;
        camera.OnBlink += () => count++;
        WebcamTracker.Instance.RaiseBlinkForTest();
        camera.Dispose();
        WebcamTracker.Instance.RaiseBlinkForTest();
        Assert.Equal(1, count);
    });
}

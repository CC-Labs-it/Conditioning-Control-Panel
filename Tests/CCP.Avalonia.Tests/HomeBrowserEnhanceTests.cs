using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            unsupported.Rules.Add(new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 1 }, Action = new TriggerEffectAction { EffectType = EffectTypes.Overlay, OverlayKind = OverlayKinds.PinkFilter } });
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
    public void HomeSpeakBandStopsOnExitAndUnbind() => AvaloniaTestDispatcher.Run(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.Current.BrowserEnhanceIfPossible;
        CoreSettings.Current.BrowserEnhanceIfPossible = false;
        var shell = new MainShellWindow();
        string? path = null;
        double time = 0;
        int shown = 0, stopped = 0, created = 0;
        try
        {
            shell.Show();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            tab.BrowserAutomaticPolling = false;
            web.ScriptInvoker = _ => Task.FromResult<string?>($"{{\"t\":{time},\"d\":20,\"paused\":false,\"rx\":0,\"ry\":0,\"rw\":1,\"rh\":1}}");
            tab.BrowserSpeakSessionFactory = (effect, source) =>
            {
                Assert.Same(tab.BrowserEnhanceSource, source);
                created++;
                var pending = new TaskCompletionSource<ConditioningControlPanel.Services.Speech.PhraseResult>();
                return new SpeakPromptSession(effect, source, () => true, () => Task.FromResult(true), () => false, () => { },
                    (_, ct) => { ct.Register(() => pending.TrySetCanceled(ct)); return pending.Task; },
                    _ => shown++, () => { }, stopCapture: () => stopped++);
            };
            const string url = "https://hypnotube.com/watch/ccp-speak-unique";
            path = Path.Combine(shell.BrowserEnhancementLibrary!.LibraryFolder, "home-browser-speak-unique.ccpenh.json");
            var enhancement = new Enhancement { MediaType = MediaTypes.Video, MediaSource = url,
                Metadata = new EnhancementMetadata { Name = "Speak test", Creator = "test" }, TimelineItems = {
                    new TimelineItem { Kind = TimelineItemKind.Effect, Start = 1, Duration = 3,
                        EffectType = EffectTypes.Speak, EffectActivation = EffectActivation.Region,
                        EffectSpeakTarget = "yes", EffectSpeakCueMode = SpeakCueMode.Persistent } } };
            File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
            tab.FindControl<CheckBox>("ToggleEnhanceIfPossible")!.IsChecked = true;
            web.OnNavigationCompleted(new Uri(url));
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            void Poll(double t) { time = t; tab.BrowserEnhanceSource!.PollAsync().GetAwaiter().GetResult(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            Poll(0); Poll(1.2);
            Assert.Equal(1, shown);
            Poll(5);
            Assert.Equal(1, stopped);
            Poll(0); Poll(1.2);
            Assert.Equal(2, shown);
            tab.StopBrowserEnhancement();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, stopped);
            Assert.Equal(2, created);
        }
        finally
        {
            shell.Close();
            if (path != null) File.Delete(path);
            CoreSettings.Current.BrowserEnhanceIfPossible = previous;
        }
    });

    [Fact]
    public void PanicStopsBrowserSpeakAndDoesNotResumeHeldMedia() => AvaloniaTestDispatcher.Run(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.Current.BrowserEnhanceIfPossible;
        CoreSettings.Current.BrowserEnhanceIfPossible = false;
        var shell = new MainShellWindow();
        string? path = null;
        var heard = new TaskCompletionSource<ConditioningControlPanel.Services.Speech.PhraseResult>();
        var scripts = new List<string>();
        int shown = 0, stopped = 0, hidden = 0;
        double time = 0;
        try
        {
            shell.Show();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            tab.BrowserAutomaticPolling = false;
            web.ScriptInvoker = script =>
            {
                scripts.Add(script);
                return Task.FromResult<string?>(FormattableString.Invariant($"{{\"t\":{time},\"d\":20,\"paused\":false,\"rx\":0,\"ry\":0,\"rw\":1,\"rh\":1}}"));
            };
            tab.BrowserSpeakSessionFactory = (effect, source) => new SpeakPromptSession(effect, source,
                () => true, () => Task.FromResult(true), () => false, () => { }, (_, _) => heard.Task,
                _ => shown++, () => hidden++, stopCapture: () => stopped++);
            const string url = "https://hypnotube.com/watch/ccp-panic-speak";
            path = Path.Combine(shell.BrowserEnhancementLibrary!.LibraryFolder, "home-browser-panic-speak.ccpenh.json");
            var enhancement = new Enhancement { MediaType = MediaTypes.Video, MediaSource = url,
                Metadata = new EnhancementMetadata { Name = "Panic speak", Creator = "test" } };
            enhancement.Rules.Add(new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 1 },
                Action = new TriggerEffectAction { EffectType = EffectTypes.Speak, SpeakTarget = "yes",
                    SpeakCueMode = SpeakCueMode.Persistent, SpeakHoldMode = SpeakHoldMode.Pause } });
            File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
            tab.FindControl<CheckBox>("ToggleEnhanceIfPossible")!.IsChecked = true;
            web.OnNavigationCompleted(new Uri(url));
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            void Poll(double t) { time = t; tab.BrowserEnhanceSource!.PollAsync().GetAwaiter().GetResult(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            Poll(0); Poll(1.2);
            Assert.Equal(1, shown);
            Assert.Contains(scripts, s => s.Contains("v.pause()"));
            scripts.Clear();
            var oldSource = tab.BrowserEnhanceSource;
            PanicSurfaces.All.Single(s => s.Id == "browser-media").Stop(shell);
            Assert.Equal(1, stopped);
            Assert.False(tab.BrowserEnhanceHost.IsRunning);
            Assert.Contains(scripts, s => s.StartsWith(WebHostMedia.PanicPauseScript) && s.Contains("ccpEnhancementResume"));
            var token = tab.BrowserEnhanceResumeToken;
            Assert.NotNull(token);
            web.OnWebMessage("{\"type\":\"ccpEnhancementResume\",\"id\":\"stale\"}");
            Assert.False(tab.BrowserEnhanceHost.IsRunning);
            web.OnWebMessage(System.Text.Json.JsonSerializer.Serialize(new { type = "ccpEnhancementResume", id = token }));
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            Assert.NotSame(oldSource, tab.BrowserEnhanceSource);
            Assert.Equal(1, shown); // the retired session is not restarted by the rebind
            heard.SetResult(ConditioningControlPanel.Services.Speech.PhraseResult.NotAvailable);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, hidden);
            Assert.DoesNotContain(scripts, s => s.Contains("v.play()"));
        }
        finally
        {
            heard.TrySetResult(ConditioningControlPanel.Services.Speech.PhraseResult.NotAvailable);
            shell.Close();
            if (path != null) File.Delete(path);
            CoreSettings.Current.BrowserEnhanceIfPossible = previous;
        }
    });

    [Fact]
    public void ResumeScriptIgnoresPagePlayUntilATrustedPress()
    {
        // The real page script under a minimal DOM: a page's own play() after panic must not rebind.
        const string dom = "var L={},v={paused:false,currentTime:1.2},sent=[];" +
            "globalThis.document={querySelector:()=>v,addEventListener:(n,f)=>(L[n]=L[n]||[]).push(f),removeEventListener:(n,f)=>L[n]=(L[n]||[]).filter(x=>x!==f)};" +
            "globalThis.window={invokeCSharpAction:m=>sent.push(m)};function fire(n,e){(L[n]||[]).slice().forEach(f=>f(e));}";
        const string drive = "fire('playing',{target:v});fire('pointerdown',{isTrusted:false});fire('playing',{target:v});var early=sent.length;" +
            "fire('keydown',{isTrusted:true});v.paused=true;fire('playing',{target:v});v.paused=false;fire('playing',{target:v});fire('playing',{target:v});" +
            "console.log(JSON.stringify({early:early,sent:sent}));";
        var psi = new System.Diagnostics.ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        System.Diagnostics.Process? node;
        try { node = System.Diagnostics.Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception e) { Assert.Skip("node not installed: " + e.Message); return; }
        using (node!)
        {
            node.StandardInput.Write(dom + SettingsTabView.ResumeScriptFor("tok") + drive);
            node.StandardInput.Close();
            var output = node.StandardOutput.ReadToEnd();
            Assert.True(node.WaitForExit(10000));
            using var result = System.Text.Json.JsonDocument.Parse(output);
            Assert.Equal(0, result.RootElement.GetProperty("early").GetInt32());
            var sent = result.RootElement.GetProperty("sent");
            Assert.Equal(1, sent.GetArrayLength());
            using var message = System.Text.Json.JsonDocument.Parse(sent[0].GetString()!);
            Assert.Equal("tok", message.RootElement.GetProperty("id").GetString());
            Assert.Equal(1.2, message.RootElement.GetProperty("t").GetDouble());
        }
    }

    [Fact]
    public void PanicThenResumeRebindsNonSpeakEnhancementOnSamePage() => AvaloniaTestDispatcher.Run(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.Current.BrowserEnhanceIfPossible;
        CoreSettings.Current.BrowserEnhanceIfPossible = false;
        var shell = new MainShellWindow();
        string? path = null;
        double time = 0;
        var scripts = new List<string>();
        try
        {
            shell.Show();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            tab.BrowserAutomaticPolling = false;
            web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(FormattableString.Invariant($"{{\"t\":{time},\"d\":20,\"paused\":false,\"rx\":0,\"ry\":0,\"rw\":1,\"rh\":1}}")); };
            const string url = "https://hypnotube.com/watch/ccp-resume-test";
            path = Path.Combine(shell.BrowserEnhancementLibrary!.LibraryFolder, "home-browser-resume.ccpenh.json");
            var enhancement = new Enhancement { MediaType = MediaTypes.Video, MediaSource = url,
                Metadata = new EnhancementMetadata { Name = "Resume test", Creator = "test" } };
            enhancement.Rules.Add(new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 1 }, Action = new PauseAction() });
            File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
            tab.FindControl<CheckBox>("ToggleEnhanceIfPossible")!.IsChecked = true;
            web.OnNavigationCompleted(new Uri(url));
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            void Poll(double t) { time = t; tab.BrowserEnhanceSource!.PollAsync().GetAwaiter().GetResult(); }
            Poll(0); Poll(1.2);
            Assert.Contains(scripts, s => s.Contains("v.pause()"));
            var old = tab.BrowserEnhanceSource;
            PanicSurfaces.All.Single(s => s.Id == "browser-media").Stop(shell);
            Assert.False(tab.BrowserEnhanceHost.IsRunning);
            var token = tab.BrowserEnhanceResumeToken;
            Assert.NotNull(token);
            web.OnWebMessage("{\"type\":\"ccpEnhancementResume\",\"id\":\"stale\"}");
            Assert.False(tab.BrowserEnhanceHost.IsRunning);
            var message = System.Text.Json.JsonSerializer.Serialize(new { type = "ccpEnhancementResume", id = token, t = 1.2 });
            web.OnWebMessage(message);
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            Assert.NotSame(old, tab.BrowserEnhanceSource);
            var current = tab.BrowserEnhanceSource;
            web.OnWebMessage(message);
            Assert.Same(current, tab.BrowserEnhanceSource);
            // The rebind starts at the resumed playhead: the pause at 1 s already fired on this page.
            scripts.Clear();
            Poll(1.2); Poll(1.5);
            Assert.DoesNotContain(scripts, s => s.Contains("v.pause()"));
            Poll(0); Poll(1.2);
            Assert.Contains(scripts, s => s.Contains("v.pause()"));
            PanicSurfaces.All.Single(s => s.Id == "browser-media").Stop(shell);
            var invalidated = tab.BrowserEnhanceResumeToken;
            tab.FindControl<CheckBox>("ToggleEnhanceIfPossible")!.IsChecked = false;
            web.OnWebMessage(System.Text.Json.JsonSerializer.Serialize(new { type = "ccpEnhancementResume", id = invalidated }));
            Assert.False(tab.BrowserEnhanceHost.IsRunning);
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

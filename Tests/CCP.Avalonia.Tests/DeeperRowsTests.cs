using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Deeper;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

/// <summary>Rows views-deeper-editor-window / views-deeper-enhancement-player: the preview host
/// fence, local audio transport + panic, and the player's create-new / open-in-editor routes.</summary>
public sealed class DeeperRowsTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Invoke(object o, string name, params object?[] args) =>
        o.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(o, args);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task EditorPreviewRefusesHostsOffTheAllowlist() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        foreach (var (url, allowed) in new[] { ("https://evil.example.com/v/1", false), ("https://www.tiktok.com/@a/video/1", true) })
        {
            var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Video, MediaSource = url }, null);
            try
            {
                editor.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(allowed, editor.BrowserPreview.IsVisible);
                Assert.Equal(!allowed, editor.PreviewPlaceholder.IsVisible);
            }
            finally { editor.Close(); }
        }
        // Every later hop goes through the same rule (WPF NavigationStarting).
        Assert.False(DeeperPreview.IsAllowedPreviewHost(new Uri("http://hypnotube.com/x")));
        Assert.True(DeeperPreview.IsAllowedPreviewHost(new Uri("https://cdn.hypnotube.com/x")));
        Assert.False(DeeperPreview.IsAllowedPreviewHost(new Uri("https://hypnotube.com.evil.io/x")));
        return Task.CompletedTask;
    });

    private sealed class FakeAudio : IDeeperLocalAudio
    {
        public bool Playing, Disposed, Rejects, RejectsPause;
        public int Volume { get; set; }
        public double DurationSeconds { get; set; } = 95;
        public double PositionSeconds { get; set; }
        public void Play() { if (Rejects) throw new InvalidOperationException("Audio transport could not start"); Playing = true; }
        public void Pause() { if (RejectsPause) throw new InvalidOperationException(); Playing = false; }
        public event Action? Ended;
        public event Action? Failed;
        public void End() { Playing = false; Ended?.Invoke(); }
        public void Fail() { Playing = false; Failed?.Invoke(); }
        public void Dispose() { Playing = false; Disposed = true; }
    }

    [Fact]
    public Task EditorLocalAudioPlaysSeeksEndsAndStopsOnPanic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-audio-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        var fake = new FakeAudio();
        var original = DeeperLocalAudio.Open;
        DeeperLocalAudio.Open = p => Task.FromResult<IDeeperLocalAudio?>(p == path ? fake : null);
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = path }, null);
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            await editor._waveformLoad!.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1:35", editor.TxtTotalTime.Text);   // duration from the transport

            Click(editor.BtnPlayPause);
            Assert.True(fake.Playing);
            Assert.Equal(IconKind.Pause, editor.PlayPauseGlyph.Kind);

            // One stepped playhead tick: the clock and an exact length come from the transport.
            fake.PositionSeconds = 30;
            fake.DurationSeconds = 100;
            Invoke(editor, "PlayheadTimer_Tick", null, EventArgs.Empty);
            Assert.Equal("0:30", editor.TxtCurrentTime.Text);
            Assert.Equal("1:40", editor.TxtTotalTime.Text);
            Invoke(editor, "SeekToFraction", 0.5);              // a timeline click seeks the clip
            Assert.Equal(50, fake.PositionSeconds);

            // Panic (registered surface) stops it.
            PanicSurfaces.All.Single(s => s.Id == "deeper-editor-audio").Stop(null);
            Assert.False(fake.Playing);
            Assert.Equal(IconKind.Play, editor.PlayPauseGlyph.Kind);

            Click(editor.BtnPlayPause);
            fake.End();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.Play, editor.PlayPauseGlyph.Kind);
            Assert.Equal("0:00", editor.TxtCurrentTime.Text);

            // A start the transport rejects leaves the editor stopped, and the next press tries again.
            fake.Rejects = true;
            Click(editor.BtnPlayPause);
            Assert.Equal(IconKind.Play, editor.PlayPauseGlyph.Kind);
            fake.Rejects = false;
            Click(editor.BtnPlayPause);
            Assert.True(fake.Playing);
            Assert.Equal(IconKind.Pause, editor.PlayPauseGlyph.Kind);

            // A playback error after the start does the same.
            fake.Fail();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.Play, editor.PlayPauseGlyph.Kind);
            Click(editor.BtnPlayPause);
            Assert.True(fake.Playing);
        }
        finally
        {
            editor.Close();
            DeeperLocalAudio.Open = original;
            Directory.Delete(dir, true);
        }
        Assert.True(fake.Disposed);
    });

    [Fact]
    public Task PanicStopsLocalVideoBeforeTheFirstPollAndCleansUp() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var path = Path.Combine(Path.GetTempPath(), "ccp-panic-preview.mp4");
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Video, MediaSource = path }, null);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var navigated = typeof(DeeperEditorWindow).GetField("_browserNavigated", flags)!;
        var timer = (DispatcherTimer)typeof(DeeperEditorWindow).GetField("_playheadTimer", flags)!.GetValue(editor)!;
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            // Model a local media document before its first time poll reports playback.
            navigated.SetValue(editor, true);
            editor.BrowserPreview.Source = new Uri(path);
            timer.Start();
            PanicSurfaces.All.Single(s => s.Id == "deeper-editor-audio").Stop(null);
            Assert.False((bool)navigated.GetValue(editor)!);
            Assert.False(timer.IsEnabled);
            Assert.Equal("about:blank", editor.BrowserPreview.Source!.ToString());
            Assert.False(editor.BrowserPreview.AllowNavigation!(new Uri(path)));
            Assert.True(editor.BrowserPreview.AllowNavigation!(new Uri("about:blank")));
            Assert.Equal(IconKind.Play, editor.PlayPauseGlyph.Kind);
        }
        finally { editor.Close(); }
        Assert.False(timer.IsEnabled);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicStoppedLocalVideoRestartsOnlyOnExplicitPlay() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-panic-video-").FullName;
        var path = Path.Combine(dir, "clip.mp4");
        File.WriteAllBytes(path, new byte[16]);
        var scripts = new List<string>();
        TaskCompletionSource<string?>? pendingRead = null;
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Video, MediaSource = path }, null);
        editor.BrowserPreview.ScriptInvoker = js =>
        {
            scripts.Add(js);
            if (js.Contains("l.currentTime+") && pendingRead != null) return pendingRead.Task;
            return Task.FromResult<string?>(js.Contains("l.currentTime+") ? "0|10|0" : "ok");
        };
        var poll = typeof(DeeperEditorWindow).GetMethod("PollBrowserTimeAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Uri(path), editor.BrowserPreview.Source);
            Invoke(editor, "OnBrowserTimeChanged", 3.0);
            pendingRead = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var oldPoll = (Task)poll.Invoke(editor, null)!;
            Assert.False(oldPoll.IsCompleted);
            PanicSurfaces.All.Single(s => s.Id == "deeper-editor-audio").Stop(null);
            Assert.Equal("about:blank", editor.BrowserPreview.Source!.ToString());
            scripts.Clear();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(scripts, js => js.Contains("l.play();"));

            Click(editor.BtnPlayPause);
            var timer = (DispatcherTimer)typeof(DeeperEditorWindow).GetField("_playheadTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
            Assert.True(timer.IsEnabled);
            timer.Stop(); // Step the polls below in a deterministic order.
            Assert.Equal(new Uri(path), editor.BrowserPreview.Source);
            Assert.True(editor.BrowserPreview.AllowNavigation!(new Uri(path)));
            Assert.False(editor.BrowserPreview.AllowNavigation!(new Uri(Path.Combine(dir, "other.mp4"))));
            pendingRead.SetResult("0|10|0");
            pendingRead = null;
            await oldPoll;
            Assert.DoesNotContain(scripts, js => js.Contains("l.play();"));
            await (Task)poll.Invoke(editor, null)!;
            Assert.Contains(scripts, js => js.Contains("l.currentTime=3;l.play();"));
            Assert.DoesNotContain(scripts, js => js.Contains("l.autoplay=false;l.pause();"));

            // A second panic while recovery is pending must cancel it too.
            PanicSurfaces.All.Single(s => s.Id == "deeper-editor-audio").Stop(null);
            Click(editor.BtnPlayPause);
            PanicSurfaces.All.Single(s => s.Id == "deeper-editor-audio").Stop(null);
            scripts.Clear();
            await (Task)poll.Invoke(editor, null)!;
            Assert.DoesNotContain(scripts, js => js.Contains("l.play();"));
            Assert.Equal("about:blank", editor.BrowserPreview.Source!.ToString());
        }
        finally { editor.Close(); Directory.Delete(dir, true); }
    });

    [Fact]
    public Task PanicStopsLocalAudioEvenWhenTheEditorThinksItIsPaused() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-panic-audio-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        var fake = new FakeAudio();
        var original = DeeperLocalAudio.Open;
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(fake);
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = path }, null);
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            await editor._waveformLoad!.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            fake.Play(); // transport has started before the editor observes it
            PanicSurfaces.All.Single(s => s.Id == "deeper-editor-audio").Stop(null);
            Assert.False(fake.Playing);
            fake.Play();
            fake.RejectsPause = true;
            PanicSurfaces.All.Single(s => s.Id == "deeper-editor-audio").Stop(null);
            Assert.True(fake.Disposed);
            Assert.False(fake.Playing);
        }
        finally { editor.Close(); DeeperLocalAudio.Open = original; Directory.Delete(dir, true); }
        Assert.True(fake.Disposed);
    });

    /// <summary>Ledger #12 (WPF DeeperEditorWindow.xaml.cs:1196-1211): an audio clip's peaks come from
    /// AudioWaveformCache and become the editor's waveform; a failed decode leaves the flat strip.</summary>
    [Theory]
    [InlineData(false, true, 95)]
    [InlineData(true, true, 95)]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    public Task EditorAudioClipDrawsTheCachedWaveformPeaks(bool decodeFails, bool playbackAvailable, double playbackDuration) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-wave-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        var peaks = new[] { 0.1f, 0.9f, 0.4f };
        string? decoded = null, decodedAtOpen = null;
        var originalOpen = DeeperLocalAudio.Open;
        var originalDecoder = AudioWaveformCache.Decoder;
        DeeperLocalAudio.Open = _ =>
        {
            decodedAtOpen = decoded;
            return Task.FromResult<IDeeperLocalAudio?>(playbackAvailable ? new FakeAudio { DurationSeconds = playbackDuration } : null);
        };
        AudioWaveformCache.Decoder = p => { decoded = p; if (decodeFails) throw new InvalidOperationException("decode failed"); return new AudioWaveformResult { Peaks = peaks, DurationSeconds = 95 }; };
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = path }, null);
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            var load = editor._waveformLoad;
            Assert.NotNull(load);
            await load!.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(path, decodedAtOpen);
            Assert.Equal(path, decoded);
            var field = typeof(DeeperEditorWindow).GetField("_waveformPeaks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            if (decodeFails) Assert.Null(field.GetValue(editor));
            else Assert.Equal(peaks, (float[]?)field.GetValue(editor));
            var audio = (FakeAudio?)typeof(DeeperEditorWindow).GetField("_localAudio", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor);
            if (playbackAvailable) Assert.Equal(playbackDuration, audio!.DurationSeconds);
            else Assert.Null(audio);
            Assert.Equal(95d, (double)typeof(DeeperEditorWindow).GetField("_totalSeconds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!);
            Assert.Equal("1:35", editor.TxtTotalTime.Text);
            if (!decodeFails) Assert.NotEmpty((System.Collections.IEnumerable)typeof(DeeperEditorWindow).GetField("_rulerVisuals", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!);
        }
        finally
        {
            editor.Close();
            DeeperLocalAudio.Open = originalOpen;
            AudioWaveformCache.Decoder = originalDecoder;
            Directory.Delete(dir, true);
        }
    });


    /// <summary>MediaPlayer as the transport sees it: Playing fires on every start AND resume.</summary>
    private sealed class FakeEngine : DeeperLocalAudio.IEngine
    {
        public LibVLCSharp.Shared.VLCState State { get; set; } = LibVLCSharp.Shared.VLCState.NothingSpecial;
        public int Volume { get; set; }
        public long Time { get; set; }
        public long Length => 6000;
        public int Stops;
        public bool Starts = true;
        public bool Play() { Volume = 100; State = LibVLCSharp.Shared.VLCState.Opening; return Starts; }
        public void SetPause(bool pause) => State = pause ? LibVLCSharp.Shared.VLCState.Paused : LibVLCSharp.Shared.VLCState.Playing;
        public void Stop() { Stops++; State = LibVLCSharp.Shared.VLCState.Stopped; }
    }

    [Fact]
    public void TransportAppliesASeekOnceAndStopsAClipStillOpening()
    {
        var e = new FakeEngine();
        var t = new DeeperLocalAudio.Transport(e, 12000, () => { });
        e.State = LibVLCSharp.Shared.VLCState.Playing; t.Volume = 135; Assert.Equal(100, e.Volume);
        t.Volume = -1; Assert.Equal(0, e.Volume);
        foreach (var (linear, native) in new[] { (25, 63), (50, 79), (75, 91), (80, 93) })
        { t.Volume = linear; Assert.Equal(native, e.Volume); Assert.Equal(linear, t.Volume); }
        e.State = LibVLCSharp.Shared.VLCState.NothingSpecial;
        Assert.Equal(12, t.DurationSeconds);              // header estimate until playing

        // Seek before playback: taken by the first Playing, never again.
        t.PositionSeconds = 3;
        t.Play();
        e.State = LibVLCSharp.Shared.VLCState.Playing;
        t.OnPlaying();
        Assert.Equal(93, e.Volume);
        Assert.Equal(3000, e.Time);
        Assert.Equal(6, t.DurationSeconds);               // exact length once playing
        e.Time = 4000;
        t.Pause();
        t.Play();
        e.Volume = 100; // The new output on resume also loses the earlier native level.
        t.OnPlaying();
        Assert.Equal(93, e.Volume);
        Assert.Equal(4000, e.Time);

        // Seek while playing, play on, pause, resume: resume must not jump back to the seek.
        t.PositionSeconds = 5;
        Assert.Equal(5000, e.Time);
        e.Time = 7000;
        t.Pause();
        t.Play();
        t.OnPlaying();
        Assert.Equal(7000, e.Time);

        // Pause (or panic) while the clip is still opening stops it rather than letting it start.
        e.State = LibVLCSharp.Shared.VLCState.Stopped;
        t.Play();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Opening, e.State);
        t.Pause();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Stopped, e.State);
        e.Starts = false; Assert.Throws<InvalidOperationException>(t.Play);
        var failed = false; t.Failed += () => failed = true; t.OnError(); Assert.True(failed);
    }

    [Fact]
    public Task PlayerOpensTheEditorForNewAndExistingEnhancements() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var recent = settings.DeeperRecentFiles.ToList();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-player-").FullName;
        var media = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(media, new byte[16]);
        var enhPath = Path.Combine(dir, "x.ccpenh.json");
        File.WriteAllText(enhPath, EnhancementSerializer.Save(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }));
        var original = DeeperLocalAudio.Open;
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(null);
        var player = new EnhancementPlayerWindow(null, null);
        try
        {
            player.Show();
            var before = DeeperEditorWindow.OpenEditors.Count;

            // No enhancement for this media -> "Create new" opens a blank editor on it.
            player.OpenLocalMediaFile(media);
            Click(player.FindControl<Button>("BtnCreateNewEnhancement")!);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 1, DeeperEditorWindow.OpenEditors.Count);
            Assert.Null(DeeperEditorWindow.OpenEditors[^1].LoadedFilePath);

            // Open in editor: re-reads the file, and a second press activates the same editor.
            player.LoadEnhancementFile(enhPath);
            var jump = player.FindControl<Button>("BtnOpenInEditor")!;
            Click(jump);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 2, DeeperEditorWindow.OpenEditors.Count);
            Assert.Equal(enhPath, DeeperEditorWindow.OpenEditors[^1].LoadedFilePath);
            Click(jump);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 2, DeeperEditorWindow.OpenEditors.Count);
            Assert.Equal(Path.GetFullPath(enhPath), settings.DeeperRecentFiles[0]);
        }
        finally
        {
            foreach (var ed in DeeperEditorWindow.OpenEditors.ToList()) ed.Close();
            player.Close();
            DeeperLocalAudio.Open = original;
            settings.DeeperRecentFiles = recent;
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });
}

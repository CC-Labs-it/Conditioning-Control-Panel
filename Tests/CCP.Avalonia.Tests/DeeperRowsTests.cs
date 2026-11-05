using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Platform;
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
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
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
        public bool Playing, Disposed;
        public int Pauses;
        public double DurationSeconds { get; set; } = 95;
        public double PositionSeconds { get; set; }
        public void Play() => Playing = true;
        public void Pause() { Pauses++; Playing = false; }
        public event Action? Ended;
        public void End() { Playing = false; Ended?.Invoke(); }
        public void Dispose() => Disposed = true;
    }

    [Theory]
    [InlineData("mkv")]
    [InlineData("avi")]
    public Task EditorLocalVideoUsesTransport(string extension) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-video-").FullName;
        var path = Path.Combine(dir, "clip." + extension);
        File.WriteAllBytes(path, new byte[16]);
        var fake = new FakeAudio();
        var original = DeeperLocalAudio.OpenVideo;
        DeeperLocalAudio.OpenVideo = (p, image) => Task.FromResult<IDeeperLocalAudio?>(p == path ? fake : null);
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Video, MediaSource = path }, null);
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.LocalVideoImage.IsVisible);
            Assert.False(editor.BrowserPreview.IsVisible);
            Assert.False(fake.Playing);
            Assert.Equal("1:35", editor.TxtTotalTime.Text);
            Click(editor.BtnPlayPause);
            Assert.True(fake.Playing);
            Invoke(editor, "SeekToFraction", 0.5);
            Assert.Equal(47.5, fake.PositionSeconds);
            fake.End();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.Play, editor.PlayPauseGlyph.Kind);
            Assert.Equal("0:00", editor.TxtCurrentTime.Text);
        }
        finally
        {
            editor.Close();
            DeeperLocalAudio.OpenVideo = original;
            Directory.Delete(dir, true);
        }
        Assert.True(fake.Disposed);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditorLocalAudioPlaysSeeksEndsAndStopsOnPanic() => AvaloniaTestDispatcher.RunAsync(() =>
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
        }
        finally
        {
            editor.Close();
            DeeperLocalAudio.Open = original;
            Directory.Delete(dir, true);
        }
        Assert.True(fake.Disposed);
        return Task.CompletedTask;
    });

    /// <summary>MediaPlayer as the transport sees it: Playing fires on every start AND resume.</summary>
    private sealed class FakeEngine : DeeperLocalAudio.IEngine
    {
        public LibVLCSharp.Shared.VLCState State { get; set; } = LibVLCSharp.Shared.VLCState.NothingSpecial;
        public long Time { get; set; }
        public long Length => 6000;
        public int Stops;
        public bool HasPreview;
        public readonly System.Collections.Generic.List<string> Calls = new();
        public void Play() { Calls.Add("play"); State = LibVLCSharp.Shared.VLCState.Opening; }
        public void SetPause(bool pause) => State = pause ? LibVLCSharp.Shared.VLCState.Paused : LibVLCSharp.Shared.VLCState.Playing;
        public void Stop() { Stops++; State = LibVLCSharp.Shared.VLCState.Stopped; }
        public bool PreparePlayback()
        {
            if (!HasPreview) return false;
            HasPreview = false;
            Calls.Add("switch-media");
            return true;
        }
    }

    [Fact]
    public void VideoPrimesPausedAndAcceptsSeeksAndPlayDuringOpening()
    {
        var e = new FakeEngine();
        var t = new DeeperLocalAudio.Transport(e, 12000, () => { });
        t.PrimeVideo();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Opening, e.State);
        t.PositionSeconds = 3;
        e.State = LibVLCSharp.Shared.VLCState.Playing;
        t.OnPlaying();
        Assert.Equal(3000, e.Time);
        Assert.Equal(LibVLCSharp.Shared.VLCState.Playing, e.State);
        t.OnVideoFrame();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Paused, e.State);
        t.OnPaused();
        t.PositionSeconds = 5;
        Assert.Equal(5000, e.Time);
        Assert.Equal(LibVLCSharp.Shared.VLCState.Paused, e.State);
        t.Play();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Playing, e.State);

        var opening = new FakeEngine();
        var second = new DeeperLocalAudio.Transport(opening, 12000, () => { });
        second.PrimeVideo();
        second.Play();
        opening.State = LibVLCSharp.Shared.VLCState.Playing;
        second.OnPlaying();
        second.OnVideoFrame();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Paused, opening.State);
        second.OnPaused();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Playing, opening.State);
    }

    [Fact]
    public void PreferredOutputIsConfiguredBeforePlayback()
    {
        var calls = new System.Collections.Generic.List<string>();
        LibVlcAudio.PreparePreferredDevice("sink-2", new[] { "sink-1", "sink-2" },
            output => { calls.Add("output:" + output); return true; },
            (id, module) => calls.Add("device:" + id + ":" + module));
        Assert.Equal(new[] { "output:pulse", "device:sink-2:pulse" }, calls);
        calls.Clear();
        LibVlcAudio.PreparePreferredDevice("missing", new[] { "sink-1" },
            output => { calls.Add(output); return true; }, (id, module) => calls.Add(id));
        Assert.Empty(calls);
    }

    [Fact]
    public void VideoSwitchesFromSilentPreviewBeforeExplicitPlayback()
    {
        var engine = new FakeEngine { HasPreview = true };
        var transport = new DeeperLocalAudio.Transport(engine, 12000, () => { });
        transport.PrimeVideo();
        engine.State = LibVLCSharp.Shared.VLCState.Playing;
        transport.OnPlaying();
        transport.OnVideoFrame();
        transport.OnPaused();
        engine.Time = 4000;
        transport.Play();
        Assert.Equal(new[] { "play", "switch-media", "play" }, engine.Calls);
        engine.State = LibVLCSharp.Shared.VLCState.Playing;
        transport.OnPlaying();
        Assert.Equal(4000, engine.Time);
        transport.Dispose();
    }

    [Fact]
    public async Task NoFramePrimingTimesOutAndPauseCancelsIt()
    {
        var engine = new FakeEngine();
        var transport = new DeeperLocalAudio.Transport(engine, 12000, () => { });
        transport.PrimeVideo();
        engine.State = LibVLCSharp.Shared.VLCState.Playing;
        transport.OnPlaying();
        for (var i = 0; i < 100 && engine.State != LibVLCSharp.Shared.VLCState.Stopped; i++) await Task.Delay(50);
        Assert.Equal(LibVLCSharp.Shared.VLCState.Stopped, engine.State);
        transport.OnVideoFrame();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Stopped, engine.State);

        transport.PrimeVideo();
        engine.State = LibVLCSharp.Shared.VLCState.Playing;
        transport.OnPlaying();
        transport.Pause();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Stopped, engine.State);
        transport.OnVideoFrame();
        Assert.Equal(LibVLCSharp.Shared.VLCState.Stopped, engine.State);
        transport.Dispose();
    }

    [Fact]
    public Task PanicPausesTheIdleGlyphTransport() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var fake = new FakeAudio();
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, null);
        var fields = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var editors = (System.Collections.Generic.List<DeeperEditorWindow>)typeof(DeeperEditorWindow).GetField("s_open", fields)!.GetValue(null)!;
        typeof(DeeperEditorWindow).GetField("_localAudio", fields)!.SetValue(editor, fake);
        editors.Add(editor);
        try
        {
            PanicSurfaces.All.Single(s => s.Id == "deeper-editor-audio").Stop(null);
            Assert.Equal(1, fake.Pauses);
        }
        finally { editors.Remove(editor); editor.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void TransportAppliesASeekOnceAndStopsAClipStillOpening()
    {
        var e = new FakeEngine();
        var t = new DeeperLocalAudio.Transport(e, 12000, () => { });
        Assert.Equal(12, t.DurationSeconds);              // header estimate until playing

        // Seek before playback: taken by the first Playing, never again.
        t.PositionSeconds = 3;
        t.Play();
        e.State = LibVLCSharp.Shared.VLCState.Playing;
        t.OnPlaying();
        Assert.Equal(3000, e.Time);
        Assert.Equal(6, t.DurationSeconds);               // exact length once playing
        e.Time = 4000;
        t.Pause();
        t.Play();
        t.OnPlaying();
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

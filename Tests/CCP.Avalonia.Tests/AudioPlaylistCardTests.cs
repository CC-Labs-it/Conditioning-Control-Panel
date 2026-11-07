using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AudioPlayer;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Deeper tab's audio player (parity lane reports; WPF AudioPlaylistCard, 7.1.8 "a simple audio player"),
/// modelled on WPF Tests/ConditioningControlPanel.Tests/AudioPlaylistCardTests.cs: the card model driven by a fake
/// <see cref="IAudioPlaylistService"/>, the card itself, and this head's service over a fake sound.
/// </summary>
public class AudioPlaylistCardModelTests
{
    internal sealed class FakePlaylist : IAudioPlaylistService
    {
        private readonly List<string> _tracks = new();
        public List<string> Calls { get; } = new();

        public IReadOnlyList<string> Tracks => _tracks;
        public int CurrentIndex { get; set; } = -1;
        public string? CurrentPath => CurrentIndex >= 0 && CurrentIndex < _tracks.Count ? _tracks[CurrentIndex] : null;
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public int Volume { get; set; } = 80;
        public AudioPlaylistLoop Loop { get; set; }
        public bool Shuffle { get; set; }

        public int Add(IEnumerable<string> paths)
        {
            var added = 0;
            foreach (var p in paths)
            {
                if (_tracks.Contains(p, StringComparer.OrdinalIgnoreCase)) continue;
                _tracks.Add(p);
                added++;
            }
            if (added > 0) TracksChanged?.Invoke();
            return added;
        }

        public void RemoveAt(int index) { Calls.Add($"remove {index}"); _tracks.RemoveAt(index); TracksChanged?.Invoke(); }

        public void Move(int fromIndex, int toIndex)
        {
            Calls.Add($"move {fromIndex}>{toIndex}");
            var t = _tracks[fromIndex];
            _tracks.RemoveAt(fromIndex);
            _tracks.Insert(toIndex, t);
            TracksChanged?.Invoke();
        }

        public void Clear() { _tracks.Clear(); CurrentIndex = -1; TracksChanged?.Invoke(); }

        public void PlayAt(int index)
        {
            Calls.Add($"playAt {index}");
            CurrentIndex = index;
            IsPlaying = true;
            IsPaused = false;
            PositionSeconds = 0;
            TrackChanged?.Invoke(CurrentPath);
            StateChanged?.Invoke();
        }

        public void Play() { Calls.Add("play"); if (CurrentIndex < 0) CurrentIndex = 0; IsPlaying = true; IsPaused = false; StateChanged?.Invoke(); }
        public void Pause() { Calls.Add("pause"); IsPlaying = false; IsPaused = true; StateChanged?.Invoke(); }
        public void Stop() { IsPlaying = false; IsPaused = false; PositionSeconds = 0; StateChanged?.Invoke(); }
        public void Next() => Calls.Add("next");
        public void Previous() => Calls.Add("previous");
        public void Seek(double seconds) { Calls.Add($"seek {seconds:0}"); PositionSeconds = seconds; }

        public void Tick(double seconds) { PositionSeconds = seconds; PositionChanged?.Invoke(seconds); }
        public int Listeners => (TracksChanged?.GetInvocationList().Length ?? 0) + (PositionChanged?.GetInvocationList().Length ?? 0);

        public event Action? TracksChanged;
        public event Action<string?>? TrackChanged;
        public event Action? StateChanged;
        public event Action<double>? PositionChanged;
    }

    private static (AudioPlaylistCardModel model, FakePlaylist fake) Loaded(params string[] paths)
    {
        var fake = new FakePlaylist();
        var model = new AudioPlaylistCardModel(fake, () => true, _ => { });
        model.Attach();
        fake.Add(paths);
        return (model, fake);
    }

    [Fact]
    public void No_service_is_an_empty_card_that_does_nothing()
    {
        var model = new AudioPlaylistCardModel(null, () => true, _ => { });
        model.Attach();

        Assert.False(model.IsAvailable);
        Assert.True(model.IsEmpty);
        Assert.Equal(0, model.AddFiles(new[] { "/a/one.mp3" }));
        model.TogglePlay(); model.Next(); model.Previous(); model.Remove(0); model.Seek(5); model.CycleLoop();
        model.Volume = 40; model.Shuffle = true;
        Assert.Equal("0:00", model.ElapsedText);
        Assert.Equal("--:--", model.TotalText);
        Assert.False(model.Shuffle);
    }

    [Fact]
    public void Adding_keeps_only_the_four_audio_types_WPF_takes()
    {
        var (model, fake) = Loaded();

        var added = model.AddFiles(new[]
        {
            "/a/one.mp3", "/a/two.WAV", "/a/three.ogg", "/a/four.flac", "/a/five.m4a",
            "/a/clip.mp4", "/a/notes.txt", "", "/a/noext",
        });

        Assert.Equal(4, added);
        Assert.Equal(4, fake.Tracks.Count);
        Assert.Equal(new[] { "one", "two", "four", "five" }, model.Rows.Select(r => r.Name));
        Assert.True(model.HasTracks);
    }

    [Fact]
    public void Rows_mark_the_loaded_track_reorder_and_remove_reach_the_service()
    {
        var (model, fake) = Loaded("/a/one.mp3", "/a/two.mp3", "/a/three.mp3");

        model.PlayRow(1);
        Assert.Equal(new[] { false, true, false }, model.Rows.Select(r => r.IsCurrent));
        Assert.Equal("two", model.CurrentName);
        Assert.False(model.Rows[0].CanMoveUp);
        Assert.False(model.Rows[2].CanMoveDown);
        fake.Calls.Clear();

        model.MoveUp(0); model.MoveDown(2); model.Remove(7);
        Assert.Empty(fake.Calls);
        model.MoveDown(0); model.MoveUp(2); model.Remove(0);

        Assert.Equal(new[] { "move 0>1", "move 2>1", "remove 0" }, fake.Calls);
        Assert.Equal(new[] { "1", "2" }, model.Rows.Select(r => r.Number));
    }

    [Fact]
    public void A_held_thumb_is_not_dragged_back_by_the_player_and_seeks_once_on_release()
    {
        var (model, fake) = Loaded("/a/one.mp3");
        model.PlayRow(0);
        fake.DurationSeconds = 200;
        fake.Calls.Clear();

        model.Scrub(120);
        fake.Tick(11);
        Assert.Equal(120, model.PositionSeconds);
        Assert.Empty(fake.Calls);

        model.Seek(500);
        Assert.Equal("seek 200", fake.Calls.Single());
        fake.Tick(150);
        Assert.Equal(150, model.PositionSeconds);
        Assert.Equal("2:30", model.ElapsedText);
        Assert.Equal("3:20", model.TotalText);
    }

    [Fact]
    public void Volume_loop_shuffle_and_the_enhancements_switch_write_through()
    {
        bool stored = true;
        var writes = new List<bool>();
        var fake = new FakePlaylist();
        var model = new AudioPlaylistCardModel(fake, () => stored, v => { stored = v; writes.Add(v); });
        Assert.False(model.HasChangedOptions);

        model.Volume = 140;
        Assert.Equal(100, fake.Volume);
        Assert.Equal("100%", model.VolumeText);
        model.CycleLoop();
        Assert.Equal(AudioPlaylistLoop.All, fake.Loop);
        model.CycleLoop(); model.CycleLoop();
        Assert.Equal(AudioPlaylistLoop.Off, fake.Loop);
        model.Shuffle = true;
        Assert.True(fake.Shuffle);

        model.Shuffle = false;
        model.Enhancements = false;
        Assert.Equal(new[] { false }, writes);
        Assert.True(model.HasChangedOptions);   // the fold never hides a changed setting
    }
}

/// <summary>This head's service (Platform/AudioPlaylistService, WPF AudioPlaylistService ported) over a fake sound.</summary>
public class AudioPlaylistServiceTests
{
    internal sealed class FakeAudio : IPlaylistAudio
    {
        public List<string> Calls { get; } = new();
        public HashSet<string> Broken { get; } = new();
        public string? CurrentPath { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public long CurrentTimeMs { get; set; }
        public long DurationMs { get; set; } = 60_000;
        public int Volume { get; set; }
        public bool Muted { get; private set; }

        public bool Play(string path)
        {
            Calls.Add("play " + Path.GetFileName(path));
            if (Broken.Contains(path)) return false;
            CurrentPath = path; IsPlaying = true; IsPaused = false; Muted = false; CurrentTimeMs = 0;
            return true;
        }
        public void Pause() { Calls.Add("pause"); IsPlaying = false; IsPaused = true; }
        public void Resume() { Calls.Add("resume"); IsPlaying = true; IsPaused = false; }
        public void Stop() { Calls.Add("stop"); CurrentPath = null; IsPlaying = false; IsPaused = false; }
        public void Seek(double seconds) { Calls.Add($"seek {seconds:0}"); CurrentTimeMs = (long)(seconds * 1000); }
        public void Mute() { Calls.Add("mute"); Muted = true; }
        public void Dispose() => Calls.Add("dispose");

        public void End() { IsPlaying = false; Ended?.Invoke(); }
        public void Fail() => StoppedUnexpectedly?.Invoke();
        public void Tick(long ms) { CurrentTimeMs = ms; PlaybackTimeMsChanged?.Invoke(ms); }

        public event Action<long>? PlaybackTimeMsChanged;
        public event Action? Ended;
        public event Action? StoppedUnexpectedly;
    }

    private static (AudioPlaylistService svc, FakeAudio audio, AppSettings s) New(params string[] paths)
    {
        var s = new AppSettings { AudioPlaylistPaths = paths.ToList(), AudioPlaylistVolume = 50, MasterVolume = 80 };
        var audio = new FakeAudio();
        var svc = new AudioPlaylistService(audio, () => s, () => { });
        return (svc, audio, s);
    }

    [Fact]
    public Task Loads_the_saved_list_and_scales_the_level_by_the_master_volume() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var (svc, audio, s) = New("/m/one.mp3", "/m/two.mp3");
        Assert.Equal(2, svc.Tracks.Count);
        Assert.Equal(50, svc.Volume);
        Assert.Equal(40, audio.Volume);          // 50 x 80%

        s.MasterVolume = 50;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(25, audio.Volume);

        svc.Volume = 100;
        Assert.Equal(100, s.AudioPlaylistVolume);
        Assert.Equal(50, audio.Volume);
        svc.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_track_that_will_not_open_is_skipped_and_the_end_moves_on() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var (svc, audio, _) = New("/m/one.mp3", "/m/two.mp3", "/m/three.mp3");
        audio.Broken.Add("/m/one.mp3");
        var changed = new List<string?>();
        svc.TrackChanged += changed.Add;

        svc.Play();
        Assert.Equal("/m/two.mp3", svc.CurrentPath);
        Assert.True(svc.IsPlaying);

        audio.End();
        Assert.Equal("/m/three.mp3", svc.CurrentPath);
        audio.End();                               // loop off: rest, rewound
        Assert.False(svc.IsPlaying);
        Assert.Equal(new[] { "/m/two.mp3", "/m/three.mp3" }, changed.Take(2));
        svc.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public Task Panic_mutes_at_once_stops_and_keeps_the_list() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var (svc, audio, s) = New("/m/one.mp3", "/m/two.mp3");
        svc.PlayAt(1);
        var states = 0;
        svc.StateChanged += () => states++;

        svc.StopEmergency();

        Assert.Equal(new[] { "mute", "stop" }, audio.Calls.SkipWhile(c => c != "mute").ToArray());
        Assert.False(svc.IsPlaying);
        Assert.Equal(1, states);
        Assert.Equal(2, svc.Tracks.Count);
        Assert.Equal(new[] { "/m/one.mp3", "/m/two.mp3" }, s.AudioPlaylistPaths);
        svc.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_device_error_is_a_stop_the_card_hears() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var (svc, audio, _) = New("/m/one.mp3");
        svc.Play();
        var states = 0;
        svc.StateChanged += () => states++;

        audio.Fail();

        Assert.False(svc.IsPlaying);
        Assert.Equal(1, states);
        svc.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public Task Previous_restarts_after_three_seconds_and_pause_resumes() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var (svc, audio, _) = New("/m/one.mp3", "/m/two.mp3");
        svc.PlayAt(1);
        audio.Tick(5_000);
        svc.Previous();
        Assert.Equal("seek 0", audio.Calls.Last());
        Assert.Equal(1, svc.CurrentIndex);

        svc.Pause();
        Assert.True(svc.IsPaused);
        svc.Play();
        Assert.Equal("resume", audio.Calls.Last());
        svc.Dispose();
        Assert.Equal("dispose", audio.Calls.Last());
        return Task.CompletedTask;
    });
}

/// <summary>The card builds against the app theme, empty and with tracks, and sits on the Deeper tab where WPF puts it.</summary>
public class AudioPlaylistCardViewTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Window Host(Control content)
    {
        var w = new Window { Width = 640, Height = 600, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    [Fact]
    public Task Empty_card_shows_the_empty_line_and_no_transport() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var card = new AudioPlaylistCard();
        var host = Host(card);
        try
        {
            Assert.True(card.TxtEmpty.IsVisible);
            Assert.False(card.PlayerBody.IsVisible);
            Assert.False(card.BtnAdd.IsEnabled);   // no service on this path (WPF: same)
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Card_with_tracks_shows_the_player_and_follows_the_service() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var fake = new AudioPlaylistCardModelTests.FakePlaylist();
        fake.Add(new[] { "/a/one.mp3", "/a/two.flac" });
        var card = new AudioPlaylistCard();
        card.Use(fake);
        var host = Host(card);
        try
        {
            Assert.False(card.TxtEmpty.IsVisible);
            Assert.True(card.PlayerBody.IsVisible);
            Assert.True(card.BtnAdd.IsEnabled);
            Assert.Equal(2, card.TrackList.ItemCount);
            // The Deeper switch lives in the fold, beside loop and shuffle, and starts on.
            Assert.True(card.ChkEnhancements.IsChecked);
            Assert.NotNull(card.ChkEnhancements.FindLogicalAncestorOfType<MoreFold>());
            Assert.Equal(IconKind.Play, card.IcoPlayPause.Kind);
            Assert.Equal("Loop: off", card.TxtLoop.Text);

            card.Model.PlayRow(1);
            fake.DurationSeconds = 90;
            fake.Tick(30);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(IconKind.Pause, card.IcoPlayPause.Kind);
            Assert.Equal(90, card.SliderSeek.Maximum);
            Assert.Equal(30, card.SliderSeek.Value);
            Assert.Equal(80, card.SliderVolume.Value);

            card.OptionsFold.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            if (Environment.GetEnvironmentVariable("CCP_TEST_EVIDENCE_DIR") is { Length: > 0 } evidence)
            {
                Directory.CreateDirectory(evidence);
                using var frame = host.CaptureRenderedFrame();
                frame?.Save(Path.Combine(evidence, "AudioPlaylistCard-tracks.png"));
            }
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_seek_bar_press_holds_the_bar_and_seeks_once_on_release() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var fake = new AudioPlaylistCardModelTests.FakePlaylist();
        fake.Add(new[] { "/a/one.mp3" });
        var card = new AudioPlaylistCard();
        card.Use(fake);
        var host = Host(card);
        try
        {
            card.Model.PlayRow(0);
            fake.DurationSeconds = 100;
            fake.Tick(10);
            Dispatcher.UIThread.RunJobs();
            fake.Calls.Clear();

            var bounds = card.SliderSeek.Bounds;
            var at = card.SliderSeek.TranslatePoint(new Point(bounds.Width * 0.75, bounds.Height / 2), host)!.Value;
            host.MouseDown(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.Model.IsScrubbing);
            fake.Tick(11);                             // the player does not drag the held bar back
            Assert.NotEqual(11, card.Model.PositionSeconds);
            Assert.Empty(fake.Calls);
            host.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            var seek = Assert.Single(fake.Calls);
            Assert.StartsWith("seek ", seek);
            Assert.InRange(double.Parse(seek[5..]), 60, 90);
            Assert.False(card.Model.IsScrubbing);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Deeper_tab_hosts_the_card_between_the_welcome_card_and_the_library() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var tab = new DeeperTabView();
        var column = tab.FindControl<StackPanel>("DeeperHubColumn")!;
        var kids = column.Children.ToList();
        var card = Assert.IsType<AudioPlaylistCard>(tab.DeeperAudioPlaylistCard);
        Assert.Equal(kids.IndexOf(tab.DeeperWelcomeCard) + 1, kids.IndexOf(card));
        return Task.CompletedTask;
    });
}

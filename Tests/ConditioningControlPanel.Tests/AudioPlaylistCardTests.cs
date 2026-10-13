using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services.AudioPlayer;
using ConditioningControlPanel.Views.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The playlist card (7.1.8, suggestion "a simple audio player") driven by a fake
/// <see cref="IAudioPlaylistService"/>: the real service is another lane's and is null here.
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
        var model = new AudioPlaylistCardModel(fake);
        model.Attach();
        fake.Add(paths);
        return (model, fake);
    }

    [Fact]
    public void No_service_is_an_empty_card_that_does_nothing()
    {
        var model = new AudioPlaylistCardModel(null);
        model.Attach();

        Assert.False(model.IsAvailable);
        Assert.True(model.IsEmpty);
        Assert.Equal(0, model.AddFiles(new[] { @"C:\a\one.mp3" }));
        model.TogglePlay(); model.Next(); model.Previous(); model.Remove(0); model.Seek(5); model.CycleLoop();
        model.Volume = 40; model.Shuffle = true;
        Assert.Equal("0:00", model.ElapsedText);
        Assert.Equal("--:--", model.TotalText);
        Assert.False(model.Shuffle);
    }

    [Fact]
    public void Adding_keeps_only_the_four_audio_types()
    {
        var (model, fake) = Loaded();

        // .ogg is refused: the audio stack (MediaFoundation) cannot decode it.
        var added = model.AddFiles(new[]
        {
            @"C:\a\one.mp3", @"C:\a\two.WAV", @"C:\a\three.ogg", @"C:\a\four.flac", @"C:\a\five.m4a",
            @"C:\a\clip.mp4", @"C:\a\notes.txt", "", @"C:\a\noext",
        });

        Assert.Equal(4, added);
        Assert.Equal(4, fake.Tracks.Count);
        Assert.Equal(new[] { "one", "two", "four", "five" }, model.Rows.Select(r => r.Name));
        Assert.True(model.HasTracks);
        Assert.Contains("*.mp3;*.wav;*.flac;*.m4a", AudioPlaylistCardModel.DialogFilter("Audio files"));
    }

    [Fact]
    public void Same_filename_in_two_folders_is_two_rows()
    {
        var (model, _) = Loaded(@"C:\a\loop.mp3", @"C:\b\loop.mp3");

        Assert.Equal(2, model.Rows.Count);
        Assert.NotEqual(model.Rows[0].Path, model.Rows[1].Path);
    }

    [Fact]
    public void Rows_mark_the_loaded_track_and_know_their_edges()
    {
        var (model, fake) = Loaded(@"C:\a\one.mp3", @"C:\a\two.mp3", @"C:\a\three.mp3");

        model.PlayRow(1);

        Assert.Equal(new[] { false, true, false }, model.Rows.Select(r => r.IsCurrent));
        Assert.Equal("two", model.CurrentName);
        Assert.True(model.HasCurrent);
        Assert.False(model.Rows[0].CanMoveUp);
        Assert.False(model.Rows[2].CanMoveDown);
        Assert.Equal("playAt 1", fake.Calls.Single());
    }

    [Fact]
    public void Reorder_and_remove_reach_the_service_and_stop_at_the_edges()
    {
        var (model, fake) = Loaded(@"C:\a\one.mp3", @"C:\a\two.mp3", @"C:\a\three.mp3");

        model.MoveUp(0);
        model.MoveDown(2);
        model.Remove(7);
        Assert.Empty(fake.Calls);

        model.MoveDown(0);
        model.MoveUp(2);
        model.Remove(0);

        Assert.Equal(new[] { "move 0>1", "move 2>1", "remove 0" }, fake.Calls);
        Assert.Equal(new[] { "three", "one" }, model.Rows.Select(r => r.Name));
        Assert.Equal(new[] { "1", "2" }, model.Rows.Select(r => r.Number));
    }

    [Fact]
    public void Play_button_toggles_and_does_nothing_on_an_empty_list()
    {
        var (model, fake) = Loaded();
        model.TogglePlay();
        Assert.Empty(fake.Calls);

        fake.Add(new[] { @"C:\a\one.mp3" });
        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        model.TogglePlay();
        Assert.True(model.IsPlaying);
        model.TogglePlay();
        Assert.False(model.IsPlaying);
        model.Next();
        model.Previous();

        Assert.Equal(new[] { "play", "pause", "next", "previous" }, fake.Calls);
        Assert.Contains(nameof(AudioPlaylistCardModel.IsPlaying), raised);
    }

    [Fact]
    public void Position_ticks_move_the_bar_and_the_times()
    {
        var (model, fake) = Loaded(@"C:\a\one.mp3");
        model.PlayRow(0);
        fake.DurationSeconds = 3725;

        fake.Tick(65.9);

        Assert.Equal("1:05", model.ElapsedText);
        Assert.Equal("1:02:05", model.TotalText);
        Assert.Equal(3725, model.SeekMaximum);
        Assert.True(model.CanSeek);
    }

    [Fact]
    public void A_held_thumb_is_not_dragged_back_by_the_player_and_seeks_once_on_release()
    {
        var (model, fake) = Loaded(@"C:\a\one.mp3");
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
    }

    [Fact]
    public void Unknown_length_cannot_be_seeked()
    {
        var (model, fake) = Loaded(@"C:\a\one.mp3");
        model.PlayRow(0);
        fake.Calls.Clear();

        model.Seek(30);

        Assert.False(model.CanSeek);
        Assert.Equal(1, model.SeekMaximum);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void Volume_loop_and_shuffle_write_through()
    {
        var (model, fake) = Loaded(@"C:\a\one.mp3");
        Assert.False(model.HasChangedOptions);

        model.Volume = 140;
        Assert.Equal(100, fake.Volume);
        Assert.Equal("100%", model.VolumeText);
        model.Volume = -3;
        Assert.Equal(0, fake.Volume);

        model.CycleLoop();
        Assert.Equal(AudioPlaylistLoop.All, fake.Loop);
        model.CycleLoop();
        Assert.Equal(AudioPlaylistLoop.One, fake.Loop);
        Assert.True(model.HasChangedOptions);
        model.CycleLoop();
        Assert.Equal(AudioPlaylistLoop.Off, fake.Loop);

        model.Shuffle = true;
        Assert.True(fake.Shuffle);
        Assert.True(model.HasChangedOptions);
    }

    [Fact]
    public void Enhancements_switch_is_on_by_default_writes_once_and_opens_the_fold_when_off()
    {
        bool stored = true;
        var writes = new List<bool>();
        var model = new AudioPlaylistCardModel(new FakePlaylist(), () => stored, v => { stored = v; writes.Add(v); });
        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.True(model.Enhancements);
        Assert.False(model.HasChangedOptions);

        model.Enhancements = true;                  // no change, no write
        Assert.Empty(writes);

        model.Enhancements = false;
        Assert.Equal(new[] { false }, writes);
        Assert.False(model.Enhancements);
        Assert.True(model.HasChangedOptions);       // the fold never hides a changed setting
        Assert.Contains(nameof(AudioPlaylistCardModel.Enhancements), raised);

        // No service: the switch is inert. No setting: it reads as on (the default).
        var none = new AudioPlaylistCardModel(null, () => stored, v => writes.Add(v));
        none.Enhancements = true;
        Assert.Single(writes);
        Assert.False(none.HasChangedOptions);
        Assert.True(new AudioPlaylistCardModel(new FakePlaylist()).Enhancements);
    }

    [Fact]
    public void Clear_empties_the_card_and_detach_stops_listening()
    {
        var (model, fake) = Loaded(@"C:\a\one.mp3", @"C:\a\two.mp3");
        model.Attach();
        Assert.Equal(2, fake.Listeners);

        model.Clear();
        Assert.True(model.IsEmpty);
        Assert.False(model.HasCurrent);

        model.Detach();
        Assert.Equal(0, fake.Listeners);
        fake.Add(new[] { @"C:\a\three.mp3" });
        Assert.True(model.IsEmpty);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59.99, "0:59")]
    [InlineData(600, "10:00")]
    [InlineData(-4, "0:00")]
    [InlineData(double.NaN, "0:00")]
    public void Times_read_as_minutes_and_seconds(double seconds, string expected) =>
        Assert.Equal(expected, AudioPlaylistCardModel.FormatTime(seconds));
}

/// <summary>The card itself builds against the app theme, empty and with tracks.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class AudioPlaylistCardRenderTests
{
    private static T Named<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new Xunit.Sdk.XunitException(name + " is missing from the card");

    private static void Realize(FrameworkElement card)
    {
        var host = new Grid();
        host.Children.Add(card);
        host.Measure(new Size(700, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, 700, Math.Max(1, host.DesiredSize.Height)));
        host.UpdateLayout();
    }

    [Fact]
    public void Empty_card_shows_the_empty_line_and_no_transport() => WpfRenderHarness.OnStaThread(() =>
    {
        var card = new AudioPlaylistCard();
        Realize(card);

        Assert.Equal(Visibility.Visible, Named<TextBlock>(card, "TxtEmpty").Visibility);
        Assert.Equal(Visibility.Collapsed, Named<StackPanel>(card, "PlayerBody").Visibility);
        Assert.False(Named<Button>(card, "BtnAdd").IsEnabled);
    });

    [Fact]
    public void Card_with_tracks_shows_the_player_and_follows_the_service() => WpfRenderHarness.OnStaThread(() =>
    {
        var fake = new AudioPlaylistCardModelTests.FakePlaylist();
        fake.Add(new[] { @"C:\a\one.mp3", @"C:\a\two.flac" });
        var card = new AudioPlaylistCard();
        card.Use(fake);
        card.Model.Attach();
        Realize(card);

        Assert.Equal(Visibility.Collapsed, Named<TextBlock>(card, "TxtEmpty").Visibility);
        Assert.Equal(Visibility.Visible, Named<StackPanel>(card, "PlayerBody").Visibility);
        Assert.True(Named<Button>(card, "BtnAdd").IsEnabled);
        Assert.Equal(2, Named<ItemsControl>(card, "TrackList").Items.Count);
        // The Deeper switch lives in the fold, beside loop and shuffle, and starts on.
        var enhancements = Named<CheckBox>(card, "ChkEnhancements");
        Assert.True(enhancements.IsChecked);
        DependencyObject? parent = enhancements;
        while (parent != null && parent is not ConditioningControlPanel.Features.MoreFold)
            parent = LogicalTreeHelper.GetParent(parent);
        Assert.NotNull(parent);
        Assert.Equal("▶", Named<Button>(card, "BtnPlayPause").Content);

        card.Model.PlayRow(1);
        fake.DurationSeconds = 90;
        fake.Tick(30);
        card.UpdateLayout();

        Assert.Equal("⏸", Named<Button>(card, "BtnPlayPause").Content);
        Assert.Equal(90, Named<Slider>(card, "SliderSeek").Maximum);
        Assert.Equal(30, Named<Slider>(card, "SliderSeek").Value);
        Assert.Equal(80, Named<Slider>(card, "SliderVolume").Value);
    });
}

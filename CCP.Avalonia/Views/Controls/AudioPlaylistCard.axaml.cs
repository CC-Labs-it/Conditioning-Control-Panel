using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Primitives;
using ConditioningControlPanel.Avalonia.AudioPlayer;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.AudioPlayer;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

// ponytail: the service lane exposes WPF's playlist controls here; the full row-hover/MoreFold
// visual treatment and playlist-to-enhancement bridge remain for the Deeper/card lanes.
public partial class AudioPlaylistCard : UserControl
{
    private IAudioPlaylistService? _service;
    private bool _refreshing;
    public AudioPlaylistCard() { InitializeComponent(); Refresh(); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); Bind(App.AudioPlaylist); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { Bind(null); base.OnDetachedFromVisualTree(e); }
    internal void Bind(IAudioPlaylistService? service)
    {
        if (_service != null)
        { _service.TracksChanged -= Refresh; _service.StateChanged -= Refresh; _service.TrackChanged -= TrackChanged; _service.PositionChanged -= PositionChanged; }
        _service = service;
        if (service != null)
        { service.TracksChanged += Refresh; service.StateChanged += Refresh; service.TrackChanged += TrackChanged; service.PositionChanged += PositionChanged; }
        Refresh();
    }
    private void TrackChanged(string? _) => Refresh();
    private void PositionChanged(double position)
    {
        _refreshing = true;
        Seek.Maximum = Math.Max(1, _service?.DurationSeconds ?? 0); Seek.Value = position;
        _refreshing = false;
    }
    private void Refresh()
    {
        if (Body == null) return;
        _refreshing = true;
        int selected = Tracks.SelectedIndex;
        Tracks.ItemsSource = _service?.Tracks.Select(Path.GetFileName).ToArray(); Tracks.SelectedIndex = selected;
        Body.IsVisible = _service?.Tracks.Count > 0; Empty.IsVisible = !Body.IsVisible;
        AddButton.IsEnabled = _service != null;
        Current.Text = _service?.CurrentPath is { } p ? Path.GetFileName(p) : Loc.Get("audio_player_nothing_loaded");
        PlayGlyph.Kind = _service?.IsPlaying == true ? IconKind.Pause : IconKind.Play;
        ToolTip.SetTip(PlayButton, Loc.Get(_service?.IsPlaying == true ? "audio_player_tip_pause" : "audio_player_tip_play"));
        Level.Value = _service?.Volume ?? 80; ShuffleToggle.IsChecked = _service?.Shuffle ?? false;
        LoopButton.Content = new TextBlock { Text = Loc.Get("audio_player_loop_" + (_service?.Loop switch { AudioPlaylistLoop.All => "all", AudioPlaylistLoop.One => "one", _ => "off" })) };
        _refreshing = false;
    }
    private async void AddClick(object? sender, RoutedEventArgs e)
    {
        if (_service == null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("audio_player_picker_title"), AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.Get("audio_player_picker_filter")) { Patterns = new[] { "*.mp3", "*.wav", "*.ogg", "*.flac", "*.m4a" } } },
        });
        _service?.Add(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
    private void PlayClick(object? s, RoutedEventArgs e) { if (_service?.IsPlaying == true) _service.Pause(); else _service?.Play(); }
    private void PreviousClick(object? s, RoutedEventArgs e) => _service?.Previous();
    private void NextClick(object? s, RoutedEventArgs e) => _service?.Next();
    private void TrackDoubleTapped(object? s, TappedEventArgs e) { if (Tracks.SelectedIndex >= 0) _service?.PlayAt(Tracks.SelectedIndex); }
    private void SeekChanged(object? s, RangeBaseValueChangedEventArgs e) { if (!_refreshing) _service?.Seek(e.NewValue); }
    private void VolumeChanged(object? s, RangeBaseValueChangedEventArgs e) { if (!_refreshing && _service != null) _service.Volume = (int)e.NewValue; }
    private void ShuffleChanged(object? s, RoutedEventArgs e) { if (!_refreshing && _service != null) _service.Shuffle = ShuffleToggle.IsChecked == true; }
    private void LoopClick(object? s, RoutedEventArgs e) { if (_service == null) return; _service.Loop = (AudioPlaylistLoop)(((int)_service.Loop + 1) % 3); Refresh(); }
    private void ClearClick(object? s, RoutedEventArgs e) => _service?.Clear();
    private void RemoveClick(object? s, RoutedEventArgs e) => _service?.RemoveAt(Tracks.SelectedIndex);
    private void UpClick(object? s, RoutedEventArgs e) => _service?.Move(Tracks.SelectedIndex, Tracks.SelectedIndex - 1);
    private void DownClick(object? s, RoutedEventArgs e) => _service?.Move(Tracks.SelectedIndex, Tracks.SelectedIndex + 1);
}

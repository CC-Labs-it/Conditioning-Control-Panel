using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Localization;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.AudioPlayer;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The playlist player's card (Deeper tab). All of its rules live in <see cref="AudioPlaylistCardModel"/>;
    /// this file only forwards clicks and keeps the two labels that change with state (play/pause, loop)
    /// bound to their loc keys.
    /// <para>PORTED from ConditioningControlPanel/Views/Controls/AudioPlaylistCard.xaml.cs. Deviations:
    ///  - the seek bar's thumb-drag / groove-press split becomes one press/release pair (tunnel, handled
    ///    too, as MiniPlayerWindow's seek bar): a press holds the bar (Scrub), every value change while
    ///    held follows the hand, and the one seek happens on release. Avalonia's track press already
    ///    jumps to the point, so a groove click is a press, a jump and a release: one seek, as in WPF.
    ///  - Microsoft.Win32.OpenFileDialog becomes the window's StorageProvider picker.</para>
    /// </summary>
    public partial class AudioPlaylistCard : UserControl
    {
        private AudioPlaylistCardModel _model = new(null);
        private bool _dragging;

        public AudioPlaylistCard()
        {
            InitializeComponent();
            Bind(_model);
            SliderSeek.AddHandler(PointerPressedEvent, SliderSeek_Pressed,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            SliderSeek.AddHandler(PointerReleasedEvent, SliderSeek_Released,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            SliderSeek.AddHandler(PointerCaptureLostEvent, SliderSeek_CaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
            SliderSeek.AddHandler(RangeBase.ValueChangedEvent, SliderSeek_ValueChanged);
            Loaded += (_, _) =>
            {
                // The service is created at startup, after this card may have been built.
                if (!_model.IsAvailable && App.AudioPlaylist != null) Use(App.AudioPlaylist);
                _model.Attach();
                OptionsFold.IsOpen |= _model.HasChangedOptions;
            };
            Unloaded += (_, _) => _model.Detach();
        }

        internal AudioPlaylistCardModel Model => _model;

        /// <summary>Points the card at a service (tests hand it a fake).</summary>
        internal void Use(IAudioPlaylistService? service)
        {
            _model.Detach();
            _model.PropertyChanged -= Model_PropertyChanged;
            _model = new AudioPlaylistCardModel(service);
            Bind(_model);
            if (IsLoaded) _model.Attach();
        }

        private void Bind(AudioPlaylistCardModel model)
        {
            DataContext = model;
            model.PropertyChanged += Model_PropertyChanged;
            ApplyStateLabels();
        }

        private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AudioPlaylistCardModel.IsPlaying) or nameof(AudioPlaylistCardModel.Loop))
                ApplyStateLabels();
        }

        // Bindings, not strings: a language switch relabels them live.
        private void ApplyStateLabels()
        {
            IcoPlayPause.Kind = _model.IsPlaying ? IconKind.Pause : IconKind.Play;
            BindLoc(BtnPlayPause, ToolTip.TipProperty,
                _model.IsPlaying ? "audio_player_tip_pause" : "audio_player_tip_play");
            BindLoc(TxtLoop, TextBlock.TextProperty, _model.Loop switch
            {
                AudioPlaylistLoop.All => "audio_player_loop_all",
                AudioPlaylistLoop.One => "audio_player_loop_one",
                _ => "audio_player_loop_off",
            });
        }

        private static void BindLoc(AvaloniaObject target, AvaloniaProperty property, string key) =>
            target.Bind(property, (Binding)new StrExtension(key).ProvideValue(null!));

        private static int RowIndex(object? sender) =>
            (sender as StyledElement)?.DataContext is AudioPlaylistRow row ? row.Index : -1;

        private async void BtnAdd_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } provider) return;
                var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("audio_player_picker_title"),
                    AllowMultiple = true,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType(Loc.Get("audio_player_picker_filter"))
                        {
                            Patterns = AudioPlaylistCardModel.Extensions.Select(x => "*" + x).ToArray(),
                        },
                    },
                });
                if (files.Count == 0) return;
                _model.AddFiles(files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!));
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Audio player: add files failed"); }
        }

        private void BtnPlayPause_Click(object? sender, RoutedEventArgs e) => _model.TogglePlay();
        private void BtnPrevious_Click(object? sender, RoutedEventArgs e) => _model.Previous();
        private void BtnNext_Click(object? sender, RoutedEventArgs e) => _model.Next();
        private void BtnLoop_Click(object? sender, RoutedEventArgs e) => _model.CycleLoop();
        private void BtnClear_Click(object? sender, RoutedEventArgs e) => _model.Clear();
        private void RowPlay_Click(object? sender, RoutedEventArgs e) => _model.PlayRow(RowIndex(sender));
        private void RowUp_Click(object? sender, RoutedEventArgs e) => _model.MoveUp(RowIndex(sender));
        private void RowDown_Click(object? sender, RoutedEventArgs e) => _model.MoveDown(RowIndex(sender));
        private void RowRemove_Click(object? sender, RoutedEventArgs e) => _model.Remove(RowIndex(sender));

        // A press holds the bar still under the hand (WPF DragStarted -> Scrub); the one seek happens on
        // release (WPF DragCompleted). A track change mid-hold ends the scrub in the model: no seek then.
        private void SliderSeek_Pressed(object? sender, PointerPressedEventArgs e)
        {
            if (!SliderSeek.IsEnabled || _dragging) return;
            _dragging = true;
            _model.Scrub(SliderSeek.Value);
        }

        internal void SliderSeek_Released(object? sender, PointerReleasedEventArgs? e)
        {
            if (!_dragging) return;
            bool live = _model.IsScrubbing;   // the track changed mid-drag: no seek
            _dragging = false;
            if (live) _model.Seek(SliderSeek.Value);
        }

        private void SliderSeek_CaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            // A release that never reached the slider (window lost focus): the bar lets go, seeking where it was left.
            if (_dragging) SliderSeek_Released(sender, null);
        }

        private void SliderSeek_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_dragging) _model.Scrub(e.NewValue);
        }
    }
}

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.AudioPlayer;

namespace ConditioningControlPanel.Views.Controls
{
    /// <summary>
    /// The playlist player's card. All of its rules live in <see cref="AudioPlaylistCardModel"/>;
    /// this file only forwards clicks and keeps the two labels that change with state
    /// (play/pause, loop) bound to their loc keys.
    /// </summary>
    public partial class AudioPlaylistCard : UserControl
    {
        private AudioPlaylistCardModel _model = new(null);
        private bool _dragging;

        public AudioPlaylistCard()
        {
            InitializeComponent();
            Bind(_model);
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
            BtnPlayPause.Content = _model.IsPlaying ? "⏸" : "▶";
            BindLoc(BtnPlayPause, ToolTipProperty,
                _model.IsPlaying ? "audio_player_tip_pause" : "audio_player_tip_play");
            BindLoc(BtnLoop, ContentControl.ContentProperty, _model.Loop switch
            {
                AudioPlaylistLoop.All => "audio_player_loop_all",
                AudioPlaylistLoop.One => "audio_player_loop_one",
                _ => "audio_player_loop_off",
            });
        }

        private static void BindLoc(DependencyObject target, DependencyProperty property, string key) =>
            BindingOperations.SetBinding(target, property,
                new Binding($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });

        private static int RowIndex(object sender) =>
            (sender as FrameworkElement)?.DataContext is AudioPlaylistRow row ? row.Index : -1;

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.Get("audio_player_picker_title"),
                Filter = AudioPlaylistCardModel.DialogFilter(Loc.Get("audio_player_picker_filter")),
                Multiselect = true,
                CheckFileExists = true,
            };
            var owner = Window.GetWindow(this);
            if ((owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) != true) return;
            _model.AddFiles(dlg.FileNames);
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => _model.TogglePlay();
        private void BtnPrevious_Click(object sender, RoutedEventArgs e) => _model.Previous();
        private void BtnNext_Click(object sender, RoutedEventArgs e) => _model.Next();
        private void BtnLoop_Click(object sender, RoutedEventArgs e) => _model.CycleLoop();
        private void BtnClear_Click(object sender, RoutedEventArgs e) => _model.Clear();
        private void RowPlay_Click(object sender, RoutedEventArgs e) => _model.PlayRow(RowIndex(sender));
        private void RowUp_Click(object sender, RoutedEventArgs e) => _model.MoveUp(RowIndex(sender));
        private void RowDown_Click(object sender, RoutedEventArgs e) => _model.MoveDown(RowIndex(sender));
        private void RowRemove_Click(object sender, RoutedEventArgs e) => _model.Remove(RowIndex(sender));

        // A press on the bar (thumb or groove) is a drag from WPF's side. The model holds the bar
        // still under the hand from the press on, and the one seek happens on release. Value
        // changes outside a drag are the model (or a Maximum coercion) and are never a seek.
        private void SliderSeek_DragStarted(object sender, DragStartedEventArgs e)
        {
            _dragging = true;
            _model.Scrub(SliderSeek.Value);
        }

        private void SliderSeek_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _dragging = false;
            _model.Seek(SliderSeek.Value);
        }

        private void SliderSeek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_dragging) _model.Scrub(e.NewValue);
        }
    }
}

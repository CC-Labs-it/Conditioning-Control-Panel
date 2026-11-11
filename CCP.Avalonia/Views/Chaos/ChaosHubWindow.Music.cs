using System;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    public partial class ChaosHubWindow
    {
        // WPF ChaosHubWindow.xaml.cs:2789-2913. The loop voice cannot pause yet, so leaving
        // the menu releases it after the fade and returning starts the track from the beginning.
        private MindWipePlayer.IVoice? _menuMusic;
        private DispatcherTimer? _menuMusicTimer;
        private AppSettings? _menuMusicSettings;
        private double _menuMusicVolume, _menuMusicFrom, _menuMusicTo, _menuMusicLevel;
        private int _menuMusicStep, _menuMusicSteps;
        private bool _menuMusicLeaving, _menuMusicInitialized, _menuMusicClosed;
        internal Func<string, double, bool, Action, MindWipePlayer.IVoice?>? MenuMusicPlay;

        private void InitializeMenuMusic()
        {
            if (_menuMusicInitialized || RenderProof.Rendering) return;
            _menuMusicInitialized = true;
            _menuView.PropertyChanged += MenuMusicVisibilityChanged;
            Closed += (_, _) => DisposeMenuMusic();
            StartMenuMusic();
        }

        private void MenuMusicVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != IsVisibleProperty) return;
            if (_menuView.IsVisible) StartMenuMusic(); else StopMenuMusic();
        }

        internal void StartMenuMusic()
        {
            if (_menuMusicClosed) return;
            try
            {
                if (_menuMusic == null)
                {
                    var path = CoreModArt.AudioOverridePath("chaos/menu_theme.mp3") ??
                        ContentLocator.Resolve(Path.Combine("Resources", "sounds", "chaos", "menu_theme.mp3"));
                    if (MenuMusicPlay == null && !File.Exists(path)) return;
                    var play = MenuMusicPlay;
                    if (play == null && LibVlcAudio.Instance is { } audio) play = audio.PlayVoice;
                    _menuMusic = play?.Invoke(path, 0, true, () => { });
                    if (_menuMusic == null) return;
                    _menuMusicVolume = 0;
                    _menuMusicSettings = CoreSettings.Current;
                    _menuMusicSettings.PropertyChanged += MenuMusicSettingsChanged;
                }
                FadeMenuMusic(CoreSettings.Current.ChaosMenuMusicMuted ? 0 : 0.5, 40, false);
            }
            catch (Exception ex) { Serilog.Log.Debug("ChaosHub.StartMenuMusic: {E}", ex.Message); }
        }

        internal void StopMenuMusic() => FadeMenuMusic(0, 40, true);

        internal void FadeMenuMusic(double level, int steps, bool leaving = false)
        {
            if (_menuMusic == null) return;
            _menuMusicLevel = level;
            _menuMusicFrom = _menuMusicVolume;
            _menuMusicTo = Math.Clamp(level * CoreSettings.Current.MasterVolume / 100.0, 0, 1);
            _menuMusicStep = 0;
            _menuMusicSteps = steps;
            _menuMusicLeaving = leaving;
            if (_menuMusicTimer == null)
            {
                _menuMusicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _menuMusicTimer.Tick += (_, _) => MenuMusicTick();
            }
            _menuMusicTimer.Start();
        }

        internal void MenuMusicTick()
        {
            if (_menuMusic == null) return;
            var t = Math.Min(1.0, (double)++_menuMusicStep / _menuMusicSteps);
            _menuMusicVolume = Math.Clamp(_menuMusicFrom + (_menuMusicTo - _menuMusicFrom) * t, 0, 1);
            _menuMusic.Volume = _menuMusicVolume;
            if (t < 1) return;
            _menuMusicTimer?.Stop();
            if (_menuMusicLeaving) ReleaseMenuMusic();
        }

        private void MenuMusicSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AppSettings.MasterVolume) || _menuMusic == null) return;
            _menuMusicTo = Math.Clamp(_menuMusicLevel * CoreSettings.Current.MasterVolume / 100.0, 0, 1);
            if (_menuMusicTimer?.IsEnabled != true) _menuMusic.Volume = _menuMusicVolume = _menuMusicTo;
        }

        private void ReleaseMenuMusic()
        {
            if (_menuMusicSettings != null) _menuMusicSettings.PropertyChanged -= MenuMusicSettingsChanged;
            _menuMusicSettings = null;
            _menuMusic?.Dispose();
            _menuMusic = null;
        }

        internal void DisposeMenuMusic()
        {
            _menuMusicClosed = true;
            _menuMusicTimer?.Stop();
            _menuView.PropertyChanged -= MenuMusicVisibilityChanged;
            ReleaseMenuMusic();
        }
    }
}

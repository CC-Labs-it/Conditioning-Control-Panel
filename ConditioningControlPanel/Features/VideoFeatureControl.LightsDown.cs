using System;
using System.Windows;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Lights Down's options on the video page: room darkness, dimming speed, lights back speed. The rows sit in the gold box and show only while
    /// the V2 is the pick; every clamp and default lives in CCP.Core/Super/LightsDownOptions. The effect
    /// reads the settings each frame, so a slider moves it at once. Kept in its own partial so
    /// the page's own code-behind stays untouched.
    /// </summary>
    public partial class VideoFeatureControl
    {
        private bool _lightsDownLoading, _lightsDownReady;

        private void LightsDownRows_Loaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed -= OnLightsDownSuperChanged;
            SuperAccess.Changed += OnLightsDownSuperChanged;
            LoadLightsDown();
        }

        private void LightsDownRows_Unloaded(object sender, RoutedEventArgs e)
            => SuperAccess.Changed -= OnLightsDownSuperChanged;

        private void OnLightsDownSuperChanged(SuperEffect effect)
        {
            if (effect == SuperEffect.LightsDown) Dispatcher.BeginInvoke(new Action(LoadLightsDown));
        }

        private void LoadLightsDown()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _lightsDownLoading = true;
            try
            {
                ConditioningControlPanel.Controls.SuperRows.Apply(LightsDownRows, SuperEffect.LightsDown);
                SliderLightsDarkness.Value = s.LightsDownDarkness;
                TxtLightsDarkness.Text = $"{s.LightsDownDarkness}%";
                SliderLightsDimSpeed.Value = s.LightsDownDimSpeed;
                TxtLightsDimSpeed.Text = $"{s.LightsDownDimSpeed}%";
                SliderLightsReturnSpeed.Value = s.LightsDownReturnSpeed;
                TxtLightsReturnSpeed.Text = $"{s.LightsDownReturnSpeed}%";
                _lightsDownReady = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("LightsDown options: {E}", ex.Message);
            }
            finally { _lightsDownLoading = false; }
        }

        private void SliderLightsDarkness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_lightsDownLoading || !_lightsDownReady || TxtLightsDarkness == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.LightsDownDarkness = LightsDownOptions.ClampDarkness(LightsDownOptions.Snap(e.NewValue));
            TxtLightsDarkness.Text = $"{s.LightsDownDarkness}%";
            App.Settings?.Save();
        }

        private void SliderLightsDimSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_lightsDownLoading || !_lightsDownReady || TxtLightsDimSpeed == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.LightsDownDimSpeed = LightsDownOptions.ClampDimSpeed(LightsDownOptions.Snap(e.NewValue));
            TxtLightsDimSpeed.Text = $"{s.LightsDownDimSpeed}%";
            App.Settings?.Save();
        }

        private void SliderLightsReturnSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_lightsDownLoading || !_lightsDownReady || TxtLightsReturnSpeed == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.LightsDownReturnSpeed = LightsDownOptions.ClampReturnSpeed(LightsDownOptions.Snap(e.NewValue));
            TxtLightsReturnSpeed.Text = $"{s.LightsDownReturnSpeed}%";
            App.Settings?.Save();
        }
    }
}

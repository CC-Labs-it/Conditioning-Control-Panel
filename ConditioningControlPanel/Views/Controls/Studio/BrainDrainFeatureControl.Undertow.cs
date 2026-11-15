using System;
using System.Windows;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Views.Controls.Studio
{
    /// <summary>
    /// Undertow's options on the Brain Drain page: lens size, shrink speed, click widen. The rows sit in the gold box and show only while
    /// the V2 is the pick; every clamp and default lives in CCP.Core/Super/UndertowOptions. The effect
    /// reads the settings each frame, so a slider moves it at once. Kept in its own partial so
    /// the page's own code-behind stays untouched.
    /// </summary>
    public partial class BrainDrainFeatureControl
    {
        private bool _undertowLoading, _undertowReady;

        private void UndertowRows_Loaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed -= OnUndertowSuperChanged;
            SuperAccess.Changed += OnUndertowSuperChanged;
            LoadUndertow();
        }

        private void UndertowRows_Unloaded(object sender, RoutedEventArgs e)
            => SuperAccess.Changed -= OnUndertowSuperChanged;

        private void OnUndertowSuperChanged(SuperEffect effect)
        {
            if (effect == SuperEffect.Undertow) Dispatcher.BeginInvoke(new Action(LoadUndertow));
        }

        private void LoadUndertow()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _undertowLoading = true;
            try
            {
                bool lit = false;
                try { lit = SuperAccess.IsSelected(SuperEffect.Undertow); } catch { }
                UndertowRows.Visibility = lit ? Visibility.Visible : Visibility.Collapsed;
                SliderUndertowSize.Value = s.UndertowLensSize;
                TxtUndertowSize.Text = $"{s.UndertowLensSize}%";
                SliderUndertowShrink.Value = s.UndertowShrinkSpeed;
                TxtUndertowShrink.Text = $"{s.UndertowShrinkSpeed}%";
                SliderUndertowWiden.Value = s.UndertowClickWiden;
                TxtUndertowWiden.Text = $"{s.UndertowClickWiden}%";
                _undertowReady = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Undertow options: {E}", ex.Message);
            }
            finally { _undertowLoading = false; }
        }

        private void SliderUndertowSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_undertowLoading || !_undertowReady || TxtUndertowSize == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.UndertowLensSize = UndertowOptions.ClampSize(UndertowOptions.Snap(e.NewValue));
            TxtUndertowSize.Text = $"{s.UndertowLensSize}%";
            App.Settings?.Save();
        }

        private void SliderUndertowShrink_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_undertowLoading || !_undertowReady || TxtUndertowShrink == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.UndertowShrinkSpeed = UndertowOptions.ClampShrink(UndertowOptions.Snap(e.NewValue));
            TxtUndertowShrink.Text = $"{s.UndertowShrinkSpeed}%";
            App.Settings?.Save();
        }

        private void SliderUndertowWiden_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_undertowLoading || !_undertowReady || TxtUndertowWiden == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.UndertowClickWiden = UndertowOptions.ClampWiden(UndertowOptions.Snap(e.NewValue));
            TxtUndertowWiden.Text = $"{s.UndertowClickWiden}%";
            App.Settings?.Save();
        }
    }
}

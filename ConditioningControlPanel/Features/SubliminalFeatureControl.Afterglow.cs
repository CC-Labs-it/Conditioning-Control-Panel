using System;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Afterglow's own rows in the gold box (take two, owner 2026-11-15): words per line, how long
    /// the finished line stays lit, how visible a waiting ghost is. Shown only while Afterglow is
    /// lit. The line layer reads the settings every frame. The October near-cursor options
    /// (frequency, count, size, colours, duration, opacity, tilt, "only") are not read by this
    /// effect and get no rows; their settings stay. Clamps: CCP.Core/Super/AfterglowLineOptions.cs.
    /// </summary>
    public partial class SubliminalFeatureControl
    {
        private bool _afterglowLoading;

        private void AfterglowRows_Loaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed -= OnAfterglowSuperChanged;
            SuperAccess.Changed += OnAfterglowSuperChanged;
            LoadAfterglowRows();
        }

        private void AfterglowRows_Unloaded(object sender, RoutedEventArgs e)
            => SuperAccess.Changed -= OnAfterglowSuperChanged;

        private void OnAfterglowSuperChanged(SuperEffect effect)
        {
            if (effect == SuperEffect.Afterglow) Dispatcher.BeginInvoke(new Action(LoadAfterglowRows));
        }

        private void LoadAfterglowRows()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _afterglowLoading = true;
            try
            {
                ConditioningControlPanel.Controls.SuperRows.Apply(AfterglowRows, SuperEffect.Afterglow);
                SliderAfterglowWords.Value = s.AfterglowLineWords;
                TxtAfterglowWords.Text = s.AfterglowLineWords.ToString();
                SliderAfterglowHold.Value = s.AfterglowLineHold;
                TxtAfterglowHold.Text = HoldText(s.AfterglowLineHold);
                SliderAfterglowGhost.Value = s.AfterglowGhostOpacity;
                TxtAfterglowGhost.Text = $"{s.AfterglowGhostOpacity}%";
            }
            catch (Exception ex) { App.Logger?.Debug("Afterglow rows: {E}", ex.Message); }
            finally { _afterglowLoading = false; }
        }

        private static string HoldText(double seconds) => Loc.GetF("super_opt_seconds", seconds.ToString("0.0"));

        private void SliderAfterglowWords_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_afterglowLoading || TxtAfterglowWords == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AfterglowLineWords = (int)Math.Round(e.NewValue);
            TxtAfterglowWords.Text = s.AfterglowLineWords.ToString();
            App.Settings?.Save();
        }

        private void SliderAfterglowHold_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_afterglowLoading || TxtAfterglowHold == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AfterglowLineHold = AfterglowLineOptions.ClampHold(e.NewValue);
            TxtAfterglowHold.Text = HoldText(s.AfterglowLineHold);
            App.Settings?.Save();
        }

        private void SliderAfterglowGhost_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_afterglowLoading || TxtAfterglowGhost == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AfterglowGhostOpacity = (int)Math.Round(e.NewValue);
            TxtAfterglowGhost.Text = $"{s.AfterglowGhostOpacity}%";
            App.Settings?.Save();
        }
    }
}

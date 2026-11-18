using System;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Scrawl's own rows in the gold box (owner, 2026-11-15): how long the wall stamps stay, how
    /// many the walls hold, how hard a clicked word slams. Shown only while Scrawl is lit. The
    /// bouncing text service reads the settings every frame, so a slider moves the walls at once.
    /// Clamps and defaults: CCP.Core/Super/ScrawlOptions.cs.
    /// </summary>
    public partial class BouncingTextFeatureControl
    {
        private bool _scrawlLoading;

        private void ScrawlRows_Loaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed -= OnScrawlSuperChanged;
            SuperAccess.Changed += OnScrawlSuperChanged;
            LoadScrawlRows();
        }

        private void ScrawlRows_Unloaded(object sender, RoutedEventArgs e)
            => SuperAccess.Changed -= OnScrawlSuperChanged;

        private void OnScrawlSuperChanged(SuperEffect effect)
        {
            if (effect == SuperEffect.Scrawl) Dispatcher.BeginInvoke(new Action(LoadScrawlRows));
        }

        private void LoadScrawlRows()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _scrawlLoading = true;
            try
            {
                ConditioningControlPanel.Controls.SuperRows.Apply(ScrawlRows, SuperEffect.Scrawl);
                SliderScrawlLife.Value = s.ScrawlStampSeconds;
                TxtScrawlLife.Text = Loc.GetF("super_opt_seconds", s.ScrawlStampSeconds);
                SliderScrawlCap.Value = s.ScrawlStampCap;
                TxtScrawlCap.Text = s.ScrawlStampCap.ToString();
                SliderScrawlSize.Value = s.ScrawlStampSize;
                TxtScrawlSize.Text = $"{s.ScrawlStampSize}%";
                SliderScrawlSlam.Value = s.ScrawlSlam;
                TxtScrawlSlam.Text = SlamText(s.ScrawlSlam);
            }
            catch (Exception ex) { App.Logger?.Debug("Scrawl rows: {E}", ex.Message); }
            finally { _scrawlLoading = false; }
        }

        private static string SlamText(int slam) => slam <= 0 ? Loc.Get("super_opt_off") : $"{slam}%";

        private void SliderScrawlLife_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_scrawlLoading || TxtScrawlLife == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.ScrawlStampSeconds = (int)Math.Round(e.NewValue);
            TxtScrawlLife.Text = Loc.GetF("super_opt_seconds", s.ScrawlStampSeconds);
            App.Settings?.Save();
        }

        private void SliderScrawlCap_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_scrawlLoading || TxtScrawlCap == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.ScrawlStampCap = (int)Math.Round(e.NewValue);
            TxtScrawlCap.Text = s.ScrawlStampCap.ToString();
            App.Settings?.Save();
        }

        private void SliderScrawlSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_scrawlLoading || TxtScrawlSize == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.ScrawlStampSize = ScrawlOptions.ClampStampSize((int)Math.Round(e.NewValue));
            TxtScrawlSize.Text = $"{s.ScrawlStampSize}%";
            App.Settings?.Save();
        }

        private void SliderScrawlSlam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_scrawlLoading || TxtScrawlSlam == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.ScrawlSlam = ScrawlOptions.ClampSlam((int)Math.Round(e.NewValue));
            TxtScrawlSlam.Text = SlamText(s.ScrawlSlam);
            App.Settings?.Save();
        }
    }
}

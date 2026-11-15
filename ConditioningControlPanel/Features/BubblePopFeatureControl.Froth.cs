using System;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Froth's own rows in the gold box (owner, 2026-11-15): the biggest a merged bubble may grow,
    /// how far a big pop reaches, how hard the cursor pushes. Shown only while Froth is lit, above
    /// the Back Room v2 prize rows (which keep their own logic). The bubble field reads the settings
    /// every step. OWNER RULE: none of these changes pay; a merged bubble pays the sum of its parts.
    /// Clamps: CCP.Core/Super/FrothOptions.cs.
    /// </summary>
    public partial class BubblePopFeatureControl
    {
        private bool _frothLoading;

        private void FrothRows_Loaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed -= OnFrothSuperChanged;
            SuperAccess.Changed += OnFrothSuperChanged;
            LoadFrothRows();
        }

        private void FrothRows_Unloaded(object sender, RoutedEventArgs e)
            => SuperAccess.Changed -= OnFrothSuperChanged;

        private void OnFrothSuperChanged(SuperEffect effect)
        {
            if (effect == SuperEffect.InnerBloom) Dispatcher.BeginInvoke(new Action(LoadFrothRows));
        }

        private void LoadFrothRows()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _frothLoading = true;
            try
            {
                ConditioningControlPanel.Controls.SuperRows.Apply(FrothRows, SuperEffect.InnerBloom);
                SliderFrothSize.Value = s.FrothMaxSize;
                TxtFrothSize.Text = SizeText(s.FrothMaxSize);
                SliderFrothReach.Value = s.FrothPopReach;
                TxtFrothReach.Text = PercentOrOff(s.FrothPopReach);
                SliderFrothPush.Value = s.FrothCursorPush;
                TxtFrothPush.Text = PercentOrOff(s.FrothCursorPush);
            }
            catch (Exception ex) { App.Logger?.Debug("Froth rows: {E}", ex.Message); }
            finally { _frothLoading = false; }
        }

        private static string SizeText(double grow) => grow.ToString("0.0") + "x";
        private static string PercentOrOff(int v) => v <= 0 ? Loc.Get("super_opt_off") : $"{v}%";

        private void SliderFrothSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_frothLoading || TxtFrothSize == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FrothMaxSize = FrothOptions.ClampMaxSize(e.NewValue);
            TxtFrothSize.Text = SizeText(s.FrothMaxSize);
            App.Settings?.Save();
        }

        private void SliderFrothReach_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_frothLoading || TxtFrothReach == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FrothPopReach = (int)Math.Round(e.NewValue);
            TxtFrothReach.Text = PercentOrOff(s.FrothPopReach);
            App.Settings?.Save();
        }

        private void SliderFrothPush_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_frothLoading || TxtFrothPush == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FrothCursorPush = (int)Math.Round(e.NewValue);
            TxtFrothPush.Text = PercentOrOff(s.FrothCursorPush);
            App.Settings?.Save();
        }
    }
}

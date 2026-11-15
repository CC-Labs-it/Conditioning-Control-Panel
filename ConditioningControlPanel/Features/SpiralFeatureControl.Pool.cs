using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// The Whirlpool's one option in the gold box (owner, 2026-11-15): "Always new" (Loom spirals
    /// that melt into each other) or one saved Loom spiral to hold. Kept in its own partial so the
    /// spiral page's own code-behind stays untouched.
    /// </summary>
    public partial class SpiralFeatureControl
    {
        private bool _poolLoading;

        private void PoolSpiralRow_Loaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed += OnPoolSuperChanged;
            DtrhLoomStore.Changed += OnPoolLoomChanged;
            LoadPoolSpiral();
        }

        private void PoolSpiralRow_Unloaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed -= OnPoolSuperChanged;
            DtrhLoomStore.Changed -= OnPoolLoomChanged;
        }

        private void OnPoolSuperChanged(SuperEffect effect)
        {
            if (effect == SuperEffect.Vortex) Dispatcher.BeginInvoke(new Action(LoadPoolSpiral));
        }

        private void OnPoolLoomChanged() => Dispatcher.BeginInvoke(new Action(LoadPoolSpiral));

        private void LoadPoolSpiral()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _poolLoading = true;
            try
            {
                PoolSpiralRow.Visibility = SuperAccess.IsSelected(SuperEffect.Vortex) ? Visibility.Visible : Visibility.Collapsed;
                // DarkComboBoxStyle has a dark popup, so the items take the light brush.
                var fg = (System.Windows.Media.Brush)FindResource("TextLightBrush");
                CmbPoolSpiral.Items.Clear();
                var first = new ComboBoxItem { Content = Loc.Get("super_pool_spiral_new"), Tag = "", Foreground = fg };
                CmbPoolSpiral.Items.Add(first);
                CmbPoolSpiral.SelectedItem = first;
                foreach (var (slug, _, json) in DtrhLoomStore.List())
                {
                    if (LoomSpecJson.Parse(json) == null) continue;      // no dials saved with it: nothing to draw from
                    var item = new ComboBoxItem { Content = slug.Replace('_', ' ').Replace('-', ' '), Tag = slug, Foreground = fg };
                    CmbPoolSpiral.Items.Add(item);
                    if (slug == s.WhirlpoolSpiral) CmbPoolSpiral.SelectedItem = item;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Spiral page, Whirlpool pick: {E}", ex.Message);
            }
            finally { _poolLoading = false; }
        }

        // WhirlpoolLayer reads the setting each frame.
        private void CmbPoolSpiral_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_poolLoading || CmbPoolSpiral.SelectedItem is not ComboBoxItem { Tag: string slug }) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.WhirlpoolSpiral = slug;
            App.Settings?.Save();
        }
    }
}

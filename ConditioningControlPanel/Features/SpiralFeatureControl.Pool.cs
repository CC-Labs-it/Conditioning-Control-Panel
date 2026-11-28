using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// The Whirlpool's options in the gold box (owner, 2026-11-15): the spiral ("Always new" Loom
    /// spirals that melt into each other, or one saved Loom spiral to hold), how often a new one
    /// rolls in, the pool's largest size, and what it drags in. They show while the Whirlpool is
    /// picked. Owner 2026-11-22: Pool size, Smallest size, Stay centred and Restless pool stay on
    /// show; the rest sit behind a "More options" fold (Features/MoreFold) in the same box.
    /// Numbers and clamps live in CCP.Core/Super/WhirlpoolOptions. Kept in its own partial
    /// so the spiral page's own code-behind stays untouched.
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
                ConditioningControlPanel.Controls.SuperRows.Apply(PoolRows, SuperEffect.Vortex);
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
                SliderPoolEvery.Value = s.WhirlpoolNewEvery;
                TxtPoolEvery.Text = Loc.GetF("super_pool_every_value", s.WhirlpoolNewEvery);
                SliderPoolSize.Value = s.WhirlpoolSize;
                TxtPoolSize.Text = $"{s.WhirlpoolSize}%";
                ChkPoolCentred.IsChecked = s.WhirlpoolStayCentred;
                SliderPoolSmallest.Value = s.WhirlpoolSmallest;
                TxtPoolSmallest.Text = $"{s.WhirlpoolSmallest}%";
                ChkPoolRestless.IsChecked = s.WhirlpoolRestless;
                ChkPoolPops.IsChecked = s.MovingEffectsPop;
                ChkPoolDragsFog.IsChecked = s.WhirlpoolDragsFog;
                ChkPoolDragsPictures.IsChecked = s.WhirlpoolDragsPictures;
                ChkPoolDragsBubbles.IsChecked = s.WhirlpoolDragsBubbles;
                ChkPoolDragsWords.IsChecked = s.WhirlpoolDragsWords;
                DimPoolEvery();
                // The "More options" fold opens on its own when a row inside is off its default,
                // so a setting someone changed is never hidden from them. It never closes itself.
                if (PoolFoldedRowsChanged(s)) FoldPoolMore.IsOpen = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Spiral page, Whirlpool options: {E}", ex.Message);
            }
            finally { _poolLoading = false; }
        }

        /// <summary>The folded rows (spiral, new every, moving effects pop, drags) away from their defaults.</summary>
        internal static bool PoolFoldedRowsChanged(Models.AppSettings s) =>
            s.WhirlpoolSpiral.Length > 0
            || s.WhirlpoolNewEvery != ConditioningControlPanel.SuperCore.WhirlpoolOptions.HoldDefault
            || s.MovingEffectsPop
            || !s.WhirlpoolDragsFog || !s.WhirlpoolDragsPictures || !s.WhirlpoolDragsBubbles || !s.WhirlpoolDragsWords;

        /// <summary>"New spiral every" only means something on "Always new": dimmed and locked while
        /// one spiral is held, the same look as SuperDim. The value is kept.</summary>
        private void DimPoolEvery()
        {
            bool held = (App.Settings?.Current?.WhirlpoolSpiral ?? "").Length > 0;
            PoolEveryRow.Opacity = held ? SuperDim.DimOpacity : 1;
            PoolEveryRow.IsHitTestVisible = !held;
        }

        // WhirlpoolLayer reads the setting each frame.
        private void CmbPoolSpiral_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_poolLoading || CmbPoolSpiral.SelectedItem is not ComboBoxItem { Tag: string slug }) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.WhirlpoolSpiral = slug;
            App.Settings?.Save();
            DimPoolEvery();
        }

        // WhirlpoolLayer hands the morph the hold each frame.
        private void SliderPoolEvery_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_poolLoading || TxtPoolEvery == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.WhirlpoolNewEvery = (int)Math.Round(e.NewValue);
            TxtPoolEvery.Text = Loc.GetF("super_pool_every_value", s.WhirlpoolNewEvery);
            App.Settings?.Save();
        }

        // SuperStageHost hands the pool its reach each step.
        private void SliderPoolSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_poolLoading || TxtPoolSize == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.WhirlpoolSize = (int)(Math.Round(e.NewValue / 5) * 5);
            TxtPoolSize.Text = $"{s.WhirlpoolSize}%";
            App.Settings?.Save();
        }

        // SuperStageHost reads it each step.
        private void ChkPoolCentred_Changed(object sender, RoutedEventArgs e)
        {
            if (_poolLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.WhirlpoolStayCentred = ChkPoolCentred.IsChecked ?? false;
            App.Settings?.Save();
        }

        // SuperStageHost hands the pool its floor each step (ccp-bugs #1719).
        private void SliderPoolSmallest_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_poolLoading || TxtPoolSmallest == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.WhirlpoolSmallest = ConditioningControlPanel.SuperCore.WhirlpoolOptions.SnapSmallest(e.NewValue);
            TxtPoolSmallest.Text = $"{s.WhirlpoolSmallest}%";
            App.Settings?.Save();
        }

        // SuperStageHost reads it each step (ccp-bugs #1707).
        private void ChkPoolRestless_Changed(object sender, RoutedEventArgs e)
        {
            if (_poolLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.WhirlpoolRestless = ChkPoolRestless.IsChecked ?? false;
            App.Settings?.Save();
        }

        // SuperStageHost and the bouncing word hand it to the stage each frame. Same setting as the Scrawl box row.
        private void ChkPoolPops_Changed(object sender, RoutedEventArgs e)
        {
            if (_poolLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MovingEffectsPop = ChkPoolPops.IsChecked ?? false;
            App.Settings?.Save();
        }

        // SuperStageHost hands the stage the four switches each step.
        private void ChkPoolDrags_Changed(object sender, RoutedEventArgs e)
        {
            if (_poolLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.WhirlpoolDragsFog = ChkPoolDragsFog.IsChecked ?? true;
            s.WhirlpoolDragsPictures = ChkPoolDragsPictures.IsChecked ?? true;
            s.WhirlpoolDragsBubbles = ChkPoolDragsBubbles.IsChecked ?? true;
            s.WhirlpoolDragsWords = ChkPoolDragsWords.IsChecked ?? true;
            App.Settings?.Save();
        }
    }
}

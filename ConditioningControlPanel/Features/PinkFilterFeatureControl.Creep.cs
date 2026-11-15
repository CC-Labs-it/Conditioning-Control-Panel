using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Steam's options in the gold box on the pink filter page (words, creep speed, wipe size, fog
    /// returns, drift). They show while Steam is picked. Numbers and clamps live in
    /// CCP.Core/Super/SteamOptions. Kept in its own partial so the pink filter's own code-behind
    /// stays untouched.
    /// </summary>
    public partial class PinkFilterFeatureControl
    {
        private bool _creepLoading;

        private void CreepCard_Loaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed += OnSuperChanged;
            LoadCreep();
        }

        private void CreepCard_Unloaded(object sender, RoutedEventArgs e)
            => SuperAccess.Changed -= OnSuperChanged;

        private void OnSuperChanged(SuperEffect effect)
        {
            if (effect == SuperEffect.Creep) Dispatcher.BeginInvoke(new Action(LoadCreep));
        }

        private void LoadCreep()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _creepLoading = true;
            try
            {
                // Take two: Steam reads the pink filter's colour, strength, monitor and order, so
                // its box holds only what is Steam's own, shown while Steam is the pick. The old
                // fog cap stays folded (Steam does not read it).
                CreepCard.Visibility = Visibility.Collapsed;
                SteamRows.Visibility = SuperAccess.IsSelected(SuperEffect.Creep) ? Visibility.Visible : Visibility.Collapsed;
                ChkSteamWords.IsChecked = s.SteamWordsInFog;
                SliderSteamSize.Value = s.SteamWipeSize;
                TxtSteamSize.Text = $"{s.SteamWipeSize}%";
                SliderSteamReturn.Value = s.SteamFogReturn;
                TxtSteamReturn.Text = $"{s.SteamFogReturn}%";
                SliderSteamDrift.Value = s.SteamDrift;
                TxtSteamDrift.Text = $"{s.SteamDrift}%";
                if (CmbCreepSpeed.Items.Count == 0)
                {
                    // DarkComboBoxStyle has a dark popup, so the items take the light brush.
                    var fg = (System.Windows.Media.Brush)FindResource("TextLightBrush");
                    foreach (var (speed, key) in new[]
                             {
                                 (CreepSpeed.Slow, "super_creep_speed_slow"),
                                 (CreepSpeed.Normal, "super_creep_speed_normal"),
                                 (CreepSpeed.Fast, "super_creep_speed_fast"),
                             })
                        CmbCreepSpeed.Items.Add(new ComboBoxItem { Content = Loc.Get(key), Tag = speed, Foreground = fg });
                }
                foreach (ComboBoxItem item in CmbCreepSpeed.Items)
                    if ((CreepSpeed)item.Tag == s.SuperCreepSpeed) CmbCreepSpeed.SelectedItem = item;
                var cap = CreepFog.ClampCap(s.SuperCreepCap);
                SliderCreepCap.Value = Math.Round(cap * 100);
                TxtCreepCap.Text = $"{Math.Round(cap * 100)}%";
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PinkFilter Creep card: {E}", ex.Message);
            }
            finally { _creepLoading = false; }
        }

        // The fog keeps going; the words leave the soft way (SteamLayer reads the setting each frame).
        private void ChkSteamWords_Changed(object sender, RoutedEventArgs e)
        {
            if (_creepLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SteamWordsInFog = ChkSteamWords.IsChecked ?? true;
            App.Settings?.Save();
        }

        // SuperStageHost reads the size each frame.
        private void SliderSteamSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_creepLoading || TxtSteamSize == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SteamWipeSize = (int)(Math.Round(e.NewValue / 5) * 5);
            TxtSteamSize.Text = $"{s.SteamWipeSize}%";
            App.Settings?.Save();
        }

        // SteamLayer hands the field the heal speed each frame.
        private void SliderSteamReturn_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_creepLoading || TxtSteamReturn == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SteamFogReturn = (int)(Math.Round(e.NewValue / 5) * 5);
            TxtSteamReturn.Text = $"{s.SteamFogReturn}%";
            App.Settings?.Save();
        }

        // SteamLayer hands the field the billow share each frame.
        private void SliderSteamDrift_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_creepLoading || TxtSteamDrift == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SteamDrift = (int)(Math.Round(e.NewValue / 5) * 5);
            TxtSteamDrift.Text = $"{s.SteamDrift}%";
            App.Settings?.Save();
        }

        // Steam's creep speed (SuperStageHost reads it each step; Normal = four minutes).
        private void CmbCreepSpeed_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_creepLoading || CmbCreepSpeed.SelectedItem is not ComboBoxItem { Tag: CreepSpeed speed }) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SuperCreepSpeed = speed;
            App.Settings?.Save();
        }

        private void SliderCreepCap_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_creepLoading || TxtCreepCap == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = Math.Round(e.NewValue);
            TxtCreepCap.Text = $"{v}%";
            s.SuperCreepCap = CreepFog.ClampCap(v / 100.0);
            App.Settings?.Save();
        }
    }
}

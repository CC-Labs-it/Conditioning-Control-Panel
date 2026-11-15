using System;
using System.Windows;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Deck's options on the flash page: flips to break, break reach. A new flips value applies to the cards dealt after it. The rows sit in the gold box and show only while
    /// the V2 is the pick; every clamp and default lives in CCP.Core/Super/DeckOptions. The effect
    /// reads the settings each frame, so a slider moves it at once. Kept in its own partial so
    /// the page's own code-behind stays untouched.
    /// </summary>
    public partial class FlashFeatureControl
    {
        private bool _deckLoading, _deckReady;

        private void DeckRows_Loaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed -= OnDeckSuperChanged;
            SuperAccess.Changed += OnDeckSuperChanged;
            LoadDeck();
        }

        private void DeckRows_Unloaded(object sender, RoutedEventArgs e)
            => SuperAccess.Changed -= OnDeckSuperChanged;

        private void OnDeckSuperChanged(SuperEffect effect)
        {
            if (effect == SuperEffect.FlickerDeck) Dispatcher.BeginInvoke(new Action(LoadDeck));
        }

        private void LoadDeck()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _deckLoading = true;
            try
            {
                bool lit = false;
                try { lit = SuperAccess.IsSelected(SuperEffect.FlickerDeck); } catch { }
                DeckRows.Visibility = lit ? Visibility.Visible : Visibility.Collapsed;
                SliderDeckFlips.Value = s.DeckFlips;
                TxtDeckFlips.Text = s.DeckFlips.ToString();
                SliderDeckReach.Value = s.DeckBreakReach;
                TxtDeckReach.Text = $"{s.DeckBreakReach}%";
                _deckReady = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FlickerDeck options: {E}", ex.Message);
            }
            finally { _deckLoading = false; }
        }

        private void SliderDeckFlips_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_deckLoading || !_deckReady || TxtDeckFlips == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.DeckFlips = DeckOptions.ClampFlips((int)Math.Round(e.NewValue));
            TxtDeckFlips.Text = s.DeckFlips.ToString();
            App.Settings?.Save();
        }

        private void SliderDeckReach_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_deckLoading || !_deckReady || TxtDeckReach == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.DeckBreakReach = DeckOptions.ClampReach(DeckOptions.SnapReach(e.NewValue));
            TxtDeckReach.Text = $"{s.DeckBreakReach}%";
            App.Settings?.Save();
        }
    }
}

using System;
using System.Windows;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Deck's options on the flash page: fewest and most flips to break (ccp-bugs #1636), break reach. A new roll applies to the cards dealt after it. The rows sit in the gold box and show only while
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
                ConditioningControlPanel.Controls.SuperRows.Apply(DeckRows, SuperEffect.FlickerDeck);
                ShowDeckRoll(s);
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

        /// <summary>Both ends as the deck rolls them: unset ends follow the older single "Flips to break".</summary>
        private void ShowDeckRoll(Models.AppSettings s)
        {
            var (lo, hi) = DeckOptions.BreakRange(s.DeckFlips, s.DeckFlipsFewest, s.DeckFlipsMost);
            bool was = _deckLoading;
            _deckLoading = true;
            try
            {
                SliderDeckFewest.Value = lo;
                SliderDeckMost.Value = hi;
                TxtDeckFewest.Text = lo.ToString();
                TxtDeckMost.Text = hi.ToString();
            }
            finally { _deckLoading = was; }
        }

        // Moving one end past the other drags the other along, so the roll never runs backwards.
        private void SliderDeckFewest_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => SetDeckRoll(fewest: (int)Math.Round(e.NewValue), most: null);

        private void SliderDeckMost_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => SetDeckRoll(fewest: null, most: (int)Math.Round(e.NewValue));

        private void SetDeckRoll(int? fewest, int? most)
        {
            if (_deckLoading || !_deckReady || TxtDeckFewest == null || TxtDeckMost == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var (lo, hi) = DeckOptions.BreakRange(s.DeckFlips, s.DeckFlipsFewest, s.DeckFlipsMost);
            if (fewest is int f) { lo = DeckOptions.ClampRoll(f); hi = Math.Max(lo, hi); }
            if (most is int m) { hi = DeckOptions.ClampRoll(m); lo = Math.Min(lo, hi); }
            // An end the old single slider already rolls stays unset, so it never reads as "changed".
            var (storeLo, storeHi) = DeckOptions.StoredEnds(s.DeckFlips, lo, hi);
            s.DeckFlipsFewest = storeLo;
            s.DeckFlipsMost = storeHi;
            ShowDeckRoll(s);
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

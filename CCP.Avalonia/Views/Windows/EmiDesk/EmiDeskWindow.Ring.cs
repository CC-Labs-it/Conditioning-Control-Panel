using System;
using Avalonia;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The ring half of the widget: a click on her toggles the card fan, which lives in its own
    /// <see cref="EmiRingWindow"/>. Port of ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.Ring.cs.
    ///
    /// <para>Moments and the delayed pin nudge use the shared Core line/nudge services.</para>
    /// </summary>
    public partial class EmiDeskWindow
    {
        private EmiRingWindow? _ring;

        partial void OnBodyClickedCore(ref bool handled)
        {
            try
            {
                if (InputLocked || Transiting) return;
                handled = true;
                ToggleRing();
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] ring toggle failed"); }
        }

        partial void OnTearDownCore()
        {
            try { _ring?.CloseRing(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring tear-down failed"); }
        }

        partial void OnRingOpenQuery(ref bool open)
        {
            if (RingOpen) open = true;
        }

        /// <summary>True while the fan is on screen.</summary>
        public bool RingOpen => _ring?.IsOpen == true;

        /// <summary>Recompose the fan in place (a pin changed elsewhere).</summary>
        public void RebuildRing()
        {
            try { _ring?.Rebuild(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring rebuild failed"); }
        }

        public void ToggleRing()
        {
            var ring = EnsureRing();
            if (ring == null) return;
            if (ring.IsOpen) { ring.CloseRing(); return; }

            ring.SetWidgetGeometry(BodyPx(), AnchorPx());
            ring.OpenRing();
            if (!ring.IsOpen) return;
            try
            {
                EmiDeskService.Instance.Fire("ringOpen");
                EmiDeskService.Instance.NoteRingOpened();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring-open bookkeeping failed"); }
        }

        public void CloseRing()
        {
            try { _ring?.CloseRing(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] CloseRing failed"); }
        }

        private PixelRect BodyPx()
        {
            var b = BodyScreenRect;
            return new PixelRect((int)Math.Round(b.X), (int)Math.Round(b.Y),
                                 (int)Math.Round(b.Width), (int)Math.Round(b.Height));
        }

        private PixelPoint AnchorPx()
        {
            var a = RingAnchorScreenPoint;
            return new PixelPoint((int)Math.Round(a.X), (int)Math.Round(a.Y));
        }

        private EmiRingWindow? EnsureRing()
        {
            try
            {
                if (_ring != null) return _ring;
                _ring = new EmiRingWindow(BodyPx(), AnchorPx());
                _ring.CardPicked += OnRingCardPicked;
                _ring.RingClosed += OnRingClosed;
                Resized += (_, _) =>
                {
                    try { _ring?.SetWidgetGeometry(BodyPx(), AnchorPx()); _ring?.Relayout(); }
                    catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring follow-resize failed"); }
                };
                Moved += (_, _) => CloseRing();
                Closed += (_, _) =>
                {
                    try { _ring?.Kill(); _ring = null; }
                    catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring shutdown failed"); }
                };
                return _ring;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring window could not be created");
                _ring = null;
                return null;
            }
        }

        /// <summary>WPF's pin-made seam: the first accepted pin retires its onboarding track forever.</summary>
        public void NotePinMadeElsewhere(string id, bool pinned)
        {
            EmiDeskService.Instance.NotePinMadeElsewhere(id, pinned);
        }

        private void OnRingCardPicked(object? sender, EmiRingCard card)
        {
            try { EmiTargets.Find(card.Id)?.Open(); }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] ring card {Target} failed to open", card.Id); }
        }

        /// <summary>WPF OnRingClosed: three dismissals in a row is her cue that the fan is not landing.</summary>
        private void OnRingClosed(object? sender, bool picked)
        {
            try
            {
                var st = EmiState.Current;
                if (picked)
                {
                    if (st.RingIgnoreStreak == 0) return;
                    st.RingIgnoreStreak = 0;
                }
                else
                {
                    EmiDeskService.Instance.Fire("ringDismissed");
                    if (++st.RingIgnoreStreak >= 3)
                    {
                        st.RingIgnoreStreak = 0;
                        EmiDeskService.Instance.Fire("suggestionIgnored3x");
                    }
                }
                EmiState.SaveSoon();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring close bookkeeping failed"); }
        }
    }
}

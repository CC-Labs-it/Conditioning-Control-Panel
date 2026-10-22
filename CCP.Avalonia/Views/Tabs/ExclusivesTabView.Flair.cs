using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Exclusives.Flair.cs (polish 12 round 2) plus
    /// OnExclusiveCardHover (MainWindow.Exclusives.cs:912): the hover lift and glow bloom, the click burst and
    /// press, the cards rising in as the page opens, the shimmer across each group's tier sign and the owned
    /// cards' breathing rim + glint (<see cref="VaultCardAura"/>).
    ///
    /// <para>The sign shimmer and the auras ride the vault's one motion clock (MotionFrame), which runs at Full
    /// motion on a tier that allows loops; WPF's slower Reduced-level variants are not ported (the clock does
    /// not run there; the aura rests as WPF's Off rim). The entrance runs at Full and Reduced like WPF.</para>
    /// </summary>
    public partial class ExclusivesTabView
    {
        internal const int EntranceStepMs = 38, EntranceCap = 16;
        private const double SignSheenOff = -1.2;

        private readonly HashSet<VaultCardAura> _auras = new();
        private readonly List<Rectangle> _signSheens = new();
        private BeatLoop? _entrance;
        private long _entranceStart;
        private bool _entranceFull;

        /// <summary>How a press waits before the door opens; tests capture it (P08).</summary>
        internal static Action<TimeSpan, Action> Later = (delay, then) => DispatcherTimer.RunOnce(then, delay);

        /// <summary>True while the entrance clock runs. Test seam.</summary>
        internal bool EntranceRunning => _entrance != null;

        internal IReadOnlyCollection<VaultCardAura> Auras => _auras;
        internal IReadOnlyList<Rectangle> SignSheens => _signSheens;

        // ---------------- hover + click (WPF OnExclusiveCardHover, OnVaultCardClicked) ----------------

        /// <summary>WPF OnExclusiveCardHover: lift to 1.02, a bloom in the theme glow, the shared hover pop on the art.</summary>
        internal static void HoverCard(Control? card, bool on)
        {
            if (card is not Border border) return;
            Lift(border, on ? 1.02 : 1.0, 150);
            var tier = AmbientFxCanvas.Env.CurrentTier;
            // The bloom re-reads the glow colour every hover, so a mod switch never leaves an old colour behind.
            border.BoxShadow = on && AmbientFxCanvas.Env.AllowGlow(tier)
                ? Glow(AmbientFxCanvas.Env.GlowColor, 0.55, Math.Min(24, AmbientFxCanvas.Env.MaxGlowBlurRadius(tier)))
                : default;
            if (on) HoverPop.Enter(CardArt(card)); else HoverPop.Leave(CardArt(card));
        }

        /// <summary>WPF MotionFx.HoverLift / PressSquish: the card's own scale, quadratic ease-out; snapped at Off.</summary>
        internal static void Lift(Control card, double to, int ms)
        {
            if (card.RenderTransform is not ScaleTransform scale) return;
            scale.Transitions = AmbientFxCanvas.Env.AllowTransitions
                ? new Transitions
                {
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new QuadraticEaseOut() },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new QuadraticEaseOut() },
                }
                : null;
            scale.ScaleX = scale.ScaleY = to;
        }

        /// <summary>WPF OnVaultCardClicked: a burst in the card's group colour and a press, then the door 140 ms
        /// later; with no burst to see (particles off) the door opens at once. Inert cards do nothing.</summary>
        internal void ClickCard(Control card, ExclusiveFeature feature)
        {
            if (!IsOnThisBuild(feature.Key)) return;
            bool burst = false;
            try
            {
                if (IsVisible && AmbientFxCanvas.Env.AllowParticles && card.Bounds.Width > 0
                    && card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), _ambientFx) is { } at)
                {
                    _ambientFx.Burst(at.X, at.Y, ExclusiveGroupRow.HueOf(feature), 90);
                    Lift(card, 0.97, 80);
                    burst = true;
                }
            }
            catch (Exception ex) { Log.Debug("Vault card burst: {E}", ex.Message); }

            if (!burst) { Open(feature); return; }
            Later(TimeSpan.FromMilliseconds(140), () =>
            {
                Lift(card, 1.0, 80);
                Open(feature);
            });
        }

        // ---------------- entrance (WPF PlayVaultEntrance) ----------------

        /// <summary>WPF PlayVaultEntrance, on every show of the page: each shelf row fades in on its turn (38 ms
        /// apart, capped at 16; Reduced 20 ms, cap 8, a shorter fade) and at Full rises 18 px on a back-ease.</summary>
        internal void PlayEntrance()
        {
            EndEntrance();
            if (!AmbientFxCanvas.Env.AllowTransitions) return;
            _entranceFull = AmbientFxCanvas.Env.Level == MotionLevel.Full;
            _entranceStart = FxAdorner.Time.GetTimestamp();
            _entrance = new BeatLoop(this, _ => EntranceFrame());
            _entrance.Start();
            EntranceFrame();
        }

        /// <summary>One entrance frame from <see cref="FxAdorner.Time"/>; ends itself once the last row is in.</summary>
        internal void EntranceFrame()
        {
            if (_entrance == null) return;
            double ms = FxAdorner.Time.GetElapsedTime(_entranceStart).TotalMilliseconds;
            var shelf = Shelf;
            foreach (var c in shelf.GetRealizedContainers()) ApplyEntrance(c, shelf.IndexFromContainer(c), ms);
            double end = _entranceFull ? EntranceStepMs * EntranceCap + 420 : 20 * 8 + 200;
            if (ms >= end) EndEntrance();
        }

        /// <summary>Rows realized mid-entrance (the shelf is rebuilt on every repaint) join on the current frame.</summary>
        private void Shelf_ContainerPrepared(object? sender, ContainerPreparedEventArgs e)
        {
            if (_entrance != null)
                ApplyEntrance(e.Container, e.Index, FxAdorner.Time.GetElapsedTime(_entranceStart).TotalMilliseconds);
        }

        private void ApplyEntrance(Control row, int index, double ms)
        {
            if (index < 0) return;
            var (opacity, y) = EntranceAt(index, ms, _entranceFull);
            row.Opacity = opacity;
            if (row.RenderTransform is not TranslateTransform slide) row.RenderTransform = slide = new TranslateTransform();
            slide.Y = y;
        }

        /// <summary>WPF's keyframes: hidden until the row's turn, then quadratic-out to 1 over 320 ms (Reduced 200 ms);
        /// the rise 18 -> 0 over 420 ms on BackEase(0.35) at Full only.</summary>
        internal static (double Opacity, double Y) EntranceAt(int index, double ms, bool full)
        {
            double delay = (full ? EntranceStepMs : 20) * Math.Min(index, full ? EntranceCap : 8);
            double x = ms - delay, fade = full ? 320 : 200;
            double opacity = x <= 0 ? 0 : x >= fade ? 1 : 1 - Math.Pow(1 - x / fade, 2);
            double y = !full || x >= 420 ? 0 : x <= 0 ? 18 : 18 * (1 - VaultCardAura.BackOut(x / 420, 0.35));
            return (opacity, y);
        }

        /// <summary>Stops the entrance and leaves every row in place (hidden page, motion off, a new entrance).</summary>
        private void EndEntrance()
        {
            if (_entrance == null) return;
            _entrance.Stop();
            _entrance = null;
            foreach (var c in Shelf.GetRealizedContainers())
            {
                c.Opacity = 1;
                if (c.RenderTransform is TranslateTransform slide) slide.Y = 0;
            }
        }

        private ItemsControl Shelf => this.FindControl<ItemsControl>("ExclusivesShelf")!;

        // ---------------- the motion clock's flair (WPF StartVaultSignSheens, VaultCardAura.Start) ----------------

        /// <summary>The flair's share of one MotionFrame: auras breathe and glint, sign shimmers cross.</summary>
        private void FlairFrame(double t)
        {
            foreach (var aura in _auras)
                if (aura.IsVisible) aura.Frame(t);
            for (int i = 0; i < _signSheens.Count; i++)
                SetSignSheen(_signSheens[i], SignSheenAt(t, i));
        }

        /// <summary>WPF StartVaultSignSheens (Full): parked at -1.2 until 0.5 + 0.7 s per sign, then each 4.2 s
        /// a sine-eased crossing to 1.2 in 1.1 s, held off the sign until the next.</summary>
        internal static double SignSheenAt(double t, int index)
        {
            double u = t - (0.5 + 0.7 * index);
            if (u < 0) return SignSheenOff;
            u %= 4.2;
            return u >= 1.1 ? 1.2 : SignSheenOff + 2.4 * (1 - Math.Cos(Math.PI * u / 1.1)) / 2;
        }

        /// <summary>The band slides in units of the sign's width (WPF's RelativeTransform on the brush). Parked or
        /// not yet measured, it is transparent: a band at X 0 would sit across the middle of the sign.</summary>
        private static void SetSignSheen(Rectangle band, double slide)
        {
            if (band.RenderTransform is not TranslateTransform tt) band.RenderTransform = tt = new TranslateTransform();
            tt.X = slide * band.Bounds.Width;
            band.Opacity = slide > SignSheenOff && band.Bounds.Width > 0 ? 1 : 0;
        }

        /// <summary>WPF StopVaultFlair: auras back to the static rim, shimmers parked off the sign.</summary>
        private void RestFlair()
        {
            foreach (var aura in _auras) aura.Rest();
            foreach (var band in _signSheens) SetSignSheen(band, SignSheenOff);
        }

        private void Aura_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is VaultCardAura aura && _auras.Add(aura) && _motion == null) aura.Rest();
        }

        private void Aura_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is VaultCardAura aura) _auras.Remove(aura);
        }

        private void SignSheen_Loaded(object? sender, RoutedEventArgs e)
        {
            // Free has no sign (WPF builds no sheen for it), so its collapsed band never joins.
            if (sender is not Rectangle { DataContext: ExclusiveGroupRow { HasSign: true } } band || _signSheens.Contains(band)) return;
            _signSheens.Add(band);
            SetSignSheen(band, SignSheenOff);
        }

        private void SignSheen_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is Rectangle band) _signSheens.Remove(band);
        }
    }
}

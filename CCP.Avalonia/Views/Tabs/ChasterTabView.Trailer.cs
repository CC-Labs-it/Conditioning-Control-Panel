// WPF ChasterTabView.xaml.cs:58-59,1378-1457: hover, dressing and lifecycle.
// WPF ChasterTabView.xaml:1076-1114: opaque 320 px card below the row.
// WPF ChasterTabView.Fx.cs:593-655: drift, 1700 ms figure and cleanup.
// Unlink/panic cleanup is stricter than WPF ChasterTabView.xaml.cs:192-208 and
// MainWindow/MainWindow.xaml.cs:1934-2013, which do not explicitly close this popup.
// Part B: native scenes (Controls/ChasterTrailerView.cs:90-252).
// One popup, re-aimed at whichever row is hovered: the row's picture, its flavour line, its tier
// sign, the plain explanation with the live figure in it, and the figure itself. It opens 150 ms
// after the pointer settles on a row, stays while the pointer is on the card, and closes 140 ms
// after it leaves both. It sits BELOW the row (a popup over its own trigger flickers).
// The picture drifts and pans while the card is up, and the app's own floating figure lifts off the plate
// every 1.7 s (FxTrailerStart, ChasterTabView.Fx.cs).
// Reused from old lane 7cf2cee4dcfa; native scenes are deferred to part B.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private static readonly TimeSpan TrailerOpenDelay = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan TrailerCloseGrace = TimeSpan.FromMilliseconds(140);

        private readonly DispatcherTimer _trailerOpen = new() { Interval = TrailerOpenDelay };
        private readonly DispatcherTimer _trailerClose = new() { Interval = TrailerCloseGrace };
        private ToggleButton? _trailerRow;
        private bool _trailerShown;
        private bool _overTrailer;
        private static readonly List<WeakReference<ChasterTabView>> TrailerOwners = new();
        private BeatLoop? _trailerDrift;
        private readonly Stopwatch _trailerTime = new();
        private readonly ScaleTransform _trailerScale = new();
        private readonly TranslateTransform _trailerPan = new();
        private ChasterBookedFlash? _trailerFlash;
        private TabPrice? _trailerPrice;
        private readonly DispatcherTimer _trailerFigure = new() { Interval = TimeSpan.FromMilliseconds(1700) };
        internal DispatcherTimer TrailerFigureTimer => _trailerFigure;
        internal bool TrailerFigureTicking => _trailerFigure.IsEnabled;
        internal bool TrailerDrifting => _trailerDrift?.IsRunning == true;

        private void TrailerInit()
        {
            TrailerOwners.RemoveAll(w => !w.TryGetTarget(out _));
            TrailerOwners.Add(new(this));
            TrailerArt.RenderTransform = new TransformGroup { Children = { _trailerScale, _trailerPan } };
            _trailerFigure.Tick += (_, _) => TrailerFigure();
            _trailerOpen.Tick += (_, _) => TrailerOpenTick();
            _trailerClose.Tick += (_, _) => TrailerCloseTick();
            // WPF ChasterTabView.xaml.cs:103: a page that leaves the screen takes its trailer with it.
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) HideTrailer(); };
            AttachedToVisualTree += (_, _) => AmbientFxCanvas.Env.MotionGateChanged += RefreshTrailerMotion;
            DetachedFromVisualTree += (_, _) =>
            {
                AmbientFxCanvas.Env.MotionGateChanged -= RefreshTrailerMotion;
                HideTrailer();
            };
        }

        // Test seams (P08): tests step the timers instead of sleeping on them.
        internal DispatcherTimer TrailerOpenTimer => _trailerOpen;
        internal DispatcherTimer TrailerCloseTimer => _trailerClose;
        internal void TrailerOpenTick() { _trailerOpen.Stop(); OpenTrailer(); }
        internal void TrailerCloseTick() { _trailerClose.Stop(); if (!_overTrailer) HideTrailer(); }

        private void Row_PointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is not ToggleButton row) return;
            _trailerClose.Stop();
            _trailerRow = row;
            if (_trailerShown) OpenTrailer();
            else { _trailerOpen.Stop(); _trailerOpen.Start(); }
        }

        private void Row_PointerExited(object? sender, PointerEventArgs e)
        {
            _trailerOpen.Stop();
            _trailerClose.Stop();
            _trailerClose.Start();
        }

        private void Trailer_PointerEntered(object? sender, PointerEventArgs e)
        {
            _overTrailer = true;
            _trailerClose.Stop();
        }

        private void Trailer_PointerExited(object? sender, PointerEventArgs e)
        {
            _overTrailer = false;
            _trailerClose.Stop();
            _trailerClose.Start();
        }

        /// <summary>The id the trailer is aimed at while it is up, for the tests. Null when closed.
        /// Kept apart from the popup's own IsOpen: without a window behind it a Popup may refuse
        /// to open, and the dressing must still be right.</summary>
        internal string? TrailerId => _trailerShown ? _trailerRow?.Tag as string : null;

        /// <summary>Aim the one popup at the hovered row and dress it for that row's price.</summary>
        internal void OpenTrailer(ToggleButton? row = null)
        {
            row ??= _trailerRow;
            if (row?.Tag is not string id || TabPrices.Find(id) is not { } price) return;
            _trailerRow = row;
            try
            {
                TrailerArt.Source = TabMenuCopy.ArtFor(id) is { } art ? Helpers.ModArt.TryLoadBuiltIn(art, 640) : null;
                TxtTrailerFlavour.Text = Loc.Get(TabMenuCopy.FlavourKey(id));
                TxtTrailerWhy.Text = TabMenuCopy.Why(id, Loc.Get, ShownSeconds(id));
                var tier = TabMenuCopy.BadgeTier(price.Gate);
                TrailerBadgeHost.Child = tier > 0 ? new TierBadge { Tier = tier, MaxWidthOverride = 64 } : null;
                var shown = TabPriceEdit.Shown(price, Overrides);
                Trailer.PlacementTarget = row;
                _trailerShown = true;
                FxTrailerStart(shown);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer"); }
            try { Trailer.IsOpen = true; }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer open"); }
        }

        internal void HideTrailer()
        {
            _trailerOpen.Stop();
            _trailerClose.Stop();
            _overTrailer = false;
            if (!_trailerShown) return;
            _trailerShown = false;
            FxTrailerStop();
            try { Trailer.IsOpen = false; }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer close"); }
        }
        internal static void StopTrailersForPanic()
        {
            TrailerOwners.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var weak in TrailerOwners)
                if (weak.TryGetTarget(out var tab)) tab.HideTrailer();
        }

        private void FxTrailerStart(TabPrice price)
        {
            FxTrailerStop();
            _trailerPrice = price;
            _trailerTime.Restart();
            RefreshTrailerMotion();
            _trailerFigure.Start();
            Dispatcher.UIThread.Post(TrailerFigure, DispatcherPriority.Background);
        }

        private void RefreshTrailerMotion()
        {
            if (_trailerShown && AmbientFxCanvas.Env.AllowAmbientLoops)
            {
                // The window's shared 30 fps beat (AmbientBeatLoopsTests); the phase stays on the card's own clock.
                _trailerDrift ??= new BeatLoop(TrailerArt, _ => AdvanceTrailerDrift(_trailerTime.Elapsed.TotalSeconds));
                _trailerDrift.Start();
            }
            else
            {
                _trailerDrift?.Stop();
                _trailerScale.ScaleX = _trailerScale.ScaleY = 1;
                _trailerPan.X = 0;
            }
        }

        internal void AdvanceTrailerDrift(double seconds)
        {
            // WPF SineEase InOut, auto-reverse: 7 s scale, 9.1 s pan, at 20 fps.
            _trailerScale.ScaleX = _trailerScale.ScaleY = 1 + .09 * (1 - Math.Cos(Math.PI * seconds / 7)) / 2;
            _trailerPan.X = 6 * Math.Cos(Math.PI * seconds / 9.1);
        }

        private void TrailerFigure()
        {
            if (!_trailerShown || _trailerPrice is not { } price) return;
            if (BookedFlashPlan.For(price.Id, price.Seconds, AmbientFxCanvas.Env.Level) is not { } plan) return;
            _trailerFlash?.Dismiss();
            _trailerFlash = ChasterBookedFlash.Show(TrailerFigureAnchor, plan);
        }

        private void FxTrailerStop()
        {
            _trailerFigure.Stop();
            _trailerDrift?.Stop();
            _trailerTime.Stop();
            _trailerFlash?.Dismiss();
            _trailerFlash = null;
            _trailerPrice = null;
            _trailerScale.ScaleX = _trailerScale.ScaleY = 1;
            _trailerPan.X = 0;
        }
    }
}

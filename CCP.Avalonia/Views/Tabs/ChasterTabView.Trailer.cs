// WPF ChasterTabView.xaml.cs:58-59,103,1378-1457; xaml:1076-1114; Fx.cs:593-655.
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
namespace ConditioningControlPanel.Avalonia.Views.Tabs {
    public partial class ChasterTabView {
        private static readonly TimeSpan TrailerOpenDelay = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan TrailerCloseGrace = TimeSpan.FromMilliseconds(140);
        private readonly DispatcherTimer _trailerOpen = new() { Interval = TrailerOpenDelay };
        private readonly DispatcherTimer _trailerClose = new() { Interval = TrailerCloseGrace };
        private ToggleButton? _trailerRow;
        private bool _trailerShown;
        private bool _overTrailer;
        private IDisposable? _trailerPark;
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
        private void TrailerInit() {
            TrailerOwners.RemoveAll(w => !w.TryGetTarget(out _));
            TrailerWeb.Ready += (_, _) => TrailerWeb.IsVisible = _trailerShown;
            TrailerWeb.Failed += (_, _) => TrailerWeb.IsVisible = false;
            TrailerOwners.Add(new(this));
            TrailerArt.RenderTransform = new TransformGroup { Children = { _trailerScale, _trailerPan } };
            _trailerFigure.Tick += (_, _) => TrailerFigure();
            _trailerOpen.Tick += (_, _) => TrailerOpenTick();
            _trailerClose.Tick += (_, _) => TrailerCloseTick();
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) HideTrailer(); };
            AttachedToVisualTree += (_, _) => AmbientFxCanvas.Env.MotionGateChanged += RefreshTrailerMotion;
            DetachedFromVisualTree += (_, _) => {
                AmbientFxCanvas.Env.MotionGateChanged -= RefreshTrailerMotion;
                HideTrailer();
                TrailerWeb.Shutdown(); _trailerPark?.Dispose(); _trailerPark = null;
            };
        }
        internal DispatcherTimer TrailerOpenTimer => _trailerOpen;
        internal DispatcherTimer TrailerCloseTimer => _trailerClose;
        internal void TrailerOpenTick() { _trailerOpen.Stop(); OpenTrailer(); }
        internal void TrailerCloseTick() { _trailerClose.Stop(); if (!_overTrailer) HideTrailer(); }
        private void Row_PointerEntered(object? sender, PointerEventArgs e) {
            if (sender is not ToggleButton row) return;
            _trailerClose.Stop();
            _trailerRow = row;
            if (_trailerShown) OpenTrailer();
            else { _trailerOpen.Stop(); _trailerOpen.Start(); }
        }
        private void Row_PointerExited(object? sender, PointerEventArgs e) {
            _trailerOpen.Stop();
            _trailerClose.Stop();
            _trailerClose.Start();
        }
        private void Trailer_PointerEntered(object? sender, PointerEventArgs e) {
            _overTrailer = true;
            _trailerClose.Stop();
        }
        private void Trailer_PointerExited(object? sender, PointerEventArgs e) {
            _overTrailer = false;
            _trailerClose.Stop();
            _trailerClose.Start();
        }
        internal string? TrailerId => _trailerShown ? _trailerRow?.Tag as string : null;
        internal void OpenTrailer(ToggleButton? row = null) {
            row ??= _trailerRow;
            if (row?.Tag is not string id || TabPrices.Find(id) is not { } price) return;
            _trailerRow = row;
            // WPF ChasterTabView.xaml.cs:1420-1423. Keep the existing adapter through the move.
            using var warmMove = TrailerWeb.Parent == TrailerWarmHost ? TrailerWeb.PreserveForMove() : null;
            if (TrailerWeb.Parent == TrailerWarmHost) {
                TrailerWarmHost.Children.Remove(TrailerWeb);
                TrailerPlate.Children.Add(TrailerWeb);
            }
            using var reparent = _trailerPark;
            _trailerPark = null;
            try {
                TrailerArt.Source = TabMenuCopy.ArtFor(id) is { } art ? Helpers.ModArt.TryLoadBuiltIn(art, 640) : null;
                TxtTrailerFlavour.Text = Loc.Get(TabMenuCopy.FlavourKey(id));
                TxtTrailerWhy.Text = TabMenuCopy.Why(id, Loc.Get, ShownSeconds(id));
                var tier = TabMenuCopy.BadgeTier(price.Gate);
                TrailerBadgeHost.Child = tier > 0 ? new TierBadge { Tier = tier, MaxWidthOverride = 64 } : null;
                var shown = TabPriceEdit.Shown(price, Overrides);
                Trailer.PlacementTarget = row;
                _trailerShown = true;
                var (add, sub) = TabMenuCopy.SceneFigures(id, ShownSeconds);
                TrailerWeb.Show(TabMenuCopy.VignetteFor(id), add, sub);
                TrailerWeb.IsVisible = TrailerWeb.IsReady;
                FxTrailerStart(shown);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer"); }
            try { Trailer.IsOpen = true; }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer open"); }
        }
        internal void HideTrailer() {
            _trailerOpen.Stop();
            _trailerClose.Stop();
            _overTrailer = false;
            if (!_trailerShown) return;
            _trailerShown = false;
            FxTrailerStop();
            TrailerWeb.Pause();
            _trailerPark ??= TrailerWeb.PreserveForMove();
            try { Trailer.IsOpen = false; }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer close"); }
        }
        internal static void StopTrailersForPanic() {
            TrailerOwners.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var weak in TrailerOwners)
                if (weak.TryGetTarget(out var tab)) tab.HideTrailer();
        }
        private void FxTrailerStart(TabPrice price) {
            FxTrailerStop();
            _trailerPrice = price;
            _trailerTime.Restart();
            RefreshTrailerMotion();
            _trailerFigure.Start();
            Dispatcher.UIThread.Post(TrailerFigure, DispatcherPriority.Background);
        }
        private void RefreshTrailerMotion() {
            if (_trailerShown && AmbientFxCanvas.Env.AllowAmbientLoops) {
                _trailerDrift ??= new BeatLoop(TrailerArt, _ => AdvanceTrailerDrift(_trailerTime.Elapsed.TotalSeconds));
                _trailerDrift.Start();
            }
            else {
                _trailerDrift?.Stop();
                _trailerScale.ScaleX = _trailerScale.ScaleY = 1;
                _trailerPan.X = 0;
            }
        }
        internal void AdvanceTrailerDrift(double seconds) {
            _trailerScale.ScaleX = _trailerScale.ScaleY = 1 + .09 * (1 - Math.Cos(Math.PI * seconds / 7)) / 2;
            _trailerPan.X = 6 * Math.Cos(Math.PI * seconds / 9.1);
        }
        private void TrailerFigure() {
            if (!_trailerShown || _trailerPrice is not { } price) return;
            if (BookedFlashPlan.For(price.Id, price.Seconds, AmbientFxCanvas.Env.Level) is not { } plan) return;
            _trailerFlash?.Dismiss();
            _trailerFlash = ChasterBookedFlash.Show(TrailerFigureAnchor, plan);
        }
        private void FxTrailerStop() {
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

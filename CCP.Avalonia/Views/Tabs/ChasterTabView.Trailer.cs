// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs:56-59, 1378-1458.
// Fallback drift and the floating figure remain for part B (WPF ChasterTabView.Fx.cs:593-655).
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
        private IDisposable? _trailerPark;
        private static readonly List<WeakReference<ChasterTabView>> TrailerOwners = new();
        private void TrailerInit()
        {
            TrailerOwners.RemoveAll(w => !w.TryGetTarget(out _));
            TrailerOwners.Add(new(this));
            TrailerWeb.Ready += (_, _) => TrailerWeb.IsVisible = _trailerShown;
            TrailerWeb.Failed += (_, _) => TrailerWeb.IsVisible = false;
            _trailerOpen.Tick += (_, _) => TrailerOpenTick();
            _trailerClose.Tick += (_, _) => TrailerCloseTick();
            // WPF (:99): a page that leaves the screen takes its trailer with it.
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) HideTrailer(); };
            DetachedFromVisualTree += (_, _) =>
            {
                HideTrailer();
                TrailerWeb.Shutdown();
                _trailerPark?.Dispose();
                _trailerPark = null;
            };
        }
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
        internal string? TrailerId => _trailerShown ? _trailerRow?.Tag as string : null;
        internal void OpenTrailer(ToggleButton? row = null)
        {
            row ??= _trailerRow;
            if (row?.Tag is not string id || TabPrices.Find(id) is not { } price) return;
            _trailerRow = row;
            using var reparent = _trailerPark ?? TrailerWeb.PreserveForMove();
            _trailerPark = null;
            try
            {
                TrailerArt.Source = TabMenuCopy.ArtFor(id) is { } art ? Helpers.ModArt.TryLoad(art, 640) : null;
                TxtTrailerFlavour.Text = Loc.Get(TabMenuCopy.FlavourKey(id));
                TxtTrailerWhy.Text = TabMenuCopy.Why(id, Loc.Get, ShownSeconds(id));
                var tier = TabMenuCopy.BadgeTier(price.Gate);
                TrailerBadgeHost.Child = tier > 0 ? new TierBadge { Tier = tier, MaxWidthOverride = 64 } : null;
                if (TrailerWeb.Parent == TrailerWarmHost)
                {
                    TrailerWarmHost.Children.Remove(TrailerWeb);
                    TrailerPlate.Children.Add(TrailerWeb);
                }
                var (add, sub) = TabMenuCopy.SceneFigures(id, ShownSeconds);
                Trailer.PlacementTarget = row;
                _trailerShown = true;
                TrailerWeb.IsVisible = TrailerWeb.IsReady;
                TrailerWeb.Show(TabMenuCopy.VignetteFor(id), add, sub);
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
            TrailerWeb.Pause();
            _trailerPark ??= TrailerWeb.PreserveForMove();
            try { Trailer.IsOpen = false; }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer close"); }
        }
        internal static void StopTrailersForPanic()
        {
            TrailerOwners.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var weak in TrailerOwners)
                if (weak.TryGetTarget(out var tab)) tab.HideTrailer();
        }

    }
}

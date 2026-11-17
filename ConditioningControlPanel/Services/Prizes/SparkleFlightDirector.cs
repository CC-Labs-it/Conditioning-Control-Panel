using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>A window that shows a wallet the stars can fly to (the launcher, the panel).</summary>
internal interface ISparkleFlightHost
{
    Window Window { get; }
    /// <summary>The panel the flight layer is added to, over the window's content.</summary>
    Panel? FlightPanel { get; }
    /// <summary>The wallet's star: where gains land and spends leave.</summary>
    FrameworkElement? WalletStar { get; }
    /// <summary>The element that earned it, when the window knows; null = the window's bottom middle.</summary>
    FrameworkElement? OriginFor(SparkleSource source);
    /// <summary>Show <paramref name="value"/> and hold it against balance refreshes.</summary>
    void HoldCount(int value);
    /// <summary>Drop the hold: the readout goes back to the balance.</summary>
    void ReleaseCount();
    /// <summary>The wallet star kicks; <paramref name="settle"/> is the last, bigger one.</summary>
    void Kick(bool settle);
}

/// <summary>
/// Turns a <see cref="SparklesLedger.Changed"/> into stars. A gain flies from the spot to the wallet
/// of the window in front (the launcher when it is in front, else the panel), the number counting
/// as they land, soft bells climbing and a chord on the last; a spend sends stars out of the wallet.
/// Nothing flies when no wallet window is in front, for a sign-out, or for the first fill of an
/// empty wallet (a sign-in or profile load). Motion Off: the number changes and one bell plays.
/// </summary>
internal static class SparkleFlightDirector
{
    /// <summary>Pure: may this change fly at all?</summary>
    internal static bool ShouldFly(SparkleChange c)
        => c.Amount != 0 && c.Source != SparkleSource.Reset && !(c.IsGain && c.Before == 0);

    private static readonly List<ISparkleFlightHost> Hosts = new();
    private static readonly Dictionary<ISparkleFlightHost, SparkleFlightLayer> Layers = new();
    private static readonly Random Dice = new();
    private static bool _listening;
    private static int _flightId;
    private static ISparkleFlightHost? _live;
    private static DispatcherTimer? _watchdog;

    public static void Register(ISparkleFlightHost host)
    {
        if (!_listening) { SparklesLedger.Changed += OnChanged; _listening = true; }
        if (!Hosts.Contains(host)) Hosts.Add(host);
        host.Window.Deactivated += (_, _) => { if (ReferenceEquals(_live, host)) Abort(); };
    }

    public static void Unregister(ISparkleFlightHost host)
    {
        if (ReferenceEquals(_live, host)) Abort();
        Hosts.Remove(host);
        if (Layers.Remove(host, out var layer)) layer.Clear();
    }

    private static void OnChanged(object? sender, SparkleChange change)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null) return;
        if (!d.CheckAccess()) { d.BeginInvoke(new Action(() => OnChanged(sender, change))); return; }
        try { Present(sender, change); }
        catch (Exception ex) { App.Logger?.Debug("Sparkle flight: {E}", ex.Message); Abort(); }
    }

    private static void Present(object? sender, SparkleChange change)
    {
        if (!ReferenceEquals(sender, App.Settings?.Current) || !ShouldFly(change)) return;
        // The focused window first (the launcher is owned by the panel, so both can count as in front).
        var host = Hosts.FirstOrDefault(h => InFront(h) && h.Window.IsActive) ?? Hosts.FirstOrDefault(InFront);
        if (host == null) return;
        Abort();
        var level = MotionFx.Level;
        var count = SparkleFlightPlan.StarCount(change.Amount, level);
        if (count == 0 || host.FlightPanel is not { } panel || host.WalletStar is not { } star)
        {
            SparkleSfx.Bell(3);
            return;
        }
        var layer = LayerFor(host, panel);
        if (!TryCenter(star, layer, out var wallet)) { SparkleSfx.Bell(3); return; }
        var w = layer.ActualWidth > 0 ? layer.ActualWidth : panel.ActualWidth;
        var h = layer.ActualHeight > 0 ? layer.ActualHeight : panel.ActualHeight;
        var bottom = new Point(w / 2, h - 48);

        var id = ++_flightId;
        _live = host;
        StartWatchdog();
        if (change.IsGain)
        {
            var from = TryCenter(host.OriginFor(change.Source), layer, out var spot) ? spot : bottom;
            var paths = SparkleFlightPlan.Gain(from.X, from.Y, wallet.X, wallet.Y, count, Dice);
            var landed = 0;
            var lastTick = double.NegativeInfinity;
            host.HoldCount(change.Before);
            SparkleSfx.Air(up: true);
            layer.Fly(paths, _ =>
            {
                if (id != _flightId) return;
                landed++;
                host.HoldCount(SparkleFlightPlan.CountAt(change.Before, change.Balance, landed, count));
                if (landed >= count)
                {
                    layer.Ping(wallet, big: true);
                    host.Kick(settle: true);
                    SparkleSfx.Chord();
                    Finish();
                    return;
                }
                host.Kick(settle: false);
                if (landed % 2 == 1) layer.Ping(wallet, big: false);
                var now = Environment.TickCount64;
                if (now - lastTick >= SparkleFlightPlan.TickGapMs) { lastTick = now; SparkleSfx.Bell(SparkleFlightPlan.BellFor((double)landed / count)); }
            });
        }
        else
        {
            // The number goes down at once (the buy already happened); the stars leave after it.
            host.ReleaseCount();
            host.Kick(settle: false);
            var paths = SparkleFlightPlan.Spend(wallet.X, wallet.Y, bottom.X, bottom.Y, count, Dice);
            var gone = 0;
            var lastTick = double.NegativeInfinity;
            SparkleSfx.Air(up: false);
            layer.Fly(paths, i =>
            {
                if (id != _flightId) return;
                if (++gone >= count) { Finish(); return; }
                var now = Environment.TickCount64;
                if (now - lastTick >= SparkleFlightPlan.TickGapMs) { lastTick = now; SparkleSfx.Bell(SparkleFlightPlan.BellFor(1 - (double)i / count)); }
            });
        }
    }

    /// <summary>The window is on screen and it, or a window it owns, has the focus.</summary>
    private static bool InFront(ISparkleFlightHost h)
    {
        var w = h.Window;
        if (!w.IsVisible || w.WindowState == WindowState.Minimized) return false;
        return w.IsActive || w.OwnedWindows.Cast<Window>().Any(o => o.IsActive);
    }

    private static SparkleFlightLayer LayerFor(ISparkleFlightHost host, Panel panel)
    {
        if (Layers.TryGetValue(host, out var layer) && ReferenceEquals(layer.Parent, panel)) return layer;
        layer = new SparkleFlightLayer();
        Panel.SetZIndex(layer, 9999);
        if (panel is Grid grid)
        {
            Grid.SetRowSpan(layer, Math.Max(1, grid.RowDefinitions.Count));
            Grid.SetColumnSpan(layer, Math.Max(1, grid.ColumnDefinitions.Count));
        }
        panel.Children.Add(layer);
        layer.UpdateLayout();
        Layers[host] = layer;
        return layer;
    }

    private static bool TryCenter(FrameworkElement? el, Visual to, out Point center)
    {
        center = default;
        try
        {
            if (el == null || !el.IsVisible || el.ActualWidth <= 0) return false;
            center = el.TransformToVisual(to).Transform(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
            return !double.IsNaN(center.X) && !double.IsNaN(center.Y);
        }
        catch { return false; }
    }

    private static void StartWatchdog()
    {
        _watchdog ??= new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(SparkleFlightPlan.WatchdogMs) };
        _watchdog.Tick -= OnWatchdog;
        _watchdog.Tick += OnWatchdog;
        _watchdog.Stop();
        _watchdog.Start();
    }

    private static void OnWatchdog(object? sender, EventArgs e) => Abort();

    /// <summary>The last star is home: the readout goes back to the truth.</summary>
    private static void Finish()
    {
        _watchdog?.Stop();
        var host = _live;
        _live = null;
        host?.ReleaseCount();
    }

    /// <summary>Ends a live flight now. The number is put right; the stars just go.</summary>
    private static void Abort()
    {
        _flightId++;
        _watchdog?.Stop();
        var host = _live;
        _live = null;
        if (host == null) return;
        if (Layers.TryGetValue(host, out var layer)) layer.Clear();
        host.ReleaseCount();
    }
}

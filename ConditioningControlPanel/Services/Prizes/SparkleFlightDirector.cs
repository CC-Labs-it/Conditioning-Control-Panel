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
    /// <summary>Drop the hold: the readout goes back to the balance (at once when <paramref name="snap"/>).</summary>
    void ReleaseCount(bool snap = false);
    /// <summary>The wallet star kicks; <paramref name="settle"/> is the last, bigger one.</summary>
    void Kick(bool settle);
}

/// <summary>
/// Turns a <see cref="SparklesLedger.Changed"/> into stars. A gain flies from the spot to the wallet
/// of the window in front (the launcher when it is in front, else the panel), the number counting
/// as they land, soft bells climbing and a chord on the last; a spend sends stars out of the wallet.
/// Nothing flies for a sign-out or for the first fill of an empty wallet (a sign-in or profile
/// load). Motion Off: the number changes and one bell plays.
///
/// <para>BANK ON RETURN: a gain made while no wallet window is in front is owed
/// (<see cref="SparkleOwedBook"/>) and flies as one flight from the bottom middle when the launcher
/// or the panel next comes to the front, the number held at the old value until the stars land.
/// PANIC: <see cref="Stop"/> ends a flight and its sounds at once, the number snaps to the balance
/// and nothing stays owed.</para>
/// </summary>
internal static class SparkleFlightDirector
{
    /// <summary>Pure: may this change fly at all?</summary>
    internal static bool ShouldFly(SparkleChange c)
        => c.Amount != 0 && c.Source != SparkleSource.Reset && !(c.IsGain && c.Before == 0);

    /// <summary>The window has just come to the front: a beat before owed stars leave, so its own
    /// show (the launcher's count-up) has settled.</summary>
    internal const int ReturnDelayMs = 650;

    private static readonly List<ISparkleFlightHost> Hosts = new();
    private static readonly Dictionary<ISparkleFlightHost, SparkleFlightLayer> Layers = new();
    private static readonly SparkleOwedBook Owed = new();
    private static readonly Random Dice = new();
    private static bool _listening;
    private static int _flightId;
    private static ISparkleFlightHost? _live;
    private static int? _liveTo;
    private static DispatcherTimer? _watchdog;
    private static ISparkleFlightHost? _returnHost;
    private static DispatcherTimer? _returnTimer;

    public static void Register(ISparkleFlightHost host)
    {
        if (!_listening) { SparklesLedger.Changed += OnChanged; _listening = true; }
        if (!Hosts.Contains(host)) Hosts.Add(host);
        host.Window.Deactivated += (_, _) =>
        {
            if (ReferenceEquals(_live, host)) Abort();
            if (ReferenceEquals(_returnHost, host)) CancelReturn(keep: null);
        };
        host.Window.Activated += (_, _) => Guard(() => OnReturn(host));
    }

    public static void Unregister(ISparkleFlightHost host)
    {
        if (ReferenceEquals(_live, host)) Abort();
        if (ReferenceEquals(_returnHost, host)) CancelReturn(keep: null);
        Hosts.Remove(host);
        if (Layers.Remove(host, out var layer)) layer.Clear();
    }

    /// <summary>What is owed to <paramref name="settings"/> right now (the launcher counts up to the
    /// balance minus this on show, so the owed stars start where the number stands).</summary>
    internal static int OwedTo(AppSettings? settings)
        => settings == null || MotionFx.Level == MotionLevel.Off ? 0 : Owed.OwedTo(settings, settings.SkillPoints);

    /// <summary>A flight is showing, or will show on return, the credit that left the wallet at
    /// <paramref name="balance"/>: the panel's "+N" badge stays quiet (the flight wins).</summary>
    internal static bool Covers(int balance)
        => (_live != null && _liveTo == balance)
           || (Owed.Owed > 0 && Owed.Balance == balance && MotionFx.Level != MotionLevel.Off);

    /// <summary>PANIC: the stars and their bells stop now, the number snaps to the balance, and
    /// nothing owed flies afterwards (the panic itself brings the panel to the front).</summary>
    internal static void Stop()
    {
        SparkleSfx.StopAll();
        var d = Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) { d.BeginInvoke(new Action(Stop)); return; }
        Owed.Clear();
        if (_pageGain is { } pg) { _pageGain = null; pg.Host.ReleaseCount(snap: true); }
        CancelReturn(keep: null, snap: true);
        Abort(snap: true);
        foreach (var layer in Layers.Values) layer.Clear();   // the last landing's ring and motes too
    }

    private static void OnChanged(object? sender, SparkleChange change)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null) return;
        if (!d.CheckAccess()) { d.BeginInvoke(new Action(() => OnChanged(sender, change))); return; }
        Guard(() => Present(sender, change));
    }

    private static void Guard(Action a)
    {
        try { a(); }
        catch (Exception ex) { App.Logger?.Debug("Sparkle flight: {E}", ex.Message); Abort(); }
    }

    private static void Present(object? sender, SparkleChange change)
    {
        if (sender == null || !ReferenceEquals(sender, App.Settings?.Current)) return;
        var shown = ShouldFly(change) && Show(sender, change);
        Owed.Note(sender, change, shown);
    }

    /// <summary>Plays the change in the wallet window in front. False = none was in front.</summary>
    private static bool Show(object owner, SparkleChange change)
    {
        // The focused window first (the launcher is owned by the panel, so both can count as in front).
        var host = Hosts.FirstOrDefault(h => InFront(h) && h.Window.IsActive) ?? Hosts.FirstOrDefault(InFront);
        if (host == null) return false;
        Abort();
        CancelReturn(keep: host);
        if (PageFliesOver?.Invoke(host.Window) == true)
        {
            // The counter sheet is down over this window: its page flies the stars to this wallet
            // (init.walletAt), and the wallet only counts and kicks as they land (PageLanded).
            if (!change.IsGain) { host.ReleaseCount(); host.Kick(settle: false); return true; }
            var start = Owed.Take(owner, change.Before) is { } o ? o.From : change.Before;
            _pageGain = (host, start, change.Balance);
            host.HoldCount(start);
            return true;
        }
        if (!change.IsGain) { FlySpend(host, change.Amount); return true; }
        // Stars still owed from before ride along with this one.
        var from = Owed.Take(owner, change.Before) is { } owed ? owed.From : change.Before;
        FlyGain(host, from, change.Balance, host.OriginFor(change.Source));
        return true;
    }

    /// <summary>True while the counter sheet is down over that window (set by the counter host): its page flies.</summary>
    internal static Func<Window, bool>? PageFliesOver { get; set; }

    private static (ISparkleFlightHost Host, int From, int To)? _pageGain;

    /// <summary>The sheet's page says its stars reached (or, for a spend, left) the wallet of <paramref name="window"/>:
    /// <paramref name="k"/> of them so far (1 = the last). The wallet counts and kicks; with no ledger change waiting
    /// (the DEBUG preview's pretend wallet) the last one just puts the readout right.</summary>
    internal static void PageLanded(Window window, double k)
    {
        var host = Hosts.FirstOrDefault(h => ReferenceEquals(h.Window, window));
        if (host == null || !double.IsFinite(k)) return;
        k = Math.Clamp(k, 0, 1);
        bool last = k >= 1;
        if (_pageGain is { } g && ReferenceEquals(g.Host, host))
        {
            if (last) { _pageGain = null; host.ReleaseCount(); }
            else host.HoldCount((int)Math.Round(g.From + (g.To - g.From) * k));
        }
        else if (last) host.ReleaseCount();
        host.Kick(settle: last);
    }

    /// <summary>The launcher or the panel came to the front: hold the number where the owed stars
    /// start, and fly them after a beat.</summary>
    private static void OnReturn(ISparkleFlightHost host)
    {
        var s = App.Settings?.Current;
        if (s == null || _live != null) return;
        var owed = Owed.OwedTo(s, s.SkillPoints);
        if (owed <= 0) return;
        if (MotionFx.Level == MotionLevel.Off) { Owed.Clear(); host.ReleaseCount(snap: true); return; }   // nothing to fly; the number reads the truth
        CancelReturn(keep: host);
        _returnHost = host;
        host.HoldCount(s.SkillPoints - owed);
        if (_returnTimer == null)
        {
            _returnTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ReturnDelayMs) };
            _returnTimer.Tick += (_, _) => Guard(FlyOwed);
        }
        _returnTimer.Stop();
        _returnTimer.Start();
    }

    private static void FlyOwed()
    {
        _returnTimer?.Stop();
        var host = _returnHost;
        _returnHost = null;
        if (host == null) return;
        var s = App.Settings?.Current;
        // Not in front any more, or nowhere to land (Lockdown's veil): the stars stay owed.
        if (s == null || !Hosts.Contains(host) || !InFront(host) || host.FlightPanel == null
            || host.WalletStar is not { IsVisible: true, ActualWidth: > 0 })
        {
            host.ReleaseCount();
            return;
        }
        if (Owed.Take(s, s.SkillPoints) is not { } owed) { host.ReleaseCount(); return; }
        Abort();
        FlyGain(host, owed.From, owed.To, origin: null);
    }

    private static void CancelReturn(ISparkleFlightHost? keep, bool snap = false)
    {
        _returnTimer?.Stop();
        var host = _returnHost;
        _returnHost = null;
        if (host != null && !ReferenceEquals(host, keep)) host.ReleaseCount(snap);
    }

    private static void FlyGain(ISparkleFlightHost host, int before, int balance, FrameworkElement? origin)
    {
        var count = SparkleFlightPlan.StarCount(balance - before, MotionFx.Level);
        if (count == 0 || host.FlightPanel is not { } panel || host.WalletStar is not { } star
            || !TryCenter(star, LayerFor(host, panel), out var wallet))
        {
            host.ReleaseCount();
            SparkleSfx.Bell(3);
            return;
        }
        var layer = LayerFor(host, panel);
        var from = TryCenter(origin, layer, out var spot) ? spot : BottomMiddle(layer, panel);
        var paths = SparkleFlightPlan.Gain(from.X, from.Y, wallet.X, wallet.Y, count, Dice);
        var id = Begin(host, balance);
        var landed = 0;
        var lastTick = double.NegativeInfinity;
        host.HoldCount(before);
        SparkleSfx.Air(up: true);
        layer.Fly(paths, _ =>
        {
            if (id != _flightId) return;
            landed++;
            host.HoldCount(SparkleFlightPlan.CountAt(before, balance, landed, count));
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

    private static void FlySpend(ISparkleFlightHost host, int amount)
    {
        var count = SparkleFlightPlan.StarCount(amount, MotionFx.Level);
        if (count == 0 || host.FlightPanel is not { } panel || host.WalletStar is not { } star
            || !TryCenter(star, LayerFor(host, panel), out var wallet))
        {
            SparkleSfx.Bell(3);
            return;
        }
        var layer = LayerFor(host, panel);
        var bottom = BottomMiddle(layer, panel);
        // The number goes down at once (the buy already happened); the stars leave after it.
        host.ReleaseCount();
        host.Kick(settle: false);
        var paths = SparkleFlightPlan.Spend(wallet.X, wallet.Y, bottom.X, bottom.Y, count, Dice);
        var id = Begin(host, balance: null);
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

    private static int Begin(ISparkleFlightHost host, int? balance)
    {
        _live = host;
        _liveTo = balance;
        StartWatchdog();
        return ++_flightId;
    }

    private static Point BottomMiddle(FrameworkElement layer, FrameworkElement panel)
    {
        var w = layer.ActualWidth > 0 ? layer.ActualWidth : panel.ActualWidth;
        var h = layer.ActualHeight > 0 ? layer.ActualHeight : panel.ActualHeight;
        return new Point(w / 2, h - 48);
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
        _liveTo = null;
        host?.ReleaseCount();
    }

    /// <summary>Ends a live flight now. The number is put right; the stars just go.</summary>
    private static void Abort(bool snap = false)
    {
        _flightId++;
        _watchdog?.Stop();
        var host = _live;
        _live = null;
        _liveTo = null;
        if (host == null) return;
        if (Layers.TryGetValue(host, out var layer)) layer.Clear();
        host.ReleaseCount(snap);
    }
}

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    private readonly Random _sheenRandom = new();
    private readonly DispatcherTimer _wanderTimer = new();
    private readonly DispatcherTimer _wanderEnd = new() { Interval = TimeSpan.FromMilliseconds(1700) };
    internal CardSheenAdorner? CtaSheen { get; private set; }
    internal CardSheenAdorner? WanderSheen { get; private set; }
    internal bool WanderTicking => _wanderTimer.IsEnabled;

    private void HookSheens()
    {
        _wanderTimer.Tick += (_, _) => WanderSheenPass();
        _wanderEnd.Tick += (_, _) => StopWanderingSheen();
        AmbientFxCanvas.Env.MotionGateChanged += StartSheens;
        Closed += (_, _) => AmbientFxCanvas.Env.MotionGateChanged -= StartSheens;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && FxLive) StartSheens();
        };
    }

    private void StartSheens()
    {
        StopSheens();
        if (!FxLive || !_fxActive || !AmbientFxCanvas.Env.AllowAmbientLoops) return;
        CtaSheen = CardSheenAdorner.Attach(PanelCta, 12);
        _wanderTimer.Interval = TimeSpan.FromSeconds(_sheenRandom.Next(6, 11));
        // WPF Choreo.cs:439 returns on _perfLow; AllowAmbientLoops already implies it here.
        if (!PerfLow) _wanderTimer.Start();
    }

    private void StopSheens()
    {
        _wanderTimer.Stop();
        StopWanderingSheen();
        CardSheenAdorner.Detach(CtaSheen); CtaSheen = null;
    }

    private void StopWanderingSheen()
    {
        _wanderEnd.Stop();
        CardSheenAdorner.Detach(WanderSheen); WanderSheen = null;
    }

    internal void WanderSheenPass()
    {
        StopWanderingSheen();
        _wanderTimer.Interval = TimeSpan.FromSeconds(_sheenRandom.Next(6, 11));
        if (!FxLive || !_fxActive || !AmbientFxCanvas.Env.AllowAmbientLoops) return;
        // WPF TileInView permits a quarter-card of slack, never a wholly clipped tile.
        var cards = GamesGrid.Children.OfType<Border>().Where(tile =>
        {
            if (!tile.IsEffectivelyVisible || tile.Bounds.Height <= 0) return false;
            var origin = tile.TranslatePoint(default, GamesColumn);
            double slack = tile.Bounds.Height * 0.25;
            return origin is { } p && p.Y >= -slack && p.Y + tile.Bounds.Height <= GamesColumn.Bounds.Height + slack;
        }).ToArray();
        if (cards.Length == 0) return;
        WanderSheen = CardSheenAdorner.Attach(cards[_sheenRandom.Next(cards.Length)], TileRadius);
        if (WanderSheen != null) _wanderEnd.Start();
    }
}

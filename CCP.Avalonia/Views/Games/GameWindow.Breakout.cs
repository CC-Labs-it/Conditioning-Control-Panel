using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF BreakoutHostService.cs:14-17 and BreakoutAccess.cs:9-16.
    internal static bool BreakoutGate() => TierGate.DemandLab(Loc.Get("launcher_game_breakout_title"));
    private static bool FullBreakoutAllowed => TierGate.RequiresLab(Loc.Get("launcher_game_breakout_title")).Allowed;
    private static object FullBreakoutEntitlement() => FullBreakoutAllowed
        ? new { storyLimit = 8, endless = true, demo = false }
        : new { storyLimit = 3, endless = false, demo = true };

    private DispatcherTimer? _breakoutAccessWatch;
    private ProviderSubscription? _breakoutPatreon, _breakoutSubscribeStar;
    private bool _lastBreakoutAccess;

    // WPF BackRoomHostService.cs:736-763: events plus a 2 s watch for quiet grace expiry.
    private void StartBreakoutAccessWatch()
    {
        if (Spec.Id != "breakout") return;
        _lastBreakoutAccess = FullBreakoutAllowed;
        _breakoutPatreon = AccountSeed.Patreon;
        _breakoutSubscribeStar = AccountSeed.SubscribeStar;
        if (_breakoutPatreon != null) _breakoutPatreon.TierChanged += OnBreakoutTierChanged;
        if (_breakoutSubscribeStar != null) _breakoutSubscribeStar.TierChanged += OnBreakoutTierChanged;
        _breakoutAccessWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _breakoutAccessWatch.Tick += OnBreakoutAccessTick;
        _breakoutAccessWatch.Start();
    }

    private void OnBreakoutTierChanged(object? sender, PatreonTier tier) => Dispatcher.UIThread.Post(PushBreakoutAccess);
    private void OnBreakoutAccessTick(object? sender, EventArgs e) => PushBreakoutAccess();

    internal void PushBreakoutAccess()
    {
        if (Spec.Id != "breakout" || _closing) return;
        var allowed = FullBreakoutAllowed;
        if (allowed == _lastBreakoutAccess) return;
        _lastBreakoutAccess = allowed;
        // WPF BackRoomHostService.cs:620-632, 756-763. The page accepts partial settings frames.
        Post(new { type = "settings", breakout = FullBreakoutEntitlement(), breakoutStandalone = true });
    }

    private void StopBreakoutAccessWatch()
    {
        _breakoutAccessWatch?.Stop();
        if (_breakoutAccessWatch != null) _breakoutAccessWatch.Tick -= OnBreakoutAccessTick;
        _breakoutAccessWatch = null;
        if (_breakoutPatreon != null) _breakoutPatreon.TierChanged -= OnBreakoutTierChanged;
        if (_breakoutSubscribeStar != null) _breakoutSubscribeStar.TierChanged -= OnBreakoutTierChanged;
        _breakoutPatreon = _breakoutSubscribeStar = null;
    }
}

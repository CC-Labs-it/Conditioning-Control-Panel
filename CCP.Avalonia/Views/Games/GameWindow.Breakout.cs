using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private bool IsBreakout => Spec.Id is "breakout" or "breakoutdemo";
    private bool _breakoutSwitchPending;

    // WPF BackRoomHostService.cs:295-325: focus, ask with No as default, then replace.
    internal static Func<GameWindow, Task<bool>> ConfirmBreakoutSwitch = owner =>
        MessageDialog.ConfirmAsync(owner, Loc.Get("launcher_switch_title"), Loc.Get("launcher_switch_body"),
            defaultToCancel: true, okText: Loc.Get("btn_yes"), cancelText: Loc.Get("btn_no"));

    private async Task SwitchBreakoutAsync(string id)
    {
        if (_breakoutSwitchPending) return;
        _breakoutSwitchPending = true;
        try
        {
            if (!await ConfirmBreakoutSwitch(this)) return;
            // The dialog yields: panic or the title-bar X may have ended this run meanwhile.
            if (_closing) return;
            if (id == "breakout" && !BreakoutGate()) return;
            Close();
            Launch(id);
        }
        catch (Exception ex) { Log.Warning(ex, "[Breakout] switch refused"); }
        finally { _breakoutSwitchPending = false; }
    }

    private SettingsService? _breakoutSettingsService;
    private AppSettings? _breakoutSettings;

    // WPF BackRoomHostService.cs:689-720: rebind after cloud restore and push live motion changes.
    private void HookBreakoutSettings()
    {
        if (_breakoutSettings != null) _breakoutSettings.PropertyChanged -= OnBreakoutSettingChanged;
        _breakoutSettings = CoreSettings.Current;
        _breakoutSettings.PropertyChanged += OnBreakoutSettingChanged;
    }

    private void OnBreakoutSettingsReplaced() => Dispatcher.UIThread.Post(() =>
    {
        if (_closing) return;
        HookBreakoutSettings();
        PushBreakoutSettings();
    });

    private void OnBreakoutSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.MotionLevel)) Dispatcher.UIThread.Post(PushBreakoutSettings);
    }

    private void PushBreakoutSettings()
    {
        if (_closing) return;
        // WPF BackRoomHostService.cs:620-632; play.html merges partial settings frames.
        var motion = CoreSettings.Current.MotionLevel;
        Post(new { type = "settings", motion = motion.ToString().ToLowerInvariant(),
            reduced = motion != MotionLevel.Full, breakout = BreakoutEntitlementFor(Spec.Id), breakoutStandalone = true });
    }

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
        if (!IsBreakout) return;
        _breakoutSettingsService = CoreSettings.Service;
        if (_breakoutSettingsService != null) _breakoutSettingsService.CurrentReplaced += OnBreakoutSettingsReplaced;
        HookBreakoutSettings();
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
        PushBreakoutSettings();
    }

    private void StopBreakoutAccessWatch()
    {
        if (_breakoutSettings != null) _breakoutSettings.PropertyChanged -= OnBreakoutSettingChanged;
        if (_breakoutSettingsService != null) _breakoutSettingsService.CurrentReplaced -= OnBreakoutSettingsReplaced;
        _breakoutSettings = null;
        _breakoutSettingsService = null;
        _breakoutAccessWatch?.Stop();
        if (_breakoutAccessWatch != null) _breakoutAccessWatch.Tick -= OnBreakoutAccessTick;
        _breakoutAccessWatch = null;
        if (_breakoutPatreon != null) _breakoutPatreon.TierChanged -= OnBreakoutTierChanged;
        if (_breakoutSubscribeStar != null) _breakoutSubscribeStar.TierChanged -= OnBreakoutTierChanged;
        _breakoutPatreon = _breakoutSubscribeStar = null;
    }
}

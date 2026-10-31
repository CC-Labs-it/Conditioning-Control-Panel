using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal const string BackRoomId = "backroom";
    internal static Action RequestBackRoomSignIn = () => Windows.MainShellWindow.Current?.RequestBackRoomSignIn();
    internal static Func<Action<int>, IBackRoomRelay> BackRoomRelay = adopt =>
    {
        var account = FriendsHead.Identity()?.UnifiedId;
        (string UnifiedId, string Token)? Identity()
        {
            var current = FriendsHead.Identity();
            return current?.UnifiedId == account ? current : null;
        }
        var url = FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
            Environment.GetEnvironmentVariable(V2PurchaseHead.EnvVar));
        return url == null ? new OfflineRoomRelay() : new BackRoomApi(null, Identity,
            sp => { if (Identity() != null) adopt(sp); }, PrizeOwnership.ApplySnapshot, url);
    };

    // WPF BackRoomHostService.cs:312-318. No paid tier gate.
    private static bool BackRoomGate()
    {
        if (FriendsHead.Identity() != null) return true;
        RequestBackRoomSignIn();
        return false;
    }

    private BackRoomBridge? _room;
    private AppSettings? _roomSettings;
    private SettingsService? _roomService;
    private string? _roomAccount;

    // WPF BackRoomHostService.cs:399-447. Reuse protocol validation, deadlines and close watchdog.
    private void OpenBackRoom()
    {
        _roomAccount = FriendsHead.Identity()?.UnifiedId;
        _room = new BackRoomBridge(new BackRoomBridge.Deps
        {
            Post = message => Dispatcher.UIThread.Post(() => { if (!_closing) Post(message); }),
            Relay = BackRoomRelay(sp => _room?.AdoptSp(sp, SameRoomAccount)),
            Media = new BundledRoomMedia(() => _server!),
            BuildInit = BackRoomInit,
            CloseWindow = () => { if (!_closing) Close(); },
            Schedule = (delay, action) =>
            {
                var timer = DispatcherTimer.RunOnce(action, delay);
                return timer.Dispose;
            },
            OnUi = action => Dispatcher.UIThread.Post(action),
            OffUi = action => Task.Run(action),
            SetSp = sp => { if (SameRoomAccount()) CoreSettings.Current.SkillPoints = sp; },
            SetOption = option => Dispatcher.UIThread.Post(() => ApplyBackRoomOption(option)),
        });
        _roomService = CoreSettings.Service;
        if (_roomService != null) _roomService.CurrentReplaced += ReplaceRoomSettings;
        ReplaceRoomSettings();
    }

    private bool SameRoomAccount() => !_closing && _roomAccount != null && FriendsHead.Identity()?.UnifiedId == _roomAccount;

    private void ReplaceRoomSettings()
    {
        if (_roomSettings != null) _roomSettings.PropertyChanged -= RoomSettingChanged;
        _roomSettings = CoreSettings.Current;
        _roomSettings.PropertyChanged += RoomSettingChanged;
        if (IsReady)
        {
            _room?.PushSettings(BackRoomSettings("settings"));
            _room?.OnSpChanged(CoreSettings.Current.SkillPoints, "sync");
        }
    }

    // WPF BackRoomHostService.cs:689-720: external balance and settings changes reach an open room.
    private void RoomSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!SameRoomAccount()) { _room?.RequestClose("account-change"); return; }
        if (!IsReady) return;
        if (e.PropertyName == nameof(AppSettings.SkillPoints))
            _room?.OnSpChanged(CoreSettings.Current.SkillPoints, "settings");
        else if (e.PropertyName?.StartsWith("BackRoom", StringComparison.Ordinal) == true ||
                 e.PropertyName == nameof(AppSettings.MotionLevel))
            _room?.PushSettings(BackRoomSettings("settings"));
    }

    private void DisposeBackRoom()
    {
        if (_roomService != null) _roomService.CurrentReplaced -= ReplaceRoomSettings;
        if (_roomSettings != null) _roomSettings.PropertyChanged -= RoomSettingChanged;
        _room?.CloseNow();
        _room = null;
    }

    private void HandleBackRoom(JObject message)
    {
        if ((string?)message["type"] == "ready") { IsReady = true; _room?.OnReady(); }
        else if ((string?)message["type"] == "boot-error") Close();
        else _room?.Handle(message);
    }

    // WPF BackRoomHostService.cs:590-648. No token or server URL is sent to the page.
    private object BackRoomInit()
    {
        var result = JObject.FromObject(BackRoomSettings("init"));
        result["protocol"] = BackRoomBridge.Protocol;
        result["sp"] = CoreSettings.Current.SkillPoints;
        result["lang"] = CoreSettings.Current.Language;
        result["welcomeSeen"] = CoreSettings.Current.BackRoomWelcomeSeen;
        result["lex"] = JObject.FromObject(LocalizationManager.Instance.KeysWithPrefix("br_")
            .ToDictionary(k => k, Loc.Get));
        result["stations"] = JArray.FromObject(BackRoomApi.Ops.Keys);
        result["open"] = null;
        result["breakoutStandalone"] = false;
        return result;
    }

    private static object BackRoomSettings(string type)
    {
        var s = CoreSettings.Current;
        return new
        {
            type, reduced = s.MotionLevel != MotionLevel.Full,
            motion = s.MotionLevel.ToString().ToLowerInvariant(),
            intensity = s.MotionLevel == MotionLevel.Full ? s.BackRoomFxIntensity.ToString().ToLowerInvariant() : "calm",
            intensityChoice = s.BackRoomFxIntensity.ToString().ToLowerInvariant(),
            invertLook = s.BackRoomInvertLook,
            gates = new { flash = true, subliminal = true, spiral = true, brainDrain = true, tunnel = s.BackRoomTunnel, melt = s.BackRoomMelt },
            audio = new { sub = s.BackRoomSubVolume / 100.0, sfx = s.BackRoomSfxVolume / 100.0, music = s.BackRoomMusicVolume / 100.0 },
        };
    }

    // WPF BackRoomHostService.cs:60-87. Remaining media options belong with the real media feed.
    private void ApplyBackRoomOption(BackRoomBridge.RoomOption option)
    {
        if (!SameRoomAccount()) return;
        var s = CoreSettings.Current;
        switch (option.Key)
        {
            case BackRoomBridge.OptionTunnel: s.BackRoomTunnel = option.On; break;
            case BackRoomBridge.OptionMelt: s.BackRoomMelt = option.On; break;
            case BackRoomBridge.OptionInvertLook: s.BackRoomInvertLook = option.On; break;
            case BackRoomBridge.OptionWelcomeSeen: s.BackRoomWelcomeSeen = option.On; break;
            case BackRoomBridge.OptionIntensity when option.Intensity is { } i: s.BackRoomFxIntensity = i; break;
            case BackRoomBridge.OptionSubVolume when option.Level is { } sub: s.BackRoomSubVolume = sub; break;
            case BackRoomBridge.OptionSfxVolume when option.Level is { } sfx: s.BackRoomSfxVolume = sfx; break;
            case BackRoomBridge.OptionMusicVolume when option.Level is { } music: s.BackRoomMusicVolume = music; break;
            default: return;
        }
        CoreSettings.Save();
    }

    // WPF BackRoomMedia.cs:60-61,296-316: bundled fallback art, mapped through this head's asset server.
    private sealed class BundledRoomMedia(Func<WebAssetServer> server) : IBackRoomMedia
    {
        public BackRoomMediaDeal Deal(string station, int seed, int count = 4)
        {
            var deal = new NullBackRoomMedia(Loc.Get).Deal(station, seed, count);
            return deal with { Gifs = deal.Gifs.Select(g => g with
            {
                Url = server().Url(new Uri(g.Url).AbsolutePath.TrimStart('/')),
            }).ToArray() };
        }
    }

    private sealed class OfflineRoomRelay : IBackRoomRelay
    {
        public Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem, JObject? body,
            System.Threading.CancellationToken ct = default) => Task.FromResult(BackRoomStationResult.Refuse("offline"));
    }
}

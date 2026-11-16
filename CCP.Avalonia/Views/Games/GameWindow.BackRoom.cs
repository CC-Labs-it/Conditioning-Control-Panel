using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private BackRoomBridge? _roomBridge;

    // WPF BackRoomHostService.cs:26, 399-452: one room, free to open, with the account pinned per bridge.
    static partial void RegisterBackRoom(Dictionary<string, Game> games) =>
        games["backroom"] = new("backroom", "launcher_game_backroom_title", "backroom/index.html");

    private BackRoomBridge RoomBridge()
    {
        if (_roomBridge != null) return _roomBridge;
        var account = BackRoomApi.AppIdentity()?.UnifiedId;
        (string UnifiedId, string Token)? Identity()
        {
            var current = BackRoomApi.AppIdentity();
            return current?.UnifiedId == account ? current : null;
        }
        BackRoomBridge? bridge = null;
        var api = new BackRoomApi(null, Identity, sp => bridge?.AdoptSp(sp, () => Identity() != null));
        return _roomBridge = bridge = new BackRoomBridge(new BackRoomBridge.Deps
        {
            Post = msg => Dispatcher.UIThread.Post(() => { if (!_closing) Post(msg); }),
            Relay = api,
            BuildInit = RoomInit,
            CloseWindow = () => { if (!_closing) Close(); },
            Schedule = (delay, action) => { var timer = DispatcherTimer.RunOnce(action, delay); return timer.Dispose; },
            SetSp = sp => CoreSettings.Current.SkillPoints = sp,
            OnUi = action => Dispatcher.UIThread.Post(action),
            OffUi = action => Task.Run(action),
            SetOption = RoomOption,
        });
    }

    private object RoomInit()
    {
        var s = CoreSettings.Current;
        var motion = s.MotionLevel;
        var loc = LocalizationManager.Instance;
        return new
        {
            type = "init", protocol = BackRoomBridge.Protocol, lang = s.Language,
            sp = s.SkillPoints, welcomeSeen = s.BackRoomWelcomeSeen, invertLook = s.BackRoomInvertLook,
            motion = motion.ToString().ToLowerInvariant(), reduced = motion != Models.MotionLevel.Full,
            intensity = BackRoomWire.IntensityWire(s, motion), intensityChoice = BackRoomWire.IntensityChoiceWire(s),
            gates = BackRoomWire.GatesWire(s), media = BackRoomWire.MediaWire(s),
            audio = new { sub = s.BackRoomSubVolume / 100.0, sfx = s.BackRoomSfxVolume / 100.0, music = s.BackRoomMusicVolume / 100.0 },
            lex = loc.KeysWithPrefix("br_").Distinct(StringComparer.Ordinal).ToDictionary(k => k, loc.Get),
            stations = BackRoomApi.Ops.Keys.ToArray(), open = (bool?)null,
        };
    }

    private static void RoomOption(BackRoomBridge.RoomOption o)
    {
        var s = CoreSettings.Current;
        switch (o.Key)
        {
            case BackRoomBridge.OptionWelcomeSeen: s.BackRoomWelcomeSeen = o.On; break;
            case BackRoomBridge.OptionTunnel: s.BackRoomTunnel = o.On; break;
            case BackRoomBridge.OptionMelt: s.BackRoomMelt = o.On; break;
            case BackRoomBridge.OptionInvertLook: s.BackRoomInvertLook = o.On; break;
            case BackRoomBridge.OptionIntensity when o.Intensity is { } i: s.BackRoomFxIntensity = i; break;
            case BackRoomBridge.OptionMediaSource when o.Text is { } v: s.BackRoomMediaSource = v; break;
            case BackRoomBridge.OptionSubVolume when o.Level is { } v: s.BackRoomSubVolume = v; break;
            case BackRoomBridge.OptionSfxVolume when o.Level is { } v: s.BackRoomSfxVolume = v; break;
            case BackRoomBridge.OptionMusicVolume when o.Level is { } v: s.BackRoomMusicVolume = v; break;
            default: return;
        }
        CoreSettings.Service?.Save();
    }

    partial void HandleBackRoomMessage(JObject message, ref bool handled)
    {
        handled = true;
        var bridge = RoomBridge();
        switch ((string?)message["type"])
        {
            case "ready": IsReady = true; bridge.OnReady(); break;
            case "boot-error": Close(); break;
            default: bridge.Handle(message); break;
        }
    }
}

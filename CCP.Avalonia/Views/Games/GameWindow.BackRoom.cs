using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF BackRoomHostService.cs:26,306: the free account room.
    static partial void RegisterBackRoom(Dictionary<string, Game> games) =>
        games["backroom"] = new("backroom", "launcher_game_backroom_title", "backroom/index.html");

    private BackRoomBridge? _roomBridge;
    private bool _roomPanic;
    private DateTime _roomPanicAt;

    private BackRoomBridge RoomBridge()
    {
        if (_roomBridge != null) return _roomBridge;
        var account = FriendsHead.Identity()?.UnifiedId;
        (string UnifiedId, string Token)? Identity()
        {
            var current = FriendsHead.Identity();
            return account != null && current?.UnifiedId == account ? current : null;
        }
        var url = FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
            Environment.GetEnvironmentVariable(V2PurchaseHead.EnvVar));
        var api = new BackRoomApi(null, () => url == null ? null : Identity(),
            sp => _roomBridge?.AdoptSp(sp, () => Identity() != null), baseUrl: url);
        return _roomBridge = new BackRoomBridge(new BackRoomBridge.Deps
        {
            Post = o => Dispatcher.UIThread.Post(() => { if (!_closing) Post(o); }),
            Relay = api,
            BuildInit = RoomInit,
            CloseWindow = () => Dispatcher.UIThread.Post(Close),
            Schedule = (delay, action) =>
            {
                var timer = new DispatcherTimer { Interval = delay };
                timer.Tick += (_, _) => { timer.Stop(); action(); };
                timer.Start();
                return timer.Stop;
            },
            OnUi = a => Dispatcher.UIThread.Post(a),
            OffUi = a => System.Threading.Tasks.Task.Run(a),
            SetSp = sp => { if (Identity() != null) { CoreSettings.Current.SkillPoints = sp; CoreSettings.Save(); } },
        });
    }

    internal object RoomInit()
    {
        var s = CoreSettings.Current;
        var motion = s.MotionLevel;
        var loc = LocalizationManager.Instance;
        return new { type = "init", protocol = BackRoomBridge.Protocol, breakoutStandalone = false,
            sp = s.SkillPoints, reduced = motion != Models.MotionLevel.Full, motion = motion.ToString().ToLowerInvariant(),
            invertLook = s.BackRoomInvertLook, welcomeSeen = s.BackRoomWelcomeSeen,
            intensity = BackRoomWire.IntensityWire(s, motion), intensityChoice = BackRoomWire.IntensityChoiceWire(s),
            gates = BackRoomWire.GatesWire(s), media = BackRoomWire.MediaWire(s),
            audio = new { sub = s.BackRoomSubVolume / 100.0, sfx = s.BackRoomSfxVolume / 100.0, music = s.BackRoomMusicVolume / 100.0 },
            lang = s.Language, lex = loc.KeysWithPrefix("br_").Distinct(StringComparer.Ordinal).ToDictionary(k => k, loc.Get),
            stations = BackRoomApi.Ops.Keys.ToArray(), open = (bool?)null };
    }

    partial void HandleBackRoomMessage(JObject message, ref bool handled)
    {
        handled = true;
        if ((string?)message["type"] == "ready") { IsReady = true; RoomBridge().OnReady(); }
        else RoomBridge().Handle(message);
    }

    // WPF BackRoomHostService.cs:610: first press suspends, second within two seconds closes.
    internal static bool HandleBackRoomPanic(DateTime now)
    {
        GameWindow? w;
        lock (Open) w = Open.FirstOrDefault(w => w.Spec.Id == "backroom");
        if (w == null) return false;
        if (w._roomPanic && now - w._roomPanicAt <= TimeSpan.FromSeconds(2)) w.RoomBridge().RequestClose("panic");
        else { w._roomPanic = true; w._roomPanicAt = now; w.RoomBridge().Suspend(true, "panic"); }
        return true;
    }

    private void ResumeBackRoomAfterPanic(DateTime now)
    {
        if (!_roomPanic || now - _roomPanicAt <= TimeSpan.FromSeconds(2)) return;
        _roomPanic = false;
        RoomBridge().Suspend(false, "panic");
    }
}

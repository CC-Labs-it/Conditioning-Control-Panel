using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Core.Services.PrizeCounter;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal const string PrizeCounterId = "prizecounter";
    private string? _counterAccount;
    private PrizeCounterRelay? _counterRelay;

    // WPF PrizeCounterHostService.cs:339 pins the opener for the life of this window.
    private void InitPrizeCounter()
    {
        _counterAccount = FriendsHead.Identity()?.UnifiedId;
        _counterRelay = new PrizeCounterRelay(null,
            () => FriendsHead.Identity() is { } id && id.UnifiedId == _counterAccount ? id : null,
            () => CoreSettings.Current.OfflineMode || !CounterNetworkAllowed(),
            (account, buy, sp) => Dispatcher.UIThread.Post(() => V2PurchaseHead.AdoptSp(account, buy, sp)),
            (account, revision, grants) => Dispatcher.UIThread.Post(() => PrizeOwnership.ApplySnapshot(account, revision, grants)));
    }

    // The test profile must never reach production (FriendsHead's existing sandbox rule).
    internal static bool CounterNetworkAllowed()
    {
        var allowed = FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
            Environment.GetEnvironmentVariable(V2PurchaseHead.EnvVar));
        return allowed != null && string.Equals(allowed.TrimEnd('/'), BackRoomApi.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private void HandlePrizeCounter(JObject frame)
    {
        switch (frame["type"]?.Type == JTokenType.String ? (string?)frame["type"] : null)
        {
            case "ready":
                if (IsReady) return;
                IsReady = true;
                var s = CoreSettings.Current;
                var id = FriendsHead.Identity();
                Post(new
                {
                    type = "init", protocol = 1,
                    identity = id is { } actual && actual.UnifiedId == _counterAccount ? new { unifiedId = actual.UnifiedId } : null,
                    sp = s.SkillPoints, locale = LocalizationManager.Instance.CurrentLanguage,
                    lexicon = LocalizationManager.Instance.KeysWithPrefix("shop_").Distinct(StringComparer.Ordinal)
                        .ToDictionary(k => k, k => Loc.Get(k), StringComparer.Ordinal),
                    motion = s.MotionLevel == MotionLevel.Off ? "off" : s.MotionLevel == MotionLevel.Reduced || s.GentleVisuals ? "less" : "full",
                    gentle = s.GentleVisuals, reducedMotion = !UiMotionPreference.AnimationsEnabled,
                    from = "launcher", platform = "desktop", wears = Array.Empty<string>(), rainV2 = false,
                });
                break;
            case "request":
                var reqId = frame["reqId"]?.Type == JTokenType.String ? (string?)frame["reqId"] : null;
                if (string.IsNullOrWhiteSpace(reqId) || _counterRelay == null) return;
                var relay = _counterRelay;
                var op = frame["op"]?.Type == JTokenType.String ? (string?)frame["op"] : null;
                var body = frame["body"] as JObject;
                var idem = frame["idem"]?.Type == JTokenType.String ? (string?)frame["idem"] : null;
                _ = Task.Run(async () =>
                {
                    var reply = await relay.RelayAsync(op, body, idem);
                    Dispatcher.UIThread.Post(() => { if (!_closing) Post(new { type = "reply", reqId, body = reply }); });
                });
                break;
            case "wallet":
                if (frame["sp"] is JValue { Type: JTokenType.Integer } sp) _counterRelay?.HandleWalletFrame(sp.Value<int>());
                break;
            case "close":
                Close();
                break;
        }
    }
}

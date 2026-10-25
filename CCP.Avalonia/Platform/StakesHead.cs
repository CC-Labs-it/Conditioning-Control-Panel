using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Platform;

// Adapted from WPF StakeAppHooks: one settlement book for the app session, even after a window closes.
internal static class StakesHead
{
    internal static readonly StakeSettlement Settlement = new(
        (_, seconds) => ChasterHead.Service?.NoteSeconds("pvp_loss", seconds).AppliedSeconds ?? 0,
        (account, receipt, sp) => Dispatcher.UIThread.Post(() =>
        {
            var s = CoreSettings.Current;
            if (!V2WalletAdoption.Decide(FriendsHead.Identity()?.UnifiedId == account,
                    receipt, sp, s.SkillPoints, out var next)) return;
            s.SkillPoints = next;
            CoreSettings.Save();
        }), () => FriendsHead.Identity()?.UnifiedId);

    internal static bool TimeOk()
    {
        var s = CoreSettings.Current;
        return ChasterHead.Service is { } chaster && StakeRules.TimeAllowed(chaster.IsLinked,
            s.ChasterTabEnabled, s.ChasterPaused, chaster.SafetyHoldRemaining);
    }

    internal static void EnableLossRow()
    {
        void Apply()
        {
            var s = CoreSettings.Current;
            var rows = s.ChasterPrices ?? new List<string>();
            if (rows.Contains(StakeRules.LossRowId)) return;
            s.ChasterPrices = new List<string>(rows) { StakeRules.LossRowId };
            CoreSettings.Save();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Invoke(Apply);
    }

    internal static StakeBridge CreatePbp(Action<JObject> post) => new("pbp", post,
        new StakeApi(GameHttp(), FriendsHead.Identity, GameBase() ?? ""), Settlement, TimeOk, EnableLossRow);

    // Reuse the board's sandbox-aware transport and proxy selection; no second network door.
    private static System.Net.Http.HttpClient GameHttp() => Views.Games.GameWindow.PbpHttpClient();
    private static string? GameBase() => Views.Games.GameWindow.PbpServerBase();
}

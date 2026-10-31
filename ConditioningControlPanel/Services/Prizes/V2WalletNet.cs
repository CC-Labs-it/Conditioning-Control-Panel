using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// WHAT A <c>/v2/user/sync</c> REPLY DOES TO THE WALLET (owner call 2026-10-31: a spend on one
/// platform corrects the wallet on the others).
///
/// <para>A sync's <c>skill_points</c> used to be raise-only, because the client credits level-up and
/// bubble points before any sync pushes them. So a buy on the website or the phone left this PC
/// showing the old, higher number until its own next buy. Replies now carry <c>wallet_net</c>: the
/// lifetime SP moved by every server path that can lower a balance (the counter, the Back Room,
/// stakes; Sparkles-by-place credits are not in it). A net below the last one this PC saw means
/// something was spent, and the reply's balance is adopted outright, down included. That is safe
/// here and nowhere else: the server has just credited this device's level and bubble deltas from
/// this same sync. A credit earned while the reply was in flight comes back on the next sync.</para>
///
/// <para>A device's own buy adopts its receipt (<see cref="V2WalletAdoption"/>); the following
/// sync's drop adopts the same number, so nothing is taken twice.</para>
///
/// <para>Pure. The caller holds the per-account stored net and does the writes.</para>
/// </summary>
public static class V2WalletNet
{
    /// <summary>What to do with a sync reply.</summary>
    /// <param name="NextSp">The wallet after this reply (equal to the local one when nothing moves).</param>
    /// <param name="StoreNet">The net to keep for this account. Null = nothing stored yet and nothing to store.</param>
    /// <param name="Spent">The net dropped, so <paramref name="NextSp"/> is the reply's balance whichever way it moved.</param>
    public readonly record struct Step(int NextSp, int? StoreNet, bool Spent);

    /// <param name="sameAccount">The account the sync was sent for is still the one signed in.</param>
    /// <param name="replyNet">The reply's <c>wallet_net</c>. Null on an older server.</param>
    /// <param name="storedNet">The last net this PC stored for that account. Null = never.</param>
    /// <param name="serverSp">The reply's <c>skill_points</c>.</param>
    /// <param name="localSp">The balance the wallet holds now.</param>
    public static Step Decide(bool sameAccount, int? replyNet, int? storedNet, int serverSp, int localSp)
    {
        // Neither the balance nor the net may land: the stored net stays, so a good reply still
        // sees the drop. A wrong-account reply must not write anything at all.
        if (!sameAccount || serverSp < 0) return new Step(localSp, storedNet, false);
        if (replyNet is { } net && storedNet is { } seen && net < seen)
            return new Step(SparklePoints.Clamp(serverSp), net, true);
        // Missing net, first sighting, or no drop: today's raise-only merge.
        return new Step(SparklePoints.MergeMax(serverSp, localSp), replyNet ?? storedNet, false);
    }

    /// <summary>
    /// Reads <c>wallet_net</c> off the raw reply. Anything but a whole number that fits counts as
    /// missing: one odd field must not throw away the rest of the sync reply.
    /// </summary>
    public static int? ParseNet(JToken? token)
    {
        if (token == null) return null;
        switch (token.Type)
        {
            case JTokenType.Integer:
                return token.Value<long>() is >= int.MinValue and <= int.MaxValue and var whole ? (int)whole : null;
            case JTokenType.Float:
                var d = token.Value<double>();
                return d == System.Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue ? (int)d : null;
            default:
                return null;
        }
    }
}

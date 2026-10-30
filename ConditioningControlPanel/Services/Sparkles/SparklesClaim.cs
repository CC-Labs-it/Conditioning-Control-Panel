using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Sparkles;

/// <summary>How a game ended, in the one shape the claim wire takes (B1 wire contract, section 5).</summary>
public sealed record SparklesResult(string Kind, int Place = 0, int Of = 0, string? Outcome = null, string? Medal = null)
{
    /// <summary>A finishing place, 1-based, out of <paramref name="of"/> racers or players.</summary>
    public static SparklesResult ForPlace(int place, int of) => new("place", Place: place, Of: of);

    /// <summary>A head-to-head result: <c>win</c>, <c>draw</c> or <c>loss</c>.</summary>
    public static SparklesResult ForDuel(string outcome) => new("duel", Outcome: outcome);

    /// <summary>A graded result: <c>gold</c>, <c>silver</c>, <c>bronze</c> or <c>none</c>.</summary>
    public static SparklesResult ForMedal(string medal) => new("medal", Medal: medal);

    public JObject ToJson() => Kind switch
    {
        "place" => new JObject { ["kind"] = "place", ["place"] = Place, ["of"] = Of },
        "duel" => new JObject { ["kind"] = "duel", ["outcome"] = Outcome },
        "medal" => new JObject { ["kind"] = "medal", ["medal"] = Medal },
        _ => new JObject { ["kind"] = Kind },
    };
}

/// <summary>
/// One finished game, as the host posts it to <c>POST /v2/sparkles/claim</c>. The server prices
/// it; nothing here decides what it is worth. <see cref="ClaimId"/> is the idempotency key: the
/// same id always answers the same receipt, so a retry never pays twice.
/// </summary>
public sealed record SparklesClaim(
    string ClaimId,
    string Game,
    string Mode,
    SparklesResult Result,
    long DurationMs,
    JObject? Detail,
    JObject? Proof,
    long EndedAt)
{
    private static readonly Regex ClaimIdShape = new("^[A-Za-z0-9_-]{16,64}$", RegexOptions.CultureInvariant);

    public static bool IsGame(string? g) =>
        g is "rt3d" or "chess" or "goon" or "arcademy" or "breakout" or "dtrh" or "caucus" or "intake";

    public static bool IsMode(string? m) => m is "solo" or "trial" or "ghost" or "online" or "practice";

    public static bool IsClaimId(string? id) => id != null && ClaimIdShape.IsMatch(id);

    /// <summary>A fresh idempotency key (32 hex characters).</summary>
    public static string NewClaimId() => Guid.NewGuid().ToString("N");

    /// <summary>A claim that ended now by the SERVER's clock (<see cref="ServerClock"/>).</summary>
    public static SparklesClaim Create(string game, string mode, SparklesResult result, long durationMs,
        JObject? detail = null, JObject? proof = null, string? claimId = null) =>
        new(claimId ?? NewClaimId(), game, mode, result, Math.Max(0, durationMs), detail, proof,
            ServerClock.UtcNow.ToUnixTimeMilliseconds());

    /// <summary>Shaped well enough to send. The server checks everything again.</summary>
    public bool IsWellFormed =>
        IsClaimId(ClaimId) && IsGame(Game) && IsMode(Mode) && Result.Kind is "place" or "duel" or "medal";

    /// <summary>The request body, camelCase names exactly as the wire contract spells them.</summary>
    public JObject ToJson(string unifiedId)
    {
        var o = new JObject
        {
            ["unified_id"] = unifiedId,
            ["claimId"] = ClaimId,
            ["game"] = Game,
            ["mode"] = Mode,
            ["result"] = Result.ToJson(),
            ["durationMs"] = DurationMs,
            ["detail"] = Detail?.DeepClone() ?? new JObject(),
            ["endedAt"] = EndedAt,
        };
        if (Proof != null) o["proof"] = Proof.DeepClone();
        return o;
    }
}

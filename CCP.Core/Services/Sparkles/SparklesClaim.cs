using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.Sparkles.Claims;
public sealed record SparklesResult(string Kind, int Place = 0, int Of = 0, string? Outcome = null, string? Medal = null)
{
    public static SparklesResult ForPlace(int place, int of) => new("place", Place: place, Of: of);
    public static SparklesResult ForDuel(string outcome) => new("duel", Outcome: outcome);
    public static SparklesResult ForMedal(string medal) => new("medal", Medal: medal);
    public JObject ToJson() => Kind switch
    {
        "place" => new JObject { ["kind"] = "place", ["place"] = Place, ["of"] = Of },
        "duel" => new JObject { ["kind"] = "duel", ["outcome"] = Outcome },
        "medal" => new JObject { ["kind"] = "medal", ["medal"] = Medal },
        _ => new JObject { ["kind"] = Kind },
    };
}
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
    public static string NewClaimId() => Guid.NewGuid().ToString("N");
    public static SparklesClaim Create(string game, string mode, SparklesResult result, long durationMs,
        JObject? detail = null, JObject? proof = null, string? claimId = null) =>
        new(claimId ?? NewClaimId(), game, mode, result, Math.Max(0, durationMs), detail, proof,
            ServerClock.UtcNow.ToUnixTimeMilliseconds());
    public bool IsWellFormed =>
        IsClaimId(ClaimId) && IsGame(Game) && IsMode(Mode) && Result.Kind is "place" or "duel" or "medal";
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

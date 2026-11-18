using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>Shared bits for the Dares UI suites: the real English strings, a bet maker, the repo root.</summary>
internal static class DareUiTestKit
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("repo root not found from " + AppContext.BaseDirectory);
        return dir.FullName;
    }

    public static string LanguagesDir => Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");

    public static Dictionary<string, string> Strings(string lang)
    {
        var o = JObject.Parse(File.ReadAllText(Path.Combine(LanguagesDir, lang + ".json"), System.Text.Encoding.UTF8));
        return o.Properties().ToDictionary(p => p.Name, p => (string?)p.Value ?? "");
    }

    private static Dictionary<string, string>? _en;

    /// <summary>Loc.Get against en.json, without the app's localization manager.</summary>
    public static string En(string key)
    {
        _en ??= Strings("en");
        return _en.TryGetValue(key, out var v) ? v : key;
    }

    public static readonly DareFriend Moth = new("u-moth", "Moth", null);

    public static DareBet Bet(
        string id = "b1",
        DareSide me = DareSide.From,
        DareType type = DareType.Cant,
        DareWhat? what = null,
        DareState state = DareState.Running,
        DareStake? from = null,
        DareStake? to = null,
        DareSide? winner = null,
        DareDebt? debt = null,
        int fromCount = 0,
        int toCount = 0,
        bool camera = false,
        DateTimeOffset? at = null,
        DateTimeOffset? endsAt = null,
        DateTimeOffset? settledAt = null)
        => new(id, me, Moth, type, what ?? new DareWhat(DareKind.Pics, "500"), DareDeadline.Today,
            endsAt ?? DateTimeOffset.UtcNow.AddHours(2), camera, from ?? new DareStake(DareStakeKind.Sp, 100),
            to ?? new DareStake(DareStakeKind.Sp, 50), Array.Empty<DareSeal>(), state, winner, fromCount, toCount,
            null, null, debt, at ?? DateTimeOffset.UtcNow.AddHours(-1), null, settledAt, null);

    public static DaresSnapshot Snap(params DareBet[] bets)
        => new(true, new DaresMe(null, false, true, 0), bets);
}

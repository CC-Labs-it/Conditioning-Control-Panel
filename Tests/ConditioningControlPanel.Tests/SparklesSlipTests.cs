using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Sparkles;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>B1 W5: what the results slip says for each receipt, in the host's words, and that a
/// shadow receipt (the rollout day) sends nothing at all.</summary>
public class SparklesSlipTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static string LangPath(string lang)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", "Localization", "Languages", lang + ".json");
    }

    private static Dictionary<string, string> Strings(string lang) =>
        JObject.Parse(File.ReadAllText(LangPath(lang), Encoding.UTF8)).Properties()
            .Where(p => p.Value.Type == JTokenType.String).ToDictionary(p => p.Name, p => (string)p.Value!);

    private static readonly Lazy<Dictionary<string, string>> En = new(() => Strings("en"));
    private static string Loc(string key) => En.Value.TryGetValue(key, out var v) ? v : key;

    private static SparklesClaimOutcome Receipt(string json) => new(SparklesReceipt.Parse(JObject.Parse(json)), null);

    private static List<string> LinesOf(JObject? msg) => msg!["lines"]!.Values<string>().Select(s => s!).ToList();

    [Fact]
    public void A_solo_win_names_the_place_and_carries_the_stamp()
    {
        var msg = SparklesSlip.Build(Receipt(
            "{\"ok\":true,\"receipt\":{\"kind\":\"place\",\"place\":1,\"of\":8,\"paid\":20,\"credited\":20,\"online\":1,\"taper\":1,\"sp\":4310}}"),
            gentle: false, Loc);
        Assert.Equal("sparkles-receipt", (string?)msg!["type"]);
        Assert.Equal(new[] { "Place 1 of 8" }, LinesOf(msg));
        Assert.Equal(20, (int)msg["credited"]!);
        Assert.Equal(4310, (int)msg["sp"]!);
        Assert.Equal("Paid", (string?)msg["stamp"]);
        Assert.False((bool)msg["shadow"]!);
        Assert.Null(msg["medal"]);
    }

    [Theory]
    [InlineData(4400, 4400)]   // unsynced local credit: the chip shows the app's wallet
    [InlineData(4000, 4310)]   // never lower than the server's balance
    public void The_chip_counts_to_the_higher_wallet(int local, int shown)
    {
        var msg = SparklesSlip.WithWallet(SparklesSlip.Build(Receipt(
            "{\"ok\":true,\"receipt\":{\"kind\":\"place\",\"place\":1,\"of\":8,\"paid\":20,\"credited\":20,\"online\":1,\"taper\":1,\"sp\":4310}}"),
            gentle: false, Loc), local);
        Assert.Equal(shown, (int)msg!["sp"]!);
    }

    [Theory]
    [InlineData("{\"place\":2,\"of\":8,\"paid\":80,\"credited\":80,\"online\":5}", "Place 2 of 8|Online x5")]
    [InlineData("{\"result\":{\"kind\":\"medal\",\"medal\":\"gold\"},\"paid\":8,\"credited\":8,\"taper\":0.5}", "Gold|Half rate after 5 games")]
    [InlineData("{\"result\":{\"kind\":\"medal\",\"medal\":\"none\"},\"paid\":2,\"credited\":2,\"fee\":true}", "Finished|Fee only today for this game")]
    [InlineData("{\"result\":{\"kind\":\"duel\",\"outcome\":\"draw\"},\"paid\":2,\"credited\":2,\"fee\":true,\"reason\":\"ceiling\",\"resetInMs\":15120000}", "Draw|Ceiling reached. Full rate again in 5 h")]
    [InlineData("{\"place\":3,\"of\":4,\"paid\":0,\"credited\":0,\"reason\":\"short\"}", "Place 3 of 4|Too short to pay")]
    [InlineData("{\"place\":1,\"of\":8,\"paid\":20,\"credited\":20,\"reason\":\"pair\"}", "Place 1 of 8|Same pair again today. Solo rate")]
    [InlineData("{\"place\":1,\"of\":8,\"paid\":20,\"credited\":20,\"reason\":\"young\"}", "Place 1 of 8|New account. Solo rate online for now")]
    [InlineData("{\"place\":1,\"of\":8,\"paid\":20,\"credited\":20,\"reason\":\"solo_room\"}", "Place 1 of 8|Fewer than 2 players. Solo rate")]
    [InlineData("{\"result\":{\"kind\":\"duel\",\"outcome\":\"win\"},\"paid\":0,\"credited\":0,\"reason\":\"disputed\"}", "Won|Results disagreed, no Sparkles")]
    [InlineData("{\"place\":1,\"of\":8,\"paid\":0,\"credited\":0,\"reason\":\"ceiling\"}", "Place 1 of 8|Ceiling reached. Fee only until tomorrow")]
    public void Each_receipt_reads_as_its_lines(string receipt, string lines)
    {
        var msg = SparklesSlip.Build(Receipt(receipt), gentle: false, Loc);
        Assert.Equal(lines.Split('|'), LinesOf(msg));
    }

    [Fact]
    public void A_medal_receipt_names_its_medal_for_the_page()
    {
        var msg = SparklesSlip.Build(Receipt("{\"result\":{\"kind\":\"medal\",\"medal\":\"silver\"},\"paid\":9,\"credited\":9}"), true, Loc);
        Assert.Equal("silver", (string?)msg!["medal"]);
        Assert.True((bool)msg["gentle"]!);
    }

    [Fact]
    public void A_shadow_receipt_sends_nothing()
    {
        Assert.Null(SparklesSlip.Build(Receipt("{\"place\":1,\"of\":8,\"paid\":20,\"credited\":0,\"shadow\":true}"), false, Loc));
    }

    [Theory]
    [InlineData("offline", "Offline, no Sparkles")]
    [InlineData("unreachable", "Bank closed, no Sparkles this time")]
    [InlineData("signin", "Sign in to earn Sparkles")]
    [InlineData("short", "Too short to pay")]
    [InlineData("pending", null)]
    [InlineData("busy", null)]
    [InlineData("invalid", null)]
    [InlineData("off", null)]
    public void A_refusal_shows_only_a_line_the_player_can_use(string reason, string? line)
    {
        var msg = SparklesSlip.Build(SparklesClaimOutcome.Refused(reason), false, Loc);
        if (line == null) { Assert.Null(msg); return; }
        Assert.Equal(new[] { line }, LinesOf(msg));
        Assert.Equal(0, (int)msg!["credited"]!);
        Assert.Null(SparklesSlip.Build(null, false, Loc));
    }

    [Fact]
    public async Task Deliver_posts_once_and_never_throws()
    {
        var posted = new List<string>();
        await SparklesSlip.DeliverCore(Task.FromResult<SparklesClaimOutcome?>(SparklesClaimOutcome.Refused("offline")),
            posted.Add, null, o => SparklesSlip.Build(o, false, Loc));
        Assert.Single(posted);
        Assert.Contains("\"sparkles-receipt\"", posted[0]);

        await SparklesSlip.DeliverCore(Task.FromResult<SparklesClaimOutcome?>(Receipt("{\"credited\":0,\"shadow\":true}")),
            posted.Add, null, o => SparklesSlip.Build(o, false, Loc));
        await SparklesSlip.DeliverCore(Task.FromException<SparklesClaimOutcome?>(new InvalidOperationException()),
            posted.Add, null, o => SparklesSlip.Build(o, false, Loc));
        await SparklesSlip.DeliverCore(Task.FromResult<SparklesClaimOutcome?>(SparklesClaimOutcome.Refused("offline")),
            _ => throw new InvalidOperationException("host gone"), null, o => SparklesSlip.Build(o, false, Loc));
        Assert.Single(posted);
    }

    [Fact]
    public void Every_slip_key_is_in_all_ten_languages_in_the_house_voice()
    {
        var keys = En.Value.Keys.Where(k => k.StartsWith("Sparkles_Slip_", StringComparison.Ordinal)).ToList();
        Assert.True(keys.Count >= 22, "slip keys missing from en.json");
        var bad = new List<string>();
        foreach (var lang in Languages)
        {
            var s = Strings(lang);
            foreach (var k in keys)
            {
                if (!s.TryGetValue(k, out var v)) { bad.Add($"{lang} {k}: missing"); continue; }
                if (v.IndexOfAny(new[] { '!', '—', '–' }) >= 0) bad.Add($"{lang} {k}: {v}");
                var want = Regex.Matches(En.Value[k], @"\{\d\}").Select(m => m.Value).OrderBy(x => x);
                var got = Regex.Matches(v, @"\{\d\}").Select(m => m.Value).OrderBy(x => x);
                if (!want.SequenceEqual(got)) bad.Add($"{lang} {k}: placeholders {v}");
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}

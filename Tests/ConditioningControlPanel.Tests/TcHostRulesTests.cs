using System;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Terms &amp; Conditioning desktop host, pure half: the edition rule, the caps this host
/// owns, the subscriber and medal reads, and the page frame gate (Contract v2 + CONTRACT.md v1).</summary>
public class TcHostRulesTests
{
    // ---- edition ----

    [Theory]
    [InlineData(0, "small")]
    [InlineData(-1, "small")]
    [InlineData(1, "full")]
    [InlineData(2, "full")]
    public void Any_subscriber_gets_the_full_game(int tier, string expected)
        => Assert.Equal(expected, TcRules.EditionFor(tier));

    [Fact]
    public void The_basic_bar_read_picks_the_same()
    {
        Assert.Equal("full", TcRules.EditionFor(true));
        Assert.Equal("small", TcRules.EditionFor(false));
    }

    [Fact]
    public void Caps_carry_edition_platform_and_save_slot()
    {
        var caps = TcRules.Caps("small", saveSlot: true);
        Assert.Equal("small", (string?)caps["edition"]);
        Assert.Equal("desktop", (string?)caps["platform"]);
        Assert.True((bool)caps["saveSlot"]!);
        Assert.Null(caps["expo"]);
    }

    [Fact]
    public void An_unknown_edition_reads_as_small_and_small_never_gets_the_expo()
    {
        var caps = TcRules.Caps("gold", saveSlot: false, expoCap: new JObject { ["state"] = "ready" });
        Assert.Equal("small", (string?)caps["edition"]);
        Assert.Null(caps["expo"]);
    }

    [Fact]
    public void Full_caps_carry_the_expo_cap_they_are_given()
    {
        var caps = TcRules.Caps("full", true, new JObject { ["state"] = "absent", ["mb"] = 250 });
        Assert.Equal("full", (string?)caps["edition"]);
        Assert.Equal("absent", (string?)caps["expo"]!["state"]);
        Assert.Null(TcRules.Caps("full", true)["expo"]);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]   // invite week counts, like every Basic door (owner call owed)
    public void Subscriber_is_the_basic_bar(bool premium, bool inviteWeek, bool expected)
        => Assert.Equal(expected, TcRules.IsSubscriber(premium, inviteWeek));

    [Fact]
    public void An_ending_pays_the_fee_and_a_first_ending_gold()
    {
        Assert.Equal("none", TcRules.MedalFor(false));
        Assert.Equal("gold", TcRules.MedalFor(true));
    }

    // ---- frame gate ----

    private static TcGateResult Gate(string json) => TcFrameGate.Check(JToken.Parse(json));

    [Theory]
    [InlineData("{\"type\":\"ready\",\"protocol\":1}")]
    [InlineData("{\"type\":\"heartbeat\"}")]
    [InlineData("{\"type\":\"exit-done\"}")]
    [InlineData("{\"type\":\"save-ask\"}")]
    [InlineData("{\"type\":\"expo-fetch\"}")]
    [InlineData("{\"type\":\"expo-cancel\"}")]
    [InlineData("{\"type\":\"tab\",\"title\":null}")]
    public void Bare_contract_frames_pass(string json) => Assert.True(Gate(json).Accepted);

    [Theory]
    [InlineData("{\"type\":\"net\",\"path\":\"/v2/admin\"}")]
    [InlineData("{\"type\":\"EXPO-FETCH\"}")]
    [InlineData("{\"type\":\"run-ended\"}")]
    [InlineData("{\"kind\":\"ready\"}")]
    [InlineData("[\"ready\"]")]
    [InlineData("\"ready\"")]
    public void Anything_else_is_refused(string json) => Assert.False(Gate(json).Accepted);

    [Fact]
    public void Null_is_refused() => Assert.False(TcFrameGate.Check(null).Accepted);

    [Theory]
    [InlineData("ending", "upgrade")]
    [InlineData("llama8", "upgrade")]
    [InlineData("transmission", "download")]
    [InlineData("locked-tile", "account")]
    [InlineData("pause", "upgrade")]
    [InlineData("save-sleep", "upgrade")]
    public void Nudge_takes_the_fixed_ids(string where, string want)
    {
        var r = Gate($"{{\"type\":\"nudge\",\"where\":\"{where}\",\"want\":\"{want}\"}}");
        Assert.True(r.Accepted);
        Assert.Equal(where, r.Frame!.Where);
        Assert.Equal(want, r.Frame.Want);
    }

    [Theory]
    [InlineData("{\"type\":\"nudge\",\"where\":\"shop\",\"want\":\"upgrade\"}")]
    [InlineData("{\"type\":\"nudge\",\"where\":\"ending\",\"want\":\"https://evil.example\"}")]
    [InlineData("{\"type\":\"nudge\",\"where\":\"ending\"}")]
    [InlineData("{\"type\":\"nudge\",\"where\":1,\"want\":\"upgrade\"}")]
    public void Nudge_refuses_other_ids(string json) => Assert.False(Gate(json).Accepted);

    [Fact]
    public void Save_offer_passes_up_to_the_cap()
    {
        var save = new string('a', TcRules.MaxSaveChars);
        var r = TcFrameGate.Check(new JObject { ["type"] = "save-offer", ["save"] = save });
        Assert.True(r.Accepted);
        Assert.Equal(save, r.Frame!.Save);
    }

    [Fact]
    public void Save_offer_over_the_page_parser_cap_is_too_big()
    {
        // 400,001 one-byte characters: under the byte cap, over what the page can read back.
        var r = TcFrameGate.Check(new JObject { ["type"] = "save-offer", ["save"] = new string('a', TcRules.MaxSaveChars + 1) });
        Assert.False(r.Accepted);
        Assert.Equal(TcFrameGate.RefuseTooBig, r.Refusal);
    }

    [Fact]
    public void Save_offer_counts_bytes_not_characters()
    {
        // 256,001 two-byte characters = 512,002 bytes.
        var r = TcFrameGate.Check(new JObject { ["type"] = "save-offer", ["save"] = new string('é', 256_001) });
        Assert.Equal(TcFrameGate.RefuseTooBig, r.Refusal);
    }

    [Theory]
    [InlineData("{\"type\":\"save-offer\"}")]
    [InlineData("{\"type\":\"save-offer\",\"save\":\"\"}")]
    [InlineData("{\"type\":\"save-offer\",\"save\":{\"v\":2}}")]
    public void Save_offer_needs_a_string(string json) => Assert.Equal(TcFrameGate.RefuseField, Gate(json).Refusal);

    [Theory]
    [InlineData("https://cclabs.app/")]
    [InlineData("https://www.cclabs.app/download")]
    [InlineData("https://app.cclabs.app/tc?from=desktop")]
    [InlineData("https://discord.gg/YxVAMt4qaZ")]
    [InlineData("https://neal.fun/")]
    [InlineData("https://neal.fun/stimulation-clicker/")]
    public void Open_url_takes_the_contract_list(string url) => Assert.NotNull(TcFrameGate.OpenUrl(url));

    [Theory]
    [InlineData("http://cclabs.app/")]
    [InlineData("https://cclabs.app.evil.example/")]
    [InlineData("https://evilcclabs.app/")]
    [InlineData("https://tc.cclabs.app/")]
    [InlineData("https://user@cclabs.app/")]
    [InlineData("https://cclabs.app:8443/")]
    [InlineData("https://neal.fun/other")]
    [InlineData("https://discord.gg/other")]
    [InlineData("https://cclabs.app/\"onclick")]
    [InlineData("https://cclabs.app/a b")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void Open_url_refuses_the_rest(string? url) => Assert.Null(TcFrameGate.OpenUrl(url));

    [Fact]
    public void Open_url_frame_hands_back_the_checked_url()
    {
        var r = Gate("{\"type\":\"open-url\",\"url\":\"https://app.cclabs.app/tc\"}");
        Assert.True(r.Accepted);
        Assert.Equal("https://app.cclabs.app/tc", r.Frame!.Url);
        Assert.Equal(TcFrameGate.RefuseUrl, Gate("{\"type\":\"open-url\",\"url\":\"https://example.com/\"}").Refusal);
    }

    [Fact]
    public void Set_setting_is_only_the_consent_switch()
    {
        var r = Gate("{\"type\":\"set-setting\",\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}");
        Assert.True(r.Accepted);
        Assert.True(r.Frame!.Value);
        Assert.False(Gate("{\"type\":\"set-setting\",\"key\":\"panicKey\",\"value\":true}").Accepted);
        Assert.False(Gate("{\"type\":\"set-setting\",\"key\":\"media.remoteConsent\",\"value\":\"yes\"}").Accepted);
    }

    [Fact]
    public void Niches_keep_known_ids_and_at_most_two()
    {
        var r = Gate("{\"type\":\"scrolller-niches\",\"niches\":[\"bimbo\",\"nope\",\"bimbo\",\"drone\",\"goon\"]}");
        Assert.True(r.Accepted);
        Assert.Equal(new[] { "bimbo", "drone" }, r.Frame!.Niches);
        Assert.Empty(Gate("{\"type\":\"scrolller-niches\",\"niches\":[]}").Frame!.Niches!);
        Assert.False(Gate("{\"type\":\"scrolller-niches\",\"niches\":\"bimbo\"}").Accepted);
    }

    [Fact]
    public void Haptic_is_clamped()
    {
        var r = Gate("{\"type\":\"haptic\",\"level\":7,\"ms\":999999,\"tag\":\"lucky\"}");
        Assert.True(r.Accepted);
        Assert.Equal(1.0, r.Frame!.Level);
        Assert.Equal(TcFrameGate.MaxHapticMs, r.Frame.Ms);
        Assert.Equal("lucky", r.Frame.Tag);
        Assert.Equal(0.0, Gate("{\"type\":\"haptic\",\"level\":-1,\"ms\":-5}").Frame!.Level);
        Assert.False(Gate("{\"type\":\"haptic\",\"level\":\"max\",\"ms\":100}").Accepted);
    }

    [Fact]
    public void Fuzz_ended_needs_a_fixed_ending_id()
    {
        var r = Gate("{\"type\":\"fuzz-ended\",\"total\":1e12,\"minutes\":71.5,\"items\":120,\"ending\":\"llama_expo\",\"route\":[\"a\"],\"dayUtc\":\"2026-11-22\"}");
        Assert.True(r.Accepted);
        Assert.Equal("llama_expo", r.Frame!.Ending);
        Assert.Equal(71, r.Frame.Minutes);
        Assert.Equal(0, r.Frame.Ms);
        Assert.False(Gate("{\"type\":\"fuzz-ended\",\"minutes\":3,\"ending\":\"<script>\"}").Accepted);
        Assert.False(Gate("{\"type\":\"fuzz-ended\",\"ending\":\"good\"}").Accepted);
    }

    [Fact]
    public void Achievement_carries_only_a_fixed_sticker_id()
    {
        var r = Gate("{\"type\":\"achievement\",\"id\":\"ac_5k\",\"amount\":99999,\"tier\":\"rare\"}");
        Assert.True(r.Accepted);
        Assert.Equal("achievement", r.Frame!.Type);
        Assert.Equal("ac_5k", r.Frame.Id);
        Assert.Null(r.Frame.Ending);
        Assert.Equal(new string('a', 40), Gate("{\"type\":\"achievement\",\"id\":\"" + new string('a', 40) + "\"}").Frame!.Id);
        foreach (var bad in new[]
        {
            "{\"type\":\"achievement\"}",
            "{\"type\":\"achievement\",\"id\":\"\"}",
            "{\"type\":\"achievement\",\"id\":\"AC_5K\"}",
            "{\"type\":\"achievement\",\"id\":\"ac 5k\"}",
            "{\"type\":\"achievement\",\"id\":\"ac_5k\\n\"}",
            "{\"type\":\"achievement\",\"id\":\"" + new string('a', 41) + "\"}",
            "{\"type\":\"achievement\",\"id\":5}",
            "{\"type\":\"achievement\",\"id\":[\"ac_5k\"]}",
        })
        {
            var g = Gate(bad);
            Assert.False(g.Accepted, bad);
            Assert.Equal(TcFrameGate.RefuseField, g.Refusal);
        }
    }

    [Fact]
    public void A_fixed_id_never_matches_before_a_trailing_newline()
    {
        Assert.False(Gate("{\"type\":\"fuzz-ended\",\"minutes\":30,\"ending\":\"ll_end\\n\"}").Accepted);
        Assert.Null(Gate("{\"type\":\"haptic\",\"level\":0.5,\"ms\":100,\"tag\":\"lucky\\n\"}").Frame!.Tag);
    }

    [Fact]
    public void Exit_reason_falls_back_to_back()
    {
        Assert.Equal("key", Gate("{\"type\":\"exit\",\"reason\":\"key\"}").Frame!.Reason);
        Assert.Equal("back", Gate("{\"type\":\"exit\",\"reason\":\"panic\"}").Frame!.Reason);
    }

    [Fact]
    public void Log_is_trimmed()
    {
        var r = TcFrameGate.Check(new JObject { ["type"] = "log", ["level"] = "loud", ["msg"] = new string('x', 2000) });
        Assert.Equal("info", r.Frame!.Reason);
        Assert.Equal(TcFrameGate.MaxLogChars, r.Frame.Save!.Length);
    }
}

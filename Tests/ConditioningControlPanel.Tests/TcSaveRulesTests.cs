using System;
using System.IO;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>T&amp;C save carry: the host-built desktop door and the answer -&gt; frame mapping
/// (Contract v2 save-result / save-incoming, CCP-Server proxy/tc-save.js).</summary>
public class TcSaveRulesTests
{
    [Fact]
    public void The_read_path_carries_only_the_account()
    {
        Assert.Equal("/v2/tc/save?unified_id=discord%3A123", TcSaveRules.GetPath("discord:123"));
        Assert.Equal("/v2/tc/save?unified_id=a%26b%3Dc", TcSaveRules.GetPath("a&b=c"));
    }

    [Fact]
    public void The_write_body_is_unified_id_save_and_from_desktop()
    {
        var o = JObject.Parse(TcSaveRules.PutBody("discord:123", "{\"v\":2}"));
        Assert.Equal(3, o.Count);
        Assert.Equal("discord:123", (string?)o["unified_id"]);
        Assert.Equal("{\"v\":2}", (string?)o["save"]);
        Assert.Equal("desktop", (string?)o["from"]);
    }

    [Theory]
    [InlineData("discord:1", "tok", true)]
    [InlineData("discord:1", "", false)]
    [InlineData(null, "tok", false)]
    [InlineData(" ", "tok", false)]
    public void Signed_in_needs_both(string? uid, string? token, bool expected)
        => Assert.Equal(expected, TcSaveRules.SignedIn(uid, token));

    private static void AssertResult(JObject f, bool ok, string? reason)
    {
        Assert.Equal("save-result", (string?)f["type"]);
        Assert.Equal(ok, (bool)f["ok"]!);
        Assert.Equal(reason, (string?)f["reason"]);
    }

    [Theory]
    [InlineData(200, "{\"ok\":true,\"savedAt\":1,\"from\":\"desktop\"}", true, null)]
    [InlineData(200, "{\"ok\":false,\"reason\":\"too-big\"}", false, "too-big")]
    [InlineData(200, "{\"ok\":false,\"reason\":\"refused\",\"retryInMs\":3600000}", false, "refused")]
    [InlineData(200, "{\"ok\":false,\"reason\":\"weird\"}", false, "refused")]
    [InlineData(200, "not json", false, "refused")]
    [InlineData(0, null, false, "offline")]
    [InlineData(503, "", false, "offline")]
    [InlineData(401, "{\"error\":\"invalid_token\"}", false, "sign-in")]
    [InlineData(409, "{\"error\":\"merged\"}", false, "refused")]
    [InlineData(429, "", false, "refused")]
    [InlineData(400, "", false, "refused")]
    public void Write_answers(int status, string? body, bool ok, string? reason)
        => AssertResult(TcSaveRules.MapPut(status, body), ok, reason);

    [Fact]
    public void A_read_hands_the_save_over_unchanged()
    {
        var f = TcSaveRules.MapGet(200, "{\"ok\":true,\"save\":\"{\\\"v\\\":2}\",\"savedAt\":1763800000000,\"from\":\"web\"}");
        Assert.Equal("save-incoming", (string?)f["type"]);
        Assert.Equal("{\"v\":2}", (string?)f["save"]);
        Assert.Equal(1763800000000L, (long)f["savedAt"]!);
        Assert.Equal("web", (string?)f["from"]);
    }

    [Theory]
    [InlineData(200, "{\"ok\":false,\"reason\":\"none\"}", "none")]
    [InlineData(200, "{\"ok\":true,\"save\":\"\",\"savedAt\":1,\"from\":\"web\"}", "refused")]
    [InlineData(200, "{\"ok\":true,\"save\":\"x\",\"from\":\"web\"}", "refused")]
    [InlineData(200, "{\"ok\":true,\"save\":\"x\",\"savedAt\":1,\"from\":\"phone\"}", "refused")]
    [InlineData(0, null, "offline")]
    [InlineData(401, "", "sign-in")]
    [InlineData(429, "", "refused")]
    public void Read_refusals(int status, string? body, string reason)
        => AssertResult(TcSaveRules.MapGet(status, body), false, reason);

    [Fact]
    public void A_read_longer_than_the_page_can_parse_is_refused()
    {
        var body = new JObject { ["ok"] = true, ["save"] = new string('a', TcRules.MaxSaveChars + 1), ["savedAt"] = 1, ["from"] = "web" };
        AssertResult(TcSaveRules.MapGet(200, body.ToString()), false, "refused");
    }

    [Fact]
    public void The_result_frame()
    {
        AssertResult(TcSaveRules.Result(true), true, null);
        AssertResult(TcSaveRules.Result(false), false, "refused");
    }

    private static string Src(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services", "Tc")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Tc", file));
    }

    [Fact]
    public void The_carry_builds_its_own_paths_writes_nothing_and_never_touches_Sparkles()
    {
        var save = Src("TcHostService.Save.cs");
        Assert.Contains("TcSaveRules.GetPath(uid!)", save);
        Assert.Contains("TcSaveRules.Path,", save);
        Assert.DoesNotContain("File.", save);
        Assert.DoesNotContain("Sparkles", save);
        Assert.DoesNotContain("f.Url", save);
    }
}

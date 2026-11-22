using System;
using System.IO;
using ConditioningControlPanel.Services.Tc;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Llama Expo pack rules: the manifest of tag <c>tc-expo</c>, the base, room, zip-slip,
/// the ready stamp and which old folders go.</summary>
public class TcExpoPackRulesTests
{
    private const string Good =
        "{\"v\":1,\"file\":\"expo-2037.5.1-cda68162f15be5e3.zip\",\"bytes\":131367569," +
        "\"sha256\":\"cda68162f15be5e333cc52b37359895216f66eaaaad920a8b20cf5b80753edc1\",\"version\":\"2037.5.1\",\"minApp\":\"7.4.0\"}";

    private static TcExpoManifest Manifest() => TcExpoPackRules.Parse(Good, "7.4.0", out _)!;

    [Fact]
    public void The_live_manifest_reads()
    {
        var m = TcExpoPackRules.Parse(Good, "7.4.0", out var why);
        Assert.NotNull(m);
        Assert.Equal("", why);
        Assert.Equal("expo-2037.5.1-cda68162f15be5e3.zip", m!.File);
        Assert.Equal(131367569, m.Bytes);
        Assert.Equal("2037.5.1", m.Version);
        Assert.Equal(131367569L * TcExpoPackRules.UnpackFactor, m.UnpackedBytes);
        Assert.Equal(131, TcExpoPackRules.Mb(m.Bytes));
    }

    [Theory]
    [InlineData("\"v\":1", "\"v\":2", "v")]
    [InlineData("\"v\":1", "\"v\":\"1\"", "v")]
    [InlineData("expo-2037.5.1-cda68162f15be5e3.zip", "zones/expo.zip", "file")]
    [InlineData("expo-2037.5.1-cda68162f15be5e3.zip", "..\\\\expo.zip", "file")]
    [InlineData("expo-2037.5.1-cda68162f15be5e3.zip", "expo.exe", "file")]
    [InlineData("131367569", "0", "bytes")]
    [InlineData("cda68162", "zz", "sha256")]
    [InlineData("\"2037.5.1\"", "\"../x\"", "version")]
    [InlineData("\"7.4.0\"", "\"seven\"", "minApp")]
    [InlineData("\"7.4.0\"", "\"7.5.0\"", "app-too-old")]
    public void A_bad_manifest_is_refused(string from, string to, string why)
    {
        Assert.Null(TcExpoPackRules.Parse(Good.Replace(from, to), "7.4.0", out var got));
        Assert.Equal(why, got);
    }

    [Fact]
    public void Junk_is_refused_and_this_build_is_too_old_for_the_live_pack()
    {
        Assert.Null(TcExpoPackRules.Parse("not json", "7.4.0", out _));
        Assert.Null(TcExpoPackRules.Parse(null, "7.4.0", out _));
        Assert.Null(TcExpoPackRules.Parse(Good, "7.3.8", out var why));
        Assert.Equal("app-too-old", why);
        Assert.NotNull(TcExpoPackRules.Parse(Good, "7.4.1", out _));
    }

    [Fact]
    public void An_unpacked_size_in_the_manifest_is_used()
        => Assert.Equal(160_000_000, TcExpoPackRules.Parse(Good.Replace("}", ",\"unpacked\":160000000}"), "7.4.0", out _)!.UnpackedBytes);

    [Fact]
    public void The_manifest_is_read_from_the_tag_by_name()
    {
        Assert.Equal("https://github.com/CC-Labs-it/Conditioning-Control-Panel/releases/download/tc-expo/expo-manifest.json",
            TcExpoPackRules.ManifestUrl);
        Assert.DoesNotContain("latest", TcExpoPackRules.ManifestUrl);
    }

    [Fact]
    public void The_base_is_https_ccp_expo_ending_in_a_slash()
    {
        Assert.True(TcExpoPackRules.IsBase(TcRules.ExpoBase));
        Assert.False(TcExpoPackRules.IsBase("http://ccp.expo/"));
        Assert.False(TcExpoPackRules.IsBase("https://ccp.expo"));
        Assert.False(TcExpoPackRules.IsBase("https://ccp.game/"));
        Assert.False(TcExpoPackRules.IsBase("https://ccp.expo/zones/"));
    }

    [Fact]
    public void Room_counts_the_rest_of_the_zip_plus_the_unpack()
    {
        var m = Manifest();
        var need = m.Bytes + m.UnpackedBytes;
        Assert.True(TcExpoPackRules.HasRoom(need, m, 0));
        Assert.False(TcExpoPackRules.HasRoom(need - 1, m, 0));
        Assert.True(TcExpoPackRules.HasRoom(need - 1000, m, 1000));
        Assert.True(TcExpoPackRules.UnpackFits(m.UnpackedBytes, m));
        Assert.False(TcExpoPackRules.UnpackFits(m.UnpackedBytes + 1, m));
    }

    [Theory]
    [InlineData("zones/zones.json", true)]
    [InlineData("zones/", true)]
    [InlineData("a/b/../c.png", false)]
    [InlineData("../evil.dll", false)]
    [InlineData("..\\evil.dll", false)]
    [InlineData("/etc/x", false)]
    [InlineData("C:/Windows/x", false)]
    [InlineData("\\\\server\\share\\x", false)]
    [InlineData("", false)]
    public void Zip_slip_is_refused(string entry, bool ok)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-expo-stage");
        var t = TcExpoPackRules.EntryTarget(root, entry);
        Assert.Equal(ok, t != null);
        if (t != null) Assert.StartsWith(Path.GetFullPath(root), t, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_folder_without_its_stamp_is_absent_and_the_newest_stamped_one_is_ready()
    {
        Assert.Null(TcExpoPackRules.ReadyVersion(new[] { "2037.5.1" }, Array.Empty<string>()));
        Assert.Null(TcExpoPackRules.ReadyVersion(Array.Empty<string>(), new[] { "2037.5.1.ready" }));
        Assert.Equal("2037.10.0", TcExpoPackRules.ReadyVersion(
            new[] { "2037.5.1", "2037.10.0", "2038.0.0" }, new[] { "2037.5.1.ready", "2037.10.0.ready" }));
    }

    [Fact]
    public void Old_and_unstamped_folders_go_but_never_the_served_one()
    {
        var (dirs, stamps) = TcExpoPackRules.Stale(
            new[] { "2037.5.1", "2037.6.0", "junk" }, new[] { "2037.5.1.ready", "2037.6.0.ready", "gone.ready" },
            keep: "2037.6.0", serving: "2037.5.1");
        Assert.Equal(new[] { "junk" }, dirs);
        Assert.Equal(new[] { "gone.ready" }, stamps);

        var (dirs2, stamps2) = TcExpoPackRules.Stale(new[] { "2037.5.1", "2037.6.0" }, new[] { "2037.5.1.ready", "2037.6.0.ready" },
            keep: "2037.6.0", serving: null);
        Assert.Equal(new[] { "2037.5.1" }, dirs2);
        Assert.Equal(new[] { "2037.5.1.ready" }, stamps2);
    }

    [Fact]
    public void The_pack_lives_next_to_the_content_packs()
    {
        var m = Manifest();
        var root = Path.Combine("C:", "content");
        Assert.Equal(Path.Combine(root, "tc-expo", "2037.5.1"), TcExpoPackRules.VersionDir(root, m.Version));
        Assert.Equal(Path.Combine(root, "tc-expo", "2037.5.1.ready"), TcExpoPackRules.StampPath(root, m.Version));
        Assert.Equal(Path.Combine(root, ".downloads", m.File + ".partial"), TcExpoPackRules.PartialPath(root, m));
    }

    [Fact]
    public void The_host_wires_the_expo_frames_and_stops_it_on_close()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services", "Tc")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var tc = Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Tc");
        var host = File.ReadAllText(Path.Combine(tc, "TcHostService.cs"));
        Assert.Contains("case \"expo-fetch\":", host);
        Assert.Contains("case \"expo-cancel\":", host);
        Assert.Contains("StopExpo();       // the partial stays", host);
        Assert.Contains("ExpoCap()", host);
        var expo = File.ReadAllText(Path.Combine(tc, "TcHostService.Expo.cs"));
        Assert.Contains("new ResumableDownload(", expo);
        Assert.Contains("TcExpoPackRules.EntryTarget(", expo);
        Assert.DoesNotContain("Process.Start", expo);
        // The sha check comes before the unpack.
        Assert.True(expo.IndexOf("Sha256Async(", StringComparison.Ordinal) < expo.IndexOf("UnpackExpo(m, partial)", StringComparison.Ordinal));
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.PrizeCounter;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The walls around the DEBUG shop preview (BRIEF "The preview wire"): a Release build cannot reach it
/// (the switch is hard off there and every caller outside its own files sits inside <c>#if DEBUG</c>),
/// and the preview never touches the real wallet or the network.
/// </summary>
public class PrizeCounterPreviewWallTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static readonly string[] OwnFiles = { "ShopPreviewBackend.cs", "ShopPreviewRules.cs" };

    [Fact]
    public void The_switch_is_on_only_for_1_and_never_in_Release()
    {
#if DEBUG
        Assert.True(ShopPreviewBackend.EnabledFor("1"));
        Assert.True(ShopPreviewBackend.EnabledFor(" 1 "));
#else
        Assert.False(ShopPreviewBackend.EnabledFor("1"));
#endif
        Assert.False(ShopPreviewBackend.EnabledFor(null));
        Assert.False(ShopPreviewBackend.EnabledFor("0"));
        Assert.False(ShopPreviewBackend.EnabledFor("true"));
    }

    [Fact]
    public void The_switch_reads_false_in_a_Release_build()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "Services", "PrizeCounter", "ShopPreviewBackend.cs"));
        Assert.Matches(new Regex(@"#if DEBUG\s+return env\?\.Trim\(\) == ""1"";\s+#else\s+return false;\s+#endif"), src);
    }

    [Fact]
    public void Every_caller_outside_the_preview_sits_inside_if_DEBUG()
    {
        int callers = 0;
        foreach (var file in Directory.EnumerateFiles(Root(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            if (OwnFiles.Contains(Path.GetFileName(file))) continue;
            var lines = File.ReadAllLines(file);
            int depth = 0;   // inside how many #if DEBUG blocks (other #if blocks are not counted as DEBUG)
            var stack = new System.Collections.Generic.Stack<bool>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("#if")) { bool dbg = line == "#if DEBUG"; stack.Push(dbg); if (dbg) depth++; continue; }
                if (line.StartsWith("#else") || line.StartsWith("#elif")) { if (stack.Count > 0 && stack.Peek()) { depth--; stack.Pop(); stack.Push(false); } continue; }
                if (line.StartsWith("#endif")) { if (stack.Count > 0 && stack.Pop()) depth--; continue; }
                if (!line.Contains("ShopPreviewBackend") || line.StartsWith("//") || line.StartsWith("///")) continue;
                callers++;
                Assert.True(depth > 0, $"{Path.GetFileName(file)}: '{line}' reaches the shop preview outside #if DEBUG");
            }
        }
        Assert.True(callers >= 5, "the relay, the host's init and wear, and App's wire should all call it");
    }

    [Theory]
    [InlineData("SkillPoints")]
    [InlineData(".Balance")]
    [InlineData("HttpClient")]
    [InlineData("HttpRequestMessage")]
    [InlineData("System.Net")]
    [InlineData("BackRoomApi")]
    [InlineData("WebRequest")]
    [InlineData("App.Settings")]
    [InlineData("V2WalletAdoption")]
    public void The_preview_never_touches_the_real_wallet_or_the_network(string banned)
    {
        foreach (var name in OwnFiles)
        {
            var src = File.ReadAllText(Path.Combine(Root(), "Services", "PrizeCounter", name));
            Assert.DoesNotContain(banned, src);
        }
    }

    [Fact]
    public void The_relay_answers_from_the_preview_before_any_route_identity_or_wallet()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "Services", "PrizeCounter", "PrizeCounterRelay.cs"));
        int preview = src.IndexOf("ShopPreviewBackend.Shared.Handle", StringComparison.Ordinal);
        Assert.True(preview > 0);
        Assert.True(preview < src.IndexOf("if (!TryRoute(op, out _, out _))", StringComparison.Ordinal));
        Assert.True(preview < src.IndexOf("SafeIdentity();", StringComparison.Ordinal));
        // A preview receipt's wallet frame is refused before the real receipt check.
        int frame = src.IndexOf("public bool HandleWalletFrame", StringComparison.Ordinal);
        Assert.True(src.IndexOf("ShopPreviewBackend.Enabled) return false", frame, StringComparison.Ordinal)
            < src.IndexOf("_lastReceipt is not", frame, StringComparison.Ordinal));
    }

    [Fact]
    public void The_bundled_sample_is_a_Debug_only_resource()
    {
        var csproj = File.ReadAllText(Path.Combine(Root(), "ConditioningControlPanel.csproj"));
        var line = csproj.Split('\n').Single(l => l.Contains(ShopPreviewBackend.SampleResource));
        Assert.Contains("Condition=\"'$(Configuration)' == 'Debug'\"", line);
#if DEBUG
        Assert.NotNull(ShopPreviewBackend.LoadBundledSample()?["tabs"]);
#else
        Assert.Null(ShopPreviewBackend.LoadBundledSample());
#endif
    }
}

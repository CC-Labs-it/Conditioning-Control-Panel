using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// B1 W5: web trees never fetch from each other, so the results slip (<c>sparkles-slip.js</c>)
/// is COPIED into every tree that shows one. The copies must stay byte for byte the same (line
/// endings aside: Git checks them out as CRLF), and each tree must hand the host's
/// <c>sparkles-receipt</c> to it. A new game's tree joins by adding its folder to
/// <see cref="Trees"/> and copying the file (chess, Goon, Breakout, Glassroad in W3 and W4).
/// </summary>
public class SparklesSlipParityTests
{
    public static readonly string[] Trees =
    {
        "arcademy",
        "dtrh",
        "intake",
        "glassroad",
    };

    private const string File_ = "sparkles-slip.js";

    private static string WebDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources", "web")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", "Resources", "web");
    }

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    [Fact]
    public void Every_tree_carries_the_same_slip()
    {
        var copies = Trees.Select(t => (Tree: t, Path: Path.Combine(WebDir(), t, File_))).ToList();
        foreach (var c in copies) Assert.True(File.Exists(c.Path), $"{c.Tree} has no {File_}");
        var first = Read(copies[0].Path);
        foreach (var c in copies.Skip(1))
            Assert.True(first == Read(c.Path), $"{c.Tree}/{File_} differs from {copies[0].Tree}/{File_}: copy it again");
    }

    [Fact]
    public void Every_tree_hands_the_receipt_to_the_slip()
    {
        foreach (var tree in Trees)
        {
            var root = Path.Combine(WebDir(), tree);
            var wired = Directory.EnumerateFiles(root, "*.js", SearchOption.TopDirectoryOnly)
                .Where(f => !f.EndsWith(File_, StringComparison.Ordinal))
                .Select(Read)
                .Any(s => s.Contains("from './sparkles-slip.js'", StringComparison.Ordinal)
                          && s.Contains("'sparkles-receipt', showSlip", StringComparison.Ordinal));
            Assert.True(wired, $"{tree}: no boot file imports {File_} and subscribes 'sparkles-receipt'");
        }
    }

    [Fact]
    public void The_slip_stays_self_contained()
    {
        var src = Read(Path.Combine(WebDir(), Trees[0], File_));
        Assert.DoesNotContain(src.Split('\n'), l => l.StartsWith("import ", StringComparison.Ordinal));
        Assert.DoesNotContain("fetch(", src);
        Assert.Contains("prefers-reduced-motion", src);
    }
}

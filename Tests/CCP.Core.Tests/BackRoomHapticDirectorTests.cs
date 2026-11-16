using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace CCP.Core.Tests;

public class BackRoomHapticDirectorTests
{
    [Fact]
    public void LimitsAndShapesPulses()
    {
        var limit = new BackRoomHapticDirector.Limiter();
        Assert.True(limit.Accept(0));
        Assert.False(limit.Accept(79));
        Assert.True(limit.Accept(80));
        Assert.Equal(3, BackRoomHapticDirector.PriorityFor(0.7));
        Assert.Equal(2, BackRoomHapticDirector.PriorityFor(0.4));
        Assert.Equal(1, BackRoomHapticDirector.PriorityFor(0.3));
        Assert.Equal(1500, BackRoomHapticDirector.DurationFor(1000, true));
        Assert.Equal(500, BackRoomHapticDirector.DurationFor(500, false));
    }

    /// <summary>
    /// The WPF head and CCP.Core both grant ConditioningControlPanel.Tests their internals
    /// (CCP.Core/Properties/AssemblyInfo.cs, ConditioningControlPanel/Properties/AssemblyInfo.cs), so a
    /// type whose full name lives in both trees is CS0433 "type exists in both" for that test project
    /// (Tests/ConditioningControlPanel.Tests/BackRoomHapticTests.cs uses the bare BackRoomHapticDirector).
    /// A type that moves to Core moves, it is not copied (same rule as Services/Reset/UiThreadExit.cs).
    /// WPF cannot build on Linux, so this reads the source trees.
    /// </summary>
    [Fact]
    public void CoreTypesAreNotAlsoDeclaredInTheWpfHead()
    {
        // Pre-existing, outside this change: public in both (Core static facade, WPF engine).
        var baseline = new HashSet<string> { "ConditioningControlPanel.Services.Speech.SherpaWakeService" };

        var root = RepoRoot();
        var core = DeclaredTypes(Path.Combine(root, "CCP.Core"));
        var wpf = DeclaredTypes(Path.Combine(root, "ConditioningControlPanel"));

        var clashes = core.Keys.Intersect(wpf.Keys).Where(n => !baseline.Contains(n))
            .Select(n => $"{n}: {core[n]} and {wpf[n]}").OrderBy(s => s).ToList();
        Assert.True(clashes.Count == 0, "Same full name in CCP.Core and the WPF head:\n" + string.Join("\n", clashes));
    }

    private static readonly Regex FileScopedNs = new(@"^namespace\s+([\w.]+)\s*;", RegexOptions.Multiline);
    private static readonly Regex BlockNs = new(@"^namespace\s+([\w.]+)", RegexOptions.Multiline);

    private static Dictionary<string, string> DeclaredTypes(string dir)
    {
        var map = new Dictionary<string, string>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dir, path).Replace('\\', '/');
            if (rel.StartsWith("bin/") || rel.StartsWith("obj/")) continue;
            var text = File.ReadAllText(path);
            string ns, indent;
            var m = FileScopedNs.Match(text);
            if (m.Success) { ns = m.Groups[1].Value; indent = ""; }
            else
            {
                m = BlockNs.Match(text);
                if (!m.Success) continue;
                ns = m.Groups[1].Value; indent = "    ";
            }
            // Top-level declarations only (namespace indent, no deeper): nested types share no full name.
            var decl = new Regex("^" + indent +
                @"(?:(?:public|internal|static|sealed|abstract|partial|readonly|file)\s+)*" +
                @"(?:record\s+(?:class|struct)|class|struct|record|enum|interface)\s+(\w+)", RegexOptions.Multiline);
            foreach (Match t in decl.Matches(text))
                map.TryAdd(ns + "." + t.Groups[1].Value, Path.GetFileName(dir) + "/" + rel);
        }
        return map;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

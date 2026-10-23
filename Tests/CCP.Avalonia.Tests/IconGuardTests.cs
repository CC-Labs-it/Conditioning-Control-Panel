using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Fluent icons guard (oracle fluent-icons.md (e), lane L0b): every icon in CCP.Avalonia goes through
/// fx:IconGlyph. Hard rules fail at once; the ratchets fail when a count goes UP (a new icon-emoji)
/// and when it goes DOWN without the baseline being lowered in the same change, so they only move to 0.
/// Content (chat samples, user data, typography) is exempt through IconGuard.allow.txt, one reason each.
/// </summary>
public sealed class IconGuardTests
{
    private static readonly string Root = RepoRoot();
    private static readonly string Head = Path.Combine(Root, "CCP.Avalonia");
    private static readonly string Here = Path.Combine(Root, "Tests", "CCP.Avalonia.Tests");

    [Fact]
    public void NoIconFontCodepoints()
    {
        var hits = new List<string>();
        var font = new Regex(@"[\uE000-\uF8FF]|\\u[Ee][0-9A-Fa-f]{3}|\\u[Ff][0-8][0-9A-Fa-f]{2}|&#x[Ee][0-9A-Fa-f]{3};|&#x[Ff][0-8][0-9A-Fa-f]{2};|Segoe (?:MDL2 Assets|Fluent Icons|UI Symbol|UI Emoji)");
        foreach (var (rel, lines) in Sources())
        {
            if (rel.StartsWith("Controls/Icons/", StringComparison.Ordinal)) continue; // IconMap: MDL2 -> Fluent table
            var code = StripComments(rel, string.Join("\n", lines)).Split('\n');
            for (int i = 0; i < code.Length; i++)
                if (font.IsMatch(Allowed(rel, code[i], "font"))) hits.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(hits.Count == 0, "icon-font codepoints/families (use fx:IconGlyph):\n" + string.Join("\n", hits));
    }

    [Fact]
    public void IconEmojiRatchet()
    {
        var perFile = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (rel, lines) in Sources())
        {
            // Controls/Icons is the mapping itself (IconMap keys are the emoji it replaces).
            if (rel.StartsWith("Controls/Icons/", StringComparison.Ordinal) || Verbatim(rel)) continue;
            var code = StripComments(rel, string.Join("\n", lines)).Split('\n');
            for (int i = 0; i < code.Length; i++)
                foreach (var c in IconClusters(Decode(Allowed(rel, code[i], "emoji"))))
                    (perFile.TryGetValue(rel, out var l) ? l : perFile[rel] = new()).Add($"{rel}:{i + 1}: {c}");
        }
        Ratchet("icon-emoji.count", perFile);
    }

    [Fact]
    public void IconPrefixedLocKeysRatchet()
    {
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(Root, "CCP.Core", "Localization", "Languages", "en.json")))!;
        // nav_crumb_sep "›" is typography, not an icon (oracle (c).4): it joins words in breadcrumbs and the OS title bar.
        var keys = en.Where(kv => kv.Key != "nav_crumb_sep" && IconClusters(LeadingCluster(kv.Value)).Any()).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("section_system", keys); // "⚙ System": proves the key set is live, not empty by a parse slip
        // Plain references only: {loc:Str K}, Loc.Get("K"), Loc.GetF("K", LabelKeys="..K..". IconLabel Key=,
        // {loc:StrBare K} and IconLabel.ForKey are the sanctioned forms and do not match these patterns.
        var refs = new Regex(@"\{loc:Str\s+(?:Key=)?([A-Za-z0-9_]+)\s*\}|\bLoc\.GetF?\(\s*""([A-Za-z0-9_]+)""|LabelKeys=""([^""]*)""");
        var perFile = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (rel, lines) in Sources())
        {
            if (Verbatim(rel)) continue;
            var code = StripComments(rel, string.Join("\n", lines)).Split('\n');
            for (int i = 0; i < code.Length; i++)
                foreach (Match m in refs.Matches(Allowed(rel, code[i], "emoji")))
                    foreach (var k in (m.Groups[1].Value + m.Groups[2].Value + " " + m.Groups[3].Value).Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        if (keys.Contains(k))
                            (perFile.TryGetValue(rel, out var l) ? l : perFile[rel] = new()).Add($"{rel}:{i + 1}: {k}{(GlyphOnly(en[k]) ? " (glyph-only)" : "")}");
        }
        Ratchet("icon-prefixed-loc.count", perFile);
    }

    [Fact]
    public void PathIconRatchet()
    {
        var perFile = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (rel, lines) in Sources())
        {
            var path = PathRegex(rel);
            var code = StripComments(rel, string.Join("\n", lines)).Split('\n');
            for (int i = 0; i < code.Length; i++)
                if (path.IsMatch(Allowed(rel, code[i], "path")))
                    (perFile.TryGetValue(rel, out var l) ? l : perFile[rel] = new()).Add($"{rel}:{i + 1}: {lines[i].Trim()}");
        }
        Ratchet("icon-path.count", perFile);
    }

    [Fact]
    public void OnlyIconGlyphTouchesPackage()
    {
        var hits = new List<string>();
        foreach (var (rel, lines) in Sources())
        {
            if ((rel.StartsWith("Controls/Icons/", StringComparison.Ordinal) && rel.EndsWith(".cs", StringComparison.Ordinal)) || rel == "Theme/Icons.axaml") continue;
            var code = StripComments(rel, string.Join("\n", lines)).Split('\n');
            for (int i = 0; i < code.Length; i++)
                if (code[i].Contains("FluentIcons", StringComparison.Ordinal)) hits.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(hits.Count == 0, "FluentIcons.* outside Controls/Icons (use fx:IconGlyph):\n" + string.Join("\n", hits));
    }

    [Fact]
    public void AllowListEntriesAreLiveAndReasoned()
    {
        // Live = matches comment-stripped code (P23). A `*` entry must exempt at least one real Path; a "path:" literal
        // is listed once per line it covers, so a new Path built the same way breaks the count instead of hiding.
        var code = Sources().ToDictionary(s => s.Rel, s => StripComments(s.Rel, string.Join("\n", s.Lines)).Split('\n'));
        var bad = new List<string>();
        foreach (var a in Allow)
        {
            int hits = a.Literal == "*" && Kind(a) == "verbatim" ? (code.ContainsKey(a.Path) ? 1 : 0)
                : a.Literal == "*"
                ? code.Count(kv => kv.Key.StartsWith(a.Path, StringComparison.Ordinal) && kv.Value.Any(PathRegex(kv.Key).IsMatch))
                : code.TryGetValue(a.Path, out var src) ? src.Count(l => l.Contains(a.Literal, StringComparison.Ordinal)) : 0;
            int listed = Allow.Count(b => b.Path == a.Path && b.Literal == a.Literal);
            bool wrongCount = a.Literal != "*" && Kind(a) == "path" && hits != listed;
            if (a.Reason.Length < 8 || (a.Literal == "*" && Kind(a) is not ("path" or "verbatim")) || (Kind(a) == "verbatim" && a.Literal != "*") || hits == 0 || wrongCount)
                bad.Add($"{a.Path} :: {a.Literal} (covers {hits} line(s), listed {listed}x)");
        }
        Assert.True(bad.Count == 0, "stale, broad or unreasoned IconGuard.allow.txt entries:\n" + string.Join("\n", bad.Distinct()));
    }

    // One per line: `new Path { Data = Geometry.Parse(..) }` is one drawn shape. StreamGeometry.Parse counts too.
    private static readonly Regex XamlPath = new(@"<Path[\s>/]");
    private static readonly Regex CsPath = new(@"\bnew\s+Path\b|Geometry\.Parse\(");
    private static Regex PathRegex(string rel) => rel.EndsWith(".cs", StringComparison.Ordinal) ? CsPath : XamlPath;

    private static string Kind(AllowEntry a) =>
        a.Reason.StartsWith("font:", StringComparison.Ordinal) ? "font" : a.Reason.StartsWith("path:", StringComparison.Ordinal) ? "path"
        : a.Reason.StartsWith("verbatim:", StringComparison.Ordinal) ? "verbatim" : "emoji";

    /// <summary>A "verbatim:" `*` entry names ONE file (exact path) imported unchanged under Stack rule 4: it is out of
    /// the emoji and prefixed-loc ratchets until its follow-up converts it and deletes the entry.</summary>
    private static bool Verbatim(string rel) => Allow.Any(a => a.Literal == "*" && Kind(a) == "verbatim" && a.Path == rel);

    private static void Ratchet(string baselineFile, SortedDictionary<string, List<string>> perFile)
    {
        var file = Path.Combine(Here, "Baselines", baselineFile);
        int baseline = int.Parse(File.ReadAllText(file).Trim(), CultureInfo.InvariantCulture);
        int count = perFile.Values.Sum(l => l.Count);
        var report = string.Join("\n", perFile.Select(kv => $"  {kv.Value.Count,4}  {kv.Key}"));
        var lines = string.Join("\n", perFile.Values.SelectMany(l => l));
        // ICON_GUARD_DUMP=<dir>: every site, for a lane picking its files.
        if (Environment.GetEnvironmentVariable("ICON_GUARD_DUMP") is { Length: > 0 } dump)
            File.WriteAllText(Path.Combine(dump, baselineFile + ".sites.txt"), lines + "\n");
        if (count > baseline)
        {
            Assert.Fail($"{baselineFile}: {count} > baseline {baseline}. Use fx:IconGlyph / IconLabel / StrBare, or allow-list content with a reason.\n" +
                "After merging the stack tip: sites that came in from the tip and that this branch does not touch are pre-guard code; " +
                "raise the baseline by exactly those (list them in the commit message), never for sites this branch adds.\n{lines}");
        }
        Assert.True(count == baseline, $"ratchet: lower {baselineFile} to {count} in this PR (was {baseline}). " +
            "A drop after a stack-tip merge means the tip converted icons: just lower the baseline.\n{report}");
    }

    private sealed record AllowEntry(string Path, string Literal, string Reason);

    /// <summary>IconGuard.allow.txt: `path :: exact literal :: reason`, path relative to CCP.Avalonia, '#' comments.
    /// Literal `*` ("path:" only) exempts every file whose path starts with the entry's path: whole D6 art files.</summary>
    private static readonly List<AllowEntry> Allow = File.ReadAllLines(Path.Combine(Here, "IconGuard.allow.txt"))
        .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
        .Select(l => l.Split(" :: "))
        .Select(p => p.Length == 3 ? new AllowEntry(p[0].Trim(), p[1], p[2].Trim()) : throw new FormatException("allow.txt: " + string.Join(" :: ", p)))
        .ToList();

    /// <summary>The line with every allow-listed literal of this file blanked. A "font:" reason scopes an entry to
    /// NoIconFontCodepoints, "path:" to PathIconRatchet; any other reason is content for the emoji ratchet.</summary>
    private static string Allowed(string rel, string line, string kind)
    {
        foreach (var a in Allow)
        {
            if (Kind(a) != kind) continue;
            if (a.Literal == "*" && rel.StartsWith(a.Path, StringComparison.Ordinal)) return "";
            if (a.Path == rel) line = line.Replace(a.Literal, new string(' ', a.Literal.Length), StringComparison.Ordinal);
        }
        return line;
    }

    private static IEnumerable<(string Rel, string[] Lines)> Sources() =>
        new[] { "*.axaml", "*.cs", "*.xaml" }.SelectMany(p => Directory.EnumerateFiles(Head, p, SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(Head, f).Replace('\\', '/'))
            .Where(r => !r.StartsWith("obj/", StringComparison.Ordinal) && !r.StartsWith("bin/", StringComparison.Ordinal))
            .OrderBy(r => r, StringComparer.Ordinal)
            .Select(r => (r, File.ReadAllText(Path.Combine(Head, r)).ReplaceLineEndings("\n").Split('\n')));

    /// <summary>Comments blanked (newlines kept, so line numbers hold). C# string literals are matched first and kept,
    /// so "http://" inside a string is not a comment; .axaml/.xaml only have &lt;!-- --&gt;.</summary>
    private static string StripComments(string rel, string src)
    {
        static string Blank(string s) => Regex.Replace(s, "[^\n]", " ");
        if (!rel.EndsWith(".cs", StringComparison.Ordinal))
            return Regex.Replace(src, "<!--.*?-->", m => Blank(m.Value), RegexOptions.Singleline);
        return Regex.Replace(src, @"(@""(?:[^""]|"""")*""|""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)')|//[^\n]*|/\*.*?\*/",
            m => m.Groups[1].Success ? m.Value : Blank(m.Value), RegexOptions.Singleline);
    }

    /// <summary>\uXXXX, \UXXXXXXXX and &amp;#xX; escapes decoded, so an escaped emoji counts like a literal one.</summary>
    private static string Decode(string s) =>
        Regex.Replace(s, @"\\u([0-9A-Fa-f]{4})|\\U([0-9A-Fa-f]{8})|&#x([0-9A-Fa-f]{1,6});", m =>
        {
            int cp = int.Parse(m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value, NumberStyles.HexNumber);
            return cp is >= 0xD800 and <= 0xDFFF or > 0x10FFFF ? m.Value : char.ConvertFromUtf32(cp);
        });

    private static string LeadingCluster(string? s)
    {
        s = (s ?? "").TrimStart();
        return s.Length == 0 ? "" : StringInfo.GetNextTextElement(s, 0);
    }

    /// <summary>A value that is nothing but icon clusters (label_text* glyph keys): hard 0 once their lane lands.</summary>
    private static bool GlyphOnly(string v)
    {
        var e = StringInfo.GetTextElementEnumerator(v.Trim());
        bool any = false;
        while (e.MoveNext()) { var c = (string)e.Current; if (string.IsNullOrWhiteSpace(c)) continue; if (!IsIcon(c)) return false; any = true; }
        return any;
    }

    private static IEnumerable<string> IconClusters(string s)
    {
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) if (IsIcon((string)e.Current)) yield return (string)e.Current;
    }

    private const string Explicit = "✓✕×＋−─›‹⏻";

    /// <summary>Oracle (e).2 ranges: .NET Regex has no \p{Extended_Pictographic}, so the first rune decides, or VS16.</summary>
    private static bool IsIcon(string cluster)
    {
        if (cluster.Length == 0) return false;
        if (cluster.Contains('\uFE0F') || Explicit.Contains(cluster, StringComparison.Ordinal)) return true;
        if (Rune.DecodeFromUtf16(cluster, out var rune, out _) != System.Buffers.OperationStatus.Done) return false;
        int r = rune.Value;
        return r is >= 0x1F000 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF
            or >= 0x2190 and <= 0x21FF or >= 0x2300 and <= 0x23FF or >= 0x25A0 and <= 0x25FF
            or 0x2122 or 0x2139 or 0x3030 or 0x303D;
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Core.Tests;

public sealed class WpfRenderIsolationTests
{
    [Fact]
    public void EveryHarnessCallerUsesANonparallelCollection()
    {
        // Check declarations together: partial suites inherit their collection from another file.
        var directory = Path.Combine(Path.GetDirectoryName(Here())!, "..", "ConditioningControlPanel.Tests");
        var declaration = new Regex(@"(?<attributes>(?:^\[[^\r\n]+\]\s*)*)^(?:public|internal)\s+(?:(?:sealed|partial|abstract)\s+)*class\s+(?<name>\w+)", RegexOptions.Multiline);
        var classes = Directory.EnumerateFiles(directory, "*.cs").SelectMany(path =>
        {
            var source = File.ReadAllText(path);
            var matches = declaration.Matches(source).Cast<Match>().ToArray();
            return matches.Select((match, i) => (
                Name: match.Groups["name"].Value,
                Attributes: match.Groups["attributes"].Value,
                Body: source.Substring(match.Index, (i + 1 < matches.Length ? matches[i + 1].Index : source.Length) - match.Index)));
        }).ToArray();
        var collections = classes.Where(c => c.Attributes.Contains(
                "[CollectionDefinition(Name, DisableParallelization = true)]"))
            .Select(c => c.Name + ".Name").ToArray();
        Assert.NotEmpty(collections);
        var isolated = classes.Where(c => collections.Any(name => c.Attributes.Contains("[Collection(" + name + ")]")))
            .Select(c => c.Name).ToHashSet();
        var callers = classes.Where(c => Regex.IsMatch(c.Body, @"\bWpfRenderHarness\.(OnStaThread|EnsureApplication)\s*\("))
            .Select(c => c.Name).Distinct().ToArray();
        Assert.NotEmpty(callers);
        Assert.Empty(callers.Where(name => !isolated.Contains(name)));
    }

    private static string Here([CallerFilePath] string path = "") => path;
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace CCP.Core.Tests;

public partial class InstallerHandoffContractTests
{
    [Fact]
    public void AvaloniaWorkflowsNeverPublishLatestOrUploadToAnExistingRelease()
    {
        var inspected = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot(), ".github/workflows")))
        {
            if (Path.GetExtension(path) is not (".yml" or ".yaml")) continue;
            var yaml = ParseWorkflow(File.ReadAllText(path));
            // Include consumers of the Linux artifacts, not just the dotnet publish command.
            if (!Scalars(yaml).Append(Path.GetFileName(path)).Any(s => Regex.IsMatch(s,
                    @"avalonia|tarball|flatpak|packaging[/\\]linux|linux[^\s]*\.tar\.", RegexOptions.IgnoreCase))) continue;
            inspected.Add(Path.GetFileName(path));
            var errors = ReleasePolicyErrors(yaml);
            Assert.True(errors.Count == 0, Path.GetFileName(path) + ": " + string.Join("; ", errors));
        }
        Assert.Contains("build.yml", inspected);
        Assert.Contains("distro-check.yml", inspected);
    }

    [Fact]
    public void InstallerCiUploadsOnlyTheUnsignedWorkflowArtifact()
    {
        var yaml = ParseWorkflow(Read(".github/workflows/build.yml"));
        var windows = Map(Map(Map(yaml)["jobs"])["windows"]);
        var steps = (YamlSequenceNode)windows["steps"];
        Assert.Single(steps.Children.OfType<YamlMappingNode>().Where(step =>
            Scalar(step, "run").Contains("& $iscc") && Scalar(step, "run").Contains("installer-avalonia.iss")));
        var upload = Assert.Single(steps.Children.OfType<YamlMappingNode>().Where(step =>
            Scalar(MapOrEmpty(step, "with"), "name") == "avalonia-installer-unsigned-not-for-release"));
        Assert.StartsWith("actions/upload-artifact@", Scalar(upload, "uses"));
        Assert.Equal("installer-output/*-Setup.UNSIGNED-avalonia.exe", Scalar(MapOrEmpty(upload, "with"), "path"));
        Assert.Empty(ReleasePolicyErrors(yaml));
    }

    // Block release publishing until a reviewed flow refuses existing releases before mutation.
    // action-gh-release can find a WPF release by tag and replace assets/metadata even with
    // prerelease: true and make_latest: false. Those flags alone are not a new-release guarantee.
    private static List<string> ReleasePolicyErrors(YamlNode yaml)
    {
        var errors = new List<string>();
        foreach (var map in Mappings(yaml))
        {
            var uses = Scalar(map, "uses");
            var action = uses.Split('@')[0].ToLowerInvariant();
            if (action.Contains("release"))
                errors.Add("Release publishing/upload is blocked until a flow refuses existing releases: " + uses);
            // Includes run blocks and github-script's with.script, in either shell. Reject raw
            // release API access rather than pretending to interpret shell/JS conditionals.
            foreach (var key in new[] { "run", "script" })
            {
                var script = Scalar(map, key);
                if (Regex.IsMatch(script,
                    @"\bgh(?:\.exe)?\s+release\b|\bgh(?:\.exe)?\s+api\b[\s\S]*\breleases\b|" +
                    @"(?:api|uploads)\.github\.com[^\s]*?/releases\b|/repos/[^\s]+/releases\b|" +
                    @"\b(?:createRelease|updateRelease|uploadReleaseAsset)\b|\breleases\s*\.\s*(?:create|update|upload)",
                    RegexOptions.IgnoreCase))
                    errors.Add("CLI/API release publishing or existing-release upload requires explicit reviewed support");
            }
        }
        return errors;
    }

    [Fact]
    public void PreviewReleaseGuardRejectsExistingWpfTagWithoutReleaseIdOrUploadUrl()
    {
        // action-gh-release finds an existing release by tag and can mutate its assets/metadata.
        // These two flags do not prove the target is a new preview release.
        var step = ReleaseStep("tag_name: v7.1.15\nfiles: linux.tar.gz\nprerelease: true\nmake_latest: false");
        Assert.NotEmpty(ReleasePolicyErrors(ParseWorkflow(step)));
    }

    [Theory]
    [InlineData("prerelease: false\nmake_latest: false")]
    [InlineData("prerelease: true\nmake_latest: true")]
    [InlineData("prerelease: true")]
    [InlineData("make_latest: false")]
    [InlineData("prerelease: '${{ inputs.preview }}'\nmake_latest: false")]
    public void PreviewReleaseGuardRejectsMissingOrUnsafeFlags(string inputs)
    {
        Assert.NotEmpty(ReleasePolicyErrors(ParseWorkflow(ReleaseStep(inputs))));
    }

    [Fact]
    public void ReleaseGuardAllowsArtifactsButRejectsEveryReleaseStep()
    {
        Assert.Empty(ReleasePolicyErrors(ParseWorkflow(ArtifactStep)));
        var preview = ReleaseStep("prerelease: true\nmake_latest: false");
        Assert.NotEmpty(ReleasePolicyErrors(ParseWorkflow(preview)));
        Assert.NotEmpty(ReleasePolicyErrors(ParseWorkflow(ArtifactStep + "\n" + preview)));
        Assert.NotEmpty(ReleasePolicyErrors(ParseWorkflow(preview + "\n" + ArtifactStep)));
        Assert.NotEmpty(ReleasePolicyErrors(ParseWorkflow(ReleaseStep("prerelease: true\nmake_latest: false\nrelease_id: 123"))));
    }

    [Theory]
    [InlineData("- uses: actions/upload-release-asset@v1")]
    [InlineData("- uses: ncipollo/release-action@v1")]
    [InlineData("- run: gh release upload preview linux.tar.gz")]
    [InlineData("- run: gh release create preview linux.tar.gz")]
    [InlineData("- run: gh release edit preview --latest")]
    [InlineData("- run: gh api --method PATCH repos/owner/repo/releases/123 -f make_latest=true")]
    [InlineData("- run: curl -X POST https://uploads.github.com/repos/owner/repo/releases/123/assets")]
    [InlineData("- uses: actions/github-script@v7\n  with:\n    script: github.rest.repos.createRelease({owner, repo})")]
    [InlineData("- uses: actions/github-script@v7\n  with:\n    script: octokit.request('POST /repos/{owner}/{repo}/releases')")]
    public void PreviewReleaseGuardRejectsOtherPublishingRoutes(string step)
    {
        // The prefix must pass on its own, so each route independently proves rejection.
        Assert.Empty(ReleasePolicyErrors(ParseWorkflow(ArtifactStep)));
        Assert.NotEmpty(ReleasePolicyErrors(ParseWorkflow(ArtifactStep + "\n" + step)));
    }

    private const string ArtifactStep = "- uses: actions/upload-artifact@v4\n  with:\n    name: linux-preview\n    path: linux.tar.gz";
    private static string ReleaseStep(string inputs) =>
        "- uses: softprops/action-gh-release@v2\n  with:\n    " + inputs.Replace("\n", "\n    ");
    private static YamlNode ParseWorkflow(string text)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        return Assert.Single(yaml.Documents).RootNode;
    }
    private static YamlMappingNode Map(YamlNode node) => (YamlMappingNode)node;
    private static YamlMappingNode MapOrEmpty(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? Map(node) : new YamlMappingNode();
    private static string Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? (node as YamlScalarNode)?.Value ?? "" : "";
    private static IEnumerable<YamlNode> Children(YamlNode node) => node switch
    {
        YamlMappingNode map => map.Children.Values,
        YamlSequenceNode sequence => sequence.Children,
        _ => Array.Empty<YamlNode>()
    };
    private static IEnumerable<YamlMappingNode> Mappings(YamlNode node) =>
        (node is YamlMappingNode map ? new[] { map } : Array.Empty<YamlMappingNode>()).Concat(Children(node).SelectMany(Mappings));
    private static IEnumerable<string> Scalars(YamlNode node) =>
        node is YamlScalarNode scalar ? new[] { scalar.Value ?? "" } : Children(node).SelectMany(Scalars);
}

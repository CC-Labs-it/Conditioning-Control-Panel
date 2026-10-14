using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Migrations;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Found by the real-profile migration test: a WPF profile opened on Linux re-extracts
/// builtin-locked (the archive path changed), and this head shipped no LockedMod/overrides, so the
/// re-extract restored the old gendered voice lines and art. The overrides now ship beside the exe.</summary>
public sealed class CirceNeutralOverridesShippedTests
{
    [Fact]
    public void ReExtractedLockedTreeKeepsTheNeutralVoiceLinesAndArt()
    {
        var overrides = CirceNeutralPackPatch.DefaultOverridesRoot;
        var tree = Directory.CreateTempSubdirectory("ccp-circe-overrides-").FullName;
        try
        {
            // What a fresh extract of the old archive holds: the superseded lines, the old art.
            var audio = Directory.CreateDirectory(Path.Combine(tree, CirceNeutralPackPatch.FlashesAudioRelativeDir)).FullName;
            foreach (var old in CirceNeutralPackPatch.ObsoleteVoiceLineFiles)
                File.WriteAllText(Path.Combine(audio, old), "old");
            var pngs = Directory.Exists(overrides)
                ? Directory.EnumerateFiles(overrides, "*.png", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(overrides, f)).ToArray()
                : Array.Empty<string>();
            Assert.Equal(5, pngs.Length);
            foreach (var png in pngs)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(tree, png))!);
                File.WriteAllText(Path.Combine(tree, png), "old art");
            }

            CirceNeutralPackPatch.Apply(tree, overrides);

            Assert.All(CirceNeutralPackPatch.ReplacementVoiceLineFiles,
                f => Assert.True(File.Exists(Path.Combine(audio, f)), f));
            Assert.All(CirceNeutralPackPatch.ObsoleteVoiceLineFiles,
                f => Assert.False(File.Exists(Path.Combine(audio, f)), f));
            Assert.All(pngs, png => Assert.Equal(
                File.ReadAllBytes(Path.Combine(overrides, png)), File.ReadAllBytes(Path.Combine(tree, png))));
        }
        finally
        {
            Directory.Delete(tree, recursive: true);
        }
    }
}

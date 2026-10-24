using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CompanionVoiceLinesTests
{
    [Fact]
    public void PackagedFolderWinsAndFilesSortByNameAcrossAudioFormats()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-voice-" + Guid.NewGuid());
        var dir = Path.Combine(root, "resources", "sounds", "flashes_audio");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in new[] { "Zulu.mp3", "Alpha.wav", "Bravo.ogg", "Charlie.flac", "ignore.txt" })
                File.WriteAllText(Path.Combine(dir, name), "test");
            var folder = CompanionVoiceLines.ResolveFolder("custom", root);
            Assert.Equal(dir, folder.Absolute);
            Assert.Null(folder.Relative);
            Assert.Equal(new[] { "Alpha.wav", "Bravo.ogg", "Charlie.flac", "Zulu.mp3" },
                CompanionVoiceLines.GetFiles(folder).Select(Path.GetFileName));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingForeignModDoesNotBorrowBambiBaseline()
    {
        var folder = CompanionVoiceLines.ResolveFolder("missing-mod-" + Guid.NewGuid(), null);
        Assert.Contains("companion_audio", folder.Relative!);
        Assert.DoesNotContain(Path.Combine("sounds", "flashes_audio"), folder.Relative!);
        Assert.Equal(CompanionContentResolver.VoiceLinesRelativeDir,
            CompanionVoiceLines.ResolveFolder(BuiltInMods.BambiSleepId, null).Relative);
    }

    [Fact]
    public void MigrationWaitsForContentPreservesNumericFilenamesAndIsIdempotent()
    {
        var s = new AppSettings();
        s.DisabledPhraseIds.UnionWith(new[] { "VoiceLine:0", "VoiceLine:12", "Greeting:0" });
        s.RemovedPhraseIds.Add("VoiceLine:99");
        Assert.False(CompanionVoiceLines.MigrateLegacyIds(s, Array.Empty<string>()));
        Assert.Contains("VoiceLine:0", s.DisabledPhraseIds);
        Assert.True(CompanionVoiceLines.MigrateLegacyIds(s, new[] { "Alpha.mp3", "12.mp3" }));
        Assert.Contains("VoiceLine:Alpha", s.DisabledPhraseIds);
        Assert.Contains("VoiceLine:12", s.DisabledPhraseIds);
        Assert.Contains("Greeting:0", s.DisabledPhraseIds);
        Assert.DoesNotContain("VoiceLine:99", s.RemovedPhraseIds);
        Assert.False(CompanionVoiceLines.MigrateLegacyIds(s, new[] { "Alpha.mp3", "12.mp3" }));
    }

    [Fact]
    public void RowsKeepTheirOwnAudioFolderAndApplyDisabledAndRemovedIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-voice-" + Guid.NewGuid());
        var dir = Path.Combine(root, "resources", "sounds", "flashes_audio");
        Directory.CreateDirectory(dir);
        var old = (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Alpha.mp3"), "audio");
            File.WriteAllText(Path.Combine(dir, "Bravo.wav"), "audio");
            CoreMods.ActiveModIdProvider = () => "custom";
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "custom" }, root, false);
            var settings = new AppSettings();
            settings.DisabledPhraseIds.Add("VoiceLine:Alpha");
            settings.RemovedPhraseIds.Add("VoiceLine:Bravo");
            var row = Assert.Single(CompanionVoiceLines.GetRows(settings));
            Assert.Equal("Alpha", row.Text);
            Assert.Equal("VoiceLine:Alpha", row.Id);
            Assert.False(row.IsEnabled);
            Assert.True(row.IsBuiltIn);
            Assert.True(row.HasAudio);
            Assert.False(row.CanClearAudio);
            Assert.Equal(Path.Combine(dir, "Alpha.mp3"), row.AudioFilePath);
        }
        finally
        {
            (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider) = old;
            Directory.Delete(root, true);
        }
    }
}

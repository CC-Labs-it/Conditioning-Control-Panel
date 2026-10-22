using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Commands;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CompanionVoiceFilesTests
{
    [Fact]
    public void EnabledPoolKeepsStableTogglesAndOnlyEnabledCustomRecordings()
    {
        var root = Directory.CreateTempSubdirectory("ccp-voice-pool-").FullName;
        try
        {
            var custom = Path.Combine(root, "custom.mp3");
            File.WriteAllBytes(custom, new byte[] { 1 });
            var settings = new AppSettings();
            settings.DisabledPhraseIds.Add("VoiceLine:muted");
            settings.RemovedPhraseIds.Add("VoiceLine:removed");
            settings.CustomCompanionPhrases.Add(new CustomCompanionPhrase
                { Category = "VoiceLine", Enabled = true, AudioFileName = "custom.mp3" });
            settings.CustomCompanionPhrases.Add(new CustomCompanionPhrase
                { Category = "VoiceLine", Enabled = false, AudioFileName = "custom.mp3" });
            settings.CustomCompanionPhrases.Add(new CustomCompanionPhrase
                { Category = "VoiceLine", Enabled = true, AudioFileName = "missing.mp3" });
            var files = new[] { "kept.mp3", "muted.mp3", "removed.mp3" };
            Assert.Equal(new[] { "kept.mp3", custom }, CompanionVoiceFiles.Enabled(files, settings, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MigrationWaitsForContentAndKeepsNumericFilenames()
    {
        var settings = new AppSettings();
        settings.DisabledPhraseIds.Add("VoiceLine:1");
        settings.RemovedPhraseIds.Add("VoiceLine:12");
        Assert.False(CompanionVoiceFiles.MigrateLegacyIds(settings, Array.Empty<string>()));
        Assert.Contains("VoiceLine:1", settings.DisabledPhraseIds);
        Assert.True(CompanionVoiceFiles.MigrateLegacyIds(settings, new[] { "12.mp3", "hi.mp3" }));
        Assert.Contains("VoiceLine:hi", settings.DisabledPhraseIds);
        Assert.Contains("VoiceLine:12", settings.RemovedPhraseIds);
        Assert.False(CompanionVoiceFiles.MigrateLegacyIds(settings, new[] { "12.mp3", "hi.mp3" }));
    }

    [Fact]
    public void AudioFallbackPrefersOwnThenModAndNeverReadsDisallowedSharedVoice()
    {
        static string[] Forbidden() => throw new InvalidOperationException("disallowed pool was read");
        var own = MediaCommand.ChooseAudioPool(new[] { "own.mp3" }, "builtin-locked", Forbidden, Forbidden, Forbidden);
        Assert.Equal(MediaCommand.AudioPool.Own, own.Pool);
        var mod = MediaCommand.ChooseAudioPool(Array.Empty<string>(), "builtin-locked",
            () => new[] { "mod.mp3" }, Forbidden, Forbidden);
        Assert.Equal(MediaCommand.AudioPool.ModVoice, mod.Pool);
        var neutral = MediaCommand.ChooseAudioPool(Array.Empty<string>(), "builtin-locked",
            () => Array.Empty<string>(), Forbidden, () => new[] { "neutral.mp3" });
        Assert.Equal(MediaCommand.AudioPool.NeutralClips, neutral.Pool);
    }
}

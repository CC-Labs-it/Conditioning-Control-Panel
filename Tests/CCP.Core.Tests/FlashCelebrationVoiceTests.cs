using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class FlashCelebrationVoiceTests
{
    [Fact]
    public void ShuffledCycleExhaustsBeforeRepeatingAndResetDropsOldModClips()
    {
        var old = FlashVoicePool.Build;
        try
        {
            FlashVoicePool.Reset();
            FlashVoicePool.Build = _ => new List<string> { "a", "b", "c" };
            var cycle = Enumerable.Range(0, 3).Select(_ => FlashVoicePool.Next(null)).ToArray();
            Assert.Equal(3, cycle.Distinct().Count());
            Assert.Contains(FlashVoicePool.Next(null), cycle);
            FlashVoicePool.Build = _ => new List<string> { "new-mod" };
            FlashVoicePool.Reset();
            Assert.Equal("new-mod", FlashVoicePool.Next(null));
            FlashVoicePool.Build = _ => new List<string>();
            FlashVoicePool.Reset();
            Assert.Null(FlashVoicePool.Next(null));
        }
        finally { FlashVoicePool.Build = old; FlashVoicePool.Reset(); }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0.0425)]
    [InlineData(50, 0.30052038)]
    [InlineData(100, 0.85)]
    public void VolumeMatchesWpfCurveAndMuteHasNoFloor(int master, double expected) =>
        Assert.Equal(expected, FlashVoicePool.Volume(master), 6);

    [Fact]
    public void CelebrationIgnoresAmbientGatesReplacesPriorClipAndStopsIdempotently()
    {
        var oldBuild = FlashVoicePool.Build;
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var file = Path.GetTempFileName();
        int played = 0, stopped = 0;
        try
        {
            FlashVoicePool.Reset();
            FlashVoicePool.Build = _ => new List<string> { file };
            CoreAudio.PlayStoppableProvider = (path, volume, tag, _, _) =>
            {
                Assert.Equal(file, path);
                Assert.Equal(FlashVoicePool.Volume(50), volume);
                Assert.Equal("flash-sound", tag);
                Assert.Equal(played, stopped); // previous sound was stopped before replacement
                played++;
                return () => stopped++;
            };
            var settings = new AppSettings { MasterVolume = 50, FlashAudioEnabled = false,
                AvatarEnabled = false, AvatarMuted = true, CompanionVoiceLinesMuted = true };
            FlashCelebrationVoice.Play(settings);
            FlashCelebrationVoice.Play(settings);
            Assert.Equal(2, played);
            FlashCelebrationVoice.Stop();
            FlashCelebrationVoice.Stop();
            Assert.Equal(2, stopped);
        }
        finally
        {
            FlashCelebrationVoice.Stop();
            CoreAudio.PlayStoppableProvider = oldPlay;
            FlashVoicePool.Build = oldBuild;
            FlashVoicePool.Reset();
            File.Delete(file);
        }
    }

    [Fact]
    public void PoolUsesPackagedModRecursesAndHonoursPhraseFilters()
    {
        var root = Directory.CreateTempSubdirectory("ccp-voice-pool-").FullName;
        var oldMod = CoreMods.ActiveModPackageProvider;
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "resources", "sounds", "flashes_audio"));
            File.WriteAllText(Path.Combine(dir.FullName, "kept.MP3"), "");
            File.WriteAllText(Path.Combine(dir.FullName, "removed.wav"), "");
            File.WriteAllText(Path.Combine(dir.FullName, "disabled.ogg"), "");
            var sub = Directory.CreateDirectory(Path.Combine(dir.FullName, "category"));
            File.WriteAllText(Path.Combine(sub.FullName, "nested.mp3"), "");
            File.WriteAllText(Path.Combine(sub.FullName, "ignore.txt"), "");
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest(), root, false);
            var settings = new AppSettings();
            settings.RemovedPhraseIds.Add("VoiceLine:removed");
            settings.DisabledPhraseIds.Add("VoiceLine:disabled");
            var pool = FlashVoicePool.BuildPool(settings).Select(Path.GetFileName).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "kept.MP3", "nested.mp3" }, pool);
        }
        finally { CoreMods.ActiveModPackageProvider = oldMod; Directory.Delete(root, true); }
    }
}

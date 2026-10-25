using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class CompanionEventAudioTests
{
    [Fact]
    public void PackagedAudioWinsMissingClipsStaySilentAndPacksAreProbed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-event-" + Guid.NewGuid());
        var id = "test-" + Guid.NewGuid().ToString("N");
        var packaged = Path.Combine(root, "resources", "sounds", "event_audio");
        var pack = Path.Combine(ContentLocator.ContentRoot, CompanionContentResolver.PerModRelativeDir(id), "event_audio");
        var old = (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider);
        try
        {
            Directory.CreateDirectory(packaged);
            Directory.CreateDirectory(pack);
            const string phrase = "*giggles* Pretty {petname} time~";
            var name = CompanionPhraseIds.Slugify(phrase) + ".mp3";
            File.WriteAllText(Path.Combine(packaged, name), "audio");
            File.WriteAllText(Path.Combine(pack, name), "audio");
            CoreMods.ActiveModIdProvider = () => id;
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = id }, root, false);
            Assert.Equal(Path.Combine(packaged, name), CompanionEventAudio.Resolve(phrase));
            Assert.Null(CompanionEventAudio.Resolve("missing clip"));
            Assert.Null(CompanionEventAudio.Resolve("{petname}~"));
            Assert.Null(CompanionEventAudio.Resolve(null));
            Directory.Delete(packaged, true);
            Assert.Equal(Path.Combine(pack, name), CompanionEventAudio.Resolve(phrase));
            CoreMods.ActiveModIdProvider = () => "missing-" + id;
            Assert.Null(CompanionEventAudio.Resolve(phrase));
        }
        finally
        {
            (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider) = old;
            Directory.Delete(root, true);
            Directory.Delete(Path.GetDirectoryName(pack)!, true);
        }
    }
}

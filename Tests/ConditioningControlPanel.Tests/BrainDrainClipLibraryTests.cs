using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class BrainDrainClipLibraryTests
{
    [Fact]
    public void ServiceLoadsAndReloadsPrimaryAndLegacyClips()
    {
        RuntimeHelpers.RunClassConstructor(typeof(App).TypeHandle);
        var previous = CorePaths.EffectiveAssetsProvider;
        var root = Path.Combine(Path.GetTempPath(), "ccp-brain-" + Guid.NewGuid());
        var legacy = BrainDrainService.LegacyAudioFolderPath;
        var name = "ccp-test-" + Guid.NewGuid().ToString("N");
        var old = Path.Combine(legacy, name + ".mp3");
        var primary = Path.Combine(root, "braindrain", name + ".mp3");
        try
        {
            Directory.CreateDirectory(legacy);
            File.WriteAllText(old, "legacy");
            CorePaths.EffectiveAssetsProvider = () => root;
            using var service = new BrainDrainService();
            var baseline = BrainDrainService.LegacyAudioFileCount();
            Assert.Equal(baseline, service.AudioFileCount);
            Assert.Contains(old, Pool(service));
            File.WriteAllText(primary, "primary");
            File.WriteAllText(Path.Combine(root, "braindrain", name + ".wav"), "new");
            service.ReloadAudioFiles();
            Assert.Equal(baseline + 1, service.AudioFileCount);
            Assert.Contains(primary, Pool(service));
            Assert.DoesNotContain(old, Pool(service));
            Assert.Equal("legacy", File.ReadAllText(old));
            File.Delete(primary);
            service.ReloadAudioFiles();
            Assert.Equal(baseline + 1, service.AudioFileCount);
            Assert.Contains(old, Pool(service));
        }
        finally
        {
            CorePaths.EffectiveAssetsProvider = previous;
            if (File.Exists(old)) File.Delete(old);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string[] Pool(BrainDrainService service) =>
        (string[])typeof(BrainDrainService).GetField("_audioFiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
}

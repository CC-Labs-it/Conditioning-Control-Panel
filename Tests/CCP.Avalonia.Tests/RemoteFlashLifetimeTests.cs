using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RemoteFlashLifetimeTests
{
    private sealed class LocalRoll : Random { public override int Next(int maxValue) => maxValue - 1; }

    [Fact]
    public void PackOnlyMixedSourceHonoursRatioAndImageSelection()
    {
        var root = Directory.CreateTempSubdirectory("ccp-flash-pack-").FullName;
        var previous = ContentPackStore.Current;
        var s = new AppSettings { MediaSource = "mixed", RemoteMediaConsented = true, RemoteMediaRatio = 30 };
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, ".packs", "pack-guid")).FullName;
            var manifest = new InstalledPackManifest { PackId = "pack", PackGuid = "pack-guid" };
            manifest.Files.Add(new PackFileEntry { OriginalName = "a.png", FileType = "image", Extension = ".png" });
            PackEncryptionService.SaveEncryptedManifest(JsonConvert.SerializeObject(manifest), Path.Combine(dir, ContentPackStore.ManifestFileName));
            ContentPackStore.Current = new ContentPackStore(() => root, () => s, () => { });
            Assert.False(Directory.Exists(Path.Combine(root, "images")));
            Assert.True(FlashOverlay.HasLocalImages(0));
            Assert.False(FlashSourceRules.ShouldDrawRemote(s, FlashOverlay.HasLocalImages(0), new LocalRoll()));
            s.DisabledAssetPaths.Add("pack:pack/a.png");
            Assert.False(FlashOverlay.HasLocalImages(0));
            Assert.True(FlashSourceRules.ShouldDrawRemote(s, FlashOverlay.HasLocalImages(0), new LocalRoll()));
            s.DisabledAssetPaths.Clear();
            ContentPackStore.Current.DeactivatePack("pack");
            Assert.False(FlashOverlay.HasLocalImages(0));
        }
        finally { ContentPackStore.Current = previous; Directory.Delete(root, true); }
    }

    [Fact]
    public Task StopKeepsPoolButPanicInvalidatesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var field = typeof(RemoteFlashSource).GetField("Pool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var pool = (RemoteFlashPool)field.GetValue(null)!;
        try
        {
            RemoteFlashSource.CleanupCache();
            Assert.True(pool.Add(pool.Generation, "pending"));
            FlashOverlay.CloseAll(final: false);
            Assert.Equal(1, RemoteFlashSource.ReadyCount);
            PanicSurfaces.All.Single(s => s.Id == "flash").Stop(null);
            Assert.Equal(0, RemoteFlashSource.ReadyCount);
            Assert.True(pool.Add(pool.Generation, "pending"));
            FlashOverlay.CloseAll();
            Assert.Equal(0, RemoteFlashSource.ReadyCount);
        }
        finally { RemoteFlashSource.CleanupCache(); }
        return Task.CompletedTask;
    });
}

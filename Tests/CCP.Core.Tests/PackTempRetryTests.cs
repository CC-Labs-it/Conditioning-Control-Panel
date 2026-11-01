using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
public sealed class PackTempRetryTests
{
    [Fact]
    public void Refused_delete_keeps_pool_entry_and_identity_until_retry()
    {
        var root = Directory.CreateTempSubdirectory("ccp-retry-").FullName;
        try
        {
            ContentPackStoreTests.WritePack(root, "p1", "One", ("a.png", "image"));
            var settings = new AppSettings();
            var store = new ContentPackStore(() => root, () => settings, () => { }, tempDir: () => Path.Combine(root, ".temp"));
            var pool = new PackMediaPool(ContentPackStore.ImageType, store: () => store);
            var path = pool.TryDraw(0)!;
            File.Delete(path);
            Directory.CreateDirectory(path); // Portable delete refusal, even when running as root.
            pool.CleanupTemps();
            Assert.Equal(1, pool.TempCount);
            Assert.True(store.TryGetSourceKey(path, out _));
            Directory.Delete(path);
            File.WriteAllText(path, "released");
            pool.CleanupTemps();
            Assert.Equal(0, pool.TempCount);
            Assert.False(File.Exists(path));
            Assert.False(store.TryGetSourceKey(path, out _));
            var missing = pool.TryDraw(0)!; Assert.True(store.TryGetSourceKey(missing, out _));
            Directory.Delete(Path.GetDirectoryName(missing)!, true);
            Assert.True(ConditioningControlPanel.Services.MediaFiles.MediaTempFileLedger.TryDeleteFile(missing));
            pool.CleanupTemps();
            Assert.Equal(0, pool.TempCount);
            Assert.False(store.TryGetSourceKey(missing, out _));
        }
        finally { Directory.Delete(root, true); }
    }
}

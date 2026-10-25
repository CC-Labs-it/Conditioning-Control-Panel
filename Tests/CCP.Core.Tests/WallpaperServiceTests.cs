using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class WallpaperHostTests
{
    private sealed class Desktop : IWallpaperPlatform
    {
        public string? Original = "original";
        public bool RestoreSucceeds = true;
        public List<string> Writes = new();
        public string? Capture() => Original;
        public bool Apply(string path) { Writes.Add(path); return true; }
        public bool Restore(string snapshot) { Writes.Add(snapshot); return RestoreSucceeds; }
    }

    [Fact]
    public async Task ShuffleRestoresFirstOriginalAndShutdownRejectsFurtherChanges()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var s = CoreSettings.Current;
        var (oldFolder, oldOriginal) = (s.WallpaperSourceFolder, s.WallpaperOriginalPath);
        try
        {
            File.WriteAllText(Path.Combine(folder, "one.png"), "fixture");
            File.WriteAllText(Path.Combine(folder, "two.jpg"), "fixture");
            File.WriteAllText(Path.Combine(folder, "ignored.webp"), "fixture");
            s.WallpaperSourceFolder = folder;
            s.WallpaperOriginalPath = "";
            var desktop = new Desktop();
            var host = new WallpaperHost(desktop);
            Assert.True(await host.ShuffleAsync());
            desktop.Original = "overridden";
            Assert.True(await host.ShuffleAsync());
            Assert.NotEqual(desktop.Writes[0], desktop.Writes[1]);
            Assert.True(await host.ShutdownAsync());
            Assert.Equal("original", desktop.Writes[2]);
            Assert.Equal("", s.WallpaperOriginalPath);
            Assert.False(await host.ShuffleAsync());
        }
        finally { s.WallpaperSourceFolder = oldFolder; s.WallpaperOriginalPath = oldOriginal; Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task UnreadableOriginalNeverWritesDesktopOrBreadcrumb()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var s = CoreSettings.Current;
        var (oldFolder, oldOriginal) = (s.WallpaperSourceFolder, s.WallpaperOriginalPath);
        try
        {
            File.WriteAllText(Path.Combine(folder, "one.png"), "fixture");
            s.WallpaperSourceFolder = folder;
            s.WallpaperOriginalPath = "";
            var desktop = new Desktop { Original = null };
            var host = new WallpaperHost(desktop);
            Assert.False(await host.ShuffleAsync());
            Assert.Empty(desktop.Writes);
            Assert.Equal("", s.WallpaperOriginalPath);
        }
        finally { s.WallpaperSourceFolder = oldFolder; s.WallpaperOriginalPath = oldOriginal; Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task FailedRestoreKeepsBreadcrumbInSessionAndStaleRestoreClearsItAfterOneTry()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var s = CoreSettings.Current;
        var (oldFolder, oldOriginal) = (s.WallpaperSourceFolder, s.WallpaperOriginalPath);
        try
        {
            File.WriteAllText(Path.Combine(folder, "one.png"), "fixture");
            s.WallpaperSourceFolder = folder;
            s.WallpaperOriginalPath = "";
            var desktop = new Desktop { RestoreSucceeds = false };
            var host = new WallpaperHost(desktop);
            Assert.True(await host.ShuffleAsync());
            Assert.False(await host.DeactivateAsync());
            Assert.Equal("original", s.WallpaperOriginalPath); // kept for the next launch

            // Next launch (WPF RestoreStaleOriginal): one attempt, cleared either way.
            var next = new WallpaperHost(desktop);
            Assert.False(await next.RestoreStaleAsync());
            Assert.Equal("", s.WallpaperOriginalPath);

            // An unrestorable breadcrumb must not refuse every later shuffle.
            s.WallpaperOriginalPath = "deleted-original";
            var third = new WallpaperHost(desktop);
            Assert.True(await third.ShuffleAsync());
            Assert.Equal("original", s.WallpaperOriginalPath);
        }
        finally { s.WallpaperSourceFolder = oldFolder; s.WallpaperOriginalPath = oldOriginal; Directory.Delete(folder, true); }
    }
}

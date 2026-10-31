using System;
using System.IO;
using ConditioningControlPanel.Services.Companion.Rain;
using Xunit;

namespace CCP.Core.Tests;

public class RainPackReaderTests
{
    [Fact]
    public void OnlyCompleteSafeMatchingPacksAreSelected()
    {
        var root = Path.Combine(Path.GetTempPath(), "rain-reader-" + Guid.NewGuid());
        var hash = new string('a', 64);
        var folder = Path.Combine(root, hash);
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "emotes.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "clip_timing.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "idle.strip.png"), "strip");
            var manifest = $"{{rev:1,lookHash:'{hash}',clips:{{idle:{{file:'idle.strip.png',frames:2,fps:15,w:1,h:1}}}}}}";
            var path = Path.Combine(folder, "manifest.json");
            File.WriteAllText(path, manifest);
            var pack = RainPackReader.TryRead(root, hash);
            Assert.NotNull(pack);
            Assert.Equal(folder, pack!.Folder);
            Assert.Equal(new RainClipInfo("idle.strip.png", 2, 15), pack.Clips["idle"]);
            Assert.Null(RainPackReader.TryRead(root, "../bad"));
            File.WriteAllText(path, manifest.Replace("rev:1", "rev:2"));
            Assert.Null(RainPackReader.TryRead(root, hash));
            File.WriteAllText(path, manifest.Replace("idle.strip.png", "../escape"));
            Assert.Null(RainPackReader.TryRead(root, hash));
            if (!OperatingSystem.IsWindows()) { File.WriteAllText(Path.Combine(folder, "C:escape"), "x"); File.WriteAllText(path, manifest.Replace("idle.strip.png", "C:escape")); Assert.Null(RainPackReader.TryRead(root, hash)); }
            File.WriteAllText(path, manifest.Replace("idle:{", "Idle:{")); Assert.Null(RainPackReader.TryRead(root, hash));
            File.WriteAllText(path, manifest.Replace("frames:2", "frames:0"));
            Assert.Null(RainPackReader.TryRead(root, hash));
            File.WriteAllText(path, manifest);
            File.Delete(Path.Combine(folder, "clip_timing.json"));
            Assert.Null(RainPackReader.TryRead(root, hash));
        }
        finally { Directory.Delete(root, true); }
    }
}

using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class RaceModMediaTests
{
    [Theory]
    [InlineData("mix", 2)]
    [InlineData("replace", 1)]
    [InlineData("bad", 2)]
    public void ModVisualsMergeOrReplaceOnlyPopulatedKinds(string mode, int images)
    {
        var dir = Directory.CreateTempSubdirectory("race-mod-").FullName;
        var old = CoreMods.ActiveModPackageProvider;
        try
        {
            var root = Path.Combine(dir, "resources", "dtrh");
            Directory.CreateDirectory(Path.Combine(root, "images"));
            Directory.CreateDirectory(Path.Combine(root, "videos"));
            File.WriteAllText(Path.Combine(root, "dtrh.json"), "{\"imageMode\":\"" + mode + "\"}");
            File.WriteAllText(Path.Combine(root, "images", "mod pic.png"), "image");
            File.WriteAllText(Path.Combine(root, "images", "empty.jpg"), "");
            File.WriteAllText(Path.Combine(root, "videos", "ignore.txt"), "text");
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest(), dir, false);
            var m = new GameMediaManifest.Manifest();
            m.Images.Add(new("local", "/local.png"));
            m.Videos.Add(new("local-video", "/local.mp4"));
            GameMediaManifest.MergeRaceModMedia(m, rel => "/mod/" + rel);
            Assert.Equal(images, m.Images.Count);
            Assert.Contains(new GameMediaManifest.Entry("mod:mod pic.png", "/mod/images/mod pic.png"), m.Images);
            Assert.Single(m.Videos);
            File.WriteAllText(Path.Combine(root, "videos", "clip.mp4"), "video");
            var next = new GameMediaManifest.Manifest();
            GameMediaManifest.MergeRaceModMedia(next, rel => rel);
            Assert.Contains(new GameMediaManifest.Entry("mod:clip.mp4", "videos/clip.mp4"), next.Videos);
        }
        finally { CoreMods.ActiveModPackageProvider = old; Directory.Delete(dir, true); }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

// Decision 2026-10-25 (docs/avalonia-decisions.md): the Avalonia manual restore keeps the local value of every
// absolute path the restored backup names but this machine does not have.
public sealed class CloudRestorePathGuardTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("ccp-restore-paths-");
    public void Dispose() => _dir.Delete(recursive: true);

    private string Here(string name)
    {
        var p = Path.Combine(_dir.FullName, name);
        File.WriteAllText(p, "");
        return p;
    }

    [Fact]
    public void AWindowsBackupOnThisMachine_KeepsEachLocalPath_AndAdoptsThePathsThatExist()
    {
        var local = new AppSettings
        {
            StartupVideoPath = Here("intro.mp4"), SpiralPath = Here("spiral.gif"), MindWipeAudioPath = Here("wipe.mp3"),
            WallpaperSourceFolder = _dir.FullName, WallpaperOriginalPath = Here("wall.png"), DeeperLastDirectory = _dir.FullName,
            BlinkTrainerFolders = new() { _dir.FullName }, DeeperRecentFiles = new() { Here("deep.json") },
            CornerGifOverlays = new() { new() { GifPath = Here("corner.gif") } },
            AudioLayers = new() { new() { Path = Here("layer.mp3") } },
            KeywordTriggers = new() { new() { Id = "k1", AudioFilePath = Here("trigger.mp3") } },
        };
        var theirSpiral = Here("their-spiral.gif");
        var theirLayer = Here("their-layer.mp3");
        const string win = @"C:\Users\bambi\Pictures\";
        var restored = new AppSettings
        {
            StartupVideoPath = win + "intro.mp4", SpiralPath = theirSpiral, MindWipeAudioPath = win + "wipe.mp3",
            WallpaperSourceFolder = win + "walls", WallpaperOriginalPath = win + "wall.png", DeeperLastDirectory = win + "deeper",
            BlinkTrainerFolders = new() { win + "blink", _dir.FullName }, DeeperRecentFiles = new() { win + "deep.json" },
            CornerGifOverlays = new() { new() { GifPath = win + "corner.gif" }, new() { GifPath = win + "second.gif" } },
            AudioLayers = new() { new() { Path = win + "layer.mp3" }, new() { Path = theirLayer, Volume = 33 } },
            KeywordTriggers = new() { new() { Id = "k1", AudioFilePath = win + "trigger.mp3" }, new() { Id = "k2", AudioFilePath = win + "new.mp3" } },
        };
        var localValues = (local.StartupVideoPath, local.MindWipeAudioPath, local.WallpaperSourceFolder,
            local.WallpaperOriginalPath, local.DeeperLastDirectory);

        var kept = CloudSettingsBackup.KeepLocalMissingPaths(local, restored);

        Assert.Equal(new[]
        {
            "StartupVideoPath", "MindWipeAudioPath", "WallpaperSourceFolder", "WallpaperOriginalPath", "DeeperLastDirectory",
            "BlinkTrainerFolders", "DeeperRecentFiles", "AudioLayers", "CornerGifOverlays",
            "KeywordTriggers",
        }, kept);
        Assert.Equal(localValues, (restored.StartupVideoPath, restored.MindWipeAudioPath, restored.WallpaperSourceFolder,
            restored.WallpaperOriginalPath, restored.DeeperLastDirectory));
        Assert.Equal(theirSpiral, restored.SpiralPath);                                     // exists here: adopted
        Assert.Equal(new[] { _dir.FullName }, restored.BlinkTrainerFolders);                // existing kept once
        Assert.Equal(local.DeeperRecentFiles, restored.DeeperRecentFiles);
        Assert.Equal(new[] { theirLayer, local.AudioLayers[0].Path }, restored.AudioLayers.Select(l => l.Path));
        Assert.Equal(33, restored.AudioLayers[0].Volume);
        Assert.Equal(new[] { local.CornerGifOverlays[0].GifPath, "" }, restored.CornerGifOverlays.Select(c => c.GifPath));
        Assert.Equal(new[] { local.KeywordTriggers[0].AudioFilePath, null }, restored.KeywordTriggers.Select(t => t.AudioFilePath));
    }

    [Fact]
    public void RelativeAssetLists_PassThrough_WithForwardSlashes()
    {
        var restored = new AppSettings
        {
            ActiveAssetPaths = new() { @"images\a.png", "videos/b.mp4" },
            DisabledAssetPaths = new() { @"images\sub\c.png" },
            DisabledAssetFolders = new() { @"gifs\loops" },
            AssetPresets = new()
            {
                new AssetPreset { DisabledAssetPaths = new() { @"images\d.png" }, DisabledAssetFolders = new() { @"videos\old" } },
            },
        };

        Assert.Empty(CloudSettingsBackup.KeepLocalMissingPaths(new AppSettings(), restored));

        Assert.Equal(new[] { "images/a.png", "videos/b.mp4" }, restored.ActiveAssetPaths.OrderBy(p => p));
        Assert.Equal(new[] { "images/sub/c.png" }, restored.DisabledAssetPaths);
        Assert.Equal(new[] { "gifs/loops" }, restored.DisabledAssetFolders);
        Assert.Equal(new[] { "images/d.png" }, restored.AssetPresets[0].DisabledAssetPaths);
        Assert.Equal(new[] { "videos/old" }, restored.AssetPresets[0].DisabledAssetFolders);
    }

    // Path-like settings the restore guard deliberately leaves alone, each with its reason.
    private static readonly Dictionary<string, string> NotGuarded = new()
    {
        ["CustomAssetsPath"] = "never uploaded; PreserveLocalOnlyFields keeps the local value",
        ["AudioPlaylistPaths"] = "never uploaded (AudioPlaylist*); kept local",
    };

    private static readonly Regex PathLike = new("(Path|Folder|Directory|File)s?$");

    private static bool Settable(PropertyInfo p) =>
        p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute));

    private static bool Stringy(Type t) => t == typeof(string) || typeof(IEnumerable<string>).IsAssignableFrom(t);

    [Fact]
    public void EveryPathLikeSetting_IsGuarded_RelativeOrExplicitlyAllowed()
    {
        var found = new List<string>();
        foreach (var p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(Settable))
        {
            if (Stringy(p.PropertyType) && PathLike.IsMatch(p.Name)) found.Add(p.Name);
            if (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(List<>)
                && p.PropertyType.GetGenericArguments()[0] is { IsClass: true } item && item != typeof(string))
                found.AddRange(item.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(q => Settable(q) && Stringy(q.PropertyType) && PathLike.IsMatch(q.Name))
                    .Select(q => p.Name + "." + q.Name));
        }

        var known = CloudSettingsBackup.GuardedPathFields.Concat(CloudSettingsBackup.RelativePathFields).Concat(NotGuarded.Keys).ToList();
        Assert.Empty(found.Except(known));   // a new path-like setting: guard it or list it in NotGuarded with a reason
        Assert.Empty(known.Except(found));   // a listed name that no longer exists
    }
}

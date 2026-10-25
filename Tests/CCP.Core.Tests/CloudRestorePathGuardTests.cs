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
    public void AnEmptyBackupValue_OrFewerCornerSlots_NeverClearAWorkingLocalPath()
    {
        var local = new AppSettings
        {
            SpiralPath = Here("spiral.gif"), StartupVideoPath = Here("intro.mp4"), WallpaperSourceFolder = "/nowhere/walls",
            CornerGifOverlays = new() { new() { GifPath = Here("c0.gif") }, new() { GifPath = Here("c1.gif") } },
        };
        var restored = new AppSettings
        {
            SpiralPath = "", StartupVideoPath = null, WallpaperSourceFolder = "",
            CornerGifOverlays = new() { new() { GifPath = "" } },
        };

        var kept = CloudSettingsBackup.KeepLocalMissingPaths(local, restored);

        Assert.Equal(new[] { "StartupVideoPath", "SpiralPath", "CornerGifOverlays" }, kept);
        Assert.Equal((local.SpiralPath, local.StartupVideoPath), (restored.SpiralPath, restored.StartupVideoPath));
        Assert.Equal("", restored.WallpaperSourceFolder);   // the local one is broken too: nothing kept, nothing listed
        Assert.Equal(local.CornerGifOverlays.Select(c => c.GifPath), restored.CornerGifOverlays.Select(c => c.GifPath));
        Assert.Empty(CloudSettingsBackup.KeepLocalMissingPaths(local, restored));   // nothing left to keep: nothing listed
    }

    [Fact]
    public void KeywordTriggerAudioActions_AndPresetTriggers_KeepTheLocalFile_ByActionId()
    {
        var local = new AppSettings
        {
            KeywordTriggers = new() { new() { Id = "k1", Actions =
            {
                new PlayAudioAction { Id = "a1", FilePath = Here("a1.mp3") }, new PlayAudioAction { Id = "a2", FilePath = Here("a2.mp3") },
            } } },
            KeywordTriggerPresets = new() { new() { Id = "p", Triggers = { new() { Id = "pk", AudioFilePath = Here("pk.mp3"),
                Actions = { new PlayAudioAction { Id = "pa", FilePath = Here("pa.mp3") } } } } } },
        };
        var theirA2 = Here("their-a2.mp3");
        const string win = @"C:\Users\bambi\";
        var restored = new AppSettings
        {
            KeywordTriggers = new() { new() { Id = "k1", Actions =
            {
                new PlayAudioAction { Id = "a1", FilePath = win + "a1.mp3" }, new PlayAudioAction { Id = "a2", FilePath = theirA2 },
                new PlayAudioAction { Id = "a3", FilePath = win + "a3.mp3" },
            } } },
            KeywordTriggerPresets = new() { new() { Id = "p", Triggers = { new() { Id = "pk", AudioFilePath = win + "pk.mp3",
                Actions = { new PlayAudioAction { Id = "pa", FilePath = "" } } } } } },
        };

        var kept = CloudSettingsBackup.KeepLocalMissingPaths(local, restored);

        Assert.Equal(new[] { "KeywordTriggers", "KeywordTriggerPresets" }, kept);
        Assert.Equal(new[] { ((PlayAudioAction)local.KeywordTriggers[0].Actions[0]).FilePath, theirA2, null },
            restored.KeywordTriggers[0].Actions.OfType<PlayAudioAction>().Select(a => a.FilePath));
        var preset = restored.KeywordTriggerPresets[0].Triggers[0];
        Assert.Equal(local.KeywordTriggerPresets[0].Triggers[0].AudioFilePath, preset.AudioFilePath);
        Assert.Equal(((PlayAudioAction)local.KeywordTriggerPresets[0].Triggers[0].Actions[0]).FilePath,
            ((PlayAudioAction)preset.Actions[0]).FilePath);

        Assert.Empty(CloudSettingsBackup.KeepLocalMissingPaths(local, restored));   // nothing left to keep: nothing listed
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

    // Walks List<T> items (and every concrete subclass of T in its assembly) down to KeywordTriggerPresets.Triggers.Actions.
    private static IEnumerable<string> PathLikeFields(Type type, string prefix, int depth)
    {
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(Settable))
        {
            if (Stringy(p.PropertyType) && PathLike.IsMatch(p.Name)) yield return prefix + p.Name;
            if (depth < 3 && p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(List<>)
                && p.PropertyType.GetGenericArguments()[0] is { IsClass: true } item && item != typeof(string))
                foreach (var sub in item.Assembly.GetTypes().Where(t => !t.IsAbstract && item.IsAssignableFrom(t)))
                    foreach (var f in PathLikeFields(sub, prefix + p.Name + ".", depth + 1)) yield return f;
        }
    }

    [Fact]
    public void EveryPathLikeSetting_IsGuarded_RelativeOrExplicitlyAllowed()
    {
        var found = PathLikeFields(typeof(AppSettings), "", 0).Distinct().ToList();

        var known = CloudSettingsBackup.GuardedPathFields.Concat(CloudSettingsBackup.RelativePathFields).Concat(NotGuarded.Keys).ToList();
        Assert.Empty(found.Except(known));   // a new path-like setting: guard it or list it in NotGuarded with a reason
        Assert.Empty(known.Except(found));   // a listed name that no longer exists
    }

    // Listing a top-level field is not enough: each one must really keep the local path.
    [Fact]
    public void EachListedTopLevelField_KeepsTheLocalPath()
    {
        var mine = Here("mine.bin");
        foreach (var name in CloudSettingsBackup.GuardedPathFields.Where(n => !n.Contains('.')))
        {
            var p = typeof(AppSettings).GetProperty(name)!;
            foreach (var theirs in new[] { @"C:\Users\bambi\missing.bin", "" })
            {
                var (local, restored) = (new AppSettings(), new AppSettings());
                if (p.PropertyType == typeof(string)) { p.SetValue(local, mine); p.SetValue(restored, theirs); }
                else if (p.PropertyType == typeof(List<string>))
                {
                    if (theirs == "") continue;
                    p.SetValue(local, new List<string> { mine });
                    p.SetValue(restored, new List<string> { theirs });
                }
                else Assert.Fail(name + ": not a string or List<string>");

                var kept = CloudSettingsBackup.KeepLocalMissingPaths(local, restored);

                Assert.Contains(name, kept);
                if (p.GetValue(restored) is List<string> list) Assert.Equal(new[] { mine }, list);
                else Assert.Equal(mine, p.GetValue(restored));
            }
        }
    }
}

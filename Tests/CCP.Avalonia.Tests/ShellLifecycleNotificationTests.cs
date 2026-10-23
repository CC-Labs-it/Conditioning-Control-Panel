using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>wire-shell-notifications-toasts-b: companion level-up balloons (WPF MainWindow.Companion.cs:48) and the
/// empty-library check behind WarnIf*LibraryEmpty (WPF MainWindow.StartStop.cs:180).</summary>
public sealed class ShellLifecycleNotificationTests
{
    private static List<(string Title, string Body)> Capture(System.Action act)
    {
        var seen = new List<(string, string)>();
        var old = OsNotifications.Sink;
        OsNotifications.Sink = (t, b) => seen.Add((t, b));
        try { act(); } finally { OsNotifications.Sink = old; }
        return seen;
    }

    [Fact]
    public void CompanionLevelUp_notifies_at_max_and_every_tenth_level_only()
    {
        var name = CompanionDefinition.GetById(CompanionId.CultBunny).Name;
        Assert.Equal(new[] { ("MAX LEVEL!", $"{name} has reached maximum level!") },
            Capture(() => MainShellWindow.NotifyCompanionLevelUp(CompanionId.CultBunny, CompanionProgress.MaxLevel)));
        Assert.Equal(new[] { ("Companion Level Up!", $"{name} reached Level 20!") },
            Capture(() => MainShellWindow.NotifyCompanionLevelUp(CompanionId.CultBunny, 20)));
        Assert.Empty(Capture(() => MainShellWindow.NotifyCompanionLevelUp(CompanionId.CultBunny, 7)));
    }

    [Fact]
    public void CompanionMilestones_play_the_level_up_sound_after_the_notice_and_replace_the_previous_clip()
    {
        var oldAudio = CoreAudio.PlayStoppableProvider;
        var oldSink = OsNotifications.Sink;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var seen = new List<string>();
        var volumes = new List<float>();
        CoreAudio.PlayStoppableProvider = (path, volume, tag, _, _) =>
        {
            Assert.Equal("lvup.mp3", Path.GetFileName(path));
            Assert.Equal("level-up", tag);
            volumes.Add(volume);
            seen.Add("play");
            return () => seen.Add("stop");
        };
        OsNotifications.Sink = (_, _) => seen.Add("notice");
        try
        {
            CoreSettings.Current.MasterVolume = 50;
            MainShellWindow.NotifyCompanionLevelUp(CompanionId.CultBunny, 7);
            Assert.Empty(seen);
            MainShellWindow.NotifyCompanionLevelUp(CompanionId.CultBunny, 10);
            MainShellWindow.NotifyCompanionLevelUp(CompanionId.CultBunny, CompanionProgress.MaxLevel);
            Assert.Equal(new[] { "notice", "play", "notice", "stop", "play" }, seen);
            Assert.All(volumes, v => Assert.Equal((float)Math.Pow(0.5, 1.5) * 0.2625f, v, 6));
        }
        finally
        {
            // Also leaves no clip alive when this test is run against the pre-fix implementation.
            foreach (var surface in PanicSurfaces.All)
                if (surface.Id == "level-up-sound") surface.Stop(null);
            CoreAudio.PlayStoppableProvider = oldAudio;
            OsNotifications.Sink = oldSink;
            CoreSettings.Current.MasterVolume = oldVolume;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(100)]
    public void LevelUpSound_uses_Wpf_lookup_precedence_volume_floor_and_panic_cleanup(int master)
    {
        var oldAudio = CoreAudio.PlayStoppableProvider;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var dir = Directory.CreateTempSubdirectory("ccp-level-sound-").FullName;
        var paths = new[]
        {
            Path.Combine(dir, "Resources", "sounds", "lvup.mp3"),
            Path.Combine(dir, "Resources", "lvlup.mp3"),
            Path.Combine(dir, "assets", "sounds", "lvlup.mp3"),
        };
        var played = new List<string>();
        var volumes = new List<float>();
        int stopped = 0;
        MainShellWindow.StopLevelUpSound();
        CoreAudio.PlayStoppableProvider = (path, volume, tag, _, _) =>
        {
            played.Add(path);
            volumes.Add(volume);
            return () => stopped++;
        };
        try
        {
            CoreSettings.Current.MasterVolume = master;
            foreach (var path in paths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "fake audio: provider captures without a device");
            }
            foreach (var path in paths)
            {
                MainShellWindow.PlayLevelUpSound(dir);
                Assert.Equal(path, played.Last());
                File.Delete(path);
            }
            Assert.All(volumes, v => Assert.Equal(Math.Max(0.01f, (float)Math.Pow(master / 100f, 1.5) * 0.2625f), v, 6));
            Assert.Equal(2, stopped); // each replacement stops the preceding clip
            MainShellWindow.PlayLevelUpSound(dir);
            Assert.Equal(3, played.Count); // missing files neither play nor stop the current clip
            Assert.Equal(2, stopped);
            var panic = Assert.Single(PanicSurfaces.All, s => s.Id == "level-up-sound");
            panic.Stop(null);
            panic.Stop(null);
            Assert.Equal(3, stopped); // idle/panic cleanup is idempotent
        }
        finally
        {
            MainShellWindow.StopLevelUpSound();
            CoreAudio.PlayStoppableProvider = oldAudio;
            CoreSettings.Current.MasterVolume = oldVolume;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FolderHasAnyMedia_matches_nested_extensions_case_insensitively()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-media-").FullName;
        try
        {
            string[] exts = { ".png", ".jpg" };
            Assert.False(MainShellWindow.FolderHasAnyMedia(Path.Combine(dir, "missing"), exts));
            Assert.False(MainShellWindow.FolderHasAnyMedia(dir, exts));
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
            Assert.False(MainShellWindow.FolderHasAnyMedia(dir, exts));
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "sub", "a.PNG"), "x");
            Assert.True(MainShellWindow.FolderHasAnyMedia(dir, exts));
        }
        finally { Directory.Delete(dir, true); }
    }
}

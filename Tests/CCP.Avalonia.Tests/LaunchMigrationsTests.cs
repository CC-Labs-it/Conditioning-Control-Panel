using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF App.xaml.cs:2582-2589, 2612-2624, 2787, 333, 6480/6515.</summary>
public class LaunchMigrationsTests
{
    [Fact]
    public void LegacyProfileGetsGazeOffOgSpriteAndDropFolders()
    {
        var root = Directory.CreateTempSubdirectory("ccp-launch-mig");
        try
        {
            var settings = new SettingsService();
            var s = settings.Current;
            s.MigratedFlashClickableDecoupling = false;
            s.FlashClickable = false;
            s.FlashGazePopEnabled = true;
            s.FlashGazeLingerEnabled = true;
            s.CompanionProgressData.Clear();
            s.PlayerLevel = 40;
            s.ActiveCompanionId = 3;
            var assets = Path.Combine(root.FullName, "assets");

            AvApp.RunLaunchMigrations(settings, assets, root.FullName);

            Assert.False(s.FlashGazePopEnabled);
            Assert.False(s.FlashGazeLingerEnabled);
            Assert.True(s.MigratedFlashClickableDecoupling);
            Assert.Equal((int)CompanionId.OGBambiSprite, s.ActiveCompanionId);
            Assert.Equal(20, s.CompanionProgressData[(int)CompanionId.OGBambiSprite].Level);
            foreach (var sub in new[] { "images", "videos", "audio", "wallpapers", "mindwipe" })
                Assert.True(Directory.Exists(Path.Combine(assets, sub)), sub);
            Assert.True(Directory.Exists(Path.Combine(root.FullName, "Spirals")));

            // One-shot: existing progress is left alone.
            s.PlayerLevel = 200;
            s.CompanionProgressData[(int)CompanionId.OGBambiSprite].Level = 7;
            AvApp.RunLaunchMigrations(settings, assets, root.FullName);
            Assert.Equal(7, s.CompanionProgressData[(int)CompanionId.OGBambiSprite].Level);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void ExitFlushWritesPendingEmiState()
    {
        var path = EmiState.FilePath;
        var st = EmiState.Current;
        int pets0 = st.PetsTotal; bool gist0 = st.PetGistGot;
        try
        {
            int pets = EmiState.NotePet();   // debounced SaveSoon
            if (File.Exists(path)) File.Delete(path);
            AvApp.FlushEmiDeskOnExit();
            Assert.True(File.Exists(path));
            Assert.Contains(pets.ToString(), File.ReadAllText(path));
        }
        finally { st.PetsTotal = pets0; st.PetGistGot = gist0; }
    }

    [Fact]
    public void StartupSeedsPortraitProbeAndExitFiresAppClosing()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "CCP.Avalonia", "App.axaml.cs"))) dir = Path.GetDirectoryName(dir)!;
        var app = File.ReadAllText(Path.Combine(dir, "CCP.Avalonia", "App.axaml.cs"));
        Assert.Contains("CoreModArt.HasAvatarPortraitsProvider = () => Platform.AvatarPortraitLoader.Load() != null;", app);
        Assert.Contains("Fire(\"appClosing\", null)", app);
        Assert.Contains("FlushEmiDeskOnExit();", app[app.IndexOf("private void OnDesktopExit", StringComparison.Ordinal)..]);
        Assert.Contains("RunLaunchMigrations(Settings, DefaultAssetsRoot, CorePaths.UserData);", app);
    }
}

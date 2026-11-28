using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using EmiDeskService = ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk.EmiDeskService;

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
            // A private settings file: the migration's debounced save must not reach the shared profile.
            var file = Path.Combine(root.FullName, "settings.json");
            var settings = new SettingsService(file);
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
            Assert.True(s.FlashGazeDisabledByDecoupling);
            Assert.True(s.MigratedFlashClickableDecoupling);
            Assert.Equal((int)CompanionId.OGBambiSprite, s.ActiveCompanionId);
            Assert.Equal(20, s.CompanionProgressData[(int)CompanionId.OGBambiSprite].Level);
            foreach (var sub in new[] { "images", "videos", "audio", "wallpapers", "mindwipe" })
                Assert.True(Directory.Exists(Path.Combine(assets, sub)), sub);
            Assert.True(Directory.Exists(Path.Combine(root.FullName, "Spirals")));

            // The migration saved (debounced, as WPF); flush it now so no timer outlives the test.
            Assert.True(settings.HasPendingSave);
            Assert.True(settings.TrySaveImmediate());
            Assert.False(settings.HasPendingSave);
            Assert.True(new SettingsService(file).Current.MigratedFlashClickableDecoupling);

            // One-shot: existing progress is left alone, and a migrated profile's launch does not write.
            s.PlayerLevel = 200;
            s.CompanionProgressData[(int)CompanionId.OGBambiSprite].Level = 7;
            AvApp.RunLaunchMigrations(settings, assets, root.FullName);
            Assert.Equal(7, s.CompanionProgressData[(int)CompanionId.OGBambiSprite].Level);
            Assert.False(settings.HasPendingSave);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void PersonalityLegacyMigrationRunsOnEveryLaunch()
    {
        var root = Directory.CreateTempSubdirectory("ccp-personality-mig");
        var oldProvider = CoreSettings.ServiceProvider;
        try
        {
            var settings = new SettingsService(Path.Combine(root.FullName, "settings.json"));
            // Core's migration saves through CoreSettings.Service, so point it at this test's settings.
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            var assets = Path.Combine(root.FullName, "assets");
            AvApp.RunLaunchMigrations(settings, assets, root.FullName); // one-shot migrations out of the way
            settings.TrySaveImmediate();
            s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
            s.SlutModeEnabled = false;
            AvApp.RunLaunchMigrations(settings, assets, root.FullName);
            Assert.Equal(PersonalityPresets.NeutralDefaultId, s.ActivePersonalityPresetId);
            Assert.False(settings.HasPendingSave); // unchanged profile: startup leaves settings.json alone

            s.SlutModeEnabled = true;
            AvApp.RunLaunchMigrations(settings, assets, root.FullName);
            Assert.Equal(PersonalityPresets.SlutModeId, s.ActivePersonalityPresetId);
            Assert.True(settings.HasPendingSave);
            Assert.True(settings.TrySaveImmediate());
        }
        finally { CoreSettings.ServiceProvider = oldProvider; root.Delete(true); }
    }

    // WPF App.xaml.cs:2612-2633: the migration reads the saved FlashClickable before the crash heal puts the
    // journal's value back. A session left clicking on (journal: off) must keep gaze pop and linger on.
    [Fact]
    public void FlashMigrationRunsBeforeTheSessionHeal()
    {
        var root = Directory.CreateTempSubdirectory("ccp-launch-heal");
        try
        {
            var settings = new SettingsService(Path.Combine(root.FullName, "settings.json"));
            var s = settings.Current;
            s.MigratedFlashClickableDecoupling = false;
            s.FlashGazeDisabledByDecoupling = false;
            s.FlashClickable = true;
            s.FlashGazePopEnabled = true;
            s.FlashGazeLingerEnabled = true;
            File.WriteAllText(Path.Combine(root.FullName, SessionSettingsJournal.FileName),
                """{"at":1,"fields":{"FlashClickable":false}}""");

            AvApp.RunLaunchMigrations(settings, Path.Combine(root.FullName, "assets"), root.FullName);
            Assert.True(SessionSettingsJournal.HealOnLaunch(root.FullName, s));

            Assert.False(s.FlashClickable);
            Assert.True(s.FlashGazePopEnabled);
            Assert.True(s.FlashGazeLingerEnabled);
            Assert.False(s.FlashGazeDisabledByDecoupling);
            settings.TrySaveImmediate();

            // And startup calls them in that order.
            var app = File.ReadAllText(Path.Combine(ConditioningControlPanel.Tests.SourceRoots.RepoRoot, "CCP.Avalonia", "App.axaml.cs"));
            int mig = app.IndexOf("RunLaunchMigrations(Settings,", StringComparison.Ordinal);
            int heal = app.IndexOf("SessionSettingsJournal.HealOnLaunch(", StringComparison.Ordinal);
            Assert.True(mig >= 0 && heal > mig, "RunLaunchMigrations must run before SessionSettingsJournal.HealOnLaunch");
        }
        finally { root.Delete(true); }
    }

    private const string ClosingLines = """{"version":1,"moments":{"appClosing":{"hold":true,"holdMs":60000,"pools":[]}},"pools":{}}""";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // WPF App.OnExit:6480 fires the wordless appClosing hold, then :6515 EmiDesk.Dispose writes emi-desk.json
    // now (EmiDeskService.cs:2121) and drops the app event subscriptions.
    [Fact]
    public Task ExitFiresAppClosingUnwiresAndFlushesPendingEmiState() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        // Swaps in a fresh EmiState and line engine, drains the save timer and puts emi-desk.json back.
        using var scope = new EmiTestScope();
        var desk = EmiDeskService.Instance;
        var engine0 = desk.LineEngine;
        var wired = typeof(EmiDeskService).GetField("_eventsWired", Private)!;
        var sources = new[] { "_momentAutonomy", "_momentSpeech", "_momentDailyFree" };
        bool wired0 = (bool)wired.GetValue(desk)!;
        var src0 = Array.ConvertAll(sources, n => typeof(EmiDeskService).GetField(n, Private)!.GetValue(desk));
        var engine = EmiLineEngine.FromJson(ClosingLines);
        try
        {
            desk.LineEngine = engine;
            desk.WireAppEvents(null, null, null);
            int pets = EmiState.NotePet();
            // Stop the 500 ms debounce so only the exit flush can write the file.
            var timerField = typeof(EmiState).GetField("_saveTimer", BindingFlags.Static | BindingFlags.NonPublic)!;
            ((Timer?)timerField.GetValue(null))?.Dispose();
            timerField.SetValue(null, null);
            if (File.Exists(EmiState.FilePath)) File.Delete(EmiState.FilePath);

            AvApp.FlushEmiDeskOnExit();

            Assert.True(engine.HoldActive);
            Assert.False((bool)wired.GetValue(desk)!);
            Assert.Equal(pets, (int)JObject.Parse(File.ReadAllText(EmiState.FilePath))["petsTotal"]!);
        }
        finally
        {
            desk.UnwireAppEvents();
            desk.LineEngine = engine0;
            if (wired0)
                desk.WireAppEvents((ConditioningControlPanel.Services.AutonomyScheduler?)src0[0],
                    (ConditioningControlPanel.Services.Speech.SpeechEngine?)src0[1], (DailyFreeService?)src0[2]);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void PortraitProbeAnswersFromTheActiveModsPortraitsOnDisk()
    {
        var (probe0, mod0, id0) = (CoreModArt.HasAvatarPortraitsProvider, CoreMods.ActiveModPackageProvider, CoreMods.ActiveModIdProvider);
        var root = Directory.CreateTempSubdirectory("ccp-portrait-probe");
        try
        {
            var audio = Directory.CreateDirectory(Path.Combine(root.FullName, "resources", "sounds", "companion_audio")).FullName;
            var mod = new ModPackage(new ModManifest { Id = "portrait-probe" }, root.FullName, false);
            CoreMods.ActiveModPackageProvider = () => mod;
            CoreMods.ActiveModIdProvider = () => "portrait-probe";
            CoreModArt.HasAvatarPortraitsProvider = null;

            AvApp.SeedPortraitProbe();
            Assert.False(CoreModArt.HasAvatarPortraits);   // no manifest

            File.WriteAllText(Path.Combine(audio, "avatar_manifest.json"),
                """{"idleEmotion":"neutral","skins":[{"id":"a","dir":"a"}],"emotions":{"neutral":{"portraits":[{"file":"idle.png"}]}}}""");
            Assert.False(CoreModArt.HasAvatarPortraits);   // manifest, but no portrait on disk

            File.Copy(Path.Combine(ConditioningControlPanel.Tests.SourceRoots.RepoRoot, "Assets", "avatar_pose1.png"),
                Path.Combine(Directory.CreateDirectory(Path.Combine(audio, "a")).FullName, "idle.png"));
            Assert.True(CoreModArt.HasAvatarPortraits);
        }
        finally
        {
            (CoreModArt.HasAvatarPortraitsProvider, CoreMods.ActiveModPackageProvider, CoreMods.ActiveModIdProvider) = (probe0, mod0, id0);
            root.Delete(true);
        }
    }

    [Fact]
    public void StartupAndExitCallTheSeams()
    {
        var app = File.ReadAllText(Path.Combine(ConditioningControlPanel.Tests.SourceRoots.RepoRoot, "CCP.Avalonia", "App.axaml.cs"));
        Assert.Contains("SeedPortraitProbe();", app);
        Assert.Contains("RunLaunchMigrations(Settings, DefaultAssetsRoot, CorePaths.UserData);", app);
        Assert.Contains("FlushEmiDeskOnExit();", app[app.IndexOf("private void OnDesktopExit", StringComparison.Ordinal)..]);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.Migration.Tests;

/// <summary>CorePaths.UserData is immutable for the process and Avalonia sets up once per
/// process, so this suite owns its testhost and runs exactly one desktop startup.</summary>
internal static class MigrationTestProfile
{
    internal static string Root { get; } = Path.Combine(
        Path.GetTempPath(), "ccp-migration-" + Guid.NewGuid().ToString("N"));

    internal static string UserData => Path.Combine(Root, "userdata");

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(UserData);
        // Nothing may reach the real ~/.config, ~/.local or the session bus (P74).
        foreach (var xdg in new[] { "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME" })
            Environment.SetEnvironmentVariable(xdg, Directory.CreateDirectory(Path.Combine(Root, xdg)).FullName);
        Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", "disabled:");
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", UserData);
        _ = CorePaths.UserData;   // installs the SandboxNet guard before any test runs
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        };
    }
}

/// <summary>
/// No data loss when a WPF user-data folder is opened by the Avalonia head: copy the profile,
/// run the real desktop startup and shutdown headless, then compare. Decision 2026-10-14 (user):
/// the real profile is LOCAL ONLY (CCP_MIGRATION_PROFILE); CI runs the synthetic fixture through
/// the same assertions.
/// </summary>
public sealed class RealProfileMigrationTests
{
    private const string ProfileVariable = "CCP_MIGRATION_PROFILE";
    /// <summary>Optional: WPF keeps achievements.json in AppData\Roaming\ConditioningControlPanel,
    /// not beside settings.json. Only that file is copied from it.</summary>
    private const string RoamingVariable = "CCP_MIGRATION_ROAMING";

    private sealed class ProfileApp : App
    {
        protected override string AchievementsPath => Path.Combine(MigrationTestProfile.UserData, "achievements.json");
    }

    [Fact]
    public async Task RealWpfProfileSurvivesAvaloniaStartupAndShutdown()
    {
        var source = Environment.GetEnvironmentVariable(ProfileVariable);
        if (string.IsNullOrWhiteSpace(source))
            Assert.Skip(ProfileVariable + " is not set (local-only input, decision 2026-10-14).");
        await RunMigrationAsync(source!, Environment.GetEnvironmentVariable(RoamingVariable));
    }

    [Fact]
    public async Task InPlaceWpf72ProfileSurvivesAvaloniaStartupAndShutdown()
    {
        if (Environment.GetEnvironmentVariable("CCP_MIGRATION_COPIED") == "1")
            Assert.Skip("Copied-profile test owns this process.");
        await SyntheticProfileAsync(inPlace: true);
    }

    [Fact]
    public async Task SyntheticWpfProfileSurvivesAvaloniaStartupAndShutdown()
    {
        if (Environment.GetEnvironmentVariable("CCP_MIGRATION_COPIED") != "1")
            Assert.Skip("Run with CCP_MIGRATION_COPIED=1 in a separate testhost (one Avalonia startup).");
        await SyntheticProfileAsync(inPlace: false);
    }

    private static async Task SyntheticProfileAsync(bool inPlace)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProfileVariable)))
            Assert.Skip("One desktop startup per process: the real profile run owns this testhost.");

        var fixture = inPlace ? MigrationTestProfile.UserData : Path.Combine(MigrationTestProfile.Root, "synthetic");
        Directory.CreateDirectory(Path.Combine(fixture, "mods", "premove"));
        Directory.CreateDirectory(Path.Combine(fixture, "session_logs"));
        // Serialized by main's WPF model; see wpf72_settings.provenance.txt.
        // Fixed migration stamps keep the generated profile reproducible.
        var settings = JObject.Parse(File.ReadAllText(Fixture("wpf72_settings.json")));
        settings["PersonaVoiceFenceUtc"] = "2026-10-01T12:00:00Z";
        settings["PersonaIdentityFenceUtc"] = "2026-10-01T12:00:00Z";
        // Open drawers on a tab the run never shows (the exit write used to reset them).
        settings["CompanionSectionOpen"] = new JObject { ["EngineRoom"] = true, ["Workshop"] = true };
        if (inPlace) settings["PatreonTier"] = 2;
        File.WriteAllText(Path.Combine(fixture, "settings.json"), settings.ToString());
        File.Copy(Fixture("mod_manifest_premove.json"), Path.Combine(fixture, "mods", "premove", "manifest.json"));
        File.Copy(Fixture("session_log_premove.json"), Path.Combine(fixture, "session_logs", "premove.json"));
        // Generated in 2025, so the daily and weekly roll (an expected transform) runs in CI too.
        File.Copy(Fixture("quests_golden.json"), Path.Combine(fixture, "quests.json"));
        File.WriteAllText(Path.Combine(fixture, "auth_token.dat"), "never copied");
        var roaming = Directory.CreateDirectory(Path.Combine(MigrationTestProfile.Root, "synthetic-roaming")).FullName;
        File.Copy(Fixture("achievements_golden.json"), Path.Combine(roaming, "achievements.json"));
        if (!inPlace)
        {
            await RunMigrationAsync(fixture, roaming);
            Assert.False(File.Exists(Path.Combine(MigrationTestProfile.UserData, "auth_token.dat")));
            return;
        }

        const string token = "{\"access_token\":\"migration-test-only\",\"refresh_token\":\"test-refresh\",\"expires_at\":\"2099-01-01T00:00:00Z\"}";
        var tokenPath = Path.Combine(fixture, "patreon_auth.dat");
        var secrets = new Dictionary<string, string> { ["patreon_auth"] = token,
            ["authtoken"] = "synthetic-auth", ["apikey"] = "synthetic-key" };
        if (OperatingSystem.IsWindows())
        {
            // WPF's writer format, independent of Avalonia's Dpapi.Write.
            File.WriteAllBytes(tokenPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(token),
                Encoding.UTF8.GetBytes("ConditioningControlPanel_Patreon_v1"), DataProtectionScope.CurrentUser));
            foreach (var (name, value) in secrets.Where(kv => kv.Key != "patreon_auth"))
            {
                var file = name == "authtoken" ? "auth_token.dat" : "api_key.dat";
                var label = name == "authtoken" ? "AuthToken" : "ApiKey";
                File.WriteAllBytes(Path.Combine(fixture, file), ProtectedData.Protect(Encoding.UTF8.GetBytes(value),
                    Encoding.UTF8.GetBytes($"ConditioningControlPanel_{label}_v1"), DataProtectionScope.CurrentUser));
            }
            SecretStore.Sandboxed = false; // DPAPI touches only our temporary userdata folder
        }
        else
        {
            File.WriteAllBytes(tokenPath, new byte[] { 9, 8, 7, 6 }); // opaque WPF file, never interpreted on Linux
            SecretStore.OsReadOverride = name => (true, secrets.GetValueOrDefault(name));
            SecretStore.OsWriteOverride = (_, _) => true;
        }
        var secretHashes = Directory.GetFiles(fixture, "*.dat").ToDictionary(p => p, Sha256);
        try
        {
            await RunMigrationAsync(fixture, roaming, inPlace: true);
            foreach (var (name, value) in secrets) Assert.Equal(value, CoreSecrets.Retrieve(name));
            Assert.Equal(2, (int)ReadJson(Path.Combine(fixture, "settings.json"))["PatreonTier"]!);
            foreach (var (path, hash) in secretHashes) Assert.Equal(hash, Sha256(path));
        }
        finally
        {
            SecretStore.OsReadOverride = null;
            SecretStore.OsWriteOverride = null;
            SecretStore.Sandboxed = true;
        }
    }

    // WPF Services/Progression/AchievementService.cs:17 uses AchievementStore.DefaultPath too.
    [Fact]
    public void RoamingRootRemainsWithTheProcessProfileWhenEnvironmentChanges()
    {
        var before = CorePaths.RoamingData;
        var original = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", Path.Combine(MigrationTestProfile.Root, "other"));
            Assert.Equal(before, CorePaths.RoamingData);
        }
        finally { Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", original); }
    }

    [Fact]
    public void EstablishedWpfKeysArePresentAndTheirLossFailsComparison()
    {
        var sample = ReadJson(Fixture("wpf72_settings.json"));
        foreach (var key in new[] { "HasSeenProgramsTab", "HasSeenProgramsIntro", "HasSeenDeeperTab",
            "HasSeededDeeperDemos", "HasSeenDeeperWelcome", "HasSeenDeeperEditorIntro",
            "HasSeenDeeperHTInteractiveTutorial", "InstallDate" })
        {
            Assert.True(sample.ContainsKey(key), "fixture missing: " + key);
            var saved = (JObject)sample.DeepClone();
            saved.Remove(key);
            Assert.Contains("key dropped with a non-default value: " + key,
                CompareSettings(sample, saved, _ => { }));
        }
    }

    [Fact]
    public void ComparisonRejectsLostKeysAndTokenBackedTierReset()
    {
        var before = new JObject { ["UnknownWpfKey"] = 42, ["PatreonTier"] = 2 };
        var failures = CompareSettings(before, new JObject { ["PatreonTier"] = 0 }, _ => { }, false).ToArray();
        Assert.Contains("unknown key (dropped on load): UnknownWpfKey", failures);
        Assert.Contains("value changed: PatreonTier", failures);
    }

    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "CCP.Core.Tests", "Fixtures", name);

    /// <summary>Secrets, browser profiles, caches and logs stay behind (decision 2026-10-14);
    /// logs are also appended by any run, so they are not user data to compare.</summary>
    internal static bool Copied(string relative)
    {
        var parts = relative.Split(Path.DirectorySeparatorChar);
        var top = parts[0];
        if (parts.Length > 1 && (top.StartsWith("browser_data", StringComparison.OrdinalIgnoreCase)
            || top.Equals("cache", StringComparison.OrdinalIgnoreCase)
            || top.Equals("logs", StringComparison.OrdinalIgnoreCase)))
            return false;
        return !relative.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>settings.json is compared key by key; its daily backups rotate by design.</summary>
    private static bool IsSettingsFile(string relative) =>
        relative == "settings.json" || (relative.StartsWith("settings.bak-") && relative.EndsWith(".json"));

    private static IEnumerable<string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
        }).Select(f => Path.GetRelativePath(root, f)).Where(Copied).OrderBy(f => f, StringComparer.Ordinal);

    private static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static JObject ReadJson(string path) => (JObject)ReadToken(path);

    private static JToken ReadToken(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new JsonTextReader(new StreamReader(stream)) { DateParseHandling = DateParseHandling.None };
        return JToken.Load(reader);
    }

    private static async Task RunMigrationAsync(string source, string? roaming, bool inPlace = false)
    {
        var profile = MigrationTestProfile.UserData;
        Assert.Equal(profile, CorePaths.UserData);
        Assert.True(File.Exists(Path.Combine(source, "settings.json")), "profile has no settings.json");

        // relative path in the copy -> source file. The sources are only ever opened for reading.
        var inputs = Files(source).ToDictionary(r => r, r => Path.Combine(source, r), StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(roaming))
        {
            Assert.False(inputs.ContainsKey("achievements.json"), "achievements.json in both inputs");
            inputs["achievements.json"] = Path.Combine(roaming, "achievements.json");   // ProfileApp.AchievementsPath
        }
        var original = ReadJson(Path.Combine(source, "settings.json"));
        var comparisons = new Dictionary<string, string>();
        if (inPlace)
            foreach (var (relative, from) in inputs.Where(kv => ExitFlushedJson.Contains(kv.Key)))
            {
                var snapshot = Path.Combine(MigrationTestProfile.Root, "before-" + relative);
                File.Copy(from, snapshot);
                comparisons[relative] = snapshot;
            }
        var sourceHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (relative, from) in inputs)
        {
            var to = inPlace && relative == "achievements.json" ? AchievementStore.DefaultPath : Path.Combine(profile, relative);
            if (inPlace && relative == "achievements.json")
                Assert.Equal(Path.Combine(CorePaths.RoamingData, "achievements.json"), to);
            if (from == to) { sourceHashes[relative] = Sha256(from); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write))
                input.CopyTo(output);
            sourceHashes[relative] = Sha256(from);
        }
        var settingsWrittenBefore = File.GetLastWriteTimeUtc(Path.Combine(profile, "settings.json"));

        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                (inPlace ? AppBuilder.Configure<App>() : AppBuilder.Configure<ProfileApp>())
                    .UseSkia()
                    .WithInterFont()
                    .With(AppFonts.Options(systemEmoji: false))
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithLifetime(lifetime);
                if (inPlace)
                {
                    Assert.IsType<App>(Application.Current);
                    Assert.Contains("first_bloom", App.Achievements!.Progress.UnlockedAchievements);
                    Assert.Equal(777, App.Achievements.Progress.TotalFlashImages);
                }
                else Assert.IsType<ProfileApp>(Application.Current);
                Dispatcher.UIThread.RunJobs();
                await AccountSeed.InitializeAsync();
                lifetime.MainWindow?.Show();
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                try { lifetime.Shutdown(); }
                finally { Dispatcher.UIThread.RunJobs(); }
            }
        });

        var failures = new List<string>();
        var expected = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Expect(string transform) => expected[transform] = expected.GetValueOrDefault(transform) + 1;
        var savedPath = Path.Combine(profile, "settings.json");
        if (File.GetLastWriteTimeUtc(savedPath) == settingsWrittenBefore)
            failures.Add("settings.json was not saved on shutdown (the round trip did not run)");
        failures.AddRange(CompareSettings(original, ReadJson(savedPath), Expect, allowMissingTokens: !inPlace));

        foreach (var (relative, hash) in sourceHashes.Where(kv => !IsSettingsFile(kv.Key)))
        {
            var copy = inPlace && relative == "achievements.json" ? AchievementStore.DefaultPath : Path.Combine(profile, relative);
            if (!File.Exists(copy)) failures.Add("file removed: " + relative);
            else if (Sha256(copy) == hash) continue;
            else if (ExpectedChange(relative, comparisons.GetValueOrDefault(relative, inputs[relative]), copy) is { } transform) Expect(transform);
            else failures.Add("file changed: " + relative);
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"migration: {original.Count} settings keys, {sourceHashes.Count} files checked, "
            + $"expected transforms [{string.Join(", ", expected.Select(kv => kv.Key + " " + kv.Value))}], {failures.Count} failure(s)");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static readonly string[] DailyRollKeys =
    {
        "DailyQuest", "DailyQuests", "DailyRerollsUsed", "DailyRerollResetDate", "DailyQuestGeneratedAt",
        "DailyQuestsCompletedToday", "DailyCompletionResetDate", "DailyRolledUnresolved",
    };

    private static readonly string[] WeeklyRollKeys =
    {
        "WeeklyQuest", "WeeklyRerollsUsed", "WeeklyRerollResetDate", "WeeklyQuestGeneratedAt", "WeeklyRolledUnresolved",
    };

    /// <summary>JSON files the app rewrites on exit (Dispose flush) with this OS's newlines.</summary>
    private static readonly HashSet<string> ExitFlushedJson = new(StringComparer.Ordinal)
    {
        "media_history.json", "feature_day_log.json", "quests.json", "achievements.json",
    };

    /// <summary>The named, expected changes to a non-settings file, or null (a failure). Each one is
    /// precise: nothing else in the file may differ.</summary>
    private static string? ExpectedChange(string relative, string sourcePath, string copyPath)
    {
        var parts = relative.Split(Path.DirectorySeparatorChar);
        // A profile moved to another root always re-extracts its built-in mods (ModService
        // ShouldExtract: the archive path changed); the stamp records the new path and time.
        if (parts.Length == 3 && parts[0] == "builtin_mods" && parts[2] == "pack.json")
        {
            var (was, now) = (ReadJson(sourcePath), ReadJson(copyPath));
            foreach (var key in new[] { "sourcePath", "extractedUtc" }) { was.Remove(key); now.Remove(key); }
            return JToken.DeepEquals(was, now) ? "pack-stamp-new-root" : null;
        }
        // The same re-extract re-applies CirceNeutralPackPatch: a builtin-locked file may become
        // byte for byte the override THIS release ships (a profile from an older release gets the
        // newer art, exactly as that WPF release would).
        if (parts.Length > 2 && parts[0] == "builtin_mods" && parts[1] == "builtin-locked")
        {
            var shipped = Path.Combine(AppContext.BaseDirectory, "LockedMod", "overrides", Path.Combine(parts[2..]));
            return File.Exists(shipped) && Sha256(shipped) == Sha256(copyPath) ? "circe-override-of-this-release" : null;
        }
        if (!ExitFlushedJson.Contains(relative)) return null;
        // Same instant, this machine's offset: Newtonsoft reads an offset date as local time and
        // writes it back with the local offset, so a file written in another time zone (the CI
        // runner is UTC, the fixture +01:00) re-serializes as different text for the same instant.
        var (beforeToken, afterToken) = (AsUtc(ReadToken(sourcePath)), AsUtc(ReadToken(copyPath)));
        if (JToken.DeepEquals(beforeToken, afterToken)) return "exit-flush-same-content";
        if (relative != "quests.json" || beforeToken is not JObject before || afterToken is not JObject after) return null;
        // A day (week) passed since WPF last ran: the quests roll exactly as WPF rolls them.
        var progress = new QuestProgress
        {
            DailyQuestGeneratedAt = before["DailyQuestGeneratedAt"]?.ToObject<DateTime?>(),
            WeeklyQuestGeneratedAt = before["WeeklyQuestGeneratedAt"]?.ToObject<DateTime?>(),
        };
        var rolled = (progress.IsDailyExpired() ? DailyRollKeys : Array.Empty<string>())
            .Concat(progress.IsWeeklyExpired() ? WeeklyRollKeys : Array.Empty<string>()).ToArray();
        if (rolled.Length == 0) return null;
        foreach (var key in rolled) { before.Remove(key); after.Remove(key); }
        return JToken.DeepEquals(before, after) ? "quest-roll" : null;
    }

    private static readonly System.Text.RegularExpressions.Regex OffsetDate =
        new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?([+-]\d{2}:\d{2}|Z)$");

    private static JToken AsUtc(JToken token)
    {
        var clone = token.DeepClone();
        var values = clone is JContainer container ? container.Descendants().OfType<JValue>().ToList() : new List<JValue>();
        foreach (var value in values)
            if (value.Type == JTokenType.String && OffsetDate.IsMatch((string)value!))
                value.Value = DateTimeOffset.Parse((string)value!, System.Globalization.CultureInfo.InvariantCulture)
                    .UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        return clone;
    }

    /// <summary>Every original key keeps its value, or is absent and held the default (properties
    /// marked DefaultValueHandling.Ignore). A key AppSettings does not know fails by name: there is
    /// no JsonExtensionData, so the load drops it. Messages carry key names, never values.</summary>
    internal static IEnumerable<string> CompareSettings(JObject original, JObject saved, Action<string> expect, bool allowMissingTokens = true)
    {
        var contract = (JsonObjectContract)JsonSerializer.CreateDefault().ContractResolver.ResolveContract(typeof(AppSettings));
        var defaults = new AppSettings();
        foreach (var (key, value) in original)
        {
            var property = contract.Properties.GetClosestMatchProperty(key);
            if (property is null || property.Ignored || !property.Readable)
            {
                yield return "unknown key (dropped on load): " + key;
                continue;
            }
            if (saved.TryGetValue(property.PropertyName!, out var now))
            {
                if (JToken.DeepEquals(value, now)) continue;
                // Expected: Patreon tokens are secrets and never copied (Windows DPAPI files are not
                // portable either), and WPF's own rule clears the cached tier without tokens. The
                // user signs in again on Linux. Precisely: the tier, and only to 0.
                if (allowMissingTokens && key == "PatreonTier" && now.Type == JTokenType.Integer && (int)now == 0)
                    expect("patreon-tier-cleared-without-tokens");
                else yield return "value changed: " + key;
                continue;
            }
            var fallback = property.ValueProvider!.GetValue(defaults);
            var defaultToken = fallback is null ? JValue.CreateNull() : JToken.FromObject(fallback);
            if (!JToken.DeepEquals(value, defaultToken)) yield return "key dropped with a non-default value: " + key;
        }
    }
}

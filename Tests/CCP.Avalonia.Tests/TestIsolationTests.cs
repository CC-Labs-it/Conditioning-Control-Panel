using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel;
using Xunit;
using PanicAftermath = ConditioningControlPanel.Services.Safety.PanicAftermath;
using static ConditioningControlPanel.Services.Safety.PanicAftermath;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Proves the assembly-wide <see cref="IsolateProcessStateAttribute"/> hook covers the
/// seams that leaked before (PLAYBOOK P02) and really puts each one back.</summary>
public class TestIsolationTests
{
    [Fact]
    public void MotionPreferenceCacheIsRestoredWithItsTimestamp() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        var oldProbe = UiMotionPreference.OsProbe;
        try
        {
            UiMotionPreference.OsProbe = () => true;
            UiMotionPreference.CacheStamp = long.MinValue;
            Assert.True(UiMotionPreference.AnimationsEnabled);
            // Keep the cache fresh throughout the snapshot and assertion, even on a busy runner.
            UiMotionPreference.CacheStamp = System.Environment.TickCount64 + 60_000;
            var snapshot = ProcessStateSnapshot.Take();

            UiMotionPreference.OsProbe = () => false;
            UiMotionPreference.CacheStamp = long.MinValue;
            Assert.False(UiMotionPreference.AnimationsEnabled);
            snapshot.Restore();

            Assert.True(UiMotionPreference.AnimationsEnabled);
        }
        finally
        {
            UiMotionPreference.OsProbe = oldProbe;
            UiMotionPreference.CacheStamp = long.MinValue;
        }
    });

    [Fact]
    public void HeadlessBootstrapIncludesShippedFontFallbacks() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        foreach (var family in new[] { "Segoe UI Black, Segoe UI", "Comic Sans MS, Segoe UI", "Consolas, Courier New" })
            Assert.NotNull(new global::Avalonia.Media.Typeface(family,
                weight: global::Avalonia.Media.FontWeight.Black).GlyphTypeface);
    });

    [Fact]
    public void TheHookRegistersTheHeadsFonts() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        Assert.NotNull(global::Avalonia.Application.Current);
        var face = new global::Avalonia.Media.Typeface(new global::Avalonia.Media.FontFamily(
            global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter));
        Assert.NotNull(face.GlyphTypeface);
    });

    /// <summary>The first test in a testhost decides the Avalonia platform for every later one. A setup
    /// with headless drawing (no Skia) made later text measure differently and bitmaps read garbage, so
    /// ChasterRaffleCardLayout, BlinkTrainerSession and EmiBookDemo failed only after it (flake-layout).</summary>
    [Fact]
    public void EveryPlatformSetupUsesSkiaDrawing()
    {
        var bad = Directory.EnumerateFiles(Path.GetDirectoryName(Here())!, "*.cs", SearchOption.AllDirectories) // every test project
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f =>
            {
                var code = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(f), @"@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])+'|//[^\n]*|/\*.*?\*/", "",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                return System.Text.RegularExpressions.Regex.Matches(code, @"\.UseHeadless\(")
                    .Where(m => !code.Substring(m.Index, System.Math.Min(160, code.Length - m.Index)).Contains("UseHeadlessDrawing = false")
                             || !code.Substring(System.Math.Max(0, m.Index - 160), System.Math.Min(160, m.Index)).Contains("UseSkia()"))
                    .Select(_ => Path.GetFileName(f));
            }).ToList();
        Assert.Empty(bad);
    }

    [Theory]
    [InlineData("fonts:Inter#Inter")]
    [InlineData("Consolas, Courier New")]
    [InlineData("Segoe UI")]
    [InlineData("Segoe UI Black")]
    public void SharedSetupResolvesUiFontsBeforeAnyTest(string family) =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
        {
            Assert.NotNull(global::Avalonia.Application.Current);
            foreach (var weight in new[] { global::Avalonia.Media.FontWeight.Normal, global::Avalonia.Media.FontWeight.Bold })
                Assert.True(global::Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(
                    new global::Avalonia.Media.Typeface(new global::Avalonia.Media.FontFamily(family), weight: weight), out _), family);
        });

    [Fact]
    public void SharedPlatformLoadsBundledFontsBeforeAnyTestSetup()
    {
        Assert.NotNull(global::Avalonia.Application.Current);
        Assert.Equal(new global::Avalonia.Media.FontFamily(
                ConditioningControlPanel.Avalonia.Platform.AppFonts.Default).Name,
            global::Avalonia.Media.FontManager.Current.DefaultFontFamily.Name);
        foreach (var family in new[] { ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter,
                     ConditioningControlPanel.Avalonia.Platform.AppFonts.UiSans,
                     ConditioningControlPanel.Avalonia.Platform.AppFonts.Fredoka,
                     ConditioningControlPanel.Avalonia.Platform.AppFonts.Mono })
            Assert.NotNull(new global::Avalonia.Media.Typeface(family,
                weight: global::Avalonia.Media.FontWeight.Bold).GlyphTypeface);
    }

    [Fact]
    public void SharedSetupRegistersBundledFonts() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        Assert.Equal(new global::Avalonia.Media.FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Default), global::Avalonia.Media.FontManager.Current.DefaultFontFamily);
        Assert.Equal(new global::Avalonia.Media.Typeface(new global::Avalonia.Media.FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Fredoka)).GlyphTypeface.FamilyName, new global::Avalonia.Media.Typeface(new global::Avalonia.Media.FontFamily("Fredoka")).GlyphTypeface.FamilyName);
    });

    [Fact]
    public void SharedPlatformUsesTheProductionFontConfiguration() =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
        {
            Assert.IsType<AvApp>(global::Avalonia.Application.Current);
            Assert.Equal(new global::Avalonia.Media.FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Default),
                global::Avalonia.Media.FontManager.Current.DefaultFontFamily);
            var face = new global::Avalonia.Media.Typeface(
                new global::Avalonia.Media.FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter),
                weight: global::Avalonia.Media.FontWeight.Bold);
            Assert.True(global::Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(face, out var glyph), "bundled bold Inter must resolve without system fonts");
            Assert.Equal("Inter", glyph!.FamilyName);
        });

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string p = "") => Path.GetDirectoryName(p)!;

    // Run this alone in a fresh testhost: no fixture may decide the process-wide font setup.
    [Fact]
    public System.Threading.Tasks.Task TheHookInitializesTheShippedFontsBeforeAnyFixture() =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(() =>
        {
            Assert.IsType<AvApp>(global::Avalonia.Application.Current);
            foreach (var name in new[] { "fonts:Inter#Inter", "Fredoka, Segoe UI" })
            {
                var face = new global::Avalonia.Media.Typeface(name);
                Assert.True(global::Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(face, out var glyphs), name);
                Assert.True(glyphs!.CharacterToGlyphMap.TryGetGlyph('A', out var glyph));
                Assert.NotEqual((ushort)0, glyph);
            }
            return System.Threading.Tasks.Task.CompletedTask;
        });

    [Fact]
    public void EveryTestStartsWithPortableFonts() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        Assert.IsType<AvApp>(global::Avalonia.Application.Current);
        foreach (var (family, expected) in new[]
        {
            (ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter, "Inter"),
            (ConditioningControlPanel.Avalonia.Platform.AppFonts.Fredoka, "Fredoka"),   // static instances; Bold is Fredoka Bold, not the variable font's Light default
            (ConditioningControlPanel.Avalonia.Platform.AppFonts.Mono, "Noto Sans Mono"),
        })
        {
            var face = new global::Avalonia.Media.Typeface(new global::Avalonia.Media.FontFamily(family),
                global::Avalonia.Media.FontStyle.Normal, global::Avalonia.Media.FontWeight.Bold);
            Assert.True(global::Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(face, out var glyphs), family);
            Assert.Equal(expected, glyphs!.FamilyName);
        }
    });

    [Fact]
    public async System.Threading.Tasks.Task BundledFontsAreReadyBeforeAnyFixtureChoosesThePlatform()
    {
        await CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(() =>
        {
            // Model a fixture that only asks for Skia/headless. The shared hook must have
            // already installed the production font collections and family mappings.
            if (global::Avalonia.Application.Current is null)
                global::Avalonia.AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            foreach (var family in new[] { "fonts:Inter#Inter", "Fredoka" })
            {
                var typeface = new global::Avalonia.Media.Typeface(new global::Avalonia.Media.FontFamily(family));
                Assert.Contains(family.Contains("Inter") ? "Inter" : family, typeface.GlyphTypeface.FamilyName);
            }
            return System.Threading.Tasks.Task.CompletedTask;
        });
    }

    [Fact]
    public void EveryTestStartsWithProductionFonts() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        Assert.IsType<AvApp>(global::Avalonia.Application.Current);
        Assert.Equal(new global::Avalonia.Media.FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Default).Name, global::Avalonia.Media.FontManager.Current.DefaultFontFamily.Name);
        var text = new global::Avalonia.Controls.TextBlock
        {
            Text = "[ eyes closed ]",
            FontFamily = new global::Avalonia.Media.FontFamily("Consolas, Courier New"),
            FontWeight = global::Avalonia.Media.FontWeight.Bold
        };
        text.Measure(new global::Avalonia.Size(400, 100));
        Assert.True(text.DesiredSize.Width > 0);
        Assert.True(text.DesiredSize.Height > 0);
    });


    [Theory]
    [InlineData("fonts:Inter#Inter")]
    [InlineData("Fredoka")]
    public void SharedBootstrapRegistersFontsBeforeTheTestBody(string family) => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        Assert.NotNull(global::Avalonia.Application.Current);
        Assert.True(global::Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(new global::Avalonia.Media.Typeface(family), out var glyphs));
        Assert.NotNull(glyphs);
    });

    /// <summary>The isolation hook supplies desktop fonts before any fixture, independent of test order.</summary>
    [Fact]
    public void TheHookProvidesDesktopFontsBeforeAnyFixture() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.NotNull(Application.Current);
        foreach (var (family, weight) in new[]
        {
            ("fonts:Inter#Inter", FontWeight.Normal),
            ("Segoe UI Black, Segoe UI", FontWeight.Black),
            ("Fredoka, Segoe UI", FontWeight.DemiBold),
            ("Consolas, Courier New, monospace", FontWeight.Bold),
        })
        {
            var text = new TextBlock { Text = "Ready 123", FontFamily = new FontFamily(family), FontWeight = weight };
            text.Measure(Size.Infinity);
            Assert.True(text.DesiredSize.Width > 0, family);
            Assert.True(text.DesiredSize.Height > 0, family);
        }
    });

    [Fact]
    public void TheFirstTestGetsTheApplicationsFontConfiguration() =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
        {
            Assert.NotNull(global::Avalonia.Application.Current);
            Assert.Equal(new global::Avalonia.Media.FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Default),
                global::Avalonia.Media.FontManager.Current.DefaultFontFamily);
            // Shell construction measures this label before any test can repair a missing font setup.
            Assert.NotNull(new ConditioningControlPanel.Avalonia.Controls.PremiumSpark());
        });

    [Fact]
    public System.Threading.Tasks.Task EveryTestHostUsesTheProductionFontConfiguration() =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(() =>
        {
            Assert.NotNull(global::Avalonia.Application.Current);
            var fonts = global::Avalonia.Media.FontManager.Current;
            Assert.Equal(new global::Avalonia.Media.FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Default).Name, fonts.DefaultFontFamily.Name);
            Assert.True(fonts.TryGetGlyphTypeface(new global::Avalonia.Media.Typeface(
                new global::Avalonia.Media.FontFamily("Fredoka")), out var display));
            Assert.True(fonts.TryGetGlyphTypeface(new global::Avalonia.Media.Typeface(
                new global::Avalonia.Media.FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Fredoka)), out var bundled));
            Assert.Equal(bundled!.FamilyName, display!.FamilyName);
            if (!System.OperatingSystem.IsWindows())
            {
                Assert.True(fonts.TryGetGlyphTypeface(new global::Avalonia.Media.Typeface(
                    new global::Avalonia.Media.FontFamily("Consolas, Courier New")), out var mono));
                Assert.Equal("Noto Sans Mono", mono!.FamilyName);
            }
            return System.Threading.Tasks.Task.CompletedTask;
        });

    [Fact]
    public void EveryTestRunsInsideTheHook() => Assert.NotNull(IsolateProcessStateAttribute.Current);

    [Theory]
    [InlineData(typeof(ConditioningControlPanel.Services.V2AuthService), nameof(ConditioningControlPanel.Services.V2AuthService.MergedRecovery))]
    [InlineData(typeof(ConditioningControlPanel.Services.MergedAccountRecovery), nameof(ConditioningControlPanel.Services.MergedAccountRecovery.ReloadProfile))]
    [InlineData(typeof(ConditioningControlPanel.Avalonia.Views.Chaos.ChaosSkiaFxOverlay), "_refused")]
    [InlineData(typeof(CoreSettings), nameof(CoreSettings.ServiceProvider))]
    [InlineData(typeof(CoreEntitlement), nameof(CoreEntitlement.HasPremiumProvider))]
    [InlineData(typeof(CoreSecrets), nameof(CoreSecrets.RetrieveProvider))]
    [InlineData(typeof(CoreAccount), nameof(CoreAccount.UnifiedUserId))]
    [InlineData(typeof(CoreAccount), nameof(CoreAccount.IsLoggedInProvider))]
    [InlineData(typeof(CoreAccount), nameof(CoreAccount.HasPremiumAccessProvider))]
    [InlineData(typeof(CoreTutorial), nameof(CoreTutorial.IsActiveProvider))]
    [InlineData(typeof(CoreSession), nameof(CoreSession.IsSessionRunningProvider))]
    [InlineData(typeof(ConditioningControlPanel.Services.TierGate), nameof(ConditioningControlPanel.Services.TierGate.ReconnectIsTheAnswerProvider))]
    [InlineData(typeof(CoreMods), nameof(CoreMods.MakeModAwareProvider))]
    [InlineData(typeof(AvApp), nameof(AvApp.Notifications))]
    [InlineData(typeof(AvApp), nameof(AvApp.Ai))]
    [InlineData(typeof(AvApp), nameof(AvApp.Brain))]
    [InlineData(typeof(ConditioningControlPanel.Services.LockdownService), nameof(ConditioningControlPanel.Services.LockdownService.Current))]
    [InlineData(typeof(ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay), "PanicListenerLive")]
    [InlineData(typeof(ConditioningControlPanel.Services.RemoteCommands), "OverlayRunning")]
    [InlineData(typeof(ConditioningControlPanel.Services.RemoteCommands), "OverlaysStopped")]
    public void TheHookCoversEverySeamThatLeakedBefore(System.Type type, string member) =>
        Assert.Contains(ProcessStateSnapshot.Members.Value, m => m.DeclaringType == type && m.Name == member);

    [Fact]
    public void RestorePutsPanicQuietEngineHoldAndCountBack()
    {
        var original = ProcessStateSnapshot.Take();
        try
        {
            PanicAftermath.EndQuiet();
            var count = PanicAftermath.PanicCount;
            var clear = ProcessStateSnapshot.Take();
            PanicAftermath.ArmQuiet();
            Assert.True(PanicAftermath.TriggersSilenced());
            Assert.True(PanicAftermath.HoldsEngineRestart);
            Assert.Equal(count + 1, PanicAftermath.PanicCount);
            clear.Restore();
            Assert.False(PanicAftermath.TriggersSilenced());
            Assert.False(PanicAftermath.HoldsEngineRestart);
            Assert.Equal(count, PanicAftermath.PanicCount);

            PanicAftermath.ArmQuiet();
            var armed = ProcessStateSnapshot.Take();
            PanicAftermath.EndQuiet();
            armed.Restore();
            Assert.True(PanicAftermath.TriggersSilenced());
            Assert.True(PanicAftermath.HoldsEngineRestart);
        }
        finally { original.Restore(); }
    }

    [Fact]
    public void RestorePutsStaticsSettingsAndProfileFilesBack()
    {
        var settingsFile = Path.Combine(TestUserDataProfile.Root, "settings.json");
        var stray = Path.Combine(TestUserDataProfile.Root, "isolation-probe.json");
        var hadFile = File.Exists(settingsFile);
        var before = hadFile ? File.ReadAllText(settingsFile) : null;
        var user = CoreAccount.UnifiedUserId;
        var panic = (PanicCount, HoldsEngineRestart, TriggersSilenced());
        var provider = CoreSettings.ServiceProvider;
        var notifications = AvApp.Notifications;
        var premium = CoreEntitlement.HasPremiumProvider;
        var chat = CoreSettings.Current.AiChatEnabled;

        var snapshot = ProcessStateSnapshot.Take();
        CoreAccount.UnifiedUserId = "leaked-user";
        AvApp.Notifications = new();
        ArmQuiet();
        CoreSettings.Current.AiChatEnabled = !chat;
        CoreSettings.ServiceProvider = () => null;
        CoreEntitlement.HasPremiumProvider = () => true;
        File.WriteAllText(settingsFile, "{\"leaked\":true}");
        File.WriteAllText(stray, "{}");
        var leaked = new ConditioningControlPanel.Services.SettingsService();
        leaked.Save();   // armed, as a test that ends inside the debounce leaves it
        snapshot.Restore();
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() => { using var state = new TestSettingsScope(); CoreEntitlement.HasPremiumProvider = () => true; });

        Assert.Equal(panic, (PanicCount, HoldsEngineRestart, TriggersSilenced()));
        Assert.Equal(user, CoreAccount.UnifiedUserId);
        Assert.Same(provider, CoreSettings.ServiceProvider);
        Assert.Same(notifications, AvApp.Notifications);
        Assert.Same(premium, CoreEntitlement.HasPremiumProvider);
        Assert.Equal(chat, CoreSettings.Current.AiChatEnabled);
        Assert.Equal(hadFile, File.Exists(settingsFile));
        if (hadFile) Assert.Equal(before, File.ReadAllText(settingsFile));
        Assert.False(File.Exists(stray));
        Assert.False(leaked.HasPendingSave);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SeenAppMethodsRestoreTheirInputsBeforeTheAssemblyHookRuns(int method) =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
        {
            // Call the test bodies directly: the assembly hook cannot hide missing local cleanup.
            var snapshot = ProcessStateSnapshot.Take();
            try
            {
                var service = new ConditioningControlPanel.Services.SettingsService();
                CoreSettings.ServiceProvider = () => service;
                CoreEntitlement.HasPremiumProvider = () => false;
                var provider = CoreSettings.ServiceProvider;
                var premium = CoreEntitlement.HasPremiumProvider;
                var settings = service.Current;
                settings.KeywordTriggersEnabled = false;
                settings.KeywordTriggerAppScope = ConditioningControlPanel.Models.AwarenessAppScope.ExceptListed;
                settings.KeywordTriggerIgnoreOwnFocus = true;
                settings.KeywordTriggerApps = new() { "cleanup_sentinel" };
                var apps = settings.KeywordTriggerApps;
                service.SaveImmediate();
                var file = Path.Combine(TestUserDataProfile.Root, "settings.json");
                var before = File.ReadAllBytes(file);
                var tests = new AwarenessSeenAppsTests();
                switch (method)
                {
                    case 0: tests.EverywhereSamplesEveryTwoSecondsWhileScopedInputAlwaysResolves(); break;
                    case 1: tests.RunningSourceRemembersEightDistinctAppsBeforeScopeFilteringButNeverOwnOrUnknown(); break;
                    case 2: tests.ChipAddsAndSavesWithoutRearmingKeyboardAndUsesLocalizedTooltip("en", "Add 'bg3_dx11' to the list"); break;
                }
                Assert.Same(provider, CoreSettings.ServiceProvider);
                Assert.Same(premium, CoreEntitlement.HasPremiumProvider);
                Assert.False(settings.KeywordTriggersEnabled); // JsonIgnore: JSON snapshots cannot restore this
                Assert.Equal(ConditioningControlPanel.Models.AwarenessAppScope.ExceptListed, settings.KeywordTriggerAppScope);
                Assert.True(settings.KeywordTriggerIgnoreOwnFocus);
                Assert.Same(apps, settings.KeywordTriggerApps);
                Assert.Equal(new[] { "cleanup_sentinel" }, settings.KeywordTriggerApps);
                Assert.Equal(before, File.ReadAllBytes(file));
                Assert.False(service.HasPendingSave);
            }
            finally { snapshot.Restore(); }
        });

    [Fact]
    public void PrivacyRingTestRestoresProviderAndLeavesRunningEngineAlone() => AvaloniaTestDispatcher.Run(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var premium = ConditioningControlPanel.CoreEntitlement.HasPremiumProvider;
        var engine = KeywordTriggerHead.Engine;
        bool wasActive = engine.IsActive;
        try
        {
            var service = new ConditioningControlPanel.Services.SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var expectedProvider = CoreSettings.ServiceProvider;
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => true;
            engine.Start();
            Assert.True(engine.IsActive);
            var before = engine.GetRecentForegroundApps();
            new AwarenessPrivacyCandidatesTests().KeywordRingFeedsSeenAndCandidates();
            Assert.Same(expectedProvider, CoreSettings.ServiceProvider);
            Assert.True(engine.IsActive);
            Assert.Equal(before, engine.GetRecentForegroundApps());
        }
        finally
        {
            if (!wasActive) engine.Stop();
            CoreSettings.ServiceProvider = provider;
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = premium;
        }
    });

    [Fact]
    public System.Threading.Tasks.Task PrivacyFrameTestRestoresGlobalState() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var provider = CoreSettings.ServiceProvider;
        var observer = ConditioningControlPanel.Services.Awareness.AwarenessLive.ObserverEnabled;
        var pause = ConditioningControlPanel.Services.Awareness.AwarenessPause.PausedUntil;
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(ConditioningControlPanel.Services.Awareness.AwarenessLive);
        var gate = type.GetField("Lock", flags)!.GetValue(null)!;
        var frame = type.GetField("_lastFrame", flags)!;
        var at = type.GetField("_lastFrameAt", flags)!;
        object? oldFrame, oldAt;
        lock (gate) { oldFrame = frame.GetValue(null); oldAt = at.GetValue(null); }
        try
        {
            var service = new ConditioningControlPanel.Services.SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var expectedProvider = CoreSettings.ServiceProvider;
            ConditioningControlPanel.Services.Awareness.AwarenessPause.Resume();
            ConditioningControlPanel.Services.Awareness.AwarenessPause.Pause(System.TimeSpan.FromMinutes(10));
            var expectedPause = ConditioningControlPanel.Services.Awareness.AwarenessPause.PausedUntil;
            var expectedFrame = new ConditioningControlPanel.Services.Awareness.ContextFrame { AppId = "sentinel" };
            ConditioningControlPanel.Services.Awareness.AwarenessLive.Publish(expectedFrame);
            var expectedAt = ConditioningControlPanel.Services.Awareness.AwarenessLive.LastFrameAt;
            ConditioningControlPanel.Services.Awareness.AwarenessLive.ObserverEnabled = true;
            await new AwarenessPrivacyCardTests().PublishedAdultFrameUsesProjection();
            Assert.Same(expectedProvider, CoreSettings.ServiceProvider);
            Assert.Same(expectedFrame, ConditioningControlPanel.Services.Awareness.AwarenessLive.LastFrame);
            Assert.Equal(expectedAt, ConditioningControlPanel.Services.Awareness.AwarenessLive.LastFrameAt);
            Assert.Equal(expectedPause, ConditioningControlPanel.Services.Awareness.AwarenessPause.PausedUntil);
            Assert.True(ConditioningControlPanel.Services.Awareness.AwarenessLive.ObserverEnabled);
        }
        finally
        {
            lock (gate) { frame.SetValue(null, oldFrame); at.SetValue(null, oldAt); }
            ConditioningControlPanel.Services.Awareness.AwarenessLive.ObserverEnabled = observer;
            ConditioningControlPanel.Services.Awareness.AwarenessPause.Resume();
            if (pause is { } until) ConditioningControlPanel.Services.Awareness.AwarenessPause.Pause(System.TimeSpan.FromTicks(1), until.AddTicks(-1));
            CoreSettings.ServiceProvider = provider;
        }
    });

    [Fact]
    public async System.Threading.Tasks.Task ClearConversationTestRestoresSavedSettingsBeforeAssemblyCleanup()
    {
        var snapshot = ProcessStateSnapshot.Take();
        try
        {
            var service = new ConditioningControlPanel.Services.SettingsService();
            var settings = service.Current;
            settings.AvatarEnabled = false;
            settings.CompanionPrompt.AiProvider = ConditioningControlPanel.Models.AiProviderType.Cloud;
            settings.CompanionPrompt.DailyRequestLimit = 37;
            settings.CompanionPrompt.AiModel = "original-model";
            settings.CompanionPrompt.AiOllamaHost = "http://original.invalid/";
            service.SaveImmediate();

            // Invoke the body directly so the assembly hook cannot hide a leak.
            await new LocalProvidersTests().ClearConversationForgetsTheThreadAndKeepsMemoryOnlyWhenConfirmed();
            var saved = new ConditioningControlPanel.Services.SettingsService().Current;
            Assert.False(saved.AvatarEnabled);
            Assert.Equal(ConditioningControlPanel.Models.AiProviderType.Cloud, saved.CompanionPrompt.AiProvider);
            Assert.Equal(37, saved.CompanionPrompt.DailyRequestLimit);
            Assert.Equal("original-model", saved.CompanionPrompt.AiModel);
            Assert.Equal("http://original.invalid/", saved.CompanionPrompt.AiOllamaHost);
        }
        finally { snapshot.Restore(); }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TriggerTitleTestRestoresModStartupStateBeforeTheAssemblyHook(bool servicePresent, bool assertionFails) =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
        {
            var snapshot = ProcessStateSnapshot.Take();
            var resources = Application.Current!.Resources;
            var keys = new[] { "FxMistColor", "FxMistBrush", "FxParticleColor", "FxParticleBrush",
                "FxGlowColor", "FxGlowBrush", "FxFlashTintColor", "FxFlashTintBrush" };
            var originalResources = keys.ToDictionary(k => k, k => (resources.ContainsKey(k), resources.ContainsKey(k) ? resources[k] : null));
            var settingsService = new ConditioningControlPanel.Services.SettingsService();
            var coreService = typeof(CoreMods).GetProperty(nameof(CoreMods.Service))!;
            var appService = typeof(AvApp).GetProperty(nameof(AvApp.Mods), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            try
            {
                CoreSettings.ServiceProvider = () => settingsService;
                coreService.SetValue(null, null);
                appService.SetValue(null, null);
                if (servicePresent) AvApp.StartMods();
                var beforeCore = CoreMods.Service;
                var beforeApp = AvApp.Mods;
                var beforeProvider = CoreSettings.ServiceProvider;
                var settings = CoreSettings.Current;
                var beforeSettings = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var changed = typeof(ConditioningControlPanel.Models.AppSettings).GetField("PropertyChanged", flags)!;
                var replaced = typeof(ConditioningControlPanel.Services.SettingsService).GetField("CurrentReplaced", flags)!;
                var beforeChanged = (System.Delegate?)changed.GetValue(settings);
                var beforeReplaced = (System.Delegate?)replaced.GetValue(settingsService);
                var providers = typeof(CoreMods).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                    .Where(f => f.Name.EndsWith("Provider", System.StringComparison.Ordinal))
                    .Select(f => (Field: f, Value: f.GetValue(null))).ToArray();
                var links = CoreModsHooks.KnownVideoLinksProvider;
                var switched = CoreModsHooks.ModSwitched;
                var invalidate = CoreModsHooks.InvalidateStablePrompt;
                var palette = keys.ToDictionary(k => k, k => (resources.ContainsKey(k), resources.ContainsKey(k) ? resources[k] : null));

                void RunTitleTest() => new TubeTriggerModeTests().TriggerBareUrlUsesCanonicalTitle(
                    "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html",
                    assertionFails ? "wrong title" : "Bambi's Naughty TikTok Collection");
                if (assertionFails)
                {
                    var error = Record.Exception(RunTitleTest);
                    Assert.NotNull(error);
                    Assert.Contains("Assert.Equal()", error.Message);
                }
                else RunTitleTest();

                Assert.Same(beforeCore, CoreMods.Service);
                Assert.Same(beforeApp, AvApp.Mods);
                Assert.Same(beforeProvider, CoreSettings.ServiceProvider);
                Assert.Equal(beforeSettings, Newtonsoft.Json.JsonConvert.SerializeObject(settings));
                Assert.Equal(beforeChanged, (System.Delegate?)changed.GetValue(settings));
                Assert.Equal(beforeReplaced, (System.Delegate?)replaced.GetValue(settingsService));
                foreach (var (field, value) in providers) Assert.Same(value, field.GetValue(null));
                Assert.Same(links, CoreModsHooks.KnownVideoLinksProvider);
                Assert.Same(switched, CoreModsHooks.ModSwitched);
                Assert.Same(invalidate, CoreModsHooks.InvalidateStablePrompt);
                foreach (var key in keys)
                {
                    Assert.Equal(palette[key].Item1, resources.ContainsKey(key));
                    if (palette[key].Item1) Assert.Same(palette[key].Item2, resources[key]);
                }
            }
            finally
            {
                settingsService.SealForReset();
                snapshot.Restore();
                foreach (var key in keys)
                {
                    if (originalResources[key].Item1) resources[key] = originalResources[key].Item2!;
                    else resources.Remove(key);
                }
            }
        });

    /// <summary>A debounce that already FIRED is no longer armed. Its write used to land after Restore
    /// and became every later test's baseline (Welcomed, no age acceptance: an extra Age Verification
    /// dialog in JumpRightInTests, flake-stackcapture). Restore must wait for that write.</summary>
    [Fact]
    public void ASaveAlreadyWritingWhenTheTestEndsLandsBeforeTheFilesGoBack()
    {
        var settingsFile = Path.Combine(TestUserDataProfile.Root, "settings.json");
        var before = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        var snapshot = ProcessStateSnapshot.Take();
        var service = new ConditioningControlPanel.Services.SettingsService();
        service.Current.Welcomed = true;
        var flags = System.Reflection.BindingFlags.NonPublic;
        var writeLock = typeof(ConditioningControlPanel.Services.SettingsService)
            .GetField("_saveLock", flags | System.Reflection.BindingFlags.Instance)!.GetValue(service)!;
        var inFlight = typeof(ConditioningControlPanel.Services.SettingsService)
            .GetField("_debouncedSavesInFlight", flags | System.Reflection.BindingFlags.Static)!;
        System.Threading.Tasks.Task? restore = null;
        var held = new System.Threading.ManualResetEventSlim();
        var release = new System.Threading.ManualResetEventSlim();
        var holder = new System.Threading.Thread(() =>
        {
            lock (writeLock) { held.Set(); release.Wait(); }
        }) { IsBackground = true };
        holder.Start();
        held.Wait();
        try
        {
            service.Save();
            // The debounce fired and is blocked inside its write: a signal, not a guess.
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while ((int)inFlight.GetValue(null)! == 0)
            {
                Assert.True(deadline.Elapsed < System.TimeSpan.FromSeconds(10), "the debounce never fired");
                System.Threading.Thread.Yield();
            }
            restore = System.Threading.Tasks.Task.Run(snapshot.Restore);
            // Without the wait Restore is done well inside this; with it, it is still waiting (5 s bound).
            restore.Wait(System.TimeSpan.FromMilliseconds(500));
        }
        finally { release.Set(); }
        restore!.Wait();
        var drained = System.Diagnostics.Stopwatch.StartNew();   // the write is over before the files are judged
        while ((int)inFlight.GetValue(null)! != 0)
        {
            Assert.True(drained.Elapsed < System.TimeSpan.FromSeconds(10), "the write never finished");
            System.Threading.Thread.Yield();
        }
        Assert.Equal(before, File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null);
    }
}

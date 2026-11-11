using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog.Core;
using Serilog.Events;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.CatalogueStartup.Tests;

internal static class CatalogueStartupTestProfile
{
    internal static string DirectoryPath { get; } = Path.Combine(
        Path.GetTempPath(), "ccp-catalogue-startup-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(DirectoryPath);
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", DirectoryPath);
        _ = ConditioningControlPanel.CorePaths.UserData;   // installs the SandboxNet guard before any test runs
    }
}

/// <summary>Runs the real desktop composition through its catalogue-load failure branch.</summary>
public sealed class RealCatalogueFailureFallbackTests
{
    private sealed class FailingCatalogueApp : App
    {
        private readonly string _customFolder;
        private readonly string _builtInFolder;

        internal FailingCatalogueApp(string customFolder, string builtInFolder)
        {
            _customFolder = customFolder;
            _builtInFolder = builtInFolder;
        }

        internal SessionManager? Candidate { get; private set; }

        protected override string AchievementsPath =>
            System.IO.Path.Combine(CatalogueStartupTestProfile.DirectoryPath, "achievements.json");

        protected override SessionManager CreateSessionManager()
        {
            var candidate = new SessionManager(new SessionFileService(_customFolder, _builtInFolder));
            Candidate = candidate;
            return candidate;
        }
    }

    private sealed class Capture : ILogEventSink
    {
        private readonly ConcurrentQueue<string> _messages = new();
        public string Message => string.Join("\n", _messages);
        public void Emit(LogEvent entry) => _messages.Enqueue(entry.RenderMessage());
    }

    [Fact]
    public async Task RealDesktopFailureKeepsHardcodedFallbackInsteadOfPartialCandidate()
    {
        var profile = CatalogueStartupTestProfile.DirectoryPath;
        var root = Path.Combine(profile, "catalogue");
        var builtInFolder = Path.Combine(root, "built-in");
        var customFolder = Path.Combine(root, "custom");

        var oldLogger = Serilog.Log.Logger;
        var capture = new Capture();
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(capture).CreateLogger();
        var marker = Path.Combine(profile, "logs", "engine_session.active");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "prior test session");
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                var lifetime = new ClassicDesktopStyleApplicationLifetime
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };

                try
                {
                    Assert.Equal(profile, CorePaths.UserData);
                    Directory.CreateDirectory(builtInFolder);
                    Directory.CreateDirectory(customFolder);
                    new SessionFileService(customFolder, builtInFolder).ExportSession(
                        new SessionDefinition
                        {
                            Id = "real_failure_builtin",
                            Name = "Real Failure Built In",
                            Icon = "🧪",
                            Description = "Loaded before custom storage fails",
                            DurationMinutes = 19,
                            IsAvailable = true,
                        },
                        Path.Combine(builtInFolder, "real_failure_builtin.session.json"));

                    // Keep the path owned by this test, but make the custom root a file. The real
                    // SessionManager loads the valid built-in first, then Directory.CreateDirectory
                    // fails in LoadCustomSessions.
                    Directory.Delete(customFolder);
                    File.WriteAllText(customFolder, "owned custom path failure");

                    var firstAssets = Directory.CreateDirectory(Path.Combine(profile, "assets-first")).FullName;
                    var secondAssets = Directory.CreateDirectory(Path.Combine(profile, "assets-second")).FullName;
                    // Media already in UserData/assets: the default stays there on Linux too (no
                    // switch to ~/ccp media, and the test never touches the real home folder).
                    File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(profile, "assets")).FullName, "keep.png"), "x");
                    var settings = new SettingsService();
                    settings.Current.CustomAssetsPath = firstAssets;
                    settings.Current.Language = "en";
                    settings.Current.Welcomed = true;
                    settings.SaveImmediate();

                    AppBuilder.Configure<FailingCatalogueApp>(() =>
                            new FailingCatalogueApp(customFolder, builtInFolder))
                        .UseSkia()
                        .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithLifetime(lifetime);

                    Assert.False(File.Exists(marker));
                    Assert.Contains("[ENGINECRASH]", capture.Message);
                    Assert.Contains("prior test session", capture.Message);
                    var app = Assert.IsType<FailingCatalogueApp>(Application.Current);
                    Assert.Equal(firstAssets, CorePaths.EffectiveAssets);
                    CoreSettings.Current.CustomAssetsPath = secondAssets;
                    Assert.Equal(secondAssets, CorePaths.EffectiveAssets);
                    Directory.Delete(secondAssets);
                    Assert.Equal(Path.Combine(profile, "assets"), CorePaths.EffectiveAssets);
                    CoreSettings.Current.CustomAssetsPath = null;
                    Assert.Equal(Path.Combine(profile, "assets"), CorePaths.EffectiveAssets);
                    CoreSettings.Current.CustomAssetsPath = firstAssets;
                    Assert.Equal(firstAssets, CorePaths.EffectiveAssets);

                    var candidate = Assert.IsType<SessionManager>(app.Candidate);
                    var partial = Assert.Single(candidate.BuiltInSessions,
                        session => session.Id == "real_failure_builtin");
                    Assert.Equal(SessionSource.BuiltIn, partial.Source);
                    Assert.Equal(Path.Combine(builtInFolder, "real_failure_builtin.session.json"),
                        partial.SourceFilePath);
                    Assert.Empty(candidate.AllSessions);
                    Assert.True(File.Exists(customFolder));

                    var shell = Assert.IsType<MainShellWindow>(lifetime.MainWindow);
                    shell.Show();
                    Dispatcher.UIThread.RunJobs();
                    Dispatcher.UIThread.RunJobs();

                    // Locked "Coming Soon" sessions are listed too, as WPF does (audit #1318).
                    var expectedFallback = Session.GetAllSessions().ToArray();
                    var presets = Assert.IsType<PresetsTabView>(shell.FindControl<PresetsTabView>("PresetsTab"));
                    var rows = presets.FindControl<StackPanel>("SessionRackPanel")!
                        .Children.OfType<Border>()
                        .ToArray();
                    Assert.Equal(expectedFallback.Select(session => session.Id),
                        rows.Select(row => (row.Tag as Session)?.Id));
                    Assert.NotEmpty(rows);
                    Assert.DoesNotContain(rows,
                        row => (row.Tag as Session)?.Id == "real_failure_builtin");
                    Assert.All(rows, row => Assert.IsType<Session>(row.Tag));

                    // Use the hooks installed by desktop startup, not direct calls to the shell.
                    var engineSettings = CoreSettings.Current;
                    engineSettings.AudioOnlySession = true;
                    engineSettings.FlashFrequency = 17;
                    engineSettings.SimultaneousImages = 3;
                    engineSettings.BubblesEnabled = true;
                    engineSettings.MandatoryVideosEnabled = false;
                    engineSettings.SubliminalEnabled = true;
                    CoreEngine.Start();
                    Assert.True(CoreEngine.IsRunning);
                    var context = File.ReadAllText(marker);
                    Assert.Matches(@"^started \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} \| flash 17/min x3 \| bubbles on \| video off \| subliminal on$", context);
                    CoreEngine.Stop();
                    Assert.False(File.Exists(marker));
                    CoreEngine.Start();
                    Assert.True(CoreEngine.IsRunning);
                    Assert.True(File.Exists(marker));
                    // The shell's Closed handler stops the engine during shutdown. Disable that
                    // hook so only the desktop Exit handler can clear this running-session marker.
                    CoreEngine.StoppedHook = null;

                    // A tracked counter dirties the engine; the exit handler must flush it, and into
                    // this test's profile, never the real ~/.config achievements.json.
                    CoreProgression.TrackBubbleCountResult(false);
                }
                finally
                {
                    try
                    {
                        lifetime.Shutdown();
                    }
                    finally
                    {
                        Dispatcher.UIThread.RunJobs();
                    }
                }

                Assert.False(File.Exists(marker));
                Assert.Contains("\"TotalBubbleCountFailed\": 1",
                    File.ReadAllText(Path.Combine(profile, "achievements.json")));
                return Task.CompletedTask;
            });
        }
        finally
        {
            Serilog.Log.Logger = oldLogger;
            if (Directory.Exists(profile))
                Directory.Delete(profile, recursive: true);
        }
    }
}

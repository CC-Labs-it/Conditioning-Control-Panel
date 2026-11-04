using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.DeeperLifecycle.Tests;

internal static class LifecycleProfile
{
    internal const string ProfileVariable = "CCP_DEEPER_LIFECYCLE_PROFILE";
    internal const string PhaseVariable = "CCP_DEEPER_LIFECYCLE_PHASE";
    internal static readonly string Root = Environment.GetEnvironmentVariable(ProfileVariable)
        ?? Path.Combine(Path.GetTempPath(), "ccp-deeper-parent-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", Root);
        foreach (var name in new[] { "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME" })
            Environment.SetEnvironmentVariable(name, Directory.CreateDirectory(Path.Combine(Root, name)).FullName);
        Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", "disabled:");
        _ = CorePaths.UserData;
        if (Environment.GetEnvironmentVariable(PhaseVariable) == null)
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(Root, true); } catch { } };
    }
}

public sealed class DeeperDesktopLifecycleTests
{
    private sealed class ProfileApp : App
    {
        protected override string AchievementsPath => Path.Combine(LifecycleProfile.Root, "achievements.json");
    }

    [Fact]
    public async Task DesktopStartupAndExitPersistDemoSeedAcrossRestart()
    {
        var phase = Environment.GetEnvironmentVariable(LifecycleProfile.PhaseVariable);
        if (phase != null)
        {
            await RunDesktop(phase);
            return;
        }

        var profile = Directory.CreateTempSubdirectory("ccp-deeper-restart-").FullName;
        var settings = Path.Combine(profile, "settings.json");
        var demo = Path.Combine(profile, "enhancements", "welcome.ccpenh.json");
        try
        {
            File.WriteAllText(settings, "{\"HasSeededDeeperDemos\":false}");
            await RunChild(profile, "first");
            Assert.True(File.Exists(demo));
            using (var saved = JsonDocument.Parse(File.ReadAllText(settings)))
                Assert.True(saved.RootElement.GetProperty("HasSeededDeeperDemos").GetBoolean());
            File.Delete(demo);
            await RunChild(profile, "restart");
            Assert.False(File.Exists(demo));
            using var afterRestart = JsonDocument.Parse(File.ReadAllText(settings));
            Assert.True(afterRestart.RootElement.GetProperty("HasSeededDeeperDemos").GetBoolean());
        }
        finally { Directory.Delete(profile, true); }
    }

    [Fact]
    public async Task DesktopFirstStartKeepsMixedCaseEditedDemo()
    {
        var phase = Environment.GetEnvironmentVariable(LifecycleProfile.PhaseVariable);
        if (phase != null)
        {
            Assert.Equal("edited", phase);
            await RunDesktop(phase);
            return;
        }

        var profile = Directory.CreateTempSubdirectory("ccp-deeper-edited-").FullName;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(profile, "enhancements")).FullName;
            var editedPath = Path.Combine(folder, "WELCOME.CCPENH.JSON");
            var demo = EnhancementSerializer.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Resources", "DeeperDemos", "welcome.ccpenh.json"));
            demo.Metadata.Name = "Edited welcome";
            var editedJson = EnhancementSerializer.Save(demo);
            File.WriteAllText(editedPath, editedJson);
            var settings = Path.Combine(profile, "settings.json");
            File.WriteAllText(settings, "{\"HasSeededDeeperDemos\":false}");
            await RunChild(profile, "edited", nameof(DesktopFirstStartKeepsMixedCaseEditedDemo));
            Assert.Equal(editedJson, File.ReadAllText(editedPath));
            Assert.Single(Directory.GetFiles(folder), path =>
                string.Equals(Path.GetFileName(path), "welcome.ccpenh.json", StringComparison.OrdinalIgnoreCase));
            using var saved = JsonDocument.Parse(File.ReadAllText(settings));
            Assert.True(saved.RootElement.GetProperty("HasSeededDeeperDemos").GetBoolean());
        }
        finally { Directory.Delete(profile, true); }
    }

    private static async Task RunChild(string profile, string phase, string? methodName = null)
    {
        var executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(DeeperDesktopLifecycleTests).Assembly.Location);
        start.ArgumentList.Add("-noLogo");
        start.ArgumentList.Add("-noColor");
        start.ArgumentList.Add("-method");
        start.ArgumentList.Add(typeof(DeeperDesktopLifecycleTests).FullName + "." + (methodName ?? nameof(DesktopStartupAndExitPersistDemoSeedAcrossRestart)));
        start.Environment[LifecycleProfile.ProfileVariable] = profile;
        start.Environment[LifecycleProfile.PhaseVariable] = phase;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120)); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, $"Desktop {phase} failed ({process.ExitCode}):\n{await output}\n{await error}");
    }

    private static Task RunDesktop(string phase) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var property = typeof(App).GetProperty("DeeperLibrary", BindingFlags.Static | BindingFlags.NonPublic)!;
        EnhancementLibrary? library = null;
        try
        {
            // No window is shown: the tab cannot initialize the library on attachment.
            AppBuilder.Configure<ProfileApp>().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithLifetime(lifetime);
            Assert.NotNull(lifetime.MainWindow);
            library = Assert.IsType<EnhancementLibrary>(property.GetValue(null));
            Assert.NotNull(EnhancementResolver.LibraryMatchProvider);
            Assert.True(App.Settings!.Current.HasSeededDeeperDemos);
            if (phase == "edited")
            {
                var edited = Assert.Single(library.ScanLibrary(), entry =>
                    string.Equals(Path.GetFileName(entry.FilePath), "welcome.ccpenh.json", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("WELCOME.CCPENH.JSON", Path.GetFileName(edited.FilePath));
                Assert.Equal("Edited welcome", edited.Name);
            }
            else Assert.Equal(phase == "first", File.Exists(Path.Combine(LifecycleProfile.Root, "enhancements", "welcome.ccpenh.json")));
        }
        finally
        {
            lifetime.Shutdown();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Null(property.GetValue(null));
        Assert.Null(EnhancementResolver.LibraryMatchProvider);
        Assert.True((bool)typeof(EnhancementLibrary).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(library)!);
        Assert.True(new SettingsService().Current.HasSeededDeeperDemos);
        return Task.CompletedTask;
    });
}

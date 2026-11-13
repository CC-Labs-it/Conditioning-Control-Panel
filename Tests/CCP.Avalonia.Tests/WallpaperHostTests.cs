using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class WallpaperHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GnomePartialWriteRollsBack(bool rollbackFails)
    {
        var folder = Directory.CreateTempSubdirectory("wallpaper-").FullName;
        var original = Path.GetTempFileName();
        try
        {
            File.WriteAllText(Path.Combine(folder, "one.png"), "fixture");
            CoreSettings.Current.WallpaperSourceFolder = folder;
            CoreSettings.Current.WallpaperOriginalPath = "";
            var before = "'" + new Uri(original).AbsoluteUri + "'";
            var current = before;
            var writes = 0;
            var host = new WallpaperHost(new LinuxWallpaperPlatform((_, args) =>
            {
                if (args[0] == "get") return args[2] == "picture-options" ? "'scaled'" : before;
                if (++writes == 2 || (rollbackFails && writes > 3)) return null;
                if (args[2] == "picture-uri") current = args[3];
                return "";
            }, "GNOME"));
            Assert.False(await host.ShuffleAsync());
            Assert.Equal(6, writes); // three apply writes, then all three snapshot writes
            Assert.Equal(!rollbackFails, before == current);
            Assert.Equal(rollbackFails, host.IsActive);
            Assert.Equal(rollbackFails, CoreSettings.Current.WallpaperOriginalPath.Length > 0);
        }
        finally { Directory.Delete(folder, true); File.Delete(original); }
    }
    [Fact]
    public async Task GnomePreservesLightDarkAndOptionsAndPropagatesFailure()
    {
        var file = Path.GetTempFileName();
        try
        {
            var uri = new Uri(file).AbsoluteUri;
            var calls = new List<string[]>();
            bool fail = false;
            var options = "'scaled'";
            var platform = new LinuxWallpaperPlatform((exe, args) =>
            {
                Assert.Equal("gsettings", exe);
                calls.Add(args);
                if (fail) return null;
                return args[0] == "get" ? args[2] == "picture-options" ? options : "'" + uri + "'" : "";
            }, "GNOME");
            var snapshot = platform.Capture();
            Assert.NotNull(snapshot);
            Assert.True(platform.Apply(file));
            Assert.True(platform.Restore(snapshot));
            Assert.Equal("'scaled'", calls.Last()[3]);
            Assert.Contains(calls, c => c[0] == "set" && c[2] == "picture-uri-dark");
            fail = true;
            Assert.False(platform.Restore(snapshot));
            Assert.Null(platform.Capture());
            fail = false;
            options = "'none'";
            Assert.Null(platform.Capture());
            File.Delete(file);
            calls.Clear();
            CoreSettings.Current.WallpaperOriginalPath = snapshot;
            Assert.False(await new WallpaperHost(platform).DeactivateAsync());
            Assert.Equal(snapshot, CoreSettings.Current.WallpaperOriginalPath);
            Assert.Empty(calls);
        }
        finally { File.Delete(file); }
    }
    [Fact]
    public async Task KdeFallsBackToQdbusAndRestoresEachScreenWithEscapedArguments()
    {
        var file = Path.GetTempFileName();
        try
        {
            var uri = new Uri(file).AbsoluteUri;
            var scripts = new List<string>();
            var platform = new LinuxWallpaperPlatform((exe, args) =>
            {
                if (exe == "qdbus6") return null;
                scripts.Add(args[3]);
                return args[3].StartsWith("print(JSON") ? System.Text.Json.JsonSerializer.Serialize(new[] { new[] { "1", uri }, new[] { "2", uri } }) : "";
            }, "KDE");
            var snapshot = platform.Capture();
            Assert.NotNull(snapshot);
            Assert.True(platform.Apply(file));
            Assert.True(platform.Restore(snapshot));
            Assert.Equal(4, scripts.Count);
            Assert.Contains("String(d.id)", scripts[2]);
            Assert.Contains("String(d.id)", scripts[3]);
            File.Delete(file);
            scripts.Clear();
            CoreSettings.Current.WallpaperOriginalPath = snapshot;
            Assert.False(await new WallpaperHost(platform).DeactivateAsync());
            Assert.Equal(snapshot, CoreSettings.Current.WallpaperOriginalPath);
            Assert.Empty(scripts);
        }
        finally { File.Delete(file); }
    }
    [Fact]
    public void PortalOnlyDesktopIsRefusedWithoutWriting()
    {
        var platform = new LinuxWallpaperPlatform((_, _) => throw new Exception("must not write"), "unknown");
        Assert.Null(platform.Capture());
    }
    [Fact]
    public Task AutonomyPulseReplacesTimerHonorsKeepAndStopRestores() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        AmbientBeatLoopsTests.Setup();
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(file);
        File.WriteAllText(Path.Combine(file, "fixture.png"), "fixture");
        var s = CoreSettings.Current;
        var (folder, original, keep, seconds) = (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled, s.WallpaperPulseSeconds);
        var previous = WallpaperHead.Service;
        MainShellWindow? shell = null;
        try
        {
            s.WallpaperSourceFolder = file;
            s.WallpaperOriginalPath = "";
            s.WallpaperEnabled = false;
            s.WallpaperPulseSeconds = 45;
            var desktop = new Desktop();
            WallpaperHead.Service = new WallpaperHost(desktop);
            shell = new MainShellWindow();
            var timers = new List<Action>();
            shell.PulseTimer = (action, duration) => { Assert.Equal(TimeSpan.FromSeconds(45), duration); timers.Add(action); };
            await shell.TriggerWallpaperChangeAsync();
            await shell.TriggerWallpaperChangeAsync();
            Assert.Equal(2, timers.Count);
            timers[0]();
            await WallpaperHead.Service.WhenIdle;
            Assert.True(WallpaperHead.Service.IsActive); // older revert is cancelled
            File.Delete(Path.Combine(file, "fixture.png"));
            await shell.TriggerWallpaperChangeAsync();
            Assert.Equal(2, timers.Count);
            timers[1]();
            await WallpaperHead.Service.WhenIdle;
            Assert.False(WallpaperHead.Service.IsActive); // failed shuffle kept the previous revert
            File.WriteAllText(Path.Combine(file, "fixture.png"), "fixture");
            await shell.TriggerWallpaperChangeAsync();
            s.WallpaperEnabled = true;
            timers[2]();
            await WallpaperHead.Service.WhenIdle;
            Assert.True(WallpaperHead.Service.IsActive); // Keep switched on mid-pulse
            s.WallpaperEnabled = false;
            shell.CancelAutonomyPulses();
            await WallpaperHead.Service.WhenIdle;
            Assert.False(WallpaperHead.Service.IsActive);
        }
        finally
        {
            shell?.RequestExit();
            WallpaperHead.Service = previous;
            (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled, s.WallpaperPulseSeconds) = (folder, original, keep, seconds);
            Directory.Delete(file, true);
        }
    });
    private sealed class Desktop : IWallpaperPlatform
    {
        public bool Restored;
        public string? Capture() => "original";
        public bool Apply(string path) => true;
        public bool Restore(string snapshot) { Restored = snapshot == "panic-original"; return true; }
    }
    /// <summary>Owner 2026-11-11: Takeover may write the GNOME/KDE wallpaper on Linux only if the user's own
    /// wallpaper comes back. Drives the real Takeover pulse against a fake desktop that keeps state, then proves
    /// every key is back exactly as it was after panic (Keep on too), a normal Takeover end, and app exit.</summary>
    [Theory]
    [InlineData("GNOME")]
    [InlineData("KDE")]
    public Task TakeoverPictureIsUndoneByPanicNormalEndAndExit(string desktopName) => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        AmbientBeatLoopsTests.Setup();
        var folder = Directory.CreateTempSubdirectory("wallpaper-takeover-").FullName;
        var light = Path.Combine(folder, "own light.jpg");
        var dark = Path.Combine(folder, "own-dark.jpg");
        var pool = Directory.CreateDirectory(Path.Combine(folder, "pool")).FullName;
        File.WriteAllText(light, "fixture"); File.WriteAllText(dark, "fixture");
        File.WriteAllText(Path.Combine(pool, "a.png"), "fixture"); File.WriteAllText(Path.Combine(pool, "b.png"), "fixture");
        var s = CoreSettings.Current;
        var saved = (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled, s.WallpaperPulseSeconds);
        var previous = WallpaperHead.Service;
        MainShellWindow? shell = null;
        // Fake desktop state: GNOME keys, or KDE screen id -> Image.
        var state = new Dictionary<string, string>();
        void Reset()
        {
            state.Clear();
            if (desktopName == "GNOME")
            {
                state["picture-uri"] = "'" + new Uri(light).AbsoluteUri + "'";
                state["picture-uri-dark"] = "'" + new Uri(dark).AbsoluteUri + "'";
                state["picture-options"] = "'scaled'";
            }
            else { state["1"] = new Uri(light).AbsoluteUri; state["2"] = new Uri(dark).AbsoluteUri; }
        }
        Reset();
        var original = new Dictionary<string, string>(state);
        string? Run(string exe, string[] args)
        {
            if (desktopName == "GNOME")
            {
                Assert.Equal("gsettings", exe);
                if (args[0] == "get") return state[args[2]];
                state[args[2]] = args[3];
                return "";
            }
            var script = args[3];
            if (script.StartsWith("print(JSON"))
                return System.Text.Json.JsonSerializer.Serialize(state.Select(kv => new[] { kv.Key, kv.Value }).ToArray());
            var image = System.Text.Json.JsonSerializer.Deserialize<string>(script[(script.IndexOf("writeConfig('Image', ") + 21)..script.LastIndexOf("); });")])!;
            var only = script.Contains("String(d.id) !== ") ? System.Text.Json.JsonSerializer.Deserialize<string>(script[(script.IndexOf("!== ") + 4)..script.IndexOf(") return;")]) : null;
            foreach (var id in state.Keys.ToList()) if (only == null || only == id) state[id] = image;
            return "";
        }
        bool IsOriginal() => state.Count == original.Count && original.All(kv => state[kv.Key] == kv.Value);
        try
        {
            s.WallpaperSourceFolder = pool;
            s.WallpaperOriginalPath = "";
            s.WallpaperPulseSeconds = 45;
            var host = new WallpaperHost(new LinuxWallpaperPlatform(Run, desktopName));
            WallpaperHead.Service = host;
            shell = new MainShellWindow();
            shell.PulseTimer = (_, _) => { }; // the pulse revert never fires on its own here

            // Panic, with Keep on: panic takes it down in every mode.
            s.WallpaperEnabled = true;
            await shell.TriggerWallpaperChangeAsync();
            Assert.False(IsOriginal());
            Assert.NotEqual("", s.WallpaperOriginalPath); // the breadcrumb is written before the first write (#692)
            foreach (var surface in PanicSurfaces.All.Where(x => x.Id is "takeover" or "wallpaper")) surface.Stop(shell);
            await host.WhenIdle;
            Assert.True(IsOriginal());
            Assert.Equal("", s.WallpaperOriginalPath);
            Assert.False(host.IsActive);

            // Normal end: Takeover off or the engine stopping both cancel the pulses.
            s.WallpaperEnabled = false;
            await shell.TriggerWallpaperChangeAsync();
            await shell.TriggerWallpaperChangeAsync(); // a second shuffle never replaces the saved original
            Assert.False(IsOriginal());
            shell.CancelAutonomyPulses();
            await host.WhenIdle;
            Assert.True(IsOriginal());
            Assert.Equal("", s.WallpaperOriginalPath);

            // Exit with Keep on still puts the user's own wallpaper back, and nothing writes after it.
            s.WallpaperEnabled = true;
            await shell.TriggerWallpaperChangeAsync();
            Assert.False(IsOriginal());
            Assert.True(await host.ShutdownAsync());
            Assert.True(IsOriginal());
            Assert.False(await host.ShuffleAsync());
            Assert.True(IsOriginal());
        }
        finally
        {
            shell?.RequestExit();
            WallpaperHead.Service = previous;
            (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled, s.WallpaperPulseSeconds) = saved;
            Directory.Delete(folder, true);
        }
    });
    [Fact]
    public async Task PanicWallpaperEntryRestoresEvenWithoutShell()
    {
        var previous = WallpaperHead.Service;
        var original = CoreSettings.Current.WallpaperOriginalPath;
        try
        {
            CoreSettings.Current.WallpaperOriginalPath = "panic-original";
            var desktop = new Desktop();
            WallpaperHead.Service = new WallpaperHost(desktop);
            PanicSurfaces.All.Single(s => s.Id == "wallpaper").Stop(null);
            await WallpaperHead.Service.WhenIdle;
            Assert.True(desktop.Restored);
            Assert.Equal("", CoreSettings.Current.WallpaperOriginalPath);
        }
        finally { WallpaperHead.Service = previous; CoreSettings.Current.WallpaperOriginalPath = original; }
    }
}

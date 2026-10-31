using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using ConditioningControlPanel.Services.Video.Browser;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class BubbleCountBrowserTests
{
    [Fact]
    public Task Browser_gate_rejects_competing_owner_unmapped_file_and_missing_provider() => AvaloniaTestDispatcher.RunAsync(() => {
        EnsureApp();
        using var game = new BrowserGame();
        Assert.True(BubbleCountHost.Instance.CanPlayPath(game.Clip));
        var owner = new object();
        Assert.True(BrowserVideoSurface.TryAcquire(owner, game.Clip));
        var competing = new object();
        Assert.False(BrowserVideoSurface.TryAcquire(competing, game.Clip));
        BrowserVideoSurface.Release(competing);
        Assert.True(BrowserVideoSurface.SessionActive);
        BrowserVideoSurface.Release(owner);
        Assert.False(BrowserVideoSurface.SessionActive);
        Assert.Null(BrowserVideoSurface.Map(Path.Combine(Path.GetTempPath(), "outside.mp4")));
        game.Settings.Current.AudioOutputDeviceId = "headphones";
        Assert.False(BrowserVideoSurface.TryAcquire(owner, game.Clip));
        game.Settings.Current.AudioOutputDeviceId = "";
        BrowserVideoSurface.Seed(); // headless never offers a native browser, even if packages exist
        Assert.False(BrowserVideoGate.ShouldUseBrowser(game.Clip));
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData("ended")]
    [InlineData("error")]
    public Task PlayedTerminalOpensOneAnswerAndLateCallbacksDoNothing(string terminal) => AvaloniaTestDispatcher.RunAsync(() => {
        EnsureApp();
        using var game = new BrowserGame();
        BubbleCountWindow.ShowOnAllMonitors(game.Clip, BubbleCountScheduler.Difficulty.Easy, true, _ => game.Completed++);
        var w = Assert.Single(BubbleCountWindow.OpenWindows);
        var browser = w.Browser!;
        browser.View.OnNavigationCompleted(new Uri("about:blank"));
        Assert.Equal(0.75, browser.Volume);
        Send(browser, "playing"); Send(browser, terminal); Send(browser, terminal);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Single(BubbleCountResultWindow.OpenWindows);
        BubbleCountWindow.ForceCloseAll(); BubbleCountResultWindow.ForceCloseAll();
        Send(browser, "playing"); Send(browser, "ended");
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(BrowserVideoSurface.SessionActive);
        Assert.Empty(BubbleCountResultWindow.OpenWindows);
        Assert.Equal(0, game.Completed);
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(100, true)]
    [InlineData(101, true)]
    [InlineData(102, false)]
    [InlineData(103, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)] // mirror construction failure
    [InlineData(-2, false)] // mirror startup timeout
    public Task Browser_only_primary_media_failures_make_the_file_unsafe(int code, bool unsafeFile) => AvaloniaTestDispatcher.RunAsync(() => {
        EnsureApp();
        using var game = new BrowserGame();
        BubbleCountWindow.ShowOnAllMonitors(game.Clip, BubbleCountScheduler.Difficulty.Easy, true,
            ok => { Assert.False(ok); game.Completed++; });
        var primary = Assert.Single(BubbleCountWindow.OpenWindows);
        var (browser, create) = (primary.Browser!, BrowserVideoSurface.Create);
        browser.View.OnNavigationCompleted(new Uri("about:blank"));
        var secondary = new BubbleCountWindow(game.Clip, BubbleCountScheduler.Difficulty.Easy, true,
            _ => game.Completed++, isPrimary: false);
        typeof(BubbleCountWindow).GetField("_useBrowser", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(secondary, true);
        BrowserVideoSurface.Create = code == -1 ? () => null : create;
        secondary.Show();
        BrowserVideoSurface.Create = create;
        if (code != -1) {
            secondary.Browser!.View.OnNavigationCompleted(new Uri("about:blank"));
            Assert.Equal(0, secondary.Browser!.Volume);
            if (code == -2) typeof(BrowserVideoSurface).GetMethod("Fail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(secondary.Browser, new object[] { false });
            else Send(secondary.Browser, "error", code: code);
        }
        else Assert.Null(secondary.Browser);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(BrowserUnsafeVideoCache.Contains(game.Clip));
        Assert.True(primary.IsVisible);
        Assert.False(BrowserVideoSurface.StoodDown);
        BrowserVideoSurface.Release(primary);
        Assert.True(BrowserVideoSurface.TryAcquire(primary, game.Clip));
        Send(browser, "error", code: code);
        Send(browser, "playing"); Send(browser, "ended");
        Send(browser, "error", code: 4); // first failure wins before dispatcher drain
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(unsafeFile, BrowserUnsafeVideoCache.Contains(game.Clip));
        Assert.Equal(!unsafeFile, BrowserVideoSurface.StoodDown);
        Assert.Equal(!unsafeFile, BrowserVideoGate.ShouldUseBrowser(game.Clip));
        Assert.Equal(1, game.Completed);
        Assert.Equal(0, game.Skipped);
        Assert.Empty(BubbleCountResultWindow.OpenWindows);
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedInitializationAndNativeCloseResolveOnce(bool nativeClose) => AvaloniaTestDispatcher.RunAsync(async () => {
        EnsureApp();
        using var game = new BrowserGame();
        var pending = new TaskCompletionSource<string?>();
        BrowserVideoSurface.Create = () => new BrowserVideoSurface(_ => pending.Task);
        BubbleCountWindow.ShowOnAllMonitors(game.Clip, BubbleCountScheduler.Difficulty.Easy, true,
            ok => { Assert.False(ok); game.Completed++; });
        var w = Assert.Single(BubbleCountWindow.OpenWindows);
        var browser = w.Browser!;
        browser.View.OnNavigationCompleted(new Uri("about:blank"));
        if (nativeClose) w.Close();
        pending.SetResult(null);
        for (var i = 0; i < 100 && game.Completed == 0; i++) await Task.Delay(10);
        Send(browser, "error", code: 4); Send(browser, "ended");
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, game.Completed);
        Assert.False(BrowserVideoSurface.SessionActive);
        Assert.Empty(BubbleCountWindow.OpenWindows);
        Assert.Empty(BubbleCountResultWindow.OpenWindows);
        Assert.False(BrowserUnsafeVideoCache.Contains(game.Clip));
        Assert.Equal(!nativeClose, BrowserVideoSurface.StoodDown);
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BrowserEscapeUsesTheStrictPolicy(bool strict) => AvaloniaTestDispatcher.RunAsync(() => {
        EnsureApp();
        using var game = new BrowserGame();
        game.Settings.Current.PanicKeyEnabled = true;
        BubbleCountWindow.PanicListenerLive = () => true;
        BubbleCountWindow.ShowOnAllMonitors(game.Clip, BubbleCountScheduler.Difficulty.Easy, strict, _ => game.Completed++);
        var browser = Assert.Single(BubbleCountWindow.OpenWindows).Browser!;
        browser.View.OnNavigationCompleted(new Uri("about:blank"));
        browser.View.OnWebMessage(JsonSerializer.Serialize(new { session = browser.SessionId, type = "key", key = "Escape" }));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(strict ? 0 : 1, game.Completed);
        Assert.Equal(strict, BubbleCountWindow.IsAnyOpen());
        return Task.CompletedTask;
    });
    [Fact]
    public Task MissingNativeAndFailedBrowserConstructionCompletesOnceThenSkips() => AvaloniaTestDispatcher.RunAsync(async () => {
        EnsureApp();
        using var game = new BrowserGame();
        var host = BubbleCountHost.Instance;
        var shared = typeof(LibVlcAudio).GetProperty("Shared", BindingFlags.Static | BindingFlags.NonPublic)!;
        var (previous, scheduler) = (shared.GetValue(null), new BubbleCountScheduler(host, library: () => new[] { game.Clip }));
        shared.SetValue(null, null);
        try
        {
            (game.Settings.Current.BubbleCountStrictLock, game.Settings.Current.MercySystemEnabled) = (true, false);
            BrowserVideoSurface.Create = () => null;
            Assert.True(host.CanPlayPath(game.Clip));
            BubbleCountWindow.ShowOnAllMonitors(game.Clip, BubbleCountScheduler.Difficulty.Easy, true,
                ok => { Assert.False(ok); game.Completed++; });
            Assert.Equal(1, game.Completed);
            Assert.True(BrowserVideoSurface.StoodDown);
            Assert.False(host.CanPlayPath(game.Clip));
            scheduler.Trigger(forceTest: true);
            for (var n = 0; n < 200 && scheduler.IsBusy; n++) await Task.Delay(10);
            Assert.False(scheduler.IsBusy);
            Assert.Empty(host.Messages);
            Assert.Empty(BubbleCountWindow.OpenWindows);
            Assert.Equal(1, game.Completed);
        }
        finally { scheduler.ForceCleanup(); shared.SetValue(null, previous); }
    });
    private static void Send(BrowserVideoSurface surface, string type, double durationMs = 0, int code = 0) =>
        surface.View.OnWebMessage(JsonSerializer.Serialize(new { session = surface.SessionId, type, durationMs, code }));
    private static void EnsureApp() {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private sealed class BrowserGame : IDisposable
    {
        private readonly Func<string>? _assets = CorePaths.EffectiveAssetsProvider;
        private readonly Func<SettingsService?>? _settings = CoreSettings.ServiceProvider;
        private readonly Func<string, string?, bool>? _gate = BrowserVideoGate.EngineAccepts;
        private readonly Func<BrowserVideoSurface?> _create = BrowserVideoSurface.Create;
        private readonly string _dir = Directory.CreateTempSubdirectory("ccp-bcount-browser-").FullName;
        internal readonly SettingsService Settings = new();
        internal int Completed, Skipped;
        internal string Clip => Path.Combine(_dir, "clip.mp4");
        internal BrowserGame() {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), Clip);
            CorePaths.EffectiveAssetsProvider = () => _dir;
            CoreSettings.ServiceProvider = () => Settings;
            (Settings.Current.BrowserVideoEngineEnabled, Settings.Current.AudioOutputDeviceId, Settings.Current.DualMonitorEnabled, Settings.Current.MasterVolume) = (true, "", false, 75);
            BrowserVideoSurface.StoodDown = false;
            BrowserVideoGate.EngineAccepts = (p, k) => !BrowserUnsafeVideoCache.Contains(p, k) && BrowserVideoSurface.Map(p) != null;
            BrowserVideoSurface.Create = () => { return new BrowserVideoSurface(_ => Task.FromResult<string?>("true")); };
        }
        public void Dispose() {
            BubbleCountWindow.ForceCloseAll();
            BubbleCountResultWindow.ForceCloseAll();
            (BrowserVideoSurface.Create, BrowserVideoGate.EngineAccepts, CorePaths.EffectiveAssetsProvider, CoreSettings.ServiceProvider) = (_create, _gate, _assets, _settings);
            Directory.Delete(_dir, true);
        }
    }
}

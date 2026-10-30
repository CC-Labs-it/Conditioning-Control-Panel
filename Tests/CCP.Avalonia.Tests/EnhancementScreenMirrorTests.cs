using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class EnhancementScreenMirrorTests {
    private sealed class FakeTopology : IDisplayTopology {
        public int ActiveScreenCount => 2;
        public List<bool> Calls { get; } = new();
        public int Result { get; set; }
        public Action<bool>? OnApply { get; set; }
        public int Apply(bool clone) { Calls.Add(clone); OnApply?.Invoke(clone); return Result; }
    }
    private static Screen MakeScreen(int x, bool primary) {
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(new IntPtr(x + 1), "headless-test") })!;
        var bounds = new PixelRect(x, 0, 1920, 1080);
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, primary);
        return screen;
    }
    private static Task Run(bool dualMonitor, Action<EnhancementPlayerWindow, WebHost, FakeTopology, DisplayMirror> check) =>
        AvaloniaTestDispatcher.RunAsync(() => {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .With(AppFonts.Options(systemEmoji: false))
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            s.OfflineMode = false;
            s.DualMonitorEnabled = dualMonitor;
            var screens = new[] { MakeScreen(0, true), MakeScreen(1920, false) };
            ScreenList.QueryOverride = _ => screens;
            var topo = new FakeTopology();
            var mirror = new DisplayMirror(topo);
            ScreenMirrorHead.Override = mirror;
            var shell = new EnhancementPlayerWindow(null, null);
            try {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                check(shell, shell.FindControl<WebHost>("VideoBrowser")!, topo, mirror);
            }
            finally {
                shell.Close();
                ScreenMirrorHead.Override = null;
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    private static Window? Fullscreen(EnhancementPlayerWindow player) => player.VideoFullscreenWindow;
    [Theory]
    [InlineData("page")]
    [InlineData("escape")]
    [InlineData("f11")]
    [InlineData("outside")]
    [InlineData("player")]
    [InlineData("panic")]
    [InlineData("audio")]
    public Task EveryExitRestoresExtendAndReturnsTheSameHost(string exit) => Run(true, (player, host, topo, mirror) => {
        topo.OnApply = clone => { if (clone) host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOffMessage); };
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage);
        var window = Fullscreen(player);
        Assert.NotNull(window); Assert.Same(host, window.Content); Assert.True(mirror.IsMirroring);
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage); // duplicate signal must not clone twice
        Assert.Equal(new[] { true }, topo.Calls);
        switch (exit) {
            case "audio":
                typeof(EnhancementPlayerWindow).GetMethod("ShowMediaPaneFor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(player, new object[] { "audio" });
                break;
            case "page": host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOffMessage); break;
            case "escape":
            case "f11": window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
                Key = exit == "escape" ? Key.Escape : Key.F11 }); break;
            case "outside": window.Close(); break;
            case "player": player.Close(); break;
            case "panic":
                Assert.Single(ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All, s => s.Id == "deeper-player-fullscreen").Stop(null);
                break;
        }
        Assert.Null(Fullscreen(player)); Assert.False(mirror.IsMirroring); Assert.Equal(new[] { true, false }, topo.Calls);
        Assert.Contains(host, ((Grid)player.FindControl<Border>("VideoPane")!.Child!).Children);
    });
    [Fact]
    public Task NavigationKeepsTheHostAndTheClone() => Run(true, (player, host, topo, mirror) => {
        var scripts = new List<string>();
        host.ScriptInvoker = js => { scripts.Add(js); return Task.FromResult<string?>(null); };
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage);
        var window = Fullscreen(player);
        host.OnNavigationCompleted(new Uri("http://127.0.0.1:9/video"));
        Assert.Contains(scripts, js => js.Contains("fullscreenchange") && js.Contains("dblclick") && js.Contains("F11")); Assert.Contains("window._ccpForcedFs = true;", scripts);
        Assert.Same(window, Fullscreen(player)); Assert.Same(host, window!.Content); Assert.True(mirror.IsMirroring); Assert.Equal(new[] { true }, topo.Calls);
    });
    [Fact]
    public Task HostDropsTopmostWhileAnotherWindowHasFocus() => Run(true, (player, host, topo, mirror) => {
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage);
        var window = Fullscreen(player)!;
        Assert.True(window.Topmost);
        static void Raise(Window w, string name) => typeof(WindowBase).GetMethod(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(w, null);
        Raise(window, "HandleDeactivated");
        Assert.False(window.Topmost);
        Raise(window, "HandleActivated");
        Assert.True(window.Topmost);
    });
    [Fact]
    public Task SingleScreenSelectionStillFullscreensWithoutCloning() => Run(false, (player, host, topo, mirror) => {
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage);
        Assert.NotNull(Fullscreen(player));
        host.OnWebMessage(EnhancementPlayerWindow.BrowserExitFullscreenMessage);
        Assert.Null(Fullscreen(player)); Assert.Empty(topo.Calls);
    });
    [Fact]
    public Task CloneFailureStillAllowsFullscreenAndNeverRestoresAnUnchangedTopology() => Run(true, (player, host, topo, mirror) => {
        topo.Result = 50;
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage);
        Assert.NotNull(Fullscreen(player)); Assert.False(mirror.IsMirroring);
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOffMessage);
        Assert.Equal(new[] { true }, topo.Calls);
    });
    [Fact]
    public Task OnePhysicalScreenNeverRequestsClone() => Run(true, (player, host, topo, mirror) => {
        ScreenList.QueryOverride = _ => new[] { MakeScreen(0, true) };
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage);
        Assert.NotNull(Fullscreen(player)); Assert.Empty(topo.Calls);
    });
    [Fact]
    public Task PanicRetriesAFailedRestore() => Run(true, (player, host, topo, mirror) => {
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage);
        topo.Result = 31;
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOffMessage);
        Assert.True(mirror.IsMirroring);
        topo.Result = 0;
        ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(s => s.Id == "deeper-player-fullscreen").Stop(null);
        Assert.False(mirror.IsMirroring);
    });
    [Theory]
    [InlineData("reparent")]
    [InlineData("close")]
    [InlineData("cancel")]
    public Task FailedTeardownHidesAndRemainsRetryable(string failure) => Run(true, (player, host, topo, mirror) => {
        host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage); var window = Fullscreen(player)!;
        EventHandler<WindowClosingEventArgs> broken = (_, e) => e.Cancel = true;
        if (failure == "reparent") player.VideoMoveOverride = () => throw new InvalidOperationException("move");
        else if (failure == "close") player.CloseVideoHost = _ => throw new InvalidOperationException("close"); else window.Closing += broken;
        host.OnWebMessage(EnhancementPlayerWindow.BrowserExitFullscreenMessage);
        Assert.False(window.IsVisible); Assert.False(window.Topmost); Assert.Same(window, Fullscreen(player)); Assert.False(mirror.IsMirroring);
        player.VideoMoveOverride = null; player.CloseVideoHost = w => w.Close(); window.Closing -= broken; EnhancementPlayerWindow.ForceExitFullscreenAll();
        Assert.Null(Fullscreen(player)); Assert.Contains(host, ((Grid)player.FindControl<Border>("VideoPane")!.Child!).Children);
    });
    [Fact]
    public Task KeysRelayTransportRepeatAndEmergencyHold() => Run(true, (player, host, topo, mirror) => {
        var scripts = new List<string>(); host.ScriptInvoker = js => { scripts.Add(js); return Task.FromResult<string?>("\"ok\""); };
        var now = DateTimeOffset.UtcNow; player.InputNow = () => now;
        typeof(EnhancementPlayerWindow).GetField("_isVideoMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(player, true);
        typeof(EnhancementPlayerWindow).GetField("_videoNavigated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(player, true);
        typeof(EnhancementPlayerWindow).GetField("_durationSec", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(player, 100d);
        foreach (var key in new[] { "Space", "Left", "Right", "Up", "Down", "M", "F" }) host.OnWebMessage("ccp_key:" + key + ":1:0");
        Assert.NotNull(Fullscreen(player)); Assert.Contains(scripts, x => x.Contains("l.play()")); Assert.Contains(scripts, x => x.Contains("(-30)")); Assert.Contains(scripts, x => x.Contains("(30)"));
        Assert.Contains(scripts, x => x.Contains("volume+0.05")); Assert.Contains(scripts, x => x.Contains("volume+-0.05")); Assert.Contains(scripts, x => x.Contains("l.volume=0;"));
        host.OnWebMessage("ccp_key:Right:0:0"); Assert.Contains(scripts, x => x.Contains("(5)"));
        var nativeCount = scripts.Count; player.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space }); Assert.Equal(nativeCount + 1, scripts.Count); Assert.Contains(scripts, x => x.Contains("l.pause()"));
        typeof(WindowBase).GetMethod("HandleDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(player, null); player.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space }); Assert.Equal(nativeCount + 2, scripts.Count);
        var count = scripts.Count; host.OnWebMessage("ccp_key:M:0:1"); Assert.Equal(count, scripts.Count);
        host.OnWebMessage("ccp_key:Escape:0:0"); Assert.Null(Fullscreen(player)); now += TimeSpan.FromSeconds(2); host.OnWebMessage("ccp_key:Escape:0:1");
        player.TickPlayerInput(); Assert.DoesNotContain(scripts, x => x.Contains("l.pause(); l.currentTime=0"));
        now += TimeSpan.FromSeconds(1); player.TickPlayerInput(); Assert.Contains(scripts, x => x.Contains("l.pause(); l.currentTime=0"));
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("deeper_player_status_escape_stopped"), player.FindControl<TextBlock>("TxtStatus")!.Text);
        scripts.Clear(); host.OnWebMessage("ccp_key:Escape:0:0"); host.OnWebMessage("ccp_keyup:Escape"); now += TimeSpan.FromSeconds(2); host.OnWebMessage("ccp_key:Escape:0:1"); now += TimeSpan.FromSeconds(1); player.TickPlayerInput(); Assert.Empty(scripts);
        CoreSettings.Current.StrictLockEnabled = true; host.OnWebMessage("ccp_key:Escape:0:0"); now += TimeSpan.FromSeconds(3); host.OnWebMessage("ccp_key:Escape:0:1"); player.TickPlayerInput(); Assert.Empty(scripts);
        CoreSettings.Current.StrictLockEnabled = false; host.OnWebMessage("ccp_key:Escape:0:0"); now += TimeSpan.FromSeconds(3); player.TickPlayerInput(); Assert.Empty(scripts); // lost key-up silence cancels
        typeof(EnhancementPlayerWindow).GetField("_isVideoMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(player, false);
        var volume = player.FindControl<Slider>("SliderVolume")!; volume.Value = 50; player.PlayerKey(Key.Up); Assert.Equal(55, volume.Value); player.PlayerKey(Key.Down); Assert.Equal(50, volume.Value);
        player.PlayerKey(Key.M); Assert.Equal(0, volume.Value); player.PlayerKey(Key.M); Assert.Equal(50, volume.Value);
        player.PlayerKey(Key.M); volume.Value = 60; player.PlayerKey(Key.M); Assert.Equal(0, volume.Value); player.PlayerKey(Key.M); Assert.Equal(60, volume.Value);
        player.PlayerKey(Key.Right); Assert.Equal(5d, typeof(EnhancementPlayerWindow).GetField("_currentSec", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(player));
    });
    [Fact]
    public Task EffectsAndCursorFollowFullscreenLifecycle() => Run(true, (player, host, topo, mirror) => {
        var tint = new ConditioningControlPanel.Avalonia.Views.Overlays.TintOverlayWindow(); tint.Show();
        var spiral = new ConditioningControlPanel.Avalonia.Views.Overlays.SpiralOverlayWindow(); spiral.Show();
        var other = new Window(); other.Show(); var raised = new List<Window>(); var scripts = new List<string>();
        player.EffectWindows = () => new[] { other, spiral, tint }; player.RaiseEffect = raised.Add;
        host.ScriptInvoker = js => { scripts.Add(js); return Task.FromResult<string?>(null); };
        var now = DateTimeOffset.UtcNow; player.InputNow = () => now;
        try {
            host.OnWebMessage(EnhancementPlayerWindow.BrowserFullscreenOnMessage); Assert.Equal(new Window[] { tint, spiral }, raised); raised.Clear();
            typeof(WindowBase).GetMethod("HandleActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Fullscreen(player), null); Assert.Equal(new Window[] { tint, spiral }, raised);
            now += TimeSpan.FromMilliseconds(1999); player.TickPlayerInput(); Assert.DoesNotContain(scripts, x => x.Contains("cursor='none'"));
            now += TimeSpan.FromMilliseconds(1); player.TickPlayerInput(); Assert.Contains(scripts, x => x.Contains("cursor='none'")); scripts.Clear();
            host.OnWebMessage("ccp_pointer"); Assert.Contains(scripts, x => x.Contains("cursor=''")); scripts.Clear();
            now += TimeSpan.FromMilliseconds(1999); player.TickPlayerInput(); Assert.Empty(scripts);
            now += TimeSpan.FromMilliseconds(1); player.TickPlayerInput(); Assert.Contains(scripts, x => x.Contains("cursor='none'")); raised.Clear();
            var video = CoreEngine.Video; CoreEngine.Video = new MandatoryVideoScheduler(null!);
            typeof(MandatoryVideoScheduler).GetField("_playing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(CoreEngine.Video, true);
            player.TickPlayerInput(); Assert.Empty(raised); CoreEngine.Video = video;
            host.OnWebMessage(EnhancementPlayerWindow.BrowserExitFullscreenMessage); Assert.Equal(new Window[] { tint, spiral }, raised); Assert.Contains(scripts, x => x.Contains("cursor=''"));
            scripts.Clear(); now += TimeSpan.FromSeconds(3); player.TickPlayerInput(); Assert.Empty(scripts);
        } finally { tint.Close(); spiral.Close(); other.Close(); }
    });
}

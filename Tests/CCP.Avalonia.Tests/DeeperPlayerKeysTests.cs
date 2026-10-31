using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF EnhancementPlayerWindow.xaml.cs:2779-3000. Real routed keys and the existing web script seam.
public sealed class DeeperPlayerKeysTests
{
    private static EnhancementPlayerWindow Create()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        return new EnhancementPlayerWindow(null, null);
    }

    private static void Set(object w, string field, object value) =>
        w.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(w, value);
    private static void Tick(object w) => w.GetType().GetMethod("EscHoldTimer_Tick",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, new object?[] { null, EventArgs.Empty });
    private static void Press(Window w, Key key, bool down = true, KeyModifiers mods = KeyModifiers.None) =>
        w.RaiseEvent(new KeyEventArgs { RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent,
            Key = key, KeyModifiers = mods });
    private static void Tap(Window w, Key key) { Press(w, key); Press(w, key, false); }
    private static List<string> Video(EnhancementPlayerWindow w)
    {
        var scripts = new List<string>();
        w.FindControl<WebHost>("VideoBrowser")!.ScriptInvoker = s => { scripts.Add(s); return Task.FromResult<string?>("ok"); };
        Set(w, "_isVideoMode", true);
        Set(w, "_videoNavigated", true);
        Set(w, "_durationSec", 100d);
        return scripts;
    }

    [Fact]
    public Task RoutedKeysToggleOnceSeekClampAndRestoreMutedVolume() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var cleanup = new PlayerCleanup(Create());
        var w = cleanup.Window;
        var scripts = Video(w);
        Press(w, Key.Space);
        Press(w, Key.Space); // repeat must not pause again
        Assert.Single(scripts);
        Assert.Contains("l.play();", scripts[0]);
        Press(w, Key.Space, false);
        Tap(w, Key.Space);
        Assert.Contains("l.pause();", scripts[1]);
        Set(w, "_currentSec", 10d);
        Tap(w, Key.Right);
        Assert.Contains("l.currentTime=15;", scripts[^1]);
        Press(w, Key.Right, mods: KeyModifiers.Shift);
        Assert.Contains("l.currentTime=45;", scripts[^1]);
        Set(w, "_currentSec", 99d);
        Tap(w, Key.Right);
        Assert.Contains("l.currentTime=100;", scripts[^1]);
        Set(w, "_currentSec", 2d);
        Tap(w, Key.Left);
        Assert.Contains("l.currentTime=0;", scripts[^1]);
        Press(w, Key.Left, mods: KeyModifiers.Shift);
        Assert.Contains("l.currentTime=0;", scripts[^1]);

        var slider = w.FindControl<Slider>("SliderVolume")!;
        slider.Value = 37;
        Tap(w, Key.Up);
        Assert.Equal(42, slider.Value);
        Assert.Contains("l.volume=0.42;", scripts[^1]);
        Tap(w, Key.Down);
        Press(w, Key.M);
        Press(w, Key.M);
        Assert.Equal(0, slider.Value);
        Assert.Equal(37, CoreSettings.Current.DeeperPlayerVolume);
        Press(w, Key.M, false);
        Tap(w, Key.M);
        Assert.Equal(37, slider.Value);
        Tap(w, Key.M);
        slider.Value = 20; // manual adjustment ends mute
        Tap(w, Key.M);
        Tap(w, Key.M);
        Assert.Equal(20, slider.Value);
        slider.Value = 99;
        Tap(w, Key.Up);
        Assert.Equal(100, slider.Value);
        slider.Value = 1;
        Tap(w, Key.Down);
        Assert.Equal(0, slider.Value);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EscapeRequiresFreshSignalsAndRechecksStrictLockBeforeStopping() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var cleanup = new PlayerCleanup(Create());
        var w = cleanup.Window;
        var scripts = Video(w);
        CoreSettings.Current.StrictLockEnabled = false;
        var closed = false;
        w.Closed += (_, _) => closed = true;
        var now = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);
        w.UtcNow = () => now;
        Tap(w, Key.Escape); // paused loaded video is still active
        Assert.False(closed);
        Assert.Empty(scripts);
        Press(w, Key.Escape);
        now = now.AddSeconds(2);
        Tick(w); // lost key-up cancels the hold
        Assert.False(w.FindControl<Border>("HoldEscHint")!.IsVisible);
        Assert.Empty(scripts);
        Press(w, Key.Escape, false);
        Press(w, Key.Escape);
        for (var i = 0; i < 3; i++) { now = now.AddSeconds(1); Press(w, Key.Escape); Tick(w); }
        Assert.Single(scripts);
        Assert.Contains("l.pause(); l.currentTime=0;", scripts[0]);
        Press(w, Key.Escape, false);
        Assert.False(closed);
        scripts.Clear();
        Press(w, Key.Escape);
        CoreSettings.Current.StrictLockEnabled = true;
        now = now.AddSeconds(1);
        Press(w, Key.Escape);
        Tick(w);
        Assert.Empty(scripts);
        Assert.False(w.FindControl<Border>("HoldEscHint")!.IsVisible);
        Press(w, Key.Escape, false);
        Set(w, "_videoNavigated", false);
        Tap(w, Key.Escape);
        Assert.False(closed);
        CoreSettings.Current.StrictLockEnabled = false;
        w.FindControl<global::Avalonia.Controls.Primitives.Popup>("ChangePopup")!.IsOpen = true;
        Tap(w, Key.Escape); // popup dismissal consumes the tap
        Assert.False(closed);
        Assert.False(w.FindControl<global::Avalonia.Controls.Primitives.Popup>("ChangePopup")!.IsOpen);
        Tap(w, Key.Escape);
        Assert.True(closed);
        return Task.CompletedTask;
    });

    [Fact]
    public Task TextFocusAndDeactivationDoNotLeaveAKeyHeld() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var cleanup = new PlayerCleanup(Create());
        var w = cleanup.Window;
        var scripts = Video(w);
        var input = new TextBox();
        ((Grid)w.Content!).Children.Add(input);
        w.Show();
        Assert.True(input.Focus());
        Press(w, Key.Space);
        Press(w, Key.Right);
        Assert.Empty(scripts);
        w.FindControl<Button>("BtnPlayPause")!.Focus();
        Press(w, Key.Space);
        Assert.Single(scripts);
        w.GetType().GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(w, new object[] { EventArgs.Empty });
        Press(w, Key.Space); // focus loss clears the repeat latch
        Assert.Equal(2, scripts.Count);
        Assert.Contains("l.pause();", scripts[^1]);
        return Task.CompletedTask;
    });

    // Assembly TestIsolation restores settings and profile files; always stop this window's timers too.
    private sealed class PlayerCleanup(EnhancementPlayerWindow window) : IDisposable
    {
        public EnhancementPlayerWindow Window => window;
        public void Dispose() => window.Close();
    }
}

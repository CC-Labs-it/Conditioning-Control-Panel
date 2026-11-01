using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class JustDropFullscreenTests
{
    private static Button Toggle(GameWindow window)
    {
        var grid = Assert.IsType<Grid>(window.Content);
        Assert.Same(window.Web, grid.Children[1]);
        Assert.Equal(1, Grid.GetRow(window.Web));
        var bar = Assert.IsType<DockPanel>(Assert.IsType<Border>(grid.Children[0]).Child);
        return Assert.IsType<Button>(bar.Children[0]);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task ShopChromeStaysVisibleAndRestoresTheWindow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var w = new GameWindow(GameWindow.Games[GameWindow.JustDropId]);
        w.Show();
        try
        {
            var button = Toggle(w);
            Assert.Equal(WindowState.Normal, w.WindowState);
            Assert.Equal(Loc.Get("btn_fullscreen"), button.Content);
            Click(button);
            Assert.Equal(WindowState.FullScreen, w.WindowState);
            Assert.Same(button, Toggle(w));
            Assert.Equal(Loc.Get("btn_exit_fullscreen"), button.Content);
            Click(button);
            Assert.Equal(WindowState.Normal, w.WindowState);
            Assert.Equal(Loc.Get("btn_fullscreen"), button.Content);

            w.WindowState = WindowState.Maximized;
            Click(button);
            Click(button);
            Assert.Equal(WindowState.Maximized, w.WindowState);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EscapeInHostChromeLeavesFullscreen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var w = new GameWindow(GameWindow.Games[GameWindow.JustDropId]);
        w.Show();
        try
        {
            var button = Toggle(w);
            w.WindowState = WindowState.FullScreen;
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            button.RaiseEvent(key);
            Assert.True(key.Handled);
            Assert.Equal(WindowState.Normal, w.WindowState);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EscapeInPageWorksBeforeNavigationCompletes() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var w = new GameWindow(GameWindow.Games[GameWindow.JustDropId]);
        w.Load(null!); // Just Drop's live site does not use the local asset server
        w.Show();
        try
        {
            w.WindowState = WindowState.FullScreen;
            Assert.Null(w.Web.CurrentUrl);
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            Assert.False(w.IsReady); // business messages still require a completed allowed URL
            w.Web.OnWebMessage("\"{\\\"type\\\":\\\"ccp-host-escape\\\"}\"");
            Assert.Equal(WindowState.Normal, w.WindowState);
            w.Web.OnWebMessage("{\"type\":\"ccp-host-escape\"}");
            Assert.Equal(WindowState.Normal, w.WindowState); // Escape never enters fullscreen
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PageEscapeListenerIsInstalledBeforeEachEngineNavigates() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var w = new GameWindow(GameWindow.Games[GameWindow.JustDropId]);
        w.Load(null!);
        var calls = new List<string>();
        var pending = new TaskCompletionSource<bool>();
        w.Web.NativeCarrierInvoker = script => { calls.Add(script); return pending.Task; };
        var old = w.Web.NewScrolllerEngine();
        old(true);
        Assert.Null(w.Web.ScrolllerGate);
        Assert.Contains(GameWindow.JustDropEscapeScript, Assert.Single(calls));
        var current = w.Web.NewScrolllerEngine();
        pending.SetResult(true);
        await Task.Yield();
        Assert.Null(w.Web.ScrolllerGate);
        current(true);
        Assert.True(w.Web.ScrolllerGate);
        Assert.Equal(2, calls.Count);
        Assert.Contains(GameWindow.JustDropEscapeScript, calls[1]);

        string? fallback = null;
        w.Web.ScriptInvoker = script => { fallback = script; return Task.FromResult<string?>(null); };
        w.Web.OnNavigationCompleted(new Uri("https://app.cclabs.app/"));
        Assert.Contains(GameWindow.JustDropEscapeScript, fallback);
    });
}

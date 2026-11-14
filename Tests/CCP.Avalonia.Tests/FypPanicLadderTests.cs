using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FypPanicLadderTests
{
    // WPF MainWindow.xaml.cs:1776: in ladder mode the feed consumes the panic key.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanicKeyHonoursFeedLadderAndOverride(bool overridesAll) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldSurfaces = PanicSurfaces.All;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.PanicKeyEnabled = true;
        service.Current.PanicKey = "F8";
        service.Current.PanicOverridesAll = overridesAll;
        var stops = new List<string>();
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stops.Add("stop")) };
        var shell = new MainShellWindow();
        var feed = new GameWindow(GameWindow.Games[GameWindow.FypId]);
        try
        {
            feed.Show();
            Assert.True(GameWindow.IsForYouOnScreen());
            shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
            Assert.Equal(overridesAll, feed.IsVisible);
            Assert.Equal(overridesAll ? 1 : 0, stops.Count);
        }
        finally
        {
            feed.Close();
            shell.Close();
            PanicSurfaces.All = oldSurfaces;
            CoreSettings.ServiceProvider = oldProvider;
        }
    });
    // WPF FypGhostOverlay.Diagnose order: Windows/no X11 = unsupported, no compositor = composition-disabled.
    [Theory]
    [InlineData(true, true, true, "unsupported")]
    [InlineData(false, false, true, "unsupported")]
    [InlineData(false, true, false, "composition-disabled")]
    [InlineData(false, true, true, null)]
    public void GhostRefusalFollowsThePlatform(bool windows, bool x11, bool compositing, string? expected)
        => Assert.Equal(expected, GameWindow.FypGhostRefusal(windows, x11, compositing));

    /// <summary>WPF EnterGhost/ExitGhost (FypHostService.cs:458, :630), the ghost buttons (:604, :618) and the
    /// two-rung panic ladder (MainWindow.xaml.cs:1776-1795): every rung leaves no button, no click-through and
    /// full alpha behind, and a failed X11 call refuses instead of leaving a half ghost.</summary>
    [Fact]
    public void GhostModeEntersRestoresAndRunsThePanicLadder() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var old = (GameWindow.FypGhostSupport, GameWindow.FypGhostClickThrough, GameWindow.FypGhostOpacity, GameWindow.FypGhostNow, GameWindow.FypGhostButtonConfigure);
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        var lib = System.IO.Directory.CreateTempSubdirectory("fyp-ghost-").FullName;
        CorePaths.EffectiveAssetsProvider = () => lib;   // the page-reboot rung builds a manifest
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.PanicKeyEnabled = true;
        s.PanicKey = "F8";
        s.PanicOverridesAll = false;
        s.FypWindowOpacity = 0.5;
        s.FypMuted = false;
        bool clickThrough = false, opacityOk = true;
        double alpha = 1;
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        GameWindow.FypGhostButtonConfigure = (_, _, passive) => passive; // headless has no native window
        GameWindow.FypGhostSupport = () => null;
        GameWindow.FypGhostClickThrough = (_, on) => { clickThrough = on; return true; };
        GameWindow.FypGhostOpacity = (_, a) => { alpha = a; return opacityOk; };
        GameWindow.FypGhostNow = () => now;
        var shell = new MainShellWindow();
        var feed = new GameWindow(GameWindow.Games[GameWindow.FypId]);
        var sent = new List<JObject>();
        feed.Posted += json => sent.Add(JObject.Parse(json));
        void Ghost(bool on) => feed.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":" + (on ? "true" : "false") + "}");
        void AssertSolid()
        {
            Assert.False(feed.IsFypGhosted);
            Assert.False(clickThrough);
            Assert.Equal(1.0, alpha);
            Assert.Empty(feed.FypGhostButtons);
            Assert.False((bool)sent.Last(o => (string?)o["type"] == "clickThrough")["on"]!);
        }
        try
        {
            feed.Show();
            var (deco, top) = (feed.WindowDecorations, feed.Topmost);
            s.FypWindowOpacity = double.NaN;
            Ghost(true);
            Assert.True(feed.IsFypGhosted);
            Assert.True(clickThrough);
            Assert.Equal(1.0, alpha); // WPF non-finite fallback on entry
            feed.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"windowOpacity\",\"value\":\"NaN\"}");
            Assert.True(double.IsNaN(s.FypWindowOpacity));
            Assert.Equal(1.0, alpha); // and on a live update
            feed.ExitFypGhost();
            typeof(ConditioningControlPanel.Models.AppSettings).GetField("_fypWindowOpacity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(s, 0.0);
            Ghost(true);
            Assert.Equal(0.01, alpha); // a corrupted persisted value
            feed.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"windowOpacity\",\"value\":0.5}");
            Assert.Equal(0.5, alpha);
            Assert.True(feed.Topmost);
            Assert.Equal(2, feed.FypGhostButtons.Count);
            Assert.All(feed.FypGhostButtons, b => Assert.True(b.IsVisible));
            Assert.False(GameWindow.IsForYouOnScreen());   // a ghost does not own the screen (WPF BubbleCountService.cs:225)
            feed.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"windowOpacity\",\"value\":0.3}");
            Assert.Equal(0.3, alpha);
            feed.FypGhostMute();
            Assert.True(s.FypMuted);
            Assert.True((bool)sent.Last()["on"]!);
            Assert.Equal("setMuted", (string?)sent.Last()["type"]);
            Assert.True(feed.IsFypGhosted);   // mute stays a ghost

            shell.HandlePanicKeyPress(now.ToLocalTime());   // rung 1: ghost off
            AssertSolid();
            Assert.Equal((deco, top), (feed.WindowDecorations, feed.Topmost));
            Assert.True(feed.IsVisible && GameWindow.IsForYouOnScreen());
            shell.HandlePanicKeyPress(now.ToLocalTime());   // the reflexive double-tap is swallowed
            Assert.True(feed.IsVisible);

            Ghost(true);
            feed.FypGhostGear();   // gear: solid again, options up
            AssertSolid();
            Assert.Equal("openOptions", (string?)sent.Last()["type"]);

            GameWindow.FypGhostButtonConfigure = (_, _, _) => false;
            Ghost(true); // no passive buttons: fail closed and restore the feed
            Assert.Equal("mirror-threw", (string?)sent.Last()["reason"]);
            AssertSolid();
            GameWindow.FypGhostButtonConfigure = (_, _, passive) => passive;

            opacityOk = false;   // an X11 call that fails refuses, never a half ghost
            Ghost(true);
            Assert.Equal("mirror-failed", (string?)sent.Last()["reason"]);
            AssertSolid();
            opacityOk = true;

            Ghost(true);
            feed.HandleMessage("{\"type\":\"ready\"}");   // a page reboot under the ghost gives the window back
            AssertSolid();

            Ghost(true);
            var buttons = feed.FypGhostButtons.ToArray();
            now = now.AddSeconds(5);
            shell.HandlePanicKeyPress(now.ToLocalTime());   // rung 1 again
            now = now.AddSeconds(5);
            shell.HandlePanicKeyPress(now.ToLocalTime());   // rung 2: close
            Assert.False(feed.IsVisible);
            Assert.All(buttons, b => Assert.False(b.IsVisible));
        }
        finally
        {
            feed.Close();
            shell.Close();
            (GameWindow.FypGhostSupport, GameWindow.FypGhostClickThrough, GameWindow.FypGhostOpacity, GameWindow.FypGhostNow, GameWindow.FypGhostButtonConfigure) = old;
            CorePaths.EffectiveAssetsProvider = oldAssets;
            CoreSettings.ServiceProvider = oldProvider;
            System.IO.Directory.Delete(lib, true);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GhostMinimizeOrHideRestoresInputAndClosesControls(bool minimize) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = (GameWindow.FypGhostSupport, GameWindow.FypGhostClickThrough, GameWindow.FypGhostOpacity, GameWindow.FypGhostButtonConfigure);
        bool through = false;
        double alpha = 1;
        GameWindow.FypGhostSupport = () => null;
        GameWindow.FypGhostButtonConfigure = (_, _, passive) => passive;
        GameWindow.FypGhostClickThrough = (_, on) => { through = on; return true; };
        GameWindow.FypGhostOpacity = (_, value) => { alpha = value; return true; };
        var feed = new GameWindow(GameWindow.Games[GameWindow.FypId]);
        try
        {
            feed.Show();
            var position = feed.Position;
            feed.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":true}");
            var buttons = feed.FypGhostButtons.ToArray();
            Assert.True(feed.IsFypGhosted);
            if (minimize) feed.WindowState = global::Avalonia.Controls.WindowState.Minimized;
            else feed.Hide();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(feed.IsFypGhosted);
            Assert.False(through);
            Assert.Equal(1, alpha);
            Assert.Empty(feed.FypGhostButtons);
            Assert.All(buttons, button => Assert.False(button.IsVisible));
            Assert.Equal(position, feed.Position);
            if (minimize) Assert.Equal(global::Avalonia.Controls.WindowState.Normal, feed.WindowState);
            else
            {
                Assert.Same(feed, GameWindow.Launch(GameWindow.FypId));
                Assert.True(feed.IsVisible);
                feed.Close();
                Assert.DoesNotContain(feed, GameWindow.OpenWindowsForTests());
            }
        }
        finally
        {
            feed.Close();
            (GameWindow.FypGhostSupport, GameWindow.FypGhostClickThrough, GameWindow.FypGhostOpacity, GameWindow.FypGhostButtonConfigure) = old;
        }
    });

    /// <summary>WPF Close (:138): a feed closed while ghosted (override panic, the games surface) leaves nothing.</summary>
    [Fact]
    public void ClosingAGhostedFeedRestoresEverything() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var old = (GameWindow.FypGhostSupport, GameWindow.FypGhostClickThrough, GameWindow.FypGhostOpacity, GameWindow.FypGhostButtonConfigure);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        bool clickThrough = false;
        GameWindow.FypGhostButtonConfigure = (_, _, passive) => passive; // headless has no native window
        GameWindow.FypGhostSupport = () => null;
        GameWindow.FypGhostClickThrough = (_, on) => { clickThrough = on; return true; };
        GameWindow.FypGhostOpacity = (_, _) => true;
        var feed = new GameWindow(GameWindow.Games[GameWindow.FypId]);
        try
        {
            feed.Show();
            feed.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":true}");
            var buttons = feed.FypGhostButtons.ToArray();
            Assert.Equal(2, buttons.Length);
            GameWindow.CloseAllForPanic();   // the "games" panic surface (PanicSurfaces.cs:44)
            Assert.False(feed.IsVisible);
            Assert.False(feed.IsFypGhosted);
            Assert.False(clickThrough);
            Assert.All(buttons, b => Assert.False(b.IsVisible));
        }
        finally
        {
            feed.Close();
            (GameWindow.FypGhostSupport, GameWindow.FypGhostClickThrough, GameWindow.FypGhostOpacity, GameWindow.FypGhostButtonConfigure) = old;
            CoreSettings.ServiceProvider = oldProvider;
        }
    });
    [Theory]
    [InlineData("exit")]
    [InlineData("minimize")]
    [InlineData("hide")]
    [InlineData("close")]
    [InlineData("start-failure")]
    public void GhostReassertsAndStops(string exit) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = (GameWindow.FypGhostSupport, GameWindow.FypGhostClickThrough, GameWindow.FypGhostOpacity,
            GameWindow.FypGhostButtonConfigure, GameWindow.FypGhostRaise, GameWindow.FypGhostStartTick);
        Action? tick = null;
        bool stopped = false, through = false;
        var raised = new List<global::Avalonia.Controls.TopLevel>();
        GameWindow.FypGhostSupport = () => null;
        GameWindow.FypGhostClickThrough = (_, on) => { through = on; return true; };
        GameWindow.FypGhostOpacity = (_, _) => true;
        GameWindow.FypGhostButtonConfigure = (_, _, passive) => passive;
        GameWindow.FypGhostRaise = w => { raised.Add(w); return true; };
        GameWindow.FypGhostStartTick = action =>
        {
            if (exit == "start-failure") throw new InvalidOperationException();
            tick = action;
            return new TickDisposal(() => stopped = true);
        };
        var feed = new GameWindow(GameWindow.Games[GameWindow.FypId]);
        var sent = new List<JObject>();
        feed.Posted += json => sent.Add(JObject.Parse(json));
        try
        {
            feed.Show();
            feed.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":true}");
            if (exit == "start-failure")
            {
                Assert.False(feed.IsFypGhosted);
                Assert.False(through);
                Assert.Empty(feed.FypGhostButtons);
                Assert.Contains(sent, o => (string?)o["type"] == "ghost-unavailable");
                return;
            }
            Assert.True(feed.IsFypGhosted);
            var buttons = feed.FypGhostButtons.ToArray();
            tick!();
            Assert.Equal(new global::Avalonia.Controls.TopLevel[] { feed, buttons[0], buttons[1] }, raised);
            if (exit == "exit") feed.ExitFypGhost();
            else if (exit == "minimize") feed.WindowState = global::Avalonia.Controls.WindowState.Minimized;
            else if (exit == "hide") feed.Hide();
            else feed.Close();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(stopped);
            raised.Clear();
            tick();
            Assert.Empty(raised);
        }
        finally
        {
            feed.Close();
            (GameWindow.FypGhostSupport, GameWindow.FypGhostClickThrough, GameWindow.FypGhostOpacity,
                GameWindow.FypGhostButtonConfigure, GameWindow.FypGhostRaise, GameWindow.FypGhostStartTick) = old;
        }
    });

    private sealed class TickDisposal(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }

}

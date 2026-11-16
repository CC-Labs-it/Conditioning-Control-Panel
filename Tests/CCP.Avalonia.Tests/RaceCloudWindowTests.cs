using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Tests;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RaceCloudWindowTests
{
    private static readonly Uri Site = new("https://bambicloud.com/");
    private static RaceCloudWindow Create() => new(Site, u => u.Scheme == "https" && u.Host == "bambicloud.com");

    [Fact]
    public Task NavigationAndMessagesStayOnSite() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var window = Create();
        window.Web.ScriptInvoker = _ => Task.FromResult<string?>("ok");
        var opened = new List<string>();
        window.OpenExternal = url => { opened.Add(url); return true; };
        var other = new Uri("https://example.org/path");
        Assert.False(window.Web.AllowNavigation!(other));
        Assert.Equal(other.AbsoluteUri, Assert.Single(opened));
        var messages = new List<string>();
        window.Message += m => messages.Add((string)m["type"]!);
        window.Web.OnNavigationCompleted(other);
        window.Web.OnWebMessage("{\"type\":\"cloud-track\"}");
        window.Web.OnNavigationCompleted(Site);
        window.Web.OnWebMessage("{\"type\":\"unrelated\"}");
        window.Web.OnWebMessage("{\"type\":\"cloud-track\"}");
        Assert.Equal(new[] { "cloud-track" }, messages);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CloseHidesAndSavesBounds() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.RaceCloudWindowLeft, s.RaceCloudWindowTop, s.RaceCloudWindowWidth, s.RaceCloudWindowHeight);
        try
        {
            using var window = Create();
            int hidden = 0; window.Hidden += () => hidden++;
            window.ShowOrFocus();
            window.Window.Position = new global::Avalonia.PixelPoint(40, 50);
            window.Window.Width = 700; window.Window.Height = 600;
            window.Window.Close();
            Assert.False(window.IsOpen);
            Assert.Equal(1, hidden);
            Assert.Equal(40, s.RaceCloudWindowLeft);
            Assert.Equal(50, s.RaceCloudWindowTop);
            Assert.Equal(700, s.RaceCloudWindowWidth);
            Assert.Equal(600, s.RaceCloudWindowHeight);
        }
        finally { (s.RaceCloudWindowLeft, s.RaceCloudWindowTop, s.RaceCloudWindowWidth, s.RaceCloudWindowHeight) = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StartRepeatsUntilPlayAndProcessFailureIsReported() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var window = Create();
        var scripts = new List<string>();
        window.Web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); };
        var messages = new List<string>();
        window.Message += m => messages.Add((string)m["type"]!);
        window.RequestStart();
        window.Web.OnNavigationCompleted(Site);
        window.Web.OnNavigationCompleted(Site);
        Assert.Equal(2, scripts.FindAll(x => x.Contains("cloud-start") && !x.Contains("__ccpRaceCloud")).Count);
        window.Web.OnWebMessage("{\"type\":\"cloud-play\"}");
        scripts.Clear();
        window.Web.OnNavigationCompleted(Site);
        Assert.Empty(scripts.FindAll(x => x.Contains("cloud-start") && !x.Contains("__ccpRaceCloud")));
        window.Web.OnBrowserProcessFailed();
        Assert.Contains("cloud-failed", messages);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupRoutesAndWatcherFailureIsReported() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var window = Create();
        var opened = new List<string>();
        window.OpenExternal = url => { opened.Add(url); return true; };
        Assert.True(window.Web.OnBrowserPopup(new Uri("https://example.org/")));
        Assert.Single(opened);
        var frames = new List<string>();
        window.Message += m => frames.Add((string)m["type"]!);
        window.Web.ScriptInvoker = _ => Task.FromResult<string?>(null);
        window.Web.OnNavigationCompleted(Site);
        Assert.Contains("cloud-failed", frames);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DropsOffsiteAndNonCloudMessages() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var window = Create();
        window.Web.ScriptInvoker = _ => Task.FromResult<string?>("ok");
        var frames = new List<string>();
        window.Message += m => frames.Add((string)m["type"]!);
        window.Web.OnNavigationCompleted(new Uri("https://example.org/"));
        window.Web.OnWebMessage("{\"type\":\"cloud-track\"}");
        window.Web.OnNavigationCompleted(Site);
        window.Web.OnWebMessage("{\"type\":\"other\"}");
        Assert.Empty(frames);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ProcessFailureReportsCloudFailed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var window = Create();
        string? type = null;
        window.Message += m => type = (string?)m["type"];
        window.Web.OnBrowserProcessFailed();
        Assert.Equal("cloud-failed", type);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PlayStopsStartRetries() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var window = Create();
        var scripts = new List<string>();
        window.Web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); };
        window.RequestStart();
        window.Web.OnNavigationCompleted(Site);
        window.Web.OnWebMessage("{\"type\":\"cloud-play\"}");
        scripts.Clear();
        window.Web.OnNavigationCompleted(Site);
        Assert.Empty(scripts.FindAll(x => x.Contains("cloud-start") && !x.Contains("__ccpRaceCloud")));
        return Task.CompletedTask;
    });

    [Fact]
    public Task BackgroundOpenAndDisposeCloseTheWindow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        window.ShowInBackground();
        Assert.True(window.IsOpen);
        Assert.False(window.Window.ShowActivated);
        window.Dispose();
        Assert.False(window.IsOpen);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SitePopupNavigatesInPlaceAndPauseIsPosted() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var window = Create();
        var target = new Uri("https://bambicloud.com/track");
        Assert.True(window.Web.OnBrowserPopup(target));
        Assert.Equal(target, window.Web.Source);
        var scripts = new List<string>();
        window.Web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); };
        window.Web.OnNavigationCompleted(Site);
        scripts.Clear();
        window.PostToPage(new { type = "cloud-set-paused", on = true });
        Assert.Contains(scripts, script => script.Contains("cloud-set-paused"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task RememberedPositionIsClampedToScreen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.RaceCloudWindowLeft, s.RaceCloudWindowTop);
        try
        {
            s.RaceCloudWindowLeft = 100000; s.RaceCloudWindowTop = 100000;
            using var window = Create();
            var screen = window.Window.Screens.Primary!;
            Assert.InRange(window.Window.Position.X, screen.WorkingArea.X, screen.WorkingArea.Right - 200);
            Assert.InRange(window.Window.Position.Y, screen.WorkingArea.Y, screen.WorkingArea.Bottom - 120);
        }
        finally { (s.RaceCloudWindowLeft, s.RaceCloudWindowTop) = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FirstOpenSizeIsCappedToPrimaryScreen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Assert.Equal(new global::Avalonia.Size(1366 * 0.6, 768 * 0.8), RaceCloudWindow.DefaultSize(new global::Avalonia.PixelSize(1366, 768), 1));
        Assert.Equal(new global::Avalonia.Size(1280 * 0.6, 720 * 0.8), RaceCloudWindow.DefaultSize(new global::Avalonia.PixelSize(1920, 1080), 1.5));
        Assert.Equal(new global::Avalonia.Size(1100, 820), RaceCloudWindow.DefaultSize(null, 1));
        Assert.Equal(new global::Avalonia.Size(1100, 820), RaceCloudWindow.DefaultSize(new global::Avalonia.PixelSize(3840, 2160), 1));
        var s = CoreSettings.Current;
        var old = (s.RaceCloudWindowWidth, s.RaceCloudWindowHeight);
        try
        {
            s.RaceCloudWindowWidth = 0; s.RaceCloudWindowHeight = 0;
            using var window = Create();
            var screen = window.Window.Screens.Primary!;
            var expected = RaceCloudWindow.DefaultSize(screen.Bounds.Size, screen.Scaling);
            Assert.Equal(expected.Width, window.Window.Width);
            Assert.Equal(expected.Height, window.Window.Height);
        }
        finally { (s.RaceCloudWindowWidth, s.RaceCloudWindowHeight) = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ProfileIsSetBeforeAttachmentAndWatcherMatchesWpf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var window = Create();
        Assert.Equal(Path.Combine(CorePaths.UserData, "browser_data_bambicloud"), window.Web.PersistentUserDataFolder);
        Assert.True(window.Web.InjectCarrier);
        Assert.Equal(RaceCloudWindow.WatcherScript, window.Web.WindowsDocumentStartScript);
        var wpf = File.ReadAllText(Path.Combine(SourceRoots.RepoRoot, "ConditioningControlPanel", "Services", "Race", "RaceCloudWindow.cs"));
        const string marker = "private const string WatcherScript = @\"";
        var start = wpf.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = wpf.IndexOf("\";", start, StringComparison.Ordinal);
        Assert.Equal(wpf[start..end].Replace("\"\"", "\""), RaceCloudWindow.WatcherScript);
        return Task.CompletedTask;
    });
}

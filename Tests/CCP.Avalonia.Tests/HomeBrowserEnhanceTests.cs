using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public class HomeBrowserEnhanceTests
{
    [Fact]
    public void PollReadsVideoAndDetachStopsRequests() => AvaloniaTestDispatcher.Run(() =>
    {
        var scripts = new List<string>();
        var host = new WebHost { ScriptInvoker = script =>
        {
            scripts.Add(script);
            return Task.FromResult<string?>("{\"t\":12.5,\"d\":30,\"paused\":false,\"rx\":0.1,\"ry\":0.2,\"rw\":0.8,\"rh\":0.7}");
        }};
        using var source = new WebHostVideoTimeSource(host);
        double? notified = null;
        source.PlaybackTimeChanged += time => notified = time;
        source.Attach();
        source.PollAsync().GetAwaiter().GetResult();
        Assert.Equal(12.5, notified);
        Assert.Equal(30, source.GetDurationSeconds());
        Assert.True(source.IsPlaying);
        source.Detach();
        source.PollAsync().GetAwaiter().GetResult();
        Assert.Single(scripts);
    });
    [Fact]
    public void PlaybackCommandsUsePageVideo() => AvaloniaTestDispatcher.Run(() =>
    {
        var scripts = new List<string>();
        var host = new WebHost { ScriptInvoker = script =>
        {
            scripts.Add(script);
            return Task.FromResult<string?>(null);
        }};
        using var source = new WebHostVideoTimeSource(host);
        source.Seek(2.5);
        source.Pause();
        source.Play();
        Assert.Contains("currentTime=2.5", scripts[0]);
        Assert.Contains("v.pause()", scripts[1]);
        Assert.Contains("v.play()", scripts[2]);
    });
    [Fact]
    public void HomeMatchesNavigationAndUnbindsOnToggleAndDetach() => AvaloniaTestDispatcher.Run(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.Current.BrowserEnhanceIfPossible;
        CoreSettings.Current.BrowserEnhanceIfPossible = false;
        var shell = new MainShellWindow();
        string? path = null;
        try
        {
            shell.Show();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var checkbox = tab.FindControl<CheckBox>("ToggleEnhanceIfPossible")!;
            var status = tab.FindControl<TextBlock>("TxtEnhanceMatchStatus")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            web.ScriptInvoker = _ => Task.FromResult<string?>(null);
            var library = shell.BrowserEnhancementLibrary!;
            path = Path.Combine(library.LibraryFolder, "home-browser-test-unique.ccpenh.json");
            File.WriteAllText(path, "{\"$schema\":\"ccp-enhancement/v1\",\"version\":1,\"media_type\":\"video\",\"media_source\":\"https://hypnotube.com/watch/ccp-unique\",\"metadata\":{\"name\":\"Home test\",\"creator\":\"test\"}}");
            Assert.True(checkbox.IsEnabled);
            Assert.Equal(Loc.Get("browser_enhance_match_off"), status.Text);
            checkbox.IsChecked = true;
            Assert.True(CoreSettings.Current.BrowserEnhanceIfPossible);
            Assert.Equal(Loc.Get("browser_enhance_match_none"), status.Text);
            const string url = "https://hypnotube.com/watch/ccp-unique";
            Assert.NotNull(library.FindMatch(url, MediaTypes.Video));
            Assert.Same(shell, TopLevel.GetTopLevel(tab));
            web.OnNavigationCompleted(new Uri(url));
            Assert.Equal(url, web.CurrentUrl?.ToString());
            Assert.NotNull(tab.BrowserEnhanceHost.LoadedEnhancement);
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            Assert.True(tab.BrowserEnhanceHost.MatchesCurrentSource(url));
            Assert.Equal(string.Format(Loc.Get("browser_enhance_match_fmt"), "Home test"), status.Text);
            web.OnNavigationCompleted(new Uri("https://example.com/unrelated"));
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
            Assert.Equal(Loc.Get("browser_enhance_match_none"), status.Text);
            web.OnNavigationCompleted(new Uri(url));
            checkbox.IsChecked = false;
            Assert.False(CoreSettings.Current.BrowserEnhanceIfPossible);
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
            Assert.Equal(Loc.Get("browser_enhance_match_off"), status.Text);
            checkbox.IsChecked = true;
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            var original = File.ReadAllText(path);
            var unsupported = new Enhancement { MediaSource = url, Metadata = new EnhancementMetadata { Name = "Unsupported", Creator = "test" } };
            unsupported.Rules.Add(new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 1 }, Action = new TriggerEffectAction { EffectType = EffectTypes.Overlay, OverlayKind = OverlayKinds.PinkFilter } });
            File.WriteAllText(path, EnhancementSerializer.Save(unsupported));
            web.OnNavigationCompleted(new Uri(url));
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
            Assert.Equal(Loc.Get("browser_enhance_unsupported"), status.Text);
            File.WriteAllText(path, original);
            web.OnNavigationCompleted(new Uri(url));
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            shell.Close();
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
        }
        finally
        {
            shell.Close();
            if (path != null) File.Delete(path);
            CoreSettings.Current.BrowserEnhanceIfPossible = previous;
        }
    });
    [Fact]
    public void RuntimeDispatchesPause() => AvaloniaTestDispatcher.Run(() =>
    {
        var scripts = new List<string>();
        var web = new WebHost { ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); } };
        using var source = new WebHostVideoTimeSource(web);
        var dispatcher = new BrowserEnhancementDispatcher();
        dispatcher.DispatchAsync(new PauseAction(), new EnhancementDispatchContext(new Enhancement(), source, 0, null)).GetAwaiter().GetResult();
        Assert.Contains("v.pause()", Assert.Single(scripts));
    });

    [Fact]
    public void WebcamAdapterForwardsUntilDisposed() => AvaloniaTestDispatcher.Run(() =>
    {
        var camera = new BrowserEnhancementWebcam();
        int count = 0;
        camera.OnBlink += () => count++;
        WebcamTracker.Instance.RaiseBlinkForTest();
        camera.Dispose();
        WebcamTracker.Instance.RaiseBlinkForTest();
        Assert.Equal(1, count);
    });
}

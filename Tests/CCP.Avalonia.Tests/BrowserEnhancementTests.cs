using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserEnhancementTests
{
    [Fact]
    public void ToggleMatchesBackfillsAndUnbinds() => AvaloniaTestDispatcher.Run(() =>
    {
        const string url = "https://example.com/browser-enhance-test";
        var folder = Path.Combine(CorePaths.UserData, "enhancements");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "browser-enhance-test.ccpenh.json");
        Directory.CreateDirectory(CorePaths.EffectiveAssets);
        var clip = Path.Combine(CorePaths.EffectiveAssets, "browser-enhance-test-audio.tmp");
        File.WriteAllText(clip, "test");
        var audioProvider = CoreAudio.PlayOneShotProvider;
        var plays = 0;
        CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => plays++;
        var saved = CoreSettings.Current.BrowserEnhanceIfPossible;
        var dispatcher = EnhancementHostService.DispatcherFactory;
        var webcam = EnhancementHostService.WebcamProvider;
        EnhancementHostService.DispatcherFactory = null;
        EnhancementHostService.WebcamProvider = null;
        MainShellWindow? shell = null;
        try
        {
            var enh = new Enhancement { MediaType = MediaTypes.Video, MediaSource = url };
            enh.Metadata.Name = "Browser test";
            enh.TimelineItems.Add(new TimelineItem { Kind = TimelineItemKind.Rule, Trigger = new TimeReachedTrigger { Time = 1 }, Action = new PlayAudioAction { Path = Path.GetFileName(clip) }, CooldownMs = 0 });
            File.WriteAllText(path, EnhancementSerializer.Save(enh));
            CoreSettings.Current.BrowserEnhanceIfPossible = false;
            shell = new MainShellWindow(); shell.Show();
            var page = shell.Named<SettingsTabView>("SettingsTab")!;
            var web = page.FindControl<WebHost>("BrowserWebHost")!;
            string? clockId = null;
            web.ScriptInvoker = script => { var m = Regex.Match(script, "ccpVideoClock:'([0-9a-f]+)'"); if (m.Success) clockId = m.Groups[1].Value; return Task.FromResult<string?>(null); };
            var toggle = page.FindControl<CheckBox>("ToggleEnhanceIfPossible")!;
            var status = page.FindControl<TextBlock>("TxtEnhanceMatchStatus")!;
            Assert.True(toggle.IsEnabled);
            Assert.Equal(Loc.Get("browser_enhance_match_off"), status.Text);
            web.OnNavigationCompleted(new Uri(url));
            Assert.False(shell.BrowserEnhancement!.IsRunning);
            toggle.IsChecked = true;
            Assert.True(CoreSettings.Current.BrowserEnhanceIfPossible);
            Assert.True(shell.BrowserEnhancement.IsRunning);
            Assert.Equal(Loc.GetF("browser_enhance_match_fmt", "Browser test"), status.Text);
            Assert.NotNull(clockId);
            web.OnWebMessage($"{{\"ccpVideoClock\":\"{clockId}\",\"hasVideo\":true,\"t\":2,\"d\":40,\"playing\":true}}");
            Assert.Equal(1, plays);
            web.OnNavigationCompleted(new Uri("https://elsewhere.example/"));
            Assert.False(shell.BrowserEnhancement.IsRunning);
            Assert.Equal(Loc.Get("browser_enhance_match_none"), status.Text);
            web.OnNavigationCompleted(new Uri(url));
            Assert.True(shell.BrowserEnhancement.IsRunning);
            toggle.IsChecked = false;
            Assert.False(CoreSettings.Current.BrowserEnhanceIfPossible);
            Assert.False(shell.BrowserEnhancement.IsRunning);
            Assert.Equal(Loc.Get("browser_enhance_match_off"), status.Text);
            toggle.IsChecked = true;
            Assert.True(shell.BrowserEnhancement.IsRunning);
            var bridge = shell.BrowserEnhancement;
            shell.Close(); shell = null;
            Assert.False(bridge.IsRunning);
            web.OnNavigationCompleted(new Uri(url));
            Assert.False(bridge.IsRunning);
        }
        finally
        {
            shell?.Close();
            CoreSettings.Current.BrowserEnhanceIfPossible = saved;
            EnhancementHostService.DispatcherFactory = dispatcher;
            EnhancementHostService.WebcamProvider = webcam;
            CoreAudio.PlayOneShotProvider = audioProvider;
            File.Delete(clip);
            if (File.Exists(path)) File.Delete(path);
        }
    });

    [Fact]
    public void WebcamAdapterForwardsAndUnhooks() => AvaloniaTestDispatcher.Run(() =>
    {
        var adapter = new BrowserEnhancementWebcam();
        var count = 0;
        adapter.OnBlink += () => count++;
        WebcamTracker.Instance.RaiseBlinkForTest();
        Assert.Equal(1, count);
        adapter.Dispose();
        WebcamTracker.Instance.RaiseBlinkForTest();
        Assert.Equal(1, count);
    });

    [Fact]
    public Task DispatcherCallsRealAudioSeam() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var original = CoreAudio.PlayOneShotProvider;
        var path = Path.GetTempFileName();
        var count = 0;
        try
        {
            CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => count++;
            var clock = new WebHostVideoClock(new WebHost());
            var context = new EnhancementDispatchContext(new Enhancement(), clock, 0, null);
            await new BrowserEnhancementDispatcher().DispatchAsync(new PlayAudioAction { Path = path }, context);
            Assert.Equal(1, count);
        }
        finally { CoreAudio.PlayOneShotProvider = original; File.Delete(path); }
    });

    [Fact]
    public void MatchStringsResolveInEveryLanguage() => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = LocalizationManager.Instance;
        var old = loc.CurrentLanguage;
        try
        {
            foreach (var language in new[] { "en", "it", "de", "fr", "es", "pt-BR", "ru", "ja", "ko", "zh-CN" })
            {
                loc.SetLanguage(language);
                foreach (var key in new[] { "browser_enhance_match_off", "browser_enhance_match_none", "browser_enhance_match_fmt" })
                    Assert.NotEqual(key, Loc.Get(key));
                Assert.Contains("Browser test", Loc.GetF("browser_enhance_match_fmt", "Browser test"));
            }
        }
        finally { loc.SetLanguage(old); }
    });
}

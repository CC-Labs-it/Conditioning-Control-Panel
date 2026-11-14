using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF BackRoomHostService.cs:64-73,179-190,589-607 (#3921): the standalone page's own three levels.
    [Theory]
    [InlineData("breakout")]
    [InlineData("breakoutdemo")]
    public void Breakout_AudioTracksSettingsAndPageOptions(string id) => Run((_, launcher) =>
    {
        launcher.Hide();
        CoreEntitlement.HasLabProvider = () => true;
        var service = CoreSettings.Service!;
        var settings = service.Current;
        settings.BreakoutSubVolume = 12;
        settings.BreakoutSfxVolume = 34;
        settings.BreakoutMusicVolume = 56;
        settings.BackRoomMusicVolume = 7;
        settings.BackRoomTunnel = false;
        var window = GameWindow.Launch(id)!;
        var frames = new List<JObject>();
        window.Posted += json => frames.Add(JObject.Parse(json));
        window.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal(0.12, (double)frames[^1]["audio"]!["sub"]!);
        Assert.Equal(0.34, (double)frames[^1]["audio"]!["sfx"]!);
        Assert.Equal(0.56, (double)frames[^1]["audio"]!["music"]!);
        window.HandleMessage("{\"type\":\"room-option\",\"key\":\"musicVolume\",\"value\":23}");
        window.HandleMessage("{\"type\":\"room-option\",\"key\":\"sfxVolume\",\"value\":0}");
        window.HandleMessage("{\"type\":\"room-option\",\"key\":\"subVolume\",\"value\":100}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(23, settings.BreakoutMusicVolume);
        Assert.Equal(0, settings.BreakoutSfxVolume);
        Assert.Equal(100, settings.BreakoutSubVolume);
        Assert.Equal(7, settings.BackRoomMusicVolume);   // never the casino's level
        Assert.Equal(0.23, (double)frames[^1]["audio"]!["music"]!);
        Assert.Equal(0.0, (double)frames[^1]["audio"]!["sfx"]!);
        Assert.Equal(1.0, (double)frames[^1]["audio"]!["sub"]!);
        window.HandleMessage("{\"type\":\"room-option\",\"key\":\"musicVolume\",\"value\":101}");
        window.HandleMessage("{\"type\":\"room-option\",\"key\":\"musicVolume\",\"value\":1.5}");
        window.HandleMessage("{\"type\":\"room-option\",\"key\":\"tunnel\",\"value\":true}");
        Assert.Equal(23, settings.BreakoutMusicVolume);
        Assert.False(settings.BackRoomTunnel);
        // WPF BackRoomBridge.cs:175-181,240-243: options after exit begins are dropped.
        window.HandleMessage("{\"type\":\"exit\"}");
        window.HandleMessage("{\"type\":\"room-option\",\"key\":\"musicVolume\",\"value\":99}");
        Assert.Equal(23, settings.BreakoutMusicVolume);
        window.Close();
    });

    // WPF BackRoomHostService.cs:32-38,589-609 (#3921): the active language's br_ lexicon on init.
    [Fact]
    public void Breakout_InitIncludesLocalizedRoomLexicon() => Run((_, launcher) =>
    {
        launcher.Hide();
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("fr");
            var window = GameWindow.Launch("breakoutdemo")!;
            var init = JObject.FromObject(window.InitMessage());
            Assert.NotEmpty(loc.KeysWithPrefix("br_"));
            Assert.Equal(loc.Get("br_back"), (string?)init["lex"]!["br_back"]);
            Assert.Null(init["lex"]!["btn_yes"]);
            window.Close();
        }
        finally { loc.SetLanguage(previous); }
    });

    // WPF BackRoomBridge.cs:245-249 (#4066): the demo end card's funnel rides the one FunnelPageMessage path;
    // it counts after exit starts but never opens a checkout then.
    [Fact]
    public void Breakout_FunnelOpensOnlyCatalogCheckoutAndNotAfterExit() => Run((_, launcher) =>
    {
        launcher.Hide();
        FunnelPageMessage.ResetForTests();
        bool offline = CoreSettings.Current.OfflineMode;
        var opened = new List<string>();
        var sent = new List<string>();
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            CoreSettings.Current.OfflineMode = false;
            FunnelPageMessage.BaseUrlOverride = "http://127.0.0.1:9";
            FunnelPageMessage.SendOverride = (_, body) => { lock (sent) sent.Add(body); return System.Threading.Tasks.Task.CompletedTask; };
            FunnelPageMessage.OpenCheckout = url => { opened.Add(url); return true; };
            FunnelPageMessage.UtcNow = () => now;
            var window = GameWindow.Launch("breakoutdemo")!;
            const string cta = "{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"a\":\"breakout_demo\",\"b\":\"patreon\",\"plan\":\"prime\",\"url\":\"https://untrusted.invalid/\"}";
            window.HandleMessage(cta);
            Assert.Equal(new[] { PlanCatalog.Current.Prime.PatreonUrl }, opened);
            now = now.AddSeconds(5);
            window.HandleMessage("{\"type\":\"exit\"}");
            window.HandleMessage(cta);
            Assert.Single(opened);
            window.Close();
        }
        finally
        {
            CoreSettings.Current.OfflineMode = offline;
            FunnelPageMessage.ResetForTests();
        }
    });
}

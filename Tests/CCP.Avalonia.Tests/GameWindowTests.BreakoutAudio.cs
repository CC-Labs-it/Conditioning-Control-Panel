using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF BackRoomHostService.cs:64-73,179-190,589-607,620-632,689-720.
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
        window.Web.OnNavigationCompleted(window.PageUrl!);
        window.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Equal(0.12, (double)frames[^1]["audio"]!["sub"]!);
        Assert.Equal(0.34, (double)frames[^1]["audio"]!["sfx"]!);
        Assert.Equal(0.56, (double)frames[^1]["audio"]!["music"]!);
        window.Web.OnWebMessage("{\"type\":\"room-option\",\"key\":\"musicVolume\",\"value\":23}");
        window.Web.OnWebMessage("{\"type\":\"room-option\",\"key\":\"sfxVolume\",\"value\":0}");
        window.Web.OnWebMessage("{\"type\":\"room-option\",\"key\":\"subVolume\",\"value\":100}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(23, settings.BreakoutMusicVolume);
        Assert.Equal(0, settings.BreakoutSfxVolume);
        Assert.Equal(100, settings.BreakoutSubVolume);
        Assert.Equal(7, settings.BackRoomMusicVolume);
        Assert.True(service.HasPendingSave);
        Assert.Equal(0.23, (double)frames[^1]["audio"]!["music"]!);
        Assert.Equal(0.0, (double)frames[^1]["audio"]!["sfx"]!);
        Assert.Equal(1.0, (double)frames[^1]["audio"]!["sub"]!);
        window.Web.OnWebMessage("{\"type\":\"room-option\",\"key\":\"musicVolume\",\"value\":101}");
        window.Web.OnWebMessage("{\"type\":\"room-option\",\"key\":\"musicVolume\",\"value\":1.5}");
        window.Web.OnWebMessage("{\"type\":\"room-option\",\"key\":\"tunnel\",\"value\":true}");
        Assert.Equal(23, settings.BreakoutMusicVolume);
        Assert.False(settings.BackRoomTunnel);
        service.RestoreFrom(new AppSettings { BreakoutMusicVolume = 42 });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.42, (double)frames[^1]["audio"]!["music"]!);
        frames.Clear();
        settings.BreakoutMusicVolume = 2; // the replaced model is detached
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(frames);
        service.Current.BreakoutMusicVolume = 8;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.08, (double)frames[^1]["audio"]!["music"]!);
        // WPF BackRoomBridge.cs:175-181,240-243 rejects options after exit begins.
        window.Web.OnWebMessage("{\"type\":\"exit\"}");
        window.Web.OnWebMessage("{\"type\":\"room-option\",\"key\":\"musicVolume\",\"value\":99}");
        Assert.Equal(8, service.Current.BreakoutMusicVolume);
        window.Close();
        frames.Clear();
        service.Current.BreakoutMusicVolume = 9;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(frames);
    });

    // WPF BackRoomHostService.cs:32-38,589-609: the active language's br_ lexicon on init.
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
            Assert.Equal(loc.KeysWithPrefix("br_").Count, ((JObject)init["lex"]!).Count);
            Assert.Null(init["lex"]!["btn_yes"]);
        }
        finally { loc.SetLanguage(previous); }
    });
}

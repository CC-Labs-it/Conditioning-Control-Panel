using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF b9368d963: a panic pauses the Home browser's page video and the Deeper player (playhead kept),
/// through the registered surfaces, so deleting a registry line fails here.</summary>
public sealed class PanicMediaReachTests
{
    private static void Boot()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static PanicSurfaces.Surface Surface(string id) => PanicSurfaces.All.Single(x => x.Id == id);

    [Fact]
    public void PanicPausesTheDeeperPlayerWithoutRewinding() => AvaloniaTestDispatcher.Run(() =>
    {
        Boot();
        var player = new EnhancementPlayerWindow(null, null);
        player.Show();
        try
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            T Get<T>(string n) => (T)typeof(EnhancementPlayerWindow).GetField(n, f)!.GetValue(player)!;
            void Set(string n, object v) => typeof(EnhancementPlayerWindow).GetField(n, f)!.SetValue(player, v);
            var sent = new List<string>();
            Get<WebHost>("_videoBrowser").ScriptInvoker = s => { sent.Add(s); return Task.FromResult<string?>(null); };
            Set("_isVideoMode", true); Set("_videoNavigated", true); Set("_isPlaying", true); Set("_currentSec", 12.0);

            Surface("deeper-player").Stop(null);

            Assert.False(Get<bool>("_isPlaying"));
            Assert.Equal(12.0, Get<double>("_currentSec"));
            Assert.Equal(WebHostMedia.PanicPauseScript, Assert.Single(sent));
            Assert.Equal(Loc.Get("deeper_player_status_stopped"), Get<TextBlock>("_txtStatus").Text);
        }
        finally { player.Close(); }
    });

    [Fact]
    public void PanicPausesTheHomeBrowserMediaWithoutLatchingTheWorkshopPause() => AvaloniaTestDispatcher.Run(() =>
    {
        Boot();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var sent = new List<string>();
            host.ScriptInvoker = s => { sent.Add(s); return Task.FromResult<string?>(null); };

            Surface("browser-media").Stop(shell);

            Assert.Equal(WebHostMedia.PanicPauseScript, Assert.Single(sent));
            Assert.Contains("e.pause()", WebHostMedia.PanicPauseScript);
            Assert.Contains("exitFullscreen", WebHostMedia.PanicPauseScript);
            Assert.DoesNotContain("currentTime", WebHostMedia.PanicPauseScript);   // paused, never rewound
            Assert.False(host.MediaPaused);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}

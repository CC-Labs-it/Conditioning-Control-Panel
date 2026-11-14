using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace CCP.Avalonia.Tests;

public class JackpotRemixVideoTests
{
    [Fact]
    public void BrowserReportsGatePlaybackAndIgnoreStaleNavigation() => AvaloniaTestDispatcher.Run(() =>
    {
        var previousVideo = CoreEngine.Video;
        CoreEngine.Video = null;
        var page = new SettingsTabView();
        var web = page.FindControl<WebHost>("BrowserWebHost")!;
        string? id = null;
        web.ScriptInvoker = script =>
        {
            if (script.Contains("ccpRemixMedia")) id = Regex.Match(script, "const id = \\\"([^\\\"]+)\\\"").Groups[1].Value;
            return Task.FromResult<string?>("true");
        };
        var window = new Window { Content = page };
        try
        {
            window.Show();
            web.OnNavigationCompleted(new Uri("https://example.com/"));
            Assert.False(string.IsNullOrEmpty(id));
            Assert.True(FlashOverlay.RemixVideoBusy()); // wait until the page reports
            string Message(string token, bool active) => JsonSerializer.Serialize(new { type = "ccpRemixMedia", id = token, active });
            web.OnWebMessage(Message(id!, false));
            Assert.False(FlashOverlay.RemixVideoBusy());
            SettingsTabView.RemixMediaTick = System.Diagnostics.Stopwatch.GetTimestamp() - 5 * System.Diagnostics.Stopwatch.Frequency;
            Assert.True(FlashOverlay.RemixVideoBusy()); // a dead reporter is not evidence of silence
            web.OnWebMessage(Message(id!, false));
            Assert.False(FlashOverlay.RemixVideoBusy());
            web.OnWebMessage(JsonSerializer.Serialize(Message(id!, true))); // WebView2 can JSON-quote a string
            Assert.True(FlashOverlay.RemixVideoBusy());
            var oldId = id!;
            web.OnNavigationCompleted(new Uri("https://example.com/next"));
            Assert.NotEqual(oldId, id);
            web.OnWebMessage(Message(oldId, false));
            Assert.True(FlashOverlay.RemixVideoBusy());
            web.OnWebMessage(Message(id!, false));
            Assert.False(FlashOverlay.RemixVideoBusy());
            bool? audio = null;
            web.AudioProbe = () => audio;
            void Frame(bool? engine) { audio = engine; web.OnWebMessage(JsonSerializer.Serialize(new { type = "ccpRemixMedia", id, active = false, unknown = true })); }
            Frame(true); Assert.True(FlashOverlay.RemixVideoBusy()); // an unreadable embed that is playing holds the gate
            Frame(false); Assert.True(FlashOverlay.RemixVideoBusy()); // held two seconds after the engine goes quiet
            page.RemixAudioTick -= 3 * System.Diagnostics.Stopwatch.Frequency;
            Frame(false); Assert.False(FlashOverlay.RemixVideoBusy()); // an idle unreadable embed releases it
            Frame(null); Assert.True(FlashOverlay.RemixVideoBusy()); // no engine answer is not evidence of silence
            web.OnNavigationCompleted(new Uri("https://example.com/failure"), success: false);
            web.OnWebMessage(Message(id!, false));
            Assert.True(FlashOverlay.RemixVideoBusy()); // failed injection must not claim silence
        }
        finally
        {
            window.Close();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            CoreEngine.Video = previousVideo;
        }
    });

    [Fact]
    public void IndependentBrowserAndMirroredGameSessionsBlockBuilds() => AvaloniaTestDispatcher.Run(() =>
    {
        var previousVideo = CoreEngine.Video;
        var previousSettings = CoreSettings.ServiceProvider;
        var previousGate = BrowserVideoGate.EngineAccepts;
        var previousBusy = SettingsTabView.RemixBrowserMediaBusy;
        var settings = new SettingsService();
        var owner = new object();
        try
        {
            CoreEngine.Video = null; SettingsTabView.RemixBrowserMediaBusy = false;
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.BrowserVideoEngineEnabled = true;
            settings.Current.AudioOutputDeviceId = "";
            BrowserVideoGate.EngineAccepts = (_, _) => true;
            Assert.False(FlashOverlay.RemixVideoBusy());
            Assert.True(BrowserVideoSurface.TryAcquire(owner, "sample.mp4"));
            Assert.True(FlashOverlay.RemixVideoBusy());
            BrowserVideoSurface.Release(owner);
            Assert.False(FlashOverlay.RemixVideoBusy());
            _ = new BubbleCountWindow("sample.mp4", BubbleCountScheduler.Difficulty.Easy, false, _ => { }, isPrimary: false);
            Assert.True(FlashOverlay.RemixVideoBusy());
            BubbleCountWindow.ForceCloseAll();
            Assert.False(FlashOverlay.RemixVideoBusy());
        }
        finally
        {
            BubbleCountWindow.ForceCloseAll(); BrowserVideoSurface.Release(owner);
            settings.SealForReset();
            (CoreEngine.Video, CoreSettings.ServiceProvider, BrowserVideoGate.EngineAccepts, SettingsTabView.RemixBrowserMediaBusy) =
                (previousVideo, previousSettings, previousGate, previousBusy);
        }
    });
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class KeywordHighlightOverlayTests
{
    [Fact]
    public Task ProductionSourceFeedsScannerAndHighlight() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var saved = (settings.KeywordTriggers, settings.KeywordTriggersEnabled, settings.ScreenOcrEnabled,
            settings.OcrConfirmationScans, settings.OcrHighlightAll, settings.ScreenOcrIntervalMs,
            settings.AwarenessIgnoreOwnUi, settings.AwarenessLoopProtectionEnabled, settings.KeywordTriggerIgnoreOwnFocus,
            settings.KeywordTriggerAppScope, settings.KeywordGlobalCooldownSeconds, settings.KeywordHighlightEnabled,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        var source = KeywordTriggerHead.OcrSource;
        var execute = WindowsOcrSource.ExecuteForTests;
        var screens = KeywordHighlightOverlay.Screens;
        var configure = KeywordHighlightOverlay.Configure;
        var credit = KeywordTriggerHead.Engine.QuestCredit;
        // The shared engine's pulse ring is process-wide: put back exactly what earlier tests left.
        var ring = (System.Collections.Generic.LinkedList<TriggerFireRecord>)typeof(KeywordFireLog)
            .GetField("_fires", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(KeywordTriggerHead.Engine.Fires)!;
        var fires = ring.ToList();
        var calls = 0;
        try
        {
            KeywordTriggerHead.Stop(); KeywordTriggerHead.Engine.ResetCooldownsForTest();
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            settings.KeywordTriggers = [new KeywordTrigger { Keyword = "sit", CooldownSeconds = 0, Actions = [new HighlightAction { Enabled = true }] }];
            settings.KeywordTriggersEnabled = true; settings.ScreenOcrEnabled = true; settings.OcrConfirmationScans = 1;
            settings.OcrHighlightAll = true; settings.ScreenOcrIntervalMs = 60000; settings.AwarenessIgnoreOwnUi = false;
            settings.AwarenessLoopProtectionEnabled = false; settings.KeywordTriggerIgnoreOwnFocus = false;
            settings.KeywordTriggerAppScope = AwarenessAppScope.Everywhere; settings.KeywordGlobalCooldownSeconds = 0;
            settings.KeywordHighlightEnabled = true;
            CoreEntitlement.HasPremiumProvider = () => true; CoreEntitlement.IsFreeTodayProvider = _ => false;
            KeywordTriggerHead.Engine.QuestCredit = () => { };
            KeywordTriggerHead.OcrSource = null;
            WindowsOcrSource.ExecuteForTests = _ => { calls++; return Task.FromResult("[{\"Text\":\"sit\",\"X\":130,\"Y\":60,\"Width\":30,\"Height\":15}]"); };
            KeywordHighlightOverlay.Screens = () => [new(new PixelRect(0, 0, 1000, 800), 1)];
            KeywordHighlightOverlay.Configure = (_, _) => true;
            KeywordTriggerHead.Sync();
            await KeywordTriggerHead.TickOcrForTest();
            Assert.Equal(1, calls);
            var box = Assert.Single(Assert.Single(KeywordHighlightOverlay.Canvases).Children);
            Assert.Equal(120, Canvas.GetLeft(box)); Assert.Equal(50, Canvas.GetTop(box));
        }
        finally
        {
            KeywordTriggerHead.Stop(); KeywordTriggerHead.Engine.ResetCooldownsForTest(); KeywordHighlightOverlay.CloseAll();
            KeywordTriggerHead.OcrSource = source; WindowsOcrSource.ExecuteForTests = execute;
            KeywordHighlightOverlay.Screens = screens; KeywordHighlightOverlay.Configure = configure;
            KeywordTriggerHead.Engine.QuestCredit = credit;
            ring.Clear(); foreach (var fire in fires) ring.AddLast(fire);
            (settings.KeywordTriggers, settings.KeywordTriggersEnabled, settings.ScreenOcrEnabled,
                settings.OcrConfirmationScans, settings.OcrHighlightAll, settings.ScreenOcrIntervalMs,
                settings.AwarenessIgnoreOwnUi, settings.AwarenessLoopProtectionEnabled, settings.KeywordTriggerIgnoreOwnFocus,
                settings.KeywordTriggerAppScope, settings.KeywordGlobalCooldownSeconds, settings.KeywordHighlightEnabled,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = saved;
        }
        Assert.Equal(fires, KeywordTriggerHead.Engine.GetRecentFires());
    });

    [Fact] // WPF KeywordTriggerService.cs:1117-1146: a regex match unions only the words on its first word's screen.
    public async Task HelperKeepsMonitorIdentityForRegexUnions()
    {
        var execute = WindowsOcrSource.ExecuteForTests;
        try
        {
            WindowsOcrSource.ExecuteForTests = _ => Task.FromResult("[{\"Text\":\"good\",\"X\":900,\"Y\":10,\"Width\":40,\"Height\":20,\"Screen\":\"DISPLAY1\"}," +
                "{\"Text\":\"boy\",\"X\":1010,\"Y\":300,\"Width\":30,\"Height\":20,\"Screen\":\"DISPLAY2\"}]");
            var words = await WindowsOcrSource.Scan(default);
            Assert.Equal(new[] { "DISPLAY1", "DISPLAY2" }, words.Select(w => w.Screen));
            var hit = Assert.Single(KeywordAudioRuntime.FindOcrMatches(new KeywordTrigger { Keyword = "good\\s+boy", MatchType = KeywordMatchType.Regex }, words));
            Assert.Equal((new System.Drawing.Rectangle(900, 10, 40, 20), "DISPLAY1"), (hit.Bounds, hit.Screen));
        }
        finally { WindowsOcrSource.ExecuteForTests = execute; }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    [Fact]
    public Task CaptureVisibilityChangesExistingWindowsImmediately() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var screens = KeywordHighlightOverlay.Screens;
        var configure = KeywordHighlightOverlay.Configure;
        var exclude = KeywordHighlightOverlay.ExcludeFromCapture;
        var settings = CoreSettings.Current;
        var enabled = settings.KeywordHighlightEnabled;
        var visible = settings.OcrHighlightVisibleInCapture;
        var acquired = 0; var released = 0;
        try
        {
            KeywordHighlightOverlay.Screens = () => [new(new PixelRect(0, 0, 500, 500), 1), new(new PixelRect(500, 0, 500, 500), 1)];
            KeywordHighlightOverlay.Configure = (_, _) => true;
            KeywordHighlightOverlay.ExcludeFromCapture = _ => { acquired++; return new Lease(() => released++); };
            settings.KeywordHighlightEnabled = true; settings.OcrHighlightVisibleInCapture = true;
            KeywordHighlightOverlay.Show([new("a", new(10, 10, 10, 10)), new("b", new(510, 10, 10, 10))]);
            Assert.Equal(2, KeywordHighlightOverlay.WindowCount);
            Assert.Equal(0, acquired);
            settings.OcrHighlightVisibleInCapture = false;
            Assert.Equal(2, acquired); Assert.Equal(0, released);
            settings.OcrHighlightVisibleInCapture = true;
            Assert.Equal(2, released);
            settings.OcrHighlightVisibleInCapture = false;
            Assert.Equal(4, acquired);
            Assert.Equal(2, KeywordHighlightOverlay.WindowCount);
            KeywordHighlightOverlay.CloseAll();
            Assert.Equal(4, released);
            settings.OcrHighlightVisibleInCapture = true;
            Assert.Equal(4, released);
        }
        finally
        {
            KeywordHighlightOverlay.CloseAll(); KeywordHighlightOverlay.Screens = screens;
            KeywordHighlightOverlay.Configure = configure; KeywordHighlightOverlay.ExcludeFromCapture = exclude;
            settings.KeywordHighlightEnabled = enabled; settings.OcrHighlightVisibleInCapture = visible;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BoxesUseEachScreenScaleAndPanicClosesThem() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var screens = KeywordHighlightOverlay.Screens;
        var configure = KeywordHighlightOverlay.Configure;
        var enabled = CoreSettings.Current.KeywordHighlightEnabled;
        try
        {
            KeywordHighlightOverlay.Screens = () => [new(new PixelRect(0, 0, 1000, 800), 1), new(new PixelRect(1000, 0, 1500, 1200), 1.5)];
            KeywordHighlightOverlay.Configure = (_, _) => true;
            CoreSettings.Current.KeywordHighlightEnabled = true;
            var words = new[] { new ScreenOcrService.Word("a", new(100, 60, 30, 15)), new ScreenOcrService.Word("b", new(1150, 90, 45, 30)) };
            KeywordHighlightOverlay.Show(words);
            Assert.Equal(2, KeywordHighlightOverlay.WindowCount);
            var boxes = KeywordHighlightOverlay.Canvases.Select(c => Assert.Single(c.Children)).ToArray();
            Assert.Equal(90, Canvas.GetLeft(boxes[0])); Assert.Equal(50, Canvas.GetTop(boxes[0]));
            Assert.Equal(90, Canvas.GetLeft(boxes[1])); Assert.Equal(50, Canvas.GetTop(boxes[1]));
            Assert.Equal(50, boxes[0].Width); Assert.Equal(50, boxes[1].Width);
            PanicSurfaces.All.Single(x => x.Id == "keyword-highlights").Stop(null);
            Assert.Equal(0, KeywordHighlightOverlay.WindowCount);
            CoreSettings.Current.KeywordHighlightEnabled = false;
            KeywordHighlightOverlay.Show(words);
            Assert.Equal(0, KeywordHighlightOverlay.WindowCount);
            KeywordHighlightOverlay.Show(null);
        }
        finally
        {
            KeywordHighlightOverlay.CloseAll(); KeywordHighlightOverlay.Screens = screens;
            KeywordHighlightOverlay.Configure = configure; CoreSettings.Current.KeywordHighlightEnabled = enabled;
        }
        return Task.CompletedTask;
    });
}

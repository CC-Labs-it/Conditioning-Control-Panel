using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class OwnTextOcrTests
{
    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        await body();
    });

    private static async Task AssertExcluded(System.Drawing.Rectangle textRect)
    {
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggers, s.KeywordTriggersEnabled, s.ScreenOcrEnabled, s.OcrConfirmationScans,
            s.OcrHighlightAll, s.AwarenessIgnoreOwnUi, s.ScreenOcrIntervalMs, s.AwarenessLoopProtectionEnabled,
            s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope, s.KeywordGlobalCooldownSeconds,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        var word = new ScreenOcrService.Word("sit", new System.Drawing.Rectangle(textRect.X + textRect.Width / 2, textRect.Y + textRect.Height / 2, 1, 1));
        var other = new ScreenOcrService.Word("sit", new System.Drawing.Rectangle(textRect.Right + 5000, textRect.Bottom + 5000, 1, 1));
        var words = new[] { word };
        var fired = 0;
        var questCredit = KeywordTriggerHead.Engine.QuestCredit;
        void OnFire(KeywordTrigger _) => fired++;
        try
        {
            s.KeywordTriggers = [new KeywordTrigger { Keyword = "sit", CooldownSeconds = 0, Actions = [new HighlightAction()] }];
            s.KeywordTriggersEnabled = true; s.ScreenOcrEnabled = true; s.OcrConfirmationScans = 1;
            s.OcrHighlightAll = true; s.AwarenessIgnoreOwnUi = true; s.ScreenOcrIntervalMs = 60000;
            s.AwarenessLoopProtectionEnabled = false; s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere; s.KeywordGlobalCooldownSeconds = 0;
            CoreEntitlement.HasPremiumProvider = () => true; CoreEntitlement.IsFreeTodayProvider = _ => false;
            KeywordTriggerHead.Engine.ResetCooldownsForTest();
            KeywordTriggerHead.Engine.QuestCredit = () => { };
            KeywordTriggerHead.Engine.TriggerFired += OnFire;
            KeywordTriggerHead.OcrSource = _ => Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>(words);
            KeywordTriggerHead.Sync();
            await KeywordTriggerHead.TickOcrForTest();
            Assert.Equal(0, fired);
            words = [other];
            await KeywordTriggerHead.TickOcrForTest();
            Assert.Equal(1, fired);
        }
        finally
        {
            KeywordTriggerHead.Stop(); KeywordTriggerHead.OcrSource = null;
            KeywordTriggerHead.Engine.TriggerFired -= OnFire;
            KeywordTriggerHead.Engine.QuestCredit = questCredit;
            KeywordTriggerHead.Engine.ResetCooldownsForTest();
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.ScreenOcrEnabled, s.OcrConfirmationScans,
                s.OcrHighlightAll, s.AwarenessIgnoreOwnUi, s.ScreenOcrIntervalMs, s.AwarenessLoopProtectionEnabled,
                s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope, s.KeywordGlobalCooldownSeconds,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = saved;
        }
    }

    [Fact]
    public Task BouncingTextIsExcludedWhileRunning() => Run(async () =>
    {
        var running = typeof(BouncingTextOverlay).GetField("_running", BindingFlags.Static | BindingFlags.NonPublic)!;
        var scale = typeof(BouncingTextOverlay).GetField("_screenScale", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            BouncingTextOverlay.Engine.SetBounds(0, 0, 1000, 600);
            BouncingTextOverlay.Engine.Start(new AppSettings(), ["sit"]);
            var logo = BouncingTextOverlay.Engine.Logos[0];
            logo.PosX = 200; logo.PosY = 100; logo.TextWidth = 100; logo.TextHeight = 50;
            scale.SetValue(null, 1.5); running.SetValue(null, true);
            var rect = Assert.Single(BouncingTextOverlay.ActiveTextRects());
            Assert.True(rect.Contains(300, 150));
            await AssertExcluded(rect);
            running.SetValue(null, false);
            Assert.Empty(BouncingTextOverlay.ActiveTextRects());
        }
        finally { running.SetValue(null, false); scale.SetValue(null, 1.0); BouncingTextOverlay.Engine.Stop(); }
    });

    [Fact]
    public Task SubliminalTextIsExcludedOnlyWhileVisible() => Run(async () =>
    {
        var card = new SubliminalOverlayWindow("sit") { Width = 1000, Height = 600 };
        try
        {
            SubliminalOverlay.Present(card);
            Assert.Empty(SubliminalOverlay.ActiveTextRects());
            card.IsFlashing = true;
            card.Measure(new Size(1000, 600));
            card.Arrange(new Rect(0, 0, 1000, 600));
            Dispatcher.UIThread.RunJobs();
            var rect = Assert.Single(SubliminalOverlay.ActiveTextRects());
            Assert.True(rect.Width > 0 && rect.Height > 0);
            await AssertExcluded(rect);
            card.Close();
            Assert.Empty(SubliminalOverlay.ActiveTextRects());
        }
        finally { SubliminalOverlay.CloseAll(); }
    });

    [Fact] // restack b221: a solid-mode card on the shared stage is the same text (WPF SubliminalService.cs:1088)
    public Task HostedSubliminalTextIsExcludedOnlyWhileVisible() => Run(async () =>
    {
        var configure = SharedOverlayHost.Configure;
        global::Avalonia.Controls.Window? stage = null;
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 1000; w.Height = 600; return true; };
        var scheduled = new List<(TimeSpan At, Action Fire)>();
        try
        {
            Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 1000, 600), "sit", 0.8, TimeSpan.FromMilliseconds(500),
                (fire, at) => scheduled.Add((at, fire))));
            stage!.Measure(new Size(1000, 600));
            stage.Arrange(new Rect(0, 0, 1000, 600));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(SubliminalOverlay.ActiveTextRects());   // still at opacity 0
            foreach (var item in scheduled.Where(i => i.At <= SubliminalOverlayWindow.FadeDuration).OrderBy(i => i.At).ToArray())
                item.Fire();
            var rect = Assert.Single(SubliminalOverlay.ActiveTextRects());
            Assert.True(rect.Width > 0 && rect.Height > 0);
            await AssertExcluded(rect);
            SubliminalOverlay.CloseAll();
            Assert.Empty(SubliminalOverlay.ActiveTextRects());
        }
        finally { SubliminalOverlay.CloseAll(); SharedOverlayHost.Configure = configure; }
    });
}

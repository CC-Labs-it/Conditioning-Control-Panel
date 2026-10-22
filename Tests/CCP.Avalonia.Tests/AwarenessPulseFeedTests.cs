using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Awareness.cs RefreshAwarenessPulseFeed / BuildPulseRow / action chips.</summary>
public sealed class AwarenessPulseFeedTests
{
    [Fact]
    public void TourDemoAndTypedFiresReachTheOpenTabsLastDetectedFeed() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var engine = KeywordTriggerHead.Engine;
        var saved = (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerAppScope, s.KeywordTriggerIgnoreOwnFocus,
            s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, engine.Fires, engine.Now, engine.Cooldowns);
        Window? host = null;
        try
        {
            var now = new DateTime(2026, 10, 22, 12, 0, 0);
            engine.Fires = new KeywordFireLog();
            engine.Cooldowns = new KeywordCooldowns();   // the engine is static: keep other tests' cooldowns out, and ours in here
            engine.Now = () => now;
            s.KeywordTriggers = new List<KeywordTrigger> { new() { Id = "t1", Keyword = "sit", CooldownSeconds = 0,
                Actions = new List<KeywordAction> { new HapticAction { Enabled = true }, new AddXpAction { Enabled = true } } } };
            s.KeywordTriggersEnabled = false;   // the engine is started by hand below, the Windows hook never
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere; s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordGlobalCooldownSeconds = 0; s.KeywordPerKeywordCooldownSeconds = 0;
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => false;

            var view = new AwarenessTabView();
            host = new Window { Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var feed = view.FindControl<StackPanel>("AwarenessPulseFeed")!;
            var count = view.FindControl<TextBlock>("TxtAwarenessFireCount")!;
            Assert.Same(view.FindControl<TextBlock>("TxtAwarenessPulseEmpty"), Assert.Single(feed.Children));

            // WPF #887: no entitlement, the tour's demo records nothing.
            TutorialHeadHooks.FireKeywordDemo("good boy", "Tutorial");
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<TextBlock>(Assert.Single(feed.Children));
            Assert.Equal("", count.Text);

            CoreEntitlement.HasPremiumProvider = () => true;
            TutorialHeadHooks.FireKeywordDemo("good boy", "Tutorial");
            Dispatcher.UIThread.RunJobs();
            var row = Assert.IsType<Border>(Assert.Single(feed.Children));
            var texts = Texts(row);
            Assert.Equal(new[] { "\"good boy\"", "TUTORIAL", "just now" }, texts);
            Assert.Equal(new[] { "Eye", "Chat" }, Glyphs(row));
            Assert.Equal("1 fire today", count.Text);

            // A typed match is recorded as Keyboard, newest first; AddXp draws no chip.
            s.KeywordTriggersEnabled = true;
            engine.Start();
            now = now.AddMinutes(2);
            foreach (var ch in "sit") engine.Character(ch);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, feed.Children.Count);
            var newest = (Border)feed.Children[0];
            Assert.Contains("KEYBOARD", Texts(newest));
            Assert.Equal(new[] { "PhoneVibrate" }, Glyphs(newest));
            Assert.Contains("2m ago", Texts((Border)feed.Children[1]));
            Assert.Equal("2 fires today", count.Text);

            // Only five rows, the count covers every kept fire.
            for (var i = 0; i < 6; i++) engine.FireDemo("w" + i, "Tutorial");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, feed.Children.Count);
            Assert.Equal("8 fires today", count.Text);

            // Closed tab: unsubscribed, nothing rebuilt.
            host.Close(); host = null;
            engine.FireDemo("late", "Tutorial");
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("\"late\"", Texts(feed));
        }
        finally
        {
            host?.Close();
            KeywordTriggerHead.Stop();
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerAppScope, s.KeywordTriggerIgnoreOwnFocus,
                s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, engine.Fires, engine.Now, engine.Cooldowns) = saved;
        }
    });

    [Fact]
    public void FireLogKeepsTwentyNewestFirstWithWpfActionKeys()
    {
        var log = new KeywordFireLog();
        var t = new KeywordTrigger { Id = "x", Keyword = "k", Actions = new List<KeywordAction> {
            new PlayAudioAction { Enabled = true }, new HighlightAction { Enabled = false }, new AddXpAction { Enabled = true },
            new ExtendSessionAction { Enabled = true, Minutes = 5 }, new ChasterAddTimeAction { Enabled = true, Minutes = 3 },
            new VisualEffectAction { Enabled = true, Effect = KeywordVisualEffect.Bubbles } } };
        for (var i = 0; i < 25; i++) log.Record(new KeywordTrigger { Keyword = "w" + i }, "OCR", DateTime.MinValue);
        log.Record(t, "Text", DateTime.MinValue);
        var fires = log.Recent();
        Assert.Equal(KeywordFireLog.Capacity, fires.Count);
        Assert.Equal("k", fires[0].Keyword);
        Assert.Equal("w24", fires[1].Keyword);
        Assert.Equal(new[] { "PlayAudio", "ExtendSession:5", "ChasterAddTime:3", "VisualEffect:Bubbles" }, fires[0].ActionKeys);
        Assert.Null(AwarenessTabView.ActionChip("AddXp:5"));
        Assert.Equal("Adds 3 minutes to the Chaster lock", AwarenessTabView.ActionChip("ChasterAddTime:3")!.Value.Tooltip);
        Assert.Equal("Fires visual effect: Spiral", AwarenessTabView.ActionChip("VisualEffect:Spiral")!.Value.Tooltip);
        Assert.Equal("14:05", AwarenessTabView.FormatTimeAgo(new DateTime(2026, 1, 1, 14, 5, 0), new DateTime(2026, 1, 2, 15, 0, 0)));
        Assert.Equal("59s ago", AwarenessTabView.FormatTimeAgo(DateTime.MinValue, DateTime.MinValue.AddSeconds(59)));
    }

    // Row text outside the icons (a glyph may draw through its own TextBlock).
    private static List<string?> Texts(Control root) => root.GetLogicalDescendants().OfType<TextBlock>()
        .Where(t => t.FindLogicalAncestorOfType<IconGlyph>() == null).Select(t => t.Text).ToList();

    private static string[] Glyphs(Control row) =>
        row.GetLogicalDescendants().OfType<IconGlyph>().Select(g => g.Kind.ToString()).ToArray();
}

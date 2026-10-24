using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Enhancements.cs SkillCard_Click (confirm, purchase, CelebrateEnhancementPurchase + the
/// prestige burst, repaint) driven by a real pointer click and Enter; and the reused lucky toast (OnLuckyProc).</summary>
public sealed class EnhancementsPurchaseTests
{
    private static readonly System.Reflection.PropertyInfo AchievementsProp = typeof(AvApp).GetProperty("Achievements",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

    private static void Setup()
    {
        if (global::Avalonia.Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static Dictionary<string, Control> Nodes(EnhancementsTabView tab) =>
        tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>().Where(c => c.Tag is string).ToDictionary(c => (string)c.Tag!);

    [Fact]
    public Task ClickConfirmsBuysBurstsOnNodeAndPrestigeThenRepaints() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var (confirm, tell, purchase, achWas) = (EnhancementsTabView.Confirm, EnhancementsTabView.Tell, EnhancementsTabView.Purchase, AchievementsProp.GetValue(null));
        var dir = Directory.CreateTempSubdirectory("ccp-enh-buy-").FullName;
        Window? w = null;
        try
        {
            var s = CoreSettings.Current;
            (s.MotionLevel, s.PerformanceMode, s.PinkRushActive, s.SkillPoints) = (MotionLevel.Full, false, false, 100);
            s.UnlockedSkills = new List<string>();
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            engine.Progress.LifetimeSkillPointsSpent = 98;          // pink_hours (2) crosses into Prestige rank 2
            AchievementsProp.SetValue(null, engine);
            var asked = new List<(string Title, string Message)>();
            var told = new List<string>();
            var bought = new List<string>();
            var answer = (true, (string?)null);
            EnhancementsTabView.Confirm = (_, t, m) => { asked.Add((t, m)); return Task.FromResult(true); };
            EnhancementsTabView.Tell = (_, t, m) => { told.Add($"{t}|{m}"); return Task.CompletedTask; };
            EnhancementsTabView.Purchase = id =>
            {
                bought.Add(id);
                if (!answer.Item1) return Task.FromResult(answer);
                var cost = SkillDefinition.All.Single(x => x.Id == id).Cost;
                s.SkillPoints -= cost;
                s.UnlockedSkills = s.UnlockedSkills.Append(id).ToList();
                engine.TrackSkillPointsSpent(cost);
                return Task.FromResult(answer);
            };

            var clock = new SteppedClock();
            EnhancementsTabView.Time = clock;
            var tab = new EnhancementsTabView();
            w = new Window { Width = 1400, Height = 700, Content = tab };
            w.Show();
            w.Activate();
            Dispatcher.UIThread.RunJobs();

            // A locked node takes no click (WPF wires SkillCard_Click only when canPurchase).
            var locked = Nodes(tab)["lucky_bimbo"];
            Assert.Null(locked.Cursor);
            var node = Nodes(tab)["pink_hours"];
            Assert.NotNull(node.Cursor);                              // hand cursor on a purchasable node
            var p = node.TranslatePoint(new Point(40, 40), w)!.Value;
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            await tab.LastPurchase!;
            Dispatcher.UIThread.RunJobs();

            var (title, message) = Assert.Single(asked);
            Assert.Equal(Loc.Get("dialog_purchase_enhancement"), title);
            Assert.EndsWith(IconText.BareLoc("msg_skill_permanent_note"), message);
            Assert.Equal(new[] { "pink_hours" }, bought);
            Assert.Equal((1, 1), (tab.PurchaseBursts, tab.PrestigeBursts));
            Assert.True(tab.SheenRunning);                             // the prestige sheen pass, one clock
            clock.Now += TimeSpan.FromSeconds(1.2).Ticks;              // past WPF's 1.15 s
            tab.StepSheen();
            Assert.False(tab.SheenRunning);
            Assert.NotSame(node, Nodes(tab)["pink_hours"]);          // repainted: now owned, green glow
            Assert.Contains(((Panel)Nodes(tab)["pink_hours"]).Children.OfType<Border>(), b => b.BoxShadow.Count > 0 && b.BoxShadow[0].Color == Colors.LimeGreen);
            Assert.Empty(told);

            // Enter on a focused purchasable node; a refusal shows WPF's failure dialog, no burst.
            answer = (false, "Not enough points");
            var next = Nodes(tab)["sparkle_boost_1"];
            next.Focus();
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await tab.LastPurchase!;
            Assert.Equal(new[] { "pink_hours", "sparkle_boost_1" }, bought);
            Assert.Equal(new[] { $"{Loc.Get("dialog_purchase_failed")}|Not enough points" }, told);
            Assert.Equal((1, 1), (tab.PurchaseBursts, tab.PrestigeBursts));
        }
        finally
        {
            w?.Close();
            (EnhancementsTabView.Confirm, EnhancementsTabView.Tell, EnhancementsTabView.Purchase) = (confirm, tell, purchase);
            AchievementsProp.SetValue(null, achWas);
            EnhancementsTabView.Time = TimeProvider.System;
            CoreSettings.ServiceProvider = oldSettings;
            Directory.Delete(dir, recursive: true);
        }
    });

    [Fact]
    public Task LuckyToastIsOneReusedWindowThatHoldsThenHidesAndSkipsUnderGamesOrOptOut() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (suppressedWas, probeWas) = (s.SuppressPerkNotifications, LuckyToast.GameOwnsScreen);
        try
        {
            s.SuppressPerkNotifications = false;
            LuckyToast.GameOwnsScreen = () => false;
            LuckyToast.Proc("Lucky Flash", 10);
            Dispatcher.UIThread.RunJobs();
            var popup = LuckyToast.Popup!;
            Assert.True(popup.IsVisible);
            Assert.True(LuckyToast.Holding);
            var texts = popup.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Equal(new[] { "LUCKY!", "10x XP!" }, texts);
            var title = popup.GetLogicalDescendants().OfType<TextBlock>().First();
            Assert.Equal(Color.FromRgb(0xFF, 0xD7, 0x00), ((ISolidColorBrush)title.Foreground!).Color);   // "Flash" = gold

            LuckyToast.Proc("Lucky Bubble", 20);                   // a proc while up refreshes the same window
            Dispatcher.UIThread.RunJobs();
            Assert.Same(popup, LuckyToast.Popup);
            Assert.Contains("20x XP!", popup.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

            LuckyToast.HoldElapsed();                              // 3 s after the last proc: fade, then hide
            LuckyToast.FadeOutDone();
            Assert.False(popup.IsVisible);

            LuckyToast.GameOwnsScreen = () => true;                // a game owns the screen: the toast stays down
            LuckyToast.Proc("Lucky Flash", 10);
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsVisible);
            LuckyToast.GameOwnsScreen = () => false;
            s.SuppressPerkNotifications = true;                    // perk opt-out
            LuckyToast.Proc("Lucky Flash", 10);
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsVisible);
        }
        finally
        {
            LuckyToast.Close();
            s.SuppressPerkNotifications = suppressedWas;
            LuckyToast.GameOwnsScreen = probeWas;
        }
        return Task.CompletedTask;
    });
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using IconKind = FluentIcons.Common.Symbol;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Enhancements tab against WPF MainWindow.Enhancements.cs (CreateSkillNode, CreateSkillTreeHeader,
/// PopulateSecretSkills) and MainWindow.EnhancementsFx.cs (owned breath, hover pop), on a stepped clock.</summary>
public sealed class EnhancementsTabTests
{
    private static Border? Glow(Control node) =>
        ((Panel)node).Children.OfType<Border>().FirstOrDefault(b => b.BoxShadow.Count > 0);

    private static void Setup()
    {
        AmbientBeatLoopsTests.Setup();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static string?[] Texts(Control c) => c.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
    private static IconKind[] Kinds(Control c) => c.GetLogicalDescendants().OfType<IconGlyph>().Select(g => g.Kind).ToArray();

    [Fact]
    public Task TreeDrawsWpfStatesHeaderAndSecretRailAndBreathesOnlyOnScreen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new SteppedClock();
        EnhancementsTabView.Time = clock;
        Window? w = null;
        try
        {
            var s = CoreSettings.Current;
            s.MotionLevel = MotionLevel.Full;
            s.SkillPoints = 1000;
            s.NightTimeUsageCount = 10;                       // night_shift revealed
            s.EarlyMorningUsageCount = 0;                     // early_bird hidden
            s.CurrentStreak = 0;
            s.PinkRushActive = false;
            s.PerformanceMode = false;
            s.UnlockedSkills = new List<string> { "pink_hours", "ditzy_data", "sparkle_boost_1" };

            var tab = new EnhancementsTabView();
            w = new Window { Width = 1400, Height = 700, Content = tab };
            w.Show();
            Dispatcher.UIThread.RunJobs();

            var nodes = tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>()
                .Where(c => c.Tag is string).ToDictionary(c => (string)c.Tag!);
            // Owned: breathing green glow, FOREVER badge. Purchasable: pink glow, Diamond + cost. Locked: blurred art, padlock.
            // Glows are sibling BoxShadow layers, never an Effect on a node (P82 rule 3).
            Assert.DoesNotContain(nodes.Values.SelectMany(n => n.GetVisualDescendants().Prepend(n)), v => v.Effect != null);
            Assert.Equal(Colors.LimeGreen, Glow(nodes["pink_hours"])!.BoxShadow[0].Color);
            var buyable = SkillDefinition.All.Single(x => x.Id == "sparkle_boost_2");
            Assert.Equal(Colors.HotPink, Glow(nodes["sparkle_boost_2"])!.BoxShadow[0].Color);
            Assert.Contains($"{buyable.Cost}", Texts(nodes["sparkle_boost_2"]));
            Assert.Contains(IconKind.Diamond, Kinds(nodes["sparkle_boost_2"]));
            var locked = SkillDefinition.All.Single(x => x.Id == "lucky_bimbo");
            Assert.Null(Glow(nodes["lucky_bimbo"]));
            Assert.Contains($"{locked.Cost}", Texts(nodes["lucky_bimbo"]));
            Assert.Contains(IconKind.LockClosed, Kinds(nodes["lucky_bimbo"]));
            Assert.DoesNotContain(IconKind.Diamond, Kinds(nodes["lucky_bimbo"]));
            var lockedArt = Assert.Single(nodes["lucky_bimbo"].GetVisualDescendants().OfType<Image>());   // skills/ art is packaged
            Assert.NotNull(tab.BakedArt["lucky_bimbo"]);                     // blurred once into the bitmap
            Assert.Same(tab.BakedArt["lucky_bimbo"], lockedArt.Source);

            // Header: prestige, active bonus chip for sparkle_boost_1, the XP multiplier and the Ditzy stats toggle.
            var canvasTexts = tab.FindControl<Canvas>("SkillTreeCanvas")!.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains(Loc.Get("label_prestige"), canvasTexts);
            Assert.Contains(Loc.Get("label_active_bonuses"), canvasTexts);
            Assert.Contains("1.10x", canvasTexts);
            var toggle = tab.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "DitzyStatsToggle");
            var panel = tab.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "DitzyStatsPanel");
            Assert.False(panel.IsVisible);
            toggle.Focus();
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(panel.IsVisible);                      // keyboard-reachable (P17)

            // Secret rail: revealed night_shift shows its name; early_bird stays a hint.
            var rail = tab.FindControl<WrapPanel>("SecretSkills")!.Children.ToArray();
            var night = SkillDefinition.All.Single(x => x.Id == "night_shift");
            Assert.Contains(rail, c => Texts(c).Contains(night.LocalizedName));
            Assert.Contains(rail, c => Texts(c).Contains(Loc.Get("label_secret_skill_hidden")));

            // Breath: one clock, sine 0.38 <-> 0.72 over 3.8 s, on screen only.
            Assert.True(tab.FxRunning);
            var glow = Assert.Single(tab.OwnedGlows, g => ReferenceEquals(g, Glow(nodes["pink_hours"])));
            clock.Now += TimeSpan.FromSeconds(3.8).Ticks;
            tab.StepFx();
            Assert.Equal(0.72, glow.Opacity, 3);
            clock.Now += TimeSpan.FromSeconds(3.8).Ticks;
            tab.StepFx();
            Assert.Equal(0.38, glow.Opacity, 3);

            tab.IsVisible = false;                             // hidden tab parks the clock (P01)
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.FxRunning);
            tab.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.FxRunning);

            w.WindowState = WindowState.Minimized;             // minimised window parks it too
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.FxRunning);
            w.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.FxRunning);

            s.MotionLevel = MotionLevel.Off;                   // Off: no clock, static 0.6 glow
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(tab.FxRunning);
            Assert.Equal(0.6, tab.OwnedGlows[0].Opacity, 3);
            s.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(tab.FxRunning);
            s.PerformanceMode = true;                          // Performance tier: also rests
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(tab.FxRunning);
            Assert.Equal(0.6, tab.OwnedGlows[0].Opacity, 3);
            s.PerformanceMode = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();

            // A settings change while hidden does not rebuild (P07); the next show does.
            var before = tab.FindControl<Canvas>("SkillTreeCanvas")!.Children[0];
            tab.IsVisible = false;
            s.SkillPoints = 999;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(before, tab.FindControl<Canvas>("SkillTreeCanvas")!.Children[0]);
            tab.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(before, tab.FindControl<Canvas>("SkillTreeCanvas")!.Children[0]);

            // Hover pop from a real pointer: z-lift and the 1.25 target; leaving drops it.
            nodes = tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>()
                .Where(c => c.Tag is string).ToDictionary(c => (string)c.Tag!);
            var root = nodes["pink_hours"];
            var p = root.TranslatePoint(new Point(40, 40), w)!.Value;
            w.MouseMove(p, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(10, root.ZIndex);
            Assert.NotNull(root.Transitions);
            w.MouseMove(new Point(p.X, 690), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, root.ZIndex);
        }
        finally
        {
            w?.Close();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = oldSettings;
            EnhancementsTabView.Time = TimeProvider.System;
        }
        return Task.CompletedTask;
    });
}

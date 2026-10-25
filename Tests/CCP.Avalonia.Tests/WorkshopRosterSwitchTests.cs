using System;
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
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopRosterSwitchTests
{
    [Fact]
    public Task RosterClickSwitchesAndCoreEventsRefreshWhileOpen() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        // P02: CoreMods.Service is process-global and cannot be detached; a mod that hides cards 2/3
        // would make every switch below vacuous, so state that precondition instead of inheriting it.
        Assert.False(CoreMods.Service?.IsCompanionSupported((CompanionId)2) == false, "active mod hides companion card 2");
        Assert.False(CoreMods.Service?.IsCompanionSupported((CompanionId)3) == false, "active mod hides companion card 3");
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.ActiveCompanionId = 0;
        s.CompanionProgressData[2] = new CompanionProgress { Level = 7 };
        var shell = new MainShellWindow();
        bool closed = false;
        try
        {
            shell.Show();
            CollapsedRoom.Show(shell);   // the roster cell sits in the Workshop drawer, which v2 leaves in the collapsed room (WPF too)
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            shell.GetLogicalDescendants().OfType<WorkshopAccordion>().Single().ExpandAndReveal("roster");
            Dispatcher.UIThread.RunJobs();
            var cell = shell.GetLogicalDescendants().OfType<WorkshopRosterCell>().Single();
            var hero = shell.GetLogicalDescendants().OfType<CompanionHeroCard>().First().ViewModel!;
            void Press(Control target, MouseButton button)
            {
                target.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), shell)!.Value;
                shell.MouseDown(at, button);
                shell.MouseUp(at, button);
                Dispatcher.UIThread.RunJobs();
            }
            var name = cell.FindControl<TextBlock>("TxtCompanion2Name")!;
            Press(name, MouseButton.Right);
            Assert.Equal(0, s.ActiveCompanionId);
            Press(cell.FindControl<Button>("BtnCompanion2Personality")!, MouseButton.Left);
            Assert.Equal(0, s.ActiveCompanionId); // button never also switches the card
            Press(name, MouseButton.Left);
            Assert.Equal(2, s.ActiveCompanionId);
            Assert.Equal(7, hero.Level); // hero follows the click (WPF SyncHero)
            Assert.NotEqual(DateTime.MinValue, s.CompanionProgressData[2].FirstActivated);
            Color Ring(int i) => ((ISolidColorBrush)cell.FindControl<Border>($"CompanionCard{i}")!.BorderBrush!).Color;
            Assert.NotEqual(Colors.Transparent, Ring(2));
            Assert.Equal(Colors.Transparent, Ring(0));

            // Core callers (mod switching, XP awards) reach the same refresh without a tab bounce.
            await Task.Run(() => CompanionCore.SwitchCompanion((CompanionId)3));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(Colors.Transparent, Ring(3));
            Assert.Equal(Colors.Transparent, Ring(2));
            var heroName = hero.Name;
            Assert.Equal(s.ActiveCompanionProgress.Level, hero.Level); // hero follows a Core switch too
            var progress = s.ActiveCompanionProgress;
            int previous = progress.Level;
            await Task.Run(() => CompanionCore.AddCompanionXP(progress.XPForNextLevel * 4, "roster-test"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(progress.Level > previous);
            Assert.Equal($"Lv.{progress.Level}", cell.FindControl<TextBlock>("TxtCompanion3Level")!.Text);
            Assert.Equal(progress.Level, hero.Level); // and a Core level-up
            Assert.Equal(heroName, hero.Name);

            shell.Close();
            Dispatcher.UIThread.RunJobs();
            closed = true;
            var closedLevel = cell.FindControl<TextBlock>("TxtCompanion3Level")!.Text;
            CompanionCore.AddCompanionXP(progress.XPForNextLevel * 4, "closed-roster-test");
            CompanionCore.SwitchCompanion((CompanionId)0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(closedLevel, cell.FindControl<TextBlock>("TxtCompanion3Level")!.Text);
            Assert.NotEqual(Colors.Transparent, Ring(3)); // closed roster released static subscriptions
        }
        finally
        {
            if (!closed) shell.Close();
            Dispatcher.UIThread.RunJobs();
            // The private service's level/XP writes armed a debounced save; drop it here, not via the hook.
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = old;
        }
    });
}

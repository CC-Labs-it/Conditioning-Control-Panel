using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class TutorialGuideTests
{
    [Theory]
    [InlineData("FullTour")]
    [InlineData("GettingStarted")]
    [InlineData("Settings")]
    [InlineData("Presets")]
    [InlineData("Progression")]
    [InlineData("Achievements")]
    [InlineData("Companion")]
    [InlineData("Patreon")]
    [InlineData("Avatar")]
    [InlineData("Awareness")]
    public Task HelpRowClosesPanelAndStartsItsNamedGuide(string tour) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        TutorialLiveTests.Setup();
        var start = CoreTutorial.StartAction;
        var active = CoreTutorial.IsActiveProvider;
        var perf = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            string? started = null;
            CoreTutorial.IsActiveProvider = () => false;
            CoreTutorial.StartAction = name => started = name;
            shell.SetTutorialOverlay(true);
            shell.FindControl<Button>("BtnGuide" + tour)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(tour, started);
            Assert.False(shell.FindControl<Border>("MainTutorialOverlay")!.IsVisible);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreTutorial.StartAction = start;
            CoreTutorial.IsActiveProvider = active;
            CoreSettings.Current.PerformanceMode = perf;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ModGuideNavigatesEveryRequestedSection() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        TutorialLiveTests.Setup();
        var shell = TutorialHeadHooks.ShellProvider;
        var store = TutorialService.CompletionStore;
        TutorialHeadHooks.ShellProvider = () => null;
        TutorialService.CompletionStore = new MemoryStore();
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var window = new ModCreatorWindow(startWithTutorial: true);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal("Modding", CoreTutorial.CurrentTourName);
            var panels = (Dictionary<string, Border>)typeof(ModCreatorWindow).GetField("_sectionPanels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            while (CoreTutorial.IsActive)
            {
                if (TutorialHead.Service.CurrentStep?.RequiresTab is { } tab && tab.StartsWith("mod:", StringComparison.Ordinal))
                {
                    Assert.True(panels[tab.Substring(4)].IsVisible);
                    Assert.Single(panels.Where(p => p.Value.IsVisible));
                }
                CoreTutorial.Next();
            }
        }
        finally
        {
            window.Close();
            TutorialHead.ResetForTests();
            Dispatcher.UIThread.RunJobs();
            TutorialHeadHooks.ShellProvider = shell;
            TutorialService.CompletionStore = store;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FirstEditorLoadOffersCoachmarksOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        TutorialLiveTests.Setup();
        var property = typeof(ConditioningControlPanel.Avalonia.App).GetProperty("Settings", BindingFlags.Static | BindingFlags.Public)!;
        var before = property.GetValue(null);
        var settings = new ConditioningControlPanel.Services.SettingsService();
        settings.Current.HasSeenDeeperEditorIntro = false;
        var store = TutorialService.CompletionStore;
        var shell = TutorialHeadHooks.ShellProvider;
        TutorialService.CompletionStore = new MemoryStore();
        TutorialHeadHooks.ShellProvider = () => null;
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        property.SetValue(null, settings);
        var first = new ConditioningControlPanel.Avalonia.Views.Deeper.DeeperEditorWindow(new ConditioningControlPanel.Models.Deeper.Enhancement(), null);
        var second = new ConditioningControlPanel.Avalonia.Views.Deeper.DeeperEditorWindow(new ConditioningControlPanel.Models.Deeper.Enhancement(), null);
        try
        {
            first.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(settings.Current.HasSeenDeeperEditorIntro);
            Assert.Equal("DeeperEditor", CoreTutorial.CurrentTourName);
            Assert.True(CoreTutorial.IsActive);
            first.Close();
            TutorialHead.ResetForTests();
            second.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
        }
        finally
        {
            first.Close(); second.Close(); TutorialHead.ResetForTests();
            property.SetValue(null, before);
            TutorialService.CompletionStore = store;
            TutorialHeadHooks.ShellProvider = shell;
        }
        return Task.CompletedTask;
    });

    private sealed class MemoryStore : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => false;
        public void Latch(string name) { }
    }
}

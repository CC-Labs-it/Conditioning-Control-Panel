using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ModCreatorTutorialTests
{
    private sealed class Store : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => false;
        public void Latch(string name) { }
    }

    // WPF Windows/ModCreatorWindow.xaml.cs:220-222, 256-267. Exercise the public
    // auto-start route and the live card's buttons, rather than invoking callbacks directly.
    [Fact]
    public void LiveGuideNavigatesEverySectionAndPreviousAndCanRestart() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldStore = TutorialService.CompletionStore;
        var oldShell = TutorialHeadHooks.ShellProvider;
        TutorialService.CompletionStore = new Store();
        TutorialHeadHooks.ShellProvider = () => null;
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        ModCreatorWindow? window = null;
        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                window = new ModCreatorWindow(startWithTutorial: true);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(CoreTutorial.IsActive);
                var sections = 0;
                while (CoreTutorial.IsActive)
                {
                    var step = TutorialHead.Service.CurrentStep!;
                    if (step.RequiresTab?.StartsWith("mod:") == true)
                    {
                        AssertSection(window, step.RequiresTab.Substring(4));
                        sections++;
                        if (step.RequiresTab == "mod:theme")
                        {
                            ClickCard(window, "BtnPrevious");
                            AssertSection(window, "info");
                            ClickCard(window, "BtnNext");
                            AssertSection(window, "theme");
                        }
                    }
                    ClickCard(window, "BtnNext");
                }
                Assert.Equal(12, sections);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "TextPanel");
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            window?.Close();
            TutorialHead.ResetForTests();
            TutorialService.CompletionStore = oldStore;
            TutorialHeadHooks.ShellProvider = oldShell;
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static void ClickCard(Window window, string name)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
        Assert.True(button.IsVisible);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertSection(ModCreatorWindow window, string key)
    {
        var buttons = window.GetLogicalDescendants().OfType<Button>()
            .Where(b => b.Tag is string && b.BorderThickness.Left == 3).ToList();
        Assert.Equal(key, Assert.Single(buttons).Tag);
    }
}

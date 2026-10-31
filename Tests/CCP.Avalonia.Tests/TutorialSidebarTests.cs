using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class TutorialSidebarTests
{
    private sealed class Store : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => false;
        public void Latch(string name) { }
    }

    // WPF TutorialOverlay.xaml.cs:689-706; MainWindow.TabNavigation.cs:879-886.
    // Drive the live stage. Isolate preparation from the separate OnActivate navigation
    // callbacks so a ShowTab call cannot hide a broken preparation hook.
    [Theory]
    [InlineData("settings_tab", NavSections.Home)]
    [InlineData("presets_intro", NavSections.Studio)]
    [InlineData("progression_intro", NavSections.Home)]
    public void LiveCardLightsItsOwningSectionWithoutChangingThePage(string id, string section) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current == null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldStore = TutorialService.CompletionStore;
            var oldShell = TutorialHeadHooks.ShellProvider;
            TutorialService.CompletionStore = new Store();
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            MainShellWindow? shell = null;
            TutorialOverlay? overlay = null;
            TutorialStep? step = null;
            string? requiresTab = null;
            try
            {
                shell = new MainShellWindow();
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                TutorialHeadHooks.ShellProvider = () => null;
                TutorialHead.Service.ConfigureCallbacks(() => { }, () => { }, () => { },
                    () => { }, () => { }, () => { });
                TutorialHead.Service.Start(TutorialType.FullTour);
                while (TutorialHead.Service.CurrentStep!.Id != id) TutorialHead.Service.Next();
                step = TutorialHead.Service.CurrentStep;
                requiresTab = step.RequiresTab;
                // A named nav entry wins over RequiresTab, just as WPF DoorTabKeyFor does.
                if (id == "progression_intro") step.RequiresTab = "studio";
                shell.RefreshSectionRail("appsettings");
                var page = shell.CurrentTab;
                overlay = new TutorialOverlay(shell);
                overlay.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(overlay.IsOnStage);
                Assert.Equal(new[] { section }, shell.NavSectionRowsForTests
                    .Where(r => r.Button.Classes.Contains("active")).Select(r => r.Section));
                Assert.Equal(page, shell.CurrentTab);
            }
            finally
            {
                overlay?.Close();
                TutorialHead.ResetForTests();
                if (step != null) step.RequiresTab = requiresTab;
                shell?.Close();
                TutorialService.CompletionStore = oldStore;
                TutorialHeadHooks.ShellProvider = oldShell;
                Dispatcher.UIThread.RunJobs();
            }
        });
}

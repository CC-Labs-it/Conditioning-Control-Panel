using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class CompanionHeroTutorialTests
{
    private sealed class CompletionStore : TutorialService.ITourCompletionStore
    {
        private bool _latched;
        public bool Has(string name) => _latched;
        public void Latch(string name) => _latched = true;
    }

    [Fact]
    public Task HeaderChipStartsTheCompanionTourAndPanicAbandonsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var previousSettings = CoreSettings.ServiceProvider;
        var previousShell = TutorialHeadHooks.ShellProvider;
        var previousCompletion = TutorialService.CompletionStore;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.PerformanceMode = true;
        CoreSettings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        TutorialService.CompletionStore = new CompletionStore();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;

            var hero = shell.GetLogicalDescendants().OfType<CompanionHeroCard>().Single();
            var chip = hero.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, hero.ViewModel!.Header!.TutorialCommand));
            chip.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var position = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), shell)!.Value;
            shell.MouseDown(position, MouseButton.Left);
            shell.MouseUp(position, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(CoreTutorial.IsActive);
            Assert.Equal("Companion", CoreTutorial.CurrentTourName);
            Assert.Equal("comp_intro", CoreTutorial.CurrentStep!.Id);
            var overlay = TutorialHead.ShellOverlay;
            Assert.NotNull(overlay);
            Assert.True(overlay!.IsVisible);
            Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "TxtTitle" && t.Text == "Meet Your Companion");

            CoreTutorial.Next();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("comp_hero", CoreTutorial.CurrentStep!.Id);
            Assert.Equal("HeroZone", CoreTutorial.CurrentStep.TargetElementName);
            hero.ViewModel!.Header!.TutorialCommand.Execute(null);
            Assert.Equal("comp_hero", CoreTutorial.CurrentStep!.Id); // WPF keeps the running tour.

            PanicSurfaces.All.Single(s => s.Id == "tour").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
            Assert.False(TutorialHead.Service.HasCompleted(TutorialType.Companion));
        }
        finally
        {
            TutorialHead.ResetForTests();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            TutorialHeadHooks.ShellProvider = previousShell;
            TutorialService.CompletionStore = previousCompletion;
            CoreSettings.ServiceProvider = previousSettings;
        }
        return Task.CompletedTask;
    });
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class TutorialEmiSummonTests
{
    private sealed class Store : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => false;
        public void Latch(string name) { }
    }

    // WPF MainWindow.Settings.cs:565-577 and EmiTourNarrator.cs:119-140.
    [Fact]
    public void GuideSummonsEnabledAwayEmiBeforeTheFirstCard() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldStore = TutorialService.CompletionStore;
        var oldShell = TutorialHeadHooks.ShellProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        TutorialService.CompletionStore = new Store();
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var desk = EmiDeskService.Instance;
        MainShellWindow? shell = null;
        bool? outAtFirstCard = null;
        void OnStep(object? sender, TutorialStep step) => outAtFirstCard = desk.IsOut;
        TutorialHead.Service.StepChanged += OnStep;
        try
        {
            settings.Current.PerformanceMode = true;
            settings.Current.EmiDeskMuteAvatar = false;
            settings.Current.EmiDeskEnabled = false;
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            TutorialHeadHooks.ShellProvider = () => shell;
            void StartGuide()
            {
                shell.Named<Button>("BtnMainHelp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                shell.Named<Button>("BtnTutorialGettingStarted")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            StartGuide();
            Assert.True(CoreTutorial.IsActive);
            Assert.False(desk.IsOut);
            Assert.False(outAtFirstCard);
            CoreTutorial.Skip();

            settings.Current.EmiDeskEnabled = true;
            var before = EmiState.Current.SummonCount;
            outAtFirstCard = null;
            StartGuide();
            Assert.True(CoreTutorial.IsActive);
            Assert.True(desk.IsOut);
            Assert.True(outAtFirstCard);
            Assert.True(desk.Window!.IsVisible);
            Assert.Equal(before + 1, EmiState.Current.SummonCount);

            // A refused second guide and a later guide with EMI already out do not re-summon her.
            StartGuide();
            Assert.Equal(before + 1, EmiState.Current.SummonCount);
            CoreTutorial.Skip();
            StartGuide();
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal(before + 1, EmiState.Current.SummonCount);
            CoreTutorial.Skip();

            // The temporary start hook must not attach itself to another window's tour.
            desk.Window.ShutDown();
            Assert.False(desk.IsOut);
            CoreTutorial.Start("Modding");
            Assert.True(CoreTutorial.IsActive);
            Assert.False(desk.IsOut);
        }
        finally
        {
            TutorialHead.Service.StepChanged -= OnStep;
            TutorialHead.ResetForTests();
            desk.Window?.ShutDown();
            shell?.Close();
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = oldSettings;
            TutorialService.CompletionStore = oldStore;
            TutorialHeadHooks.ShellProvider = oldShell;
            Dispatcher.UIThread.RunJobs();
        }
    });
}

using System;
using System.Threading.Tasks;
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
public sealed class TutorialEmiSummonTests : Serilog.Core.ILogEventSink
{
    private Exception? _startError;
    public void Emit(Serilog.Events.LogEvent e) { if (e.Level >= Serilog.Events.LogEventLevel.Warning && e.Exception?.Message == "activation probe") _startError = e.Exception; }
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

    // WPF TutorialService.cs:354-361 and EmiDeskService.cs:286-313.
    [Theory]
    [InlineData("answer")]
    [InlineData("existing")]
    [InlineData("failure")]
    [InlineData("panic")]
    [InlineData("restart")]
    [InlineData("close")]
    [InlineData("hide")]
    [InlineData("hide_on_activate")]
    [InlineData("superseded_hidden")]
    [InlineData("activate_throws")]
    public Task PendingMutePromptPrecedesTheFirstCard(string outcome) => AvaloniaTestDispatcher.RunAsync(async () =>
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
        var oldAsk = desk.AskMute;
        var oldLogger = Serilog.Log.Logger;
        if (outcome == "activate_throws") Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(this).CreateLogger();
        var answer = new TaskCompletionSource<EmiMuteChoice>();
        var summoned = new TaskCompletionSource<bool>();
        var stepped = new TaskCompletionSource<bool>();
        int stepCount = 0;
        void OnOut(object? sender, bool isOut) { if (isOut) summoned.TrySetResult(true); }
        void OnStep(object? sender, TutorialStep step)
        {
            stepCount++;
            stepped.TrySetResult(desk.Window?.IsVisible == true);
        }
        desk.ResetMutePrompt();
        desk.AskMute = () => answer.Task;
        desk.OutChanged += OnOut;
        TutorialHead.Service.StepChanged += OnStep;
        // A returning user has no first-run modal to close the shell on hide.
        settings.Current.Welcomed = true;
        settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.WhatMovedCardShown = 1;
        var shell = new MainShellWindow();
        bool shellClosed = false;
        shell.Closed += (_, _) => shellClosed = true;
        try
        {
            settings.Current.PerformanceMode = true;
            settings.Current.EmiDeskEnabled = true;
            settings.Current.EmiDeskMuteAvatar = true;
            settings.Current.EmiDeskMuteDontAsk = false;
            settings.Current.AiChatEnabled = true;
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            TutorialHeadHooks.ShellProvider = () => shell;
            if (outcome == "existing") _ = desk.Summon("dock");
            shell.Named<Button>("BtnMainHelp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            shell.Named<Button>("BtnTutorialGettingStarted")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(CoreTutorial.IsActive);
            Assert.True(desk.IsOut);
            var pendingSummon = desk.PendingSummon!;
            Assert.False(pendingSummon.IsCompleted);
            Assert.False(desk.Window!.IsVisible);
            Assert.False(stepped.Task.IsCompleted);
            Assert.Null(TutorialHead.ShellOverlay);
            CoreTutorial.Start("Settings");
            Assert.Equal("GettingStarted", CoreTutorial.CurrentTourName);
            var firstStep = TutorialHead.Service.CurrentStep!;
            var activate = firstStep.OnActivate;
            int activated = 0;
            firstStep.OnActivate = () =>
            {
                activated++;
                if (outcome == "activate_throws") throw new InvalidOperationException("activation probe");
                if (outcome == "hide_on_activate") shell.Hide();
                activate?.Invoke();
            };

            if (outcome is "panic" or "restart") TutorialHead.OnPanic();
            if (outcome == "restart")
            {
                CoreTutorial.Start("Settings");
                Assert.Equal("Settings", CoreTutorial.CurrentTourName);
                Assert.False(stepped.Task.IsCompleted);
            }
            TutorialStep? successor = null;
            if (outcome == "superseded_hidden")
            {
                TutorialHead.Service.Start(TutorialType.Modding);
                successor = TutorialHead.Service.CurrentStep;
                shell.Hide();
            }
            if (outcome == "close") shell.Close();
            if (outcome == "hide") { shell.Hide(); Assert.False(shellClosed); }
            if (outcome == "failure") answer.SetException(new InvalidOperationException("prompt failed"));
            else answer.SetResult(EmiMuteChoice.Keep);
            await summoned.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await pendingSummon.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            if (outcome == "superseded_hidden")
            {
                Assert.True(CoreTutorial.IsActive);
                Assert.Same(successor, TutorialHead.Service.CurrentStep);
                Assert.Equal(0, activated);
                Assert.Null(TutorialHead.ShellOverlay);
            }
            else if (outcome is "answer" or "existing" or "failure" or "restart")
            {
                Assert.True(await stepped.Task.WaitAsync(TimeSpan.FromSeconds(10)));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, stepCount);
                Assert.Equal(outcome == "restart" ? 0 : 1, activated);
                Assert.NotNull(TutorialHead.ShellOverlay);
            }
            else
            {
                Dispatcher.UIThread.RunJobs();
                Assert.False(CoreTutorial.IsActive);
                Assert.Equal(outcome is "hide_on_activate" or "activate_throws" ? 1 : 0, activated);
                Assert.Equal(outcome == "hide_on_activate", stepped.Task.IsCompleted);
                Assert.Null(TutorialHead.ShellOverlay);
                if (outcome == "activate_throws") Assert.NotNull(_startError);
                if (outcome is "hide" or "hide_on_activate" or "activate_throws")
                {
                    Assert.False(shellClosed);
                    if (!shell.IsVisible) shell.Show();
                    shell.Named<Button>("BtnMainHelp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    shell.Named<Button>("BtnTutorialGettingStarted")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(CoreTutorial.IsActive);
                    Assert.NotNull(TutorialHead.ShellOverlay);
                }
            }
        }
        finally
        {
            answer.TrySetResult(EmiMuteChoice.Keep);
            TutorialHead.Service.StepChanged -= OnStep;
            desk.OutChanged -= OnOut;
            TutorialHead.ResetForTests();
            desk.Window?.ShutDown();
            desk.AskMute = oldAsk;
            Serilog.Log.Logger = oldLogger;
            desk.ResetMutePrompt();
            shell.Close();
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = oldSettings;
            TutorialService.CompletionStore = oldStore;
            TutorialHeadHooks.ShellProvider = oldShell;
            Dispatcher.UIThread.RunJobs();
        }
    });

}

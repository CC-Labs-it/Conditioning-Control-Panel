using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// wire-views-tab-programs-b: the locked premium card routes to Settings · Account (WPF
/// MainWindow.ProgramsTab.cs:623-628, 1980-1985), and the rail's first-visit pulse on the You row
/// (WPF :48-67, TabNavigation.cs:831-875) starts once and is spent by reaching the tab.
/// </summary>
public sealed class ProgramsLockedRouteTests
{
    private static void EnsureAvalonia()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task LockedPremiumCardOpensAccountSettingsNotEnrollment() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var dir = Directory.CreateTempSubdirectory("ccp-programs-locked-").FullName;
        var (oldPrograms, oldTask, oldPremium) = (AvApp.Programs, CoreProgram.TaskAvailableProvider, CoreEntitlement.HasPremiumProvider);
        var settings = CoreSettings.Current;
        var (seenTab, seenIntro) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro);
        settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;
        MainShellWindow? shell = null;
        try
        {
            CoreEntitlement.HasPremiumProvider = null;
            CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
            var svc = AvApp.Programs = new ProgramService(Path.Combine(dir, "programs.json"), readOnly: false);
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("programs");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<ProgramsTabView>("ProgramsTab")!;
            var list = tab.FindControl<ListBox>("ProgramLibraryList")!;

            // "presentation" is premium and every required task is raised here: the upsell card.
            var row = list.Items.Cast<ProgramBrowseItem>().Single(r => r.ProgramId == "presentation");
            Assert.True(row.IsLocked);
            Assert.True(row.IsActionEnabled);
            Assert.Equal(Loc.Get("btn_program_locked"), row.ActionText);
            Assert.Equal(Loc.Get("programs_locked_hint"), row.ReasonText);

            list.ScrollIntoView(row);
            Dispatcher.UIThread.RunJobs();
            var button = list.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "presentation"));
            Assert.True(button.IsEffectivelyEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.Empty(shell.OwnedWindows);          // no enroll ceremony, no refusal dialog
            Assert.Null(svc.ActiveEnrollment);
        }
        finally
        {
            if (shell != null) { foreach (var w in shell.OwnedWindows.ToList()) w.Close(); shell.Close(); }
            Dispatcher.UIThread.RunJobs();
            AvApp.Programs?.Dispose();
            (AvApp.Programs, CoreProgram.TaskAvailableProvider, CoreEntitlement.HasPremiumProvider) = (oldPrograms, oldTask, oldPremium);
            (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro) = (seenTab, seenIntro);
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RailPulseRunsOnTheYouRowUntilTheTabIsReached() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var settings = CoreSettings.Current;
        var (seenTab, seenIntro, motion, perf) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel, settings.PerformanceMode);
        MainShellWindow? shell = null;
        try
        {
            settings.HasSeenProgramsTab = false;
            settings.HasSeenProgramsIntro = true;
            settings.MotionLevel = MotionLevel.Off;
            settings.PerformanceMode = false;
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();

            shell.StartProgramsTabPulse();
            Assert.False(shell.IsNavDoorHeaderPulsing("programs"));   // Motion Off: ambient gate

            settings.MotionLevel = MotionLevel.Full;
            shell.ShowTab("discord");                                 // the You row is already lit
            shell.StartProgramsTabPulse();
            Assert.False(shell.IsNavDoorHeaderPulsing("programs"));

            shell.ShowTab("settings");
            shell.StartProgramsTabPulse();
            Assert.True(shell.IsNavDoorHeaderPulsing("programs"));

            var you = shell.NavSectionRowsForTests.Single(r => r.Section == "you").Button;
            shell.ShowTab("programs");
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.IsNavDoorHeaderPulsing("programs"));
            Assert.True(settings.HasSeenProgramsTab);
            Assert.Equal(1.0, you.Opacity);

            shell.ShowTab("settings");
            shell.StartProgramsTabPulse();                            // spent for good
            Assert.False(shell.IsNavDoorHeaderPulsing("programs"));
        }
        finally
        {
            if (shell != null) { foreach (var w in shell.OwnedWindows.ToList()) w.Close(); shell.Close(); }
            Dispatcher.UIThread.RunJobs();
            (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel, settings.PerformanceMode) = (seenTab, seenIntro, motion, perf);
        }
        return Task.CompletedTask;
    });
}

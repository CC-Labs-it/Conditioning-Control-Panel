using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class SessionFeatureLockProgramTests
{
    [Fact]
    public Task ProgramReasonPaintsTheRibbonAndOwnedDialsThenPresetUsesGenericCopy() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .With(new global::Avalonia.Media.FontManagerOptions
                {
                    DefaultFamilyName = "fonts:Inter#Inter",
                    FontFamilyMappings = new System.Collections.Generic.Dictionary<string, global::Avalonia.Media.FontFamily>
                    {
                        ["Segoe UI"] = new("fonts:Inter#Inter"), ["Segoe UI Black"] = new("fonts:Inter#Inter")
                    }
                }).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var dir = Directory.CreateTempSubdirectory("ccp-session-lock-program-").FullName;
        var (oldPrograms, oldSessions, oldRunning) = (AvApp.Programs, AvApp.Sessions, CoreSession.IsSessionRunningProvider);
        MainShellWindow? shell = null;
        try
        {
            using var svc = new ProgramService(Path.Combine(dir, "programs.json"), readOnly: true);
            AvApp.Programs = svc;
            svc.State.Active = new ProgramEnrollment { ProgramId = "first_week", CurrentDay = 3 };
            var session = Assert.IsType<Session>(svc.BuildTodaySession());
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            shell = new MainShellWindow();
            shell.Show();
            var lockCard = shell.StudioRack!.HostedFeaturePanels.OfType<LockCardFeatureControl>().Single();
            var ownedNames = new[] { "ChkRandomRepeats", "SliderRepeatsMin", "ChkTargetLength", "SliderTargetLength", "SliderTargetVariance" };
            var dials = ownedNames.Select(n => lockCard.FindControl<Control>(n)!).ToArray();
            foreach (var dial in dials)
            {
                Assert.True(SessionLock.GetOwned(dial));
                ToolTip.SetTip(dial, "original tip");
            }

            // The real runner and ProgramService discriminator, as on the Programs tab.
            runner.Start(session);
            shell.RefreshSessionFeatureLock();
            var expected = Loc.GetF("program_lock_reason", svc.ActiveProgram!.Title, 3);
            Assert.Equal(expected, shell.SessionFeatureLockReason);
            Assert.Equal(expected, shell.SettingsPage!.TxtProgramFeatureLock.Text);
            Assert.True(shell.SettingsPage.ProgramFeatureLockRibbon.IsVisible);
            foreach (var dial in dials)
            {
                Assert.False(dial.IsEnabled);
                Assert.Equal(expected, ToolTip.GetTip(dial));
            }

            // WPF's two program fallbacks: title without a day, then an unnamed program.
            svc.State.Active.CurrentDay = 999;
            Assert.Equal(Loc.GetF("program_lock_reason_no_day", svc.ActiveProgram.Title), shell.SessionFeatureLockReason);
            var title = svc.ActiveProgram.Title;
            try
            {
                svc.ActiveProgram.Title = " ";
                Assert.Equal(Loc.Get("program_lock_reason_generic"), shell.SessionFeatureLockReason);
            }
            finally { svc.ActiveProgram.Title = title; }

            runner.Stop();
            shell.RefreshSessionFeatureLock();
            Assert.False(shell.SettingsPage.ProgramFeatureLockRibbon.IsVisible);
            Assert.Equal(Loc.Get("session_lock_reason"), shell.SessionFeatureLockReason);
            foreach (var dial in dials)
            {
                Assert.True(dial.IsEnabled);
                Assert.Equal("original tip", ToolTip.GetTip(dial));
            }

            // An enrollment alone must never label a preset as the program's dose.
            runner.Start(Session.MorningDrift);
            shell.RefreshSessionFeatureLock();
            Assert.Equal(Loc.Get("session_lock_reason"), shell.SettingsPage.TxtProgramFeatureLock.Text);
            Assert.All(dials, dial => Assert.False(dial.IsEnabled));
            runner.Stop();
            AvApp.Programs = null;
            Assert.Equal(Loc.Get("session_lock_reason"), shell.SessionFeatureLockReason);
        }
        finally
        {
            AvApp.Sessions?.Stop();
            shell?.Close();
            (AvApp.Programs, AvApp.Sessions, CoreSession.IsSessionRunningProvider) = (oldPrograms, oldSessions, oldRunning);
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });
}

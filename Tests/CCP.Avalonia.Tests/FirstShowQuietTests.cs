using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Owner fix3 (showquiet): on a fresh install the Dashboard's "Today's Free Feature" card opened
/// under the first-run wizard and was still up when EMI's first show asked its first question. WPF queues
/// the wizard on the startup ladder from the shell's constructor (MainWindow.xaml.cs:543), so the ladder is
/// busy from the first frame and the Dashboard's cards are held (StartupPresenter.cs:214-243); when it
/// drains the ten-minute first-launch window is open and they become Inbox rows.</summary>
public sealed partial class FirstShowStageTests
{
    [Fact]
    public Task AFreshInstallHoldsTheDashboardCardsBehindTheWizardAndInboxesThem() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        int shows = 0;
        var cards = new System.Collections.Generic.List<string?>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is FeatureIntroPopup) cards.Add(w.Title); });
        try
        {
            CoreSettings.Current.Welcomed = false;
            CoreSettings.Current.HasAcceptedAgeVerification = false;
            CoreSettings.Current.SeenFeatureIntros.Clear();
            shell = new MainShellWindow { OpenWelcomeShow = _ => { shows++; return true; } };
            Assert.True(StartupLadder.IsQuiet);   // the ladder is busy from the constructor, as WPF's is
            shell.Show();
            await Until(() => shell.OwnedWindows.OfType<FirstRunWizard>().Any());
            var wizard = shell.OwnedWindows.OfType<FirstRunWizard>().Single();

            Assert.Empty(cards);
            Assert.DoesNotContain("daily-free", CoreSettings.Current.SeenFeatureIntros);
            Assert.False(StartupLadder.Inbox.Contains("intro:daily-free"));   // held, not filed: the ladder is only busy

            wizard.FindControl<CheckBox>("ChkAgeConfirm")!.IsChecked = true;
            wizard.FindControl<Button>("BtnNext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            wizard.Close();
            await Until(() => shows > 0);
            for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }

            // Drained into the first-launch quiet window: rows the user opens when they want them.
            Assert.True(StartupLadder.Inbox.Contains("intro:daily-free"));
            Assert.True(StartupLadder.Inbox.Contains("intro:one-account"));
            Assert.Empty(cards);
            Assert.DoesNotContain("daily-free", CoreSettings.Current.SeenFeatureIntros);   // still owed
        }
        finally
        {
            foreach (var o in shell?.OwnedWindows.ToList() ?? new()) o.Close();
            shell?.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = old;
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
        }
    });
}

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
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;
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

    /// <summary>Owner fix3: EMI ended up behind the main window. WPF's ownership chain (stage owned by main,
    /// EMI by the stage) keeps the show above the shell however it is activated. Where the window manager
    /// cannot keep the Topmost layer (WSLg), the show is mapped again on top, in WPF's order, each time the
    /// shell comes forward - and EMI once as the show opens, since she was mapped long before it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task TheShellComingForwardPutsTheShowBackOnTopWhereTheWmCannotKeepIt(bool wmCannotKeepAbove) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        var remapped = new System.Collections.Generic.List<TopLevel>();
        try
        {
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            CoreSettings.Current.MasterVolume = 0;
            FirstShowService.RequireClickThrough = false;
            X11Overlay.WmCannotKeepAboveForTests = () => wmCannotKeepAbove;
            X11Overlay.RemapRequestedForTests = w => remapped.Add(w);
            shell = new MainShellWindow { OpenWelcomeShow = _ => false };
            shell.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(FirstShowService.Open(shell));
            var stage = FirstShowService.Window!;
            var emi = EmiDeskService.Instance.Window!;
            Assert.Equal(wmCannotKeepAbove ? new TopLevel[] { emi } : Array.Empty<TopLevel>(), remapped);   // EMI lifted as the show opens

            double t = S.GreetAfter + .1; stage.Clock = () => t;
            await Until(() => { t += .05; stage.Frame(); return stage.SpeechShown; });
            remapped.Clear();
            shell.Activate(); shell.Activate();   // a burst of activations: one pass
            Dispatcher.UIThread.RunJobs();
            if (wmCannotKeepAbove)
            {
                Assert.Equal(3, remapped.Count);
                Assert.Same(stage, remapped[0]);   // WPF's order, bottom to top: stage, EMI, her speech card
                Assert.Same(emi, remapped[1]);
                Assert.Equal("CCP - Emi speech", ((Window)remapped[2]).Title);
            }
            else Assert.Empty(remapped);   // a window manager that keeps the layer needs nothing

            FirstShowService.Stop();
            Dispatcher.UIThread.RunJobs();
            remapped.Clear();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(remapped);   // the show is over: the shell is left alone
        }
        finally
        {
            X11Overlay.WmCannotKeepAboveForTests = null;
            X11Overlay.RemapRequestedForTests = null;
            FirstShowService.Stop();
            FirstShowService.RequireClickThrough = true;
            EmiDeskService.Instance.Window?.ShutDown();
            foreach (var o in shell?.OwnedWindows.ToList() ?? new()) o.Close();
            shell?.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = old;
        }
    });
}

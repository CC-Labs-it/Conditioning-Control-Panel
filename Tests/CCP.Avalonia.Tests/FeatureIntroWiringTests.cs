using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>win-feature-intro: the shell's real doors open the one-shot cards (WPF
/// MainWindow.TabNavigation.cs MaybeShowFeatureIntro / OnDashboardTabVisibilityChanged), and a
/// startup modal or update dialog holds them unspent (WPF FeatureIntroPopup.xaml.cs:211).</summary>
public sealed class FeatureIntroWiringTests
{
    private static readonly FieldInfo UpdateOffered =
        typeof(AppUpdater).GetField("_offering", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly FieldInfo UpdaterBusy =
        typeof(AppUpdater).GetField("_busy", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Task Run(Action<List<Window>, MainShellWindow> body, params string[] unseen) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        var seen = CoreSettings.Current.SeenFeatureIntros;
        var was = seen.ToList();
        // Every card spent except the ones under test, so the Dashboard's launch cards stay out of the way.
        foreach (var k in FeatureIntros.All.Keys) if (!seen.Contains(k)) seen.Add(k);
        foreach (var k in unseen) seen.Remove(k);
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            body(opened, shell);
        }
        finally
        {
            foreach (var w in opened.Where(w => w != shell).ToArray()) w.Close();
            shell.Close();
            MainShellWindow.IsStartupDialogShowing = false;
            UpdateOffered.SetValue(null, false);
            UpdaterBusy.SetValue(null, false);
            seen.Clear();
            seen.AddRange(was);
            CoreSettings.Save();
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    // Lockdown is a page inside Play's zones with no pill (NavSections hidden): the Play wall card's
    // ShowTab is its way in since the rail entries left (cd426fe36).
    private static void ClickLockdown(MainShellWindow shell) => ClickNav(shell, null, () => shell.ShowTab("lockdown"));

    /// <summary>A strip pill: open its section first, then click the pill the user sees.</summary>
    private static void ClickPill(MainShellWindow shell, string section, string pill)
    {
        shell.OpenNavSection(section);
        Dispatcher.UIThread.RunJobs();
        ClickNav(shell, null, () => shell.PageStrip!.PillFor(pill)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
    }

    private static void ClickNav(MainShellWindow shell, string? button, Action? go = null)
    {
        // Each click is a fresh offer: forget the ladder's one-passive-at-a-time settle and the
        // card's pacing, so only the gate under test can refuse it.
        StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        if (go != null) go();
        else shell.Named<Button>(button!)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static string? CardTitle(List<Window> opened) =>
        opened.OfType<FeatureIntroPopup>().SingleOrDefault()?.FindControl<TextBlock>("TxtTitle")?.Text;

    [Fact]
    public Task LockdownDoorOpensItsCardOnceAndSpendsIt() => Run((opened, shell) =>
    {
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        ClickLockdown(shell);
        Assert.Equal(FeatureIntros.All["lockdown"].Title, CardTitle(opened));
        Assert.Contains("lockdown", CoreSettings.Current.SeenFeatureIntros);

        ClickLockdown(shell);   // past the pacing: the seen flag alone refuses
        Assert.Single(opened.OfType<FeatureIntroPopup>());
    }, "lockdown");

    [Fact]
    public Task StartupModalAndUpdateDialogHoldTheCardUnspent() => Run((opened, shell) =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        ClickLockdown(shell);
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
        Assert.True(StartupLadder.Inbox.Contains("intro:lockdown")); // ModalUp parks it for later

        MainShellWindow.IsStartupDialogShowing = false;
        UpdateOffered.SetValue(null, true);
        ClickLockdown(shell);
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);

        UpdateOffered.SetValue(null, false);
        ClickLockdown(shell);
        Assert.Equal(FeatureIntros.All["lockdown"].Title, CardTitle(opened));
    }, "lockdown");

    [Fact]
    public Task LaunchingOnTheDashboardQueuesDailyFreeAndHoldsOneAccount() => Run((opened, shell) =>
    {
        // The shell lands on the Dashboard with no ShowTab behind it: attach alone queued the cards.
        Assert.Equal(FeatureIntros.All["daily-free"].Title, CardTitle(opened));
        Assert.Contains("daily-free", CoreSettings.Current.SeenFeatureIntros);
        Assert.DoesNotContain("one-account", CoreSettings.Current.SeenFeatureIntros);
    }, "daily-free", "one-account");

    [Fact]
    public Task StudioDoorOpensStudioRackAndRemoteControlOpensNothing() => Run((opened, shell) =>
    {
        // WPF MainWindow.TabNavigation.cs:505: the rack card rides case "studio"; case
        // "remotecontrol" (a Social pill) calls nothing.
        ClickPill(shell, "social", "remotecontrol");
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("studio-rack", CoreSettings.Current.SeenFeatureIntros);

        ClickPill(shell, "studio", "studio");
        Assert.Equal(FeatureIntros.All["studio-rack"].Title, CardTitle(opened));
        Assert.Contains("studio-rack", CoreSettings.Current.SeenFeatureIntros);
    }, "studio-rack");

    [Fact]
    public Task ManualUpdateCheckWaitingOnTheNetworkDoesNotHoldTheCard() => Run((opened, shell) =>
    {
        // WPF App.xaml.cs:5492-5507: _isCheckingForUpdates is separate; IsUpdateDialogActive rises
        // only once a newer version is found, so a pending check neither refuses nor parks the card.
        UpdaterBusy.SetValue(null, true);
        ClickLockdown(shell);
        Assert.Equal(FeatureIntros.All["lockdown"].Title, CardTitle(opened));
        Assert.False(StartupLadder.Inbox.Contains("intro:lockdown"));
    }, "lockdown");

    [Fact]
    public Task ManuallyOpenedWhatsNewDoesNotHoldTheCard() => Run((opened, shell) =>
    {
        // WPF UpdatesSettingsSection.xaml.cs:74 opens What's New with a plain ShowDialog; only the
        // startup RunModal sets IsStartupDialogShowing, so a manual open never parks a card.
        var news = new ConditioningControlPanel.Avalonia.Views.Dialogs.WhatsNewDialog("Update", "Notes");
        news.Show(shell);
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        ClickLockdown(shell);
        Assert.Equal(FeatureIntros.All["lockdown"].Title, CardTitle(opened));
    }, "lockdown");

    [Fact]
    public Task LongLockdownCardKeepsItsDismissButtonReachable() => Run((opened, shell) =>
    {
        ClickLockdown(shell);
        var popup = opened.OfType<FeatureIntroPopup>().Single();
        var scroll = popup.FindControl<ScrollViewer>("CardScroll");
        Assert.NotNull(scroll);
        // The WPF card scrolls its five long bullets instead of clipping its only exit action.
        popup.SizeToContent = SizeToContent.Manual;
        popup.Height = 400; // a constrained work area still leaves every action reachable
        Dispatcher.UIThread.RunJobs();
        popup.UpdateLayout();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        scroll.ScrollToEnd();
        popup.UpdateLayout();
        Assert.True(scroll.Offset.Y > 0);
        var dismiss = popup.FindControl<Button>("BtnDismiss")!;
        var at = dismiss.TranslatePoint(new Point(0, 0), scroll)!.Value;
        Assert.InRange(at.Y, 0, scroll.Bounds.Height - dismiss.Bounds.Height);
    }, "lockdown");
}

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class InboxNoticeCardTests
{
    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();
        try { body(); }
        finally { InboxNoticeCard.CloseForPanic(); StartupLadder.ResetForTests(); }
    });
    private static Button FindButton(string name) => InboxNoticeCard.Current!.GetVisualDescendants()
        .OfType<Button>().Single(b => b.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void ReplacementEscapeAndPanicNeverRunActions() => Run(() =>
    {
        var calls = 0;
        void Open() => InboxNoticeCard.Present(null, "Title", " ", "Go", () => calls++, "Later", () => calls++);
        Open();
        var first = InboxNoticeCard.Current!;
        Open();
        Assert.False(first.IsVisible);
        Assert.DoesNotContain(InboxNoticeCard.Current!.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "Body");
        InboxNoticeCard.Current.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Null(InboxNoticeCard.Current);
        Open();
        PanicSurfaces.All.Single(s => s.Id == "inbox-notice").Stop(null);
        Assert.Null(InboxNoticeCard.Current);
        PanicSurfaces.All.Single(s => s.Id == "inbox-notice").Stop(null);
        Assert.Equal(0, calls);
    });

    [Fact]
    public void ActionsRunAfterCloseAndFailuresAreContained() => Run(() =>
    {
        var calls = 0;
        void Action() { Assert.Null(InboxNoticeCard.Current); calls++; throw new InvalidOperationException("probe"); }
        InboxNoticeCard.Present(null, "Title", "Body", "Go", Action);
        Click(FindButton("Primary"));
        Assert.Equal(1, calls);
        InboxNoticeCard.Present(null, "Title", "Body", null, null, "Later", Action);
        Assert.DoesNotContain(InboxNoticeCard.Current!.GetVisualDescendants().OfType<Button>(), b => b.Name == "Primary");
        Click(FindButton("Secondary"));
        Assert.Equal(2, calls);
        InboxNoticeCard.Present(null, "Title", "", null, null);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("vaultgate_close"), ((TextBlock)FindButton("Secondary").Content!).Text);
    });

    [Fact]
    public void RepeatTierRiseOpensNoticeFromRealInbox() => Run(() =>
    {
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        var saved = CoreSettings.Current.LastKnownEffectiveTier;
        var seen = CoreSettings.Current.SeenFeatureIntros.ToArray();
        MainShellWindow? shell = null;
        Window? host = null;
        try
        {
            CoreAccount.HasPremiumAccessProvider = () => true;
            CoreAccount.HasLabAccessProvider = () => true;
            CoreSettings.Current.LastKnownEffectiveTier = 0;
            CoreSettings.Current.SeenFeatureIntros.Add(TierCelebration.KeyT2);
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            shell = new MainShellWindow();
            shell.Show();
            shell.Activate();   // the user path: Opened (celebration re-check) and the Activated focus hook
            shell.Activate();
            Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "tier-on:2");
            var flyout = new InboxFlyout();
            host = new Window { Content = flyout };
            host.Show();
            Click(flyout.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("open")));
            Assert.Empty(StartupLadder.Inbox.Items);
            var title = InboxNoticeCard.Current!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "Heading");
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("tier_on_prime"), title.Text);
            Assert.Equal(2, CoreSettings.Current.LastKnownEffectiveTier);
        }
        finally
        {
            host?.Close(); shell?.Close();
            CoreAccount.HasPremiumAccessProvider = oldPremium; CoreAccount.HasLabAccessProvider = oldLab;
            CoreSettings.Current.LastKnownEffectiveTier = saved;
            CoreSettings.Current.SeenFeatureIntros.Clear(); CoreSettings.Current.SeenFeatureIntros.AddRange(seen);
        }
    });

    [Fact]
    public void InviteWeekIsNotARiseOnFocus() => Run(() =>
    {
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        var s = CoreSettings.Current;
        var saved = s.LastKnownEffectiveTier;
        var grant = s.InviteGrantUntil;
        var cached = s.PatreonPremiumValidUntil;
        var seen = s.SeenFeatureIntros.ToArray();
        MainShellWindow? shell = null;
        try
        {
            CoreAccount.HasPremiumAccessProvider = () => true;
            CoreAccount.HasLabAccessProvider = () => false;
            s.InviteGrantUntil = DateTime.UtcNow.AddDays(5);
            s.PatreonPremiumValidUntil = null;
            s.LastKnownEffectiveTier = 0;
            s.SeenFeatureIntros.Add(TierCelebration.KeyT1);
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            shell = new MainShellWindow();
            shell.Show();
            shell.Activate();
            Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key.StartsWith("tier-on:"));
            Assert.Equal(0, s.LastKnownEffectiveTier); // a later real purchase still gets its card
        }
        finally
        {
            shell?.Close();
            CoreAccount.HasPremiumAccessProvider = oldPremium; CoreAccount.HasLabAccessProvider = oldLab;
            s.LastKnownEffectiveTier = saved; s.InviteGrantUntil = grant; s.PatreonPremiumValidUntil = cached;
            s.SeenFeatureIntros.Clear(); s.SeenFeatureIntros.AddRange(seen);
        }
    });

    [Fact]
    public void FirstTierReadingStaysInMemoryAndAChangeSaves() => Run(() =>
    {
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        var oldSettings = CoreSettings.ServiceProvider;
        MainShellWindow? shell = null;
        Window? other = null;
        try
        {
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreAccount.HasLabAccessProvider = () => false;
            var service = new SettingsService(); // real settings.json in the CCP_USERDATA_DIR sandbox
            CoreSettings.ServiceProvider = () => service;
            shell = new MainShellWindow();
            shell.Show();
            other = new Window();
            other.Show();
            void Refocus()
            {
                SettingsService.DisarmAllPendingSaves(); // only this focus pass may arm a save
                other.Activate();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                shell.Activate(); // the user path: the Activated focus hook
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
            service.Current.LastKnownEffectiveTier = null;
            Refocus();
            Assert.Equal(0, service.Current.LastKnownEffectiveTier);
            Assert.False(service.HasPendingSave); // a first reading must not write settings.json
            CoreAccount.HasPremiumAccessProvider = () => true; // a known rise 0 -> 2 is saved at once
            CoreAccount.HasLabAccessProvider = () => true;
            service.Current.SeenFeatureIntros.Add(TierCelebration.KeyT2);
            Refocus();
            Assert.Equal(2, service.Current.LastKnownEffectiveTier);
            Assert.True(service.HasPendingSave);
        }
        finally
        {
            other?.Close(); shell?.Close();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.HasPremiumAccessProvider = oldPremium; CoreAccount.HasLabAccessProvider = oldLab;
        }
    });
}

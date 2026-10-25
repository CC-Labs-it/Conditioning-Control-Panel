using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessCompanionHostingTests
{
    private static void InShell(Action<MainShellWindow, CompanionRoomView, AwarenessTabView, AwarenessPrivacyView> check)
        => AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().WithInterFont().With(AppFonts.Options())
                    .ConfigureFonts(AppFonts.Register).UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            try
            {
                var room = shell.Named<CompanionTabView>("CompanionTab")!.Room;
                var tab = shell.Named<AwarenessTabView>("AwarenessTab")!;
                var privacy = room.FindControl<AwarenessPrivacyView>("AwarenessZone")!;
                shell.Show();
                shell.ShowTab("awareness");
                Dispatcher.UIThread.RunJobs();
                check(shell, room, tab, privacy);
            }
            finally { shell.RequestExit(); Dispatcher.UIThread.RunJobs(); }
        });

    [Fact]
    public void HostsTheOriginalZonesOnceAndSyncsWithoutWriting() => InShell((shell, room, tab, privacy) =>
    {
        Assert.Same(privacy, tab.FindControl<ContentControl>("CompanionPrivacyHost")!.Content);
        Assert.Same(room.WorkshopParts!.Awareness, tab.FindControl<ContentControl>("CompanionTuningHost")!.Content);
        Assert.Single(shell.GetLogicalDescendants().OfType<AwarenessPrivacyView>());
        var fold = tab.FindControl<Expander>("CompanionTuningFold")!;
        Assert.True(fold.IsVisible);
        Assert.False(fold.IsExpanded);
        Assert.DoesNotContain(((WorkshopRuntimeVm)room.FindControl<WorkshopAccordion>("WorkshopZone")!.DataContext!).Cells,
            c => c.Key == CompanionRoomAnchors.WorkshopAwarenessCell);
        var old = CoreSettings.Current.AwarenessRetentionDays;
        try
        {
            CoreSettings.Current.AwarenessRetentionDays = 42;
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("awareness");
            Assert.Equal(42, privacy.ViewModel!.RetentionDays);
            Assert.Equal(42, CoreSettings.Current.AwarenessRetentionDays);
            Assert.Same(privacy, tab.FindControl<ContentControl>("CompanionPrivacyHost")!.Content);
        }
        finally { CoreSettings.Current.AwarenessRetentionDays = old; }
    });

    [Fact]
    public void FineTuningLinkAndRoomAnchorRevealTheHostedDrawer() => InShell((shell, room, tab, privacy) =>
    {
        var fold = tab.FindControl<Expander>("CompanionTuningFold")!;
        privacy.ViewModel!.FineTuningCommand.Execute(null);
        Assert.True(fold.IsExpanded);
        fold.IsExpanded = false;
        shell.ShowTab("companion");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(shell, TopLevel.GetTopLevel(room));
        Assert.True(ConditioningControlPanel.Services.Companion.CompanionExperience.IsV2Enabled);
        room.RevealWorkshop(CompanionRoomAnchors.WorkshopAwarenessCell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("awareness", shell.CurrentTab);
        Assert.True(fold.IsExpanded);
        Assert.Same(room.WorkshopParts!.Awareness, tab.FindControl<ContentControl>("CompanionTuningHost")!.Content);
    });

    [Fact]
    public void HeroAwarenessLinkNavigatesToTheMovedCard() => InShell((shell, room, tab, privacy) =>
    {
        shell.ShowTab("companion");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(shell, TopLevel.GetTopLevel(room));
        room.FindControl<CompanionHeroCard>("HeroZone")!.ViewModel!.FocusAwarenessCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("awareness", shell.CurrentTab);
        Assert.True(privacy.IsEffectivelyVisible);
    });

    [Fact]
    public void PanicStopsHostedClocksAndDisarmsWipe() => InShell((shell, room, tab, privacy) =>
    {
        privacy.WipeConfirm.ArmCommand.Execute(null);
        Assert.True(privacy.WipeConfirm.IsArmed);
        Assert.True(privacy.IsRefreshing);
        PanicSurfaces.All.Single(s => s.Id == "awareness-companion").Stop(shell);
        Assert.False(privacy.IsRefreshing);
        Assert.False(privacy.WipeConfirm.IsArmed);
    });

    [Fact]
    public void OnlyTheAwarenessTabOwnsTheMovedCardsRefreshClock() => InShell((shell, room, tab, privacy) =>
    {
        Assert.True(privacy.IsRefreshing);
        shell.ShowTab("companion");
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.False(privacy.IsRefreshing);
        shell.ShowTab("awareness");
        Dispatcher.UIThread.RunJobs();
        Assert.True(privacy.IsRefreshing);
        shell.ShowTab("settings");
        Dispatcher.UIThread.RunJobs();
        Assert.False(privacy.IsRefreshing);
    });
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class SpiralMigrationGateTests
{
    private static void Run(Action<SettingsService, SpiralTabView, Window> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldFuse = AvApp.DescentCountdown;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        AvApp.DescentCountdown = null; // a persisted unanswered offer must withhold even when the fuse is dark
        var s = service.Current;
        s.DescentMigrationCompleted = false;
        s.PendingDescentMigrationChoice = null;
        s.DescentMigrationOffered = false;
        var view = new SpiralTabView();
        var window = new Window { Content = view, Width = 800, Height = 650 };
        try { body(service, view, window); }
        finally { window.Close(); CoreSettings.ServiceProvider = oldProvider; AvApp.DescentCountdown = oldFuse; }
    });

    private static void Fog(SpiralTabView view, bool expected)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected, view.FindControl<Grid>("FogHost")!.IsVisible);
        Assert.Equal(!expected, view.FindControl<Border>("WaitingPanel")!.IsVisible);
    }

    [Fact]
    public void PersistedOffer_WithDarkFuse_RemainsFogUntilAValidAnswer() => Run((service, view, window) =>
    {
        var s = service.Current;
        s.DescentMigrationOffered = true;
        window.Show();
        Fog(view, true);
        s.PendingDescentMigrationChoice = "invalid";
        Fog(view, true);
        s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;
        Fog(view, false);
        s.PendingDescentMigrationChoice = null;
        Fog(view, true);
        s.DescentMigrationCompleted = true;
        Fog(view, false);
        s.DescentMigrationCompleted = false;
        s.DescentMigrationOffered = false;
        Fog(view, false);
    });

    [Fact]
    public void CloudRestore_RebindsToTheReplacementSettingsInstance() => Run((service, view, window) =>
    {
        var old = service.Current;
        old.DescentMigrationOffered = true;
        window.Show();
        Fog(view, true);
        service.RestoreFrom(new AppSettings { DescentMigrationOffered = false });
        Fog(view, false);
        old.DescentMigrationOffered = true;
        Fog(view, false);
        service.Current.DescentMigrationOffered = true;
        Fog(view, true);
    });

    [Fact]
    public void HiddenAndDetachedRoom_StopsListening_AndRepaintsOnReturn() => Run((service, view, window) =>
    {
        window.Show();
        Fog(view, false);
        view.IsVisible = false;
        service.Current.DescentMigrationOffered = true;
        Fog(view, false); // hidden tab keeps no settings subscription
        view.IsVisible = true;
        Fog(view, true);
        window.Content = null;
        service.Current.DescentMigrationOffered = false;
        Fog(view, true); // a queued update cannot restart a detached room
        window.Content = view;
        Fog(view, false);
        service.Current.DescentMigrationOffered = true; // queue a visible-room update, then detach before it runs
        window.Content = null;
        Fog(view, false);
        window.Content = view;
        Fog(view, true);
    });
}

using System;
using System.ComponentModel;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SpiralTabView
{
    private AppSettings? _migrationSettings;
    private SettingsService? _migrationSettingsService;

    // WPF re-raises DescentService.BlockChanged when migration gates change. This slice observes
    // the already-persisted gates directly until the live offer/ceremony service is ported.
    private void WireMigrationGate()
    {
        var service = CoreSettings.Service;
        if (!ReferenceEquals(service, _migrationSettingsService))
        {
            if (_migrationSettingsService != null) _migrationSettingsService.CurrentReplaced -= OnMigrationSettingsReplaced;
            _migrationSettingsService = service;
            if (service != null) service.CurrentReplaced += OnMigrationSettingsReplaced;
        }
        var settings = CoreSettings.Current;
        if (ReferenceEquals(settings, _migrationSettings)) return;
        if (_migrationSettings != null) _migrationSettings.PropertyChanged -= OnMigrationSettingsChanged;
        _migrationSettings = settings;
        settings.PropertyChanged += OnMigrationSettingsChanged;
    }

    private void UnwireMigrationGate()
    {
        if (_migrationSettings != null) _migrationSettings.PropertyChanged -= OnMigrationSettingsChanged;
        _migrationSettings = null;
        if (_migrationSettingsService != null) _migrationSettingsService.CurrentReplaced -= OnMigrationSettingsReplaced;
        _migrationSettingsService = null;
    }

    private void OnMigrationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(AppSettings.DescentMigrationOffered)
            or nameof(AppSettings.DescentMigrationCompleted) or nameof(AppSettings.PendingDescentMigrationChoice))
            QueueMigrationRefresh();
    }

    private void OnMigrationSettingsReplaced() => QueueMigrationRefresh();

    private void QueueMigrationRefresh() => Dispatcher.UIThread.Post(() =>
    {
        // A queued cloud-restore/ack callback must not re-arm a room that was hidden or detached.
        if (_migrationSettings != null && IsVisible) Refresh();
    });
}

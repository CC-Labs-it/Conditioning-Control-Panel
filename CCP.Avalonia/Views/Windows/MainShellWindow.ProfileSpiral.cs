// WPF spec: ConditioningControlPanel/MainWindow/MainWindow.ProfileSpiral.cs:70-113,
// 122-162, 182-213. Native profile doors over the shared server block and migration gate.
using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private DescentService? _profileSpiralDescent;
    private SettingsService? _profileSpiralSettingsService;
    private AppSettings? _profileSpiralSettings;

    private void WireProfileSpiral()
    {
        if (_windowClosed) return;
        if (!ReferenceEquals(_profileSpiralDescent, App.Descent))
        {
            if (_profileSpiralDescent != null) _profileSpiralDescent.BlockChanged -= OnSpiralBlockChanged;
            _profileSpiralDescent = App.Descent;
            if (_profileSpiralDescent != null) _profileSpiralDescent.BlockChanged += OnSpiralBlockChanged;
        }
        var service = CoreSettings.Service;
        if (!ReferenceEquals(service, _profileSpiralSettingsService))
        {
            if (_profileSpiralSettingsService != null) _profileSpiralSettingsService.CurrentReplaced -= OnSpiralSettingsReplaced;
            _profileSpiralSettingsService = service;
            if (service != null) service.CurrentReplaced += OnSpiralSettingsReplaced;
        }
        var settings = CoreSettings.Current;
        if (ReferenceEquals(settings, _profileSpiralSettings)) return;
        if (_profileSpiralSettings != null) _profileSpiralSettings.PropertyChanged -= OnSpiralSettingsChanged;
        _profileSpiralSettings = settings;
        settings.PropertyChanged += OnSpiralSettingsChanged;
    }

    private void UnwireProfileSpiral()
    {
        if (_profileSpiralDescent != null) _profileSpiralDescent.BlockChanged -= OnSpiralBlockChanged;
        if (_profileSpiralSettingsService != null) _profileSpiralSettingsService.CurrentReplaced -= OnSpiralSettingsReplaced;
        if (_profileSpiralSettings != null) _profileSpiralSettings.PropertyChanged -= OnSpiralSettingsChanged;
        _profileSpiralDescent = null;
        _profileSpiralSettingsService = null;
        _profileSpiralSettings = null;
    }

    private void OnSpiralSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(AppSettings.DescentMigrationOffered)
            or nameof(AppSettings.DescentMigrationCompleted) or nameof(AppSettings.PendingDescentMigrationChoice)
            or nameof(AppSettings.MotionLevel) or nameof(AppSettings.PerformanceMode) or nameof(AppSettings.GentleVisuals))
            OnSpiralSettingsReplaced();
    }

    private void OnSpiralSettingsReplaced() => Dispatcher.UIThread.Post(() =>
    {
        if (_windowClosed) return;
        RefreshProfileSpiralPlate();
        RefreshProfileMenuSpiral();
        RefreshSpiralGlyphMotion();
    });

    private void OnSpiralBlockChanged(object? sender, EventArgs e)
    {
        if (_windowClosed) return;
        RefreshProfileSpiralPlate();
        RefreshProfileMenuSpiral();
    }

    // Same persisted gate as the room. This head handles offers through the silent sync ack;
    // there is no live ceremony (WPF DescentMigrationService.cs:66, :202-226).
    private static bool SpiralWithheld => SpiralRoom.SpiralWithheldFor(CoreSettings.Current, false, false);

    internal void RefreshProfileSpiralPlate()
    {
        if (_windowClosed) return;
        WireProfileSpiral();
        if (ProfilePage?.FindControl<Border>("ProfileSpiralPlate") is not { } plate) return;
        var block = App.Descent?.Current;
        plate.IsVisible = block != null && _profileViewingSelf && !SpiralWithheld;
        if (plate.IsVisible) ProfilePage?.FindControl<SpiralGlyph>("ProfileSpiralGlyph")?.Apply(block);
    }

    internal void RefreshProfileMenuSpiral()
    {
        if (_windowClosed) return;
        WireProfileSpiral();
        if (Named<Button>("ProfileMenuSpiralRow") is not { } row) return;
        var block = App.Descent?.Current;
        row.IsVisible = block != null && !SpiralWithheld;
        if (!row.IsVisible || block == null) return;
        Named<SpiralGlyph>("ProfileMenuSpiralGlyph")?.Apply(block);
        if (Named<TextBlock>("ProfileMenuSpiralSummary") is { } summary)
            summary.Text = string.Format(Loc.Get("programs_card_day"), block.DevotionDays)
                + " · " + DescentStageCopy.Name(block.Stage?.N ?? 0);
    }

    internal void RefreshSpiralGlyphMotion()
    {
        ProfilePage?.FindControl<SpiralGlyph>("ProfileSpiralGlyph")?.RefreshMotion();
        Named<SpiralGlyph>("ProfileMenuSpiralGlyph")?.RefreshMotion();
    }

    internal void OpenSpiralMapFromProfile() => ShowTab(SpiralRoom.TabKey);

    private void ProfileMenuSpiral_Click(object? sender, RoutedEventArgs e)
    {
        CloseProfileBubbleMenu();
        OpenSpiralMapFromProfile();
    }
}

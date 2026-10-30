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
    private DescentService? _profileSpiralService;
    private SettingsService? _profileSpiralSettingsService;
    private AppSettings? _profileSpiralSettings;
    private bool _profileSpiralClosed;

    // WPF MainWindow.ProfileSpiral.cs:70-98, ProfileBubble.cs:100,130.
    private void InitializeProfileSpiral()
    {
        _profileSpiralService = App.Descent;
        if (_profileSpiralService != null) _profileSpiralService.BlockChanged += OnSpiralBlockChanged;
        _profileSpiralSettingsService = CoreSettings.Service;
        if (_profileSpiralSettingsService != null)
            _profileSpiralSettingsService.CurrentReplaced += OnSpiralSettingsReplaced;
        BindSpiralSettings();
        Closed += (_, _) =>
        {
            _profileSpiralClosed = true;
            if (_profileSpiralService != null) _profileSpiralService.BlockChanged -= OnSpiralBlockChanged;
            if (_profileSpiralSettingsService != null)
                _profileSpiralSettingsService.CurrentReplaced -= OnSpiralSettingsReplaced;
            if (_profileSpiralSettings != null) _profileSpiralSettings.PropertyChanged -= OnSpiralSettingsChanged;
        };
        RefreshProfileSpiralPlate();
        RefreshProfileMenuSpiral();
    }

    private void BindSpiralSettings()
    {
        if (_profileSpiralSettings != null) _profileSpiralSettings.PropertyChanged -= OnSpiralSettingsChanged;
        _profileSpiralSettings = CoreSettings.Current;
        _profileSpiralSettings.PropertyChanged += OnSpiralSettingsChanged;
    }

    private void OnSpiralSettingsReplaced()
    {
        BindSpiralSettings();
        OnSpiralBlockChanged(null, EventArgs.Empty);
    }

    private void OnSpiralSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(AppSettings.DescentMigrationOffered)
            or nameof(AppSettings.DescentMigrationCompleted) or nameof(AppSettings.PendingDescentMigrationChoice))
            OnSpiralBlockChanged(sender, EventArgs.Empty);
    }

    private void OnSpiralBlockChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_profileSpiralClosed) return;
        RefreshProfileSpiralPlate();
        RefreshProfileMenuSpiral();
    });

    // Same persisted withhold reader as SpiralTabView. WPF ProfileSpiral.cs:61,128,156.
    private static bool ProfileSpiralWithheld => SpiralRoom.SpiralWithheldFor(CoreSettings.Current, false, false);

    // WPF MainWindow.ProfileSpiral.cs:122-137. Never put our block on a searched card.
    internal void RefreshProfileSpiralPlate()
    {
        if (ProfilePage?.FindControl<Border>("ProfileSpiralPlate") is not { } plate) return;
        var block = _profileSpiralService?.Current;
        plate.IsVisible = block != null && _profileViewingSelf && !ProfileSpiralWithheld;
        if (plate.IsVisible) ProfilePage.FindControl<SpiralGlyph>("ProfileSpiralGlyph")?.Apply(block);
    }

    // WPF MainWindow.ProfileSpiral.cs:148-185. The rail kill switch does not gate native glyphs.
    internal void RefreshProfileMenuSpiral()
    {
        if (Named<Button>("ProfileMenuSpiralRow") is not { } row) return;
        var block = _profileSpiralService?.Current;
        row.IsVisible = block != null && !ProfileSpiralWithheld;
        if (!row.IsVisible || block == null) return;
        Named<SpiralGlyph>("ProfileMenuSpiralGlyph")?.Apply(block);
        if (Named<TextBlock>("ProfileMenuSpiralSummary") is { } summary)
            summary.Text = string.Format(Loc.Get("programs_card_day"), block.DevotionDays)
                + " · " + DescentStageCopy.Name(block.Stage?.N ?? 0);
    }

    // WPF MainWindow.ProfileSpiral.cs:204-214. The room rechecks gates on a stale click.
    internal void OpenSpiralMapFromProfile() => ShowTab(SpiralRoom.TabKey);

    private void ProfileMenuSpiral_Click(object? sender, RoutedEventArgs e)
    {
        CloseProfileBubbleMenu();
        OpenSpiralMapFromProfile();
    }
}

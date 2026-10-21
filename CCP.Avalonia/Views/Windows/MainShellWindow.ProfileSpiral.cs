using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private DescentService? _profileDescent;
    private string? _profileDescentAccount;
    private bool _profileSpiralClosed;
    internal DescentService? ProfileDescent => _profileDescent;

    private void InitializeProfileSpiral()
    {
        // Shared WPF reader: read-only, ten-second floor, stale-account fence, deferred retry.
        _profileDescent = new DescentService(() => CoreSettings.Current, AccountSeed.NewV2());
        _profileDescent.BlockChanged += OnSpiralBlockChanged;
        AccountSeed.ProfileLoaded += OnSpiralProfileLoaded;
        // WPF's visible Trainer Card poll only runs after this account has carried a block.
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        poll.Tick += (_, _) =>
        {
            if (_profileDescent.HasSeenBlock && ProfilePage?.IsEffectivelyVisible == true)
                _profileDescent.RequestRefresh("trainer card poll");
        };
        Opened += (_, _) => poll.Start();
        Closed += (_, _) =>
        {
            _profileSpiralClosed = true;
            poll.Stop();
            AccountSeed.ProfileLoaded -= OnSpiralProfileLoaded;
            _profileDescent.BlockChanged -= OnSpiralBlockChanged;
            _profileDescent.Reset();
        };
    }

    private void OnSpiralProfileLoaded() => Dispatcher.UIThread.Post(() =>
    {
        if (!_profileSpiralClosed) RefreshProfileSpiralAccount();
    });

    private void OnSpiralBlockChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_profileSpiralClosed) return;
        RefreshProfileSpiralPlate();
        RefreshProfileMenuSpiral();
    });

    private void RefreshProfileSpiralAccount()
    {
        if (_profileDescent is null || _profileSpiralClosed) return;
        var s = CoreSettings.Current;
        var account = CoreAccount.IsLoggedIn && !s.OfflineMode ? s.UnifiedId : null;
        if (!string.Equals(account, _profileDescentAccount, StringComparison.Ordinal))
        {
            _profileDescentAccount = account;
            _profileDescent.Reset();
        }
        RefreshProfileSpiralPlate();
        RefreshProfileMenuSpiral();
        if (account is not null) _profileDescent.OnSignedIn();
    }

    internal void RefreshProfileSpiralPlate()
    {
        var block = _profileDescent?.Current;
        if (ProfilePage?.FindControl<Border>("ProfileSpiralPlate") is not { } plate) return;
        plate.IsVisible = ProfileSpiral.IsVisible(block,
            ProfileSpiral.IsWithheld(CoreSettings.Current), _profileViewingSelf);
        ProfilePage.FindControl<SpiralGlyph>("ProfileSpiralGlyph")?.Apply(plate.IsVisible ? block : null);
    }

    internal void RefreshProfileMenuSpiral()
    {
        var block = _profileDescent?.Current;
        if (Named<Button>("ProfileMenuSpiralRow") is not { } row) return;
        row.IsVisible = ProfileSpiral.IsVisible(block, ProfileSpiral.IsWithheld(CoreSettings.Current));
        Named<SpiralGlyph>("ProfileMenuSpiralGlyph")?.Apply(row.IsVisible ? block : null);
        if (Named<TextBlock>("ProfileMenuSpiralSummary") is { } summary)
            summary.Text = row.IsVisible && block is not null ? ProfileSpiral.Summary(block) : "";
    }

    internal void OpenSpiralMapFromProfile() => ShowTab(SpiralRoom.TabKey);

    private void ProfileMenuSpiral_Click(object? sender, RoutedEventArgs e)
    {
        CloseProfileBubbleMenu();
        OpenSpiralMapFromProfile();
    }
}

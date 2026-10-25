using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Descent;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private DescentService? _profileDescent;
    private string? _profileDescentAccount;
    private bool _profileSpiralClosed;
    private DispatcherTimer? _profileSpiralPoll;
    internal bool ProfileSpiralPollRunning => _profileSpiralPoll?.IsEnabled == true;

    private bool ProfileSpiralPollWanted => !_profileSpiralClosed
        && _profileDescent?.HasSeenBlock == true
        && ProfilePage?.IsEffectivelyVisible == true
        && VatGlassCanvas.WindowIsPresenting(this);

    internal void EvaluateProfileSpiralPoll()
    {
        if (_profileSpiralPoll is null) return;
        if (ProfileSpiralPollWanted) _profileSpiralPoll.Start(); else _profileSpiralPoll.Stop();
    }
    internal DescentService? ProfileDescent => _profileDescent;

    private void InitializeProfileSpiral()
    {
        // Shared WPF reader: read-only, ten-second floor, stale-account fence, deferred retry.
        _profileDescent = new DescentService(() => CoreSettings.Current, AccountSeed.NewV2());
        _profileDescent.AdoptProgression = AdoptSpiralProgression;
        _profileDescent.BlockChanged += OnSpiralBlockChanged;
        if (AccountSeed.Sync is { } sync)
        {
            sync.DescentReader = _profileDescent;
            sync.MigrationResponse = OnMigrationResponse;
        }
        Named<SpiralTabView>("SpiralTab")?.SetProfileReader(_profileDescent);
        AccountSeed.ProfileLoaded += OnSpiralProfileLoaded;
        // WPF's Trainer Card poll (MainWindow.ProfileVat.cs:133-170): runs only while the card is on
        // screen, the window is presenting (not minimised/hidden/inactive) and this account has
        // carried a block. Re-evaluated on window state, tab change and block change.
        _profileSpiralPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(60) };
        _profileSpiralPoll.Tick += (_, _) =>
        {
            if (!ProfileSpiralPollWanted) { _profileSpiralPoll.Stop(); return; }
            _profileDescent.RequestRefresh("trainer card poll");
        };
        Activated += (_, _) => EvaluateProfileSpiralPoll();
        Deactivated += (_, _) => EvaluateProfileSpiralPoll();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) EvaluateProfileSpiralPoll();
        };
        Closed += (_, _) =>
        {
            _profileSpiralClosed = true;
            _profileSpiralPoll.Stop();
            AccountSeed.ProfileLoaded -= OnSpiralProfileLoaded;
            _profileDescent.BlockChanged -= OnSpiralBlockChanged;
            _profileDescent.AdoptProgression = null;
            if (AccountSeed.Sync?.DescentReader == _profileDescent)
            {
                AccountSeed.Sync.DescentReader = null;
                AccountSeed.Sync.MigrationResponse = null;
            }
            _profileDescent.Reset();
        };
    }

    private void OnSpiralProfileLoaded() => Dispatcher.UIThread.Post(() =>
    {
        if (_profileSpiralClosed) return;
        RefreshProfileSpiralAccount();
        // WPF MainWindow.xaml.cs:724: profile load asks again, floored, so a same-account block lands.
        _profileDescent?.RequestRefresh("profile loaded");
    });

    /// <summary>WPF MainWindow.ProfileVat.cs:111, the one ungated ask: opening the card lets a dark key light up.</summary>
    internal void OnProfileSpiralCardShown()
    {
        if (!_profileSpiralClosed) _profileDescent?.RequestRefresh("trainer card open");
    }

    private void OnSpiralBlockChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_profileSpiralClosed) return;
        RefreshProfileSpiralPlate();
        RefreshProfileMenuSpiral();
        Named<SpiralTabView>("SpiralTab")?.RefreshFromProfile();
        EvaluateProfileSpiralPoll();
    });

    private void OnMigrationResponse(ConditioningControlPanel.Models.AppSettings settings, JObject reply)
    {
        var account = settings.UnifiedId;
        var token = settings.AuthToken;
        Dispatcher.UIThread.Post(() =>
        {
            if (_profileSpiralClosed || settings != CoreSettings.Current || account != settings.UnifiedId
                || token != settings.AuthToken || !CoreAccount.IsLoggedIn) return;
            try
            {
                if (DescentAutoRestore.Apply(settings, reply, _profileDescent?.Current, DateTime.UtcNow))
                {
                    CoreSettings.Save();
                    AccountSeed.Sync?.Nudge("descent silent restore"); // cooldown-safe; pending choice survives failure
                }
                if (DescentAutoRestore.Drip(settings, DateTime.Now) is { } stage)
                {
                    CoreSettings.Save();
                    Views.AvatarTube.AvatarTubeWindow.Live?.GigglePriority(
                        $"Stage {DescentStageCopy.RomanNumeral(stage)}. You walked this once already. Walk it again with me.",
                        playSound: false, aiGenerated: false);
                }
                _profileDescent?.NotifySurfaces("migration response");
                UpdateQuickLoginUI();
            }
            catch (Exception ex) { Serilog.Log.Debug("[Descent] Migration response could not apply ({Kind})", ex.GetType().Name); }
        });
    }

    private void AdoptSpiralProgression(JObject user)
    {
        // Reader callbacks arrive on the pool. Fence the posted adopt against logout/account switch.
        var settings = CoreSettings.Current;
        var account = settings.UnifiedId;
        var token = settings.AuthToken;
        Dispatcher.UIThread.Post(() =>
        {
            if (_profileSpiralClosed || settings != CoreSettings.Current
                || account != settings.UnifiedId || token != settings.AuthToken) return;
            if (!ProfileAdopt.AdoptProfilePoll(settings, user)) return;
            CoreSettings.Save();
            UpdateQuickLoginUI();   // repaints the profile bubble too
        });
    }

    private void RefreshProfileSpiralAccount()
    {
        if (_profileDescent is null || _profileSpiralClosed) return;
        var s = CoreSettings.Current;
        var account = CoreAccount.IsLoggedIn && !s.OfflineMode ? s.UnifiedId : null;
        if (!string.Equals(account, _profileDescentAccount, StringComparison.Ordinal))
        {
            _profileDescentAccount = account;
            _profileDescent.Reset();
            // WPF asks unconditionally only on sign-in/profile load; repaints read the cache.
            if (account is not null) _profileDescent.OnSignedIn();
        }
        RefreshProfileSpiralPlate();
        RefreshProfileMenuSpiral();
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

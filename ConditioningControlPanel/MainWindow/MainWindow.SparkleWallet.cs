using System;
using System.ComponentModel;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel;

public partial class MainWindow
{
    private AppSettings? _sparkleWalletSettings;
    private bool _sparkleWalletActive;

    private void InitializeSparkleWallet()
    {
        _sparkleWalletActive = true;
        App.Settings.CurrentReplaced += ReplaceSparkleWalletSettings;
        SparklePointRewards.Awarded += OnSparklePointAwarded;
        HeaderSparkleWallet.VisitRequested += VisitFromSparkleWallet;
        HeaderSparkleWallet.CounterRequested += OpenCounterFromSparkleWallet;
        Closed += (_, _) => ShutdownSparkleWallet();
        Deactivated += (_, _) => HeaderSparkleWallet.ClosePopup();
        if (App.ProfileSync != null) App.ProfileSync.ProfileLoaded += OnSparkleWalletProfileLoaded;
        ReplaceSparkleWalletSettings();
        // A launcher-first boot can load the profile before this window exists: read once now too
        // (no-op signed out or offline; the 10-minute guard keeps ProfileLoaded from doubling it).
        Services.PrizeCounter.PrizeCounterHostService.RefreshWalletInBackground();
    }

    /// <summary>The profile landed: read the counter's wallet once (raise-only), so the header is not
    /// 0 until the counter opens (fix wave F1).</summary>
    private void OnSparkleWalletProfileLoaded(object? sender, EventArgs e)
    {
        if (_sparkleWalletActive) Services.PrizeCounter.PrizeCounterHostService.RefreshWalletInBackground();
    }

    private void ReplaceSparkleWalletSettings()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ReplaceSparkleWalletSettings)); return; }
        if (!_sparkleWalletActive) return;
        if (_sparkleWalletSettings != null) _sparkleWalletSettings.PropertyChanged -= OnSparkleWalletSettingChanged;
        _sparkleWalletSettings = App.Settings.Current;
        _sparkleWalletSettings.PropertyChanged += OnSparkleWalletSettingChanged;
        RefreshSparkleWalletBalance();
    }

    private void OnSparkleWalletSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.SkillPoints) || string.IsNullOrEmpty(e.PropertyName))
            RefreshSparkleWalletBalance();
        if (e.PropertyName == nameof(AppSettings.SkillPoints))
            QueueSkillTreeBalanceRefresh();
    }

    private bool _skillTreeBalanceQueued;

    /// <summary>
    /// The skill tree drew its balance (and which nodes read affordable) only when the tab was
    /// shown, so a point earned while it was open left it one behind the wallet (#1269). Coalesced
    /// at Background priority because the tree is the most expensive redraw in the app. Skipped
    /// when the tree already shows the balance: a purchase redraws it itself, and a second rebuild
    /// would cut the purchase FX short.
    /// </summary>
    private void QueueSkillTreeBalanceRefresh()
    {
        if (_skillTreeBalanceQueued) return;
        _skillTreeBalanceQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _skillTreeBalanceQueued = false;
            if (!_sparkleWalletActive || EnhancementsTab?.IsVisible != true) return;
            var shown = App.Settings.Current.SkillPoints.ToString("N0");
            if (EnhancementsTab.TxtSkillPoints.Text != shown) RefreshEnhancementsUI();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RefreshSparkleWalletBalance()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RefreshSparkleWalletBalance)); return; }
        if (_sparkleWalletActive) HeaderSparkleWallet.SetBalance(App.Settings.Current.SkillPoints);
    }

    private void OnSparklePointAwarded(object? sender, SparklePointAward award)
    {
        var creditedSettings = App.Settings.Current;
        void Present()
        {
            if (!_sparkleWalletActive || !ReferenceEquals(App.Settings.Current, creditedSettings)) return;
            // Read the current ledger: another purchase or award may have occurred while queued.
            RefreshSparkleWalletBalance();
            if (IsVisible && WindowState != WindowState.Minimized) HeaderSparkleWallet.ShowReward(award.Amount);
        }
        if (Dispatcher.CheckAccess()) Present();
        else Dispatcher.BeginInvoke(new Action(Present));
    }

    private void VisitFromSparkleWallet(object? sender, EventArgs e) => BtnStartBackRoom_Click(this, new RoutedEventArgs());

    /// <summary>The wallet pill opens the Prize Counter (7.2 shop); the Back Room keeps its own doors.</summary>
    private void OpenCounterFromSparkleWallet(object? sender, EventArgs e) => Services.PrizeCounter.PrizeCounterHostService.Launch(from: "wallet");

    private void ShutdownSparkleWallet()
    {
        _sparkleWalletActive = false;
        App.Settings.CurrentReplaced -= ReplaceSparkleWalletSettings;
        SparklePointRewards.Awarded -= OnSparklePointAwarded;
        if (App.ProfileSync != null) App.ProfileSync.ProfileLoaded -= OnSparkleWalletProfileLoaded;
        if (_sparkleWalletSettings != null) _sparkleWalletSettings.PropertyChanged -= OnSparkleWalletSettingChanged;
        _sparkleWalletSettings = null;
        HeaderSparkleWallet.VisitRequested -= VisitFromSparkleWallet;
        HeaderSparkleWallet.CounterRequested -= OpenCounterFromSparkleWallet;
        HeaderSparkleWallet.ClosePopup();
    }
}

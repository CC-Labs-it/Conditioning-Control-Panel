using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>WPF MainWindow/MainWindow.SparkleWallet.cs: the header wallet shows the one Sparkle
    /// Point balance (AppSettings.SkillPoints), refreshes on every change to it and on a profile swap
    /// (CurrentReplaced), and plays the reward cue only for a settled local credit
    /// (SparklePointRewards.Awarded, Core). WPF's QueueSkillTreeBalanceRefresh is not needed here:
    /// EnhancementsTabView already redraws itself on SkillPoints (EnhancementsTabView.axaml.cs:116).</summary>
    public partial class MainShellWindow
    {
        private AppSettings? _sparkleWalletSettings;
        private bool _sparkleWalletActive;

        private SparkleWallet? HeaderWallet => Named<SparkleWallet>("HeaderSparkleWallet");

        private void InitializeSparkleWallet()
        {
            if (HeaderWallet is not { } wallet) return;
            _sparkleWalletActive = true;
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += ReplaceSparkleWalletSettings;
            SparklePointRewards.Awarded += OnSparklePointAwarded;
            wallet.VisitRequested += VisitFromSparkleWallet;
            Closed += (_, _) => ShutdownSparkleWallet();
            Deactivated += (_, _) => HeaderWallet?.ClosePopup();
            ReplaceSparkleWalletSettings();
        }

        private void ReplaceSparkleWalletSettings()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ReplaceSparkleWalletSettings); return; }
            if (!_sparkleWalletActive) return;
            if (_sparkleWalletSettings != null) _sparkleWalletSettings.PropertyChanged -= OnSparkleWalletSettingChanged;
            _sparkleWalletSettings = CoreSettings.Current;
            _sparkleWalletSettings.PropertyChanged += OnSparkleWalletSettingChanged;
            RefreshSparkleWalletBalance();
        }

        private void OnSparkleWalletSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.SkillPoints) || string.IsNullOrEmpty(e.PropertyName))
                RefreshSparkleWalletBalance();
        }

        internal void RefreshSparkleWalletBalance()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshSparkleWalletBalance); return; }
            if (_sparkleWalletActive) HeaderWallet?.SetBalance(CoreSettings.Current.SkillPoints);
        }

        private void OnSparklePointAwarded(object? sender, SparklePointAward award)
        {
            var creditedSettings = CoreSettings.Current;
            void Present()
            {
                if (!_sparkleWalletActive || !ReferenceEquals(CoreSettings.Current, creditedSettings)) return;
                // Read the current ledger: another purchase or award may have occurred while queued.
                RefreshSparkleWalletBalance();
                if (IsVisible && WindowState != WindowState.Minimized) HeaderWallet?.ShowReward(award.Amount);
            }
            if (Dispatcher.UIThread.CheckAccess()) Present();
            else Dispatcher.UIThread.Post(Present);
        }

        /// <summary>WPF MainWindow.SparkleWallet.cs:85 -> MainWindow.Lab.cs:306.</summary>
        private void VisitFromSparkleWallet(object? sender, EventArgs e) => OpenBillboardBackRoom();

        internal void RequestBackRoomSignIn() => _ = OpenUnifiedLoginDialog();

        private void ShutdownSparkleWallet()
        {
            _sparkleWalletActive = false;
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= ReplaceSparkleWalletSettings;
            SparklePointRewards.Awarded -= OnSparklePointAwarded;
            if (_sparkleWalletSettings != null) _sparkleWalletSettings.PropertyChanged -= OnSparkleWalletSettingChanged;
            _sparkleWalletSettings = null;
            if (HeaderWallet is { } wallet) { wallet.VisitRequested -= VisitFromSparkleWallet; wallet.ClosePopup(); }
        }
    }
}

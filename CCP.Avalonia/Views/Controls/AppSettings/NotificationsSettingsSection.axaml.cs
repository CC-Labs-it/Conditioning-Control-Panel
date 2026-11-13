using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · NOTIFICATIONS, ported from the WPF head. All three rows are persisted preferences:
    /// <c>ChkIntakeNudge</c> controls the weekly intake reminder,
    /// <c>CmbPerkEffects</c> live perk announcements and visuals (Full / No cards / Off, WPF 7.2.3) and <c>ChkBannerPool</c> the
    /// header banner's pool beat.
    /// </summary>
    public partial class NotificationsSettingsSection : UserControl, IAppSettingsSection
    {
        private bool _isLoading = true;
        private SettingsService? _subscribedService;

        public NotificationsSettingsSection()
        {
            InitializeComponent();
            ChkIntakeNudge.IsCheckedChanged += ChkIntakeNudge_Changed;
            CmbPerkEffects.SelectionChanged += CmbPerkEffects_SelectionChanged;
            ChkBannerPool.IsCheckedChanged += ChkBannerPool_Changed;
            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _subscribedService = CoreSettings.Service;
            if (_subscribedService is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_subscribedService is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            _subscribedService = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        // WPF IsVisibleChanged re-seeds on every Settings-door open.
        public void OnSectionShown() => SyncFromSettings();

        /// <summary>Paints the rows from settings without raising an edit.</summary>
        internal void SyncFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkIntakeNudge.IsChecked = s.IntakeNudgeEnabled;
                CmbPerkEffects.SelectedIndex = (int)PerkEffectsRules.Clamp(s.PerkEffects);
                ChkBannerPool.IsChecked = s.BannerPoolEnabled;
            }
            finally { _isLoading = false; }
        }

        private void ChkIntakeNudge_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.IntakeNudgeEnabled = ChkIntakeNudge.IsChecked ?? true;
            CoreSettings.Save();
        }

        private void CmbPerkEffects_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || CmbPerkEffects.SelectedIndex < 0) return;
            CoreSettings.Current.PerkEffects = PerkEffectsRules.FromIndex(CmbPerkEffects.SelectedIndex);
            CoreSettings.Save();
        }

        /// <summary>WPF NotificationsSettingsSection.xaml.cs:101-108: a live editor for the header
        /// banner's pool beat. The Core BannerPoolService reads this flag on every draw, so the
        /// shell's pool beat (MainShellWindow.Marquee.cs) honours a change live, as in WPF.</summary>
        private void ChkBannerPool_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.BannerPoolEnabled = ChkBannerPool.IsChecked ?? true;
            CoreSettings.Save();
        }
    }
}

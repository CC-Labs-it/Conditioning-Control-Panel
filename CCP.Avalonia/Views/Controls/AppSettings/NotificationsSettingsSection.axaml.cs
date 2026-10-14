using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · NOTIFICATIONS, ported from the WPF head. All three rows are persisted preferences:
    /// <c>ChkIntakeNudge</c> controls the weekly intake reminder,
    /// <c>ChkSuppressPerkNotifications</c> live perk announcements and <c>ChkBannerPool</c> the
    /// header banner's pool beat.
    /// </summary>
    public partial class NotificationsSettingsSection : UserControl
    {
        private bool _isLoading = true;
        private SettingsService? _subscribedService;

        public NotificationsSettingsSection()
        {
            InitializeComponent();
            ChkIntakeNudge.IsCheckedChanged += ChkIntakeNudge_Changed;
            ChkSuppressPerkNotifications.IsCheckedChanged += ChkSuppressPerkNotifications_Changed;
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

        /// <summary>Paints the rows from settings without raising an edit.</summary>
        internal void SyncFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkIntakeNudge.IsChecked = s.IntakeNudgeEnabled;
                ChkSuppressPerkNotifications.IsChecked = s.SuppressPerkNotifications;
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

        private void ChkSuppressPerkNotifications_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.SuppressPerkNotifications = ChkSuppressPerkNotifications.IsChecked ?? false;
            CoreSettings.Save();
        }

        /// <summary>WPF NotificationsSettingsSection.xaml.cs:101-108: a live editor for the header
        /// banner's pool beat. That beat (BannerPoolService) is not ported to this head yet
        /// (parity row shell-marquee); the preference still persists for it and for WPF.</summary>
        private void ChkBannerPool_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.BannerPoolEnabled = ChkBannerPool.IsChecked ?? true;
            CoreSettings.Save();
        }
    }
}

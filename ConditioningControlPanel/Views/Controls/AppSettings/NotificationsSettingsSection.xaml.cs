using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS · NOTIFICATIONS. Two rows with deliberately different plumbing:
    ///
    /// <c>ChkIntakeNudge</c> (moved out of the collapsed LegacyDashboardHost) has no Changed
    /// handler on purpose - the value is read at launch by
    /// <c>MainWindow.Marquee.cs:CheckIntakePassNudge</c> and round-trips through
    /// <c>MainWindow.Settings.cs</c> LoadSettings/SaveSettings only. Nothing here touches it.
    ///
    /// <c>CmbPerkEffects</c> (Full / No cards / Off, owner 2026-11-05; it replaced the old
    /// ChkSuppressPerkNotifications in the same row) is a LIVE editor: the sites it gates (the
    /// lucky-proc toast, the Pink Rush popup and wash, the quest-complete popup, the lucky glow and
    /// sparkles) read <c>App.PerkNotificationsSuppressed</c> / <c>App.PerkEffectsShown</c> at the
    /// instant they fire, so the flip has to reach
    /// settings and disk immediately rather than at the next SaveSettings sweep. Same contract as
    /// PerformanceSettingsSection: write, then <c>App.Settings?.Save()</c>.
    ///
    /// Seeding must not look like a user edit. This control is built with the rest of MainWindow,
    /// before <c>App.Settings</c> is guaranteed to exist, and re-seeds every time the Settings door
    /// opens; assigning IsChecked raises the same events a click does, so every assignment happens
    /// inside the <c>_isLoading</c> guard, which starts <c>true</c>.
    /// </summary>
    public partial class NotificationsSettingsSection : UserControl
    {
        private bool _isLoading = true;

        public NotificationsSettingsSection()
        {
            InitializeComponent();
            // Items in enum order (index = PerkEffectsMode value), bound so a language switch
            // relabels them live. Built here so a render test without App.Settings still has them.
            foreach (var key in new[] { "perk_effects_full", "perk_effects_no_cards", "perk_effects_off" })
            {
                var item = new ComboBoxItem();
                item.SetBinding(ContentControl.ContentProperty,
                    new Binding($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
                CmbPerkEffects.Items.Add(item);
            }
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings != null) App.Settings.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings != null) App.Settings.CurrentReplaced -= OnCurrentReplaced;
        }

        /// <summary>
        /// A cloud restore swaps the whole <c>Current</c> instance out from under us. Every tab view
        /// stays alive for the app's lifetime, so without this the page would keep showing the
        /// pre-restore value until the next time it became visible.
        /// </summary>
        private void OnCurrentReplaced()
        {
            // DispatcherPriority.Normal (the default) on purpose - Loaded priority gets starved.
            Dispatcher.BeginInvoke(new Action(SyncFromSettings));
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool visible && visible) SyncFromSettings();
        }

        /// <summary>
        /// Paints the live row from settings without raising an edit. Safe to call at any time,
        /// including before <c>App.Settings</c> exists (render tests, early startup).
        /// ChkIntakeNudge is NOT painted here - LoadSettings owns it and painting it twice from
        /// two owners is how a toggle starts flickering back to its old value.
        /// </summary>
        internal void SyncFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            _isLoading = true;
            try
            {
                CmbPerkEffects.SelectedIndex = (int)PerkEffectsRules.Clamp(s.PerkEffects);
                ChkBannerPool.IsChecked = s.BannerPoolEnabled;
            }
            finally { _isLoading = false; }
        }

        private void CmbPerkEffects_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || CmbPerkEffects.SelectedIndex < 0) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            s.PerkEffects = PerkEffectsRules.FromIndex(CmbPerkEffects.SelectedIndex);
            App.Settings?.Save();
        }

        /// <summary>
        /// Also a LIVE editor: the header banner's pool tick reads BannerPoolEnabled at the moment
        /// it draws a line, so the flip has to reach settings and disk now rather than at the next
        /// SaveSettings sweep. Turning it off drops the pool beat at the next draw; the fixed beats
        /// and the server announcement are untouched either way.
        /// </summary>
        private void ChkBannerPool_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            s.BannerPoolEnabled = ChkBannerPool.IsChecked ?? true;
            App.Settings?.Save();
        }
    }
}

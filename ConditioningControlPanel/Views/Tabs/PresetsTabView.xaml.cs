using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class PresetsTabView : UserControl
    {
        public PresetsTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-3a). Hooked here rather than in ShowTab so the tab owns its own
            // decoration: the card sheen starts when the tab appears and its clock is dropped the
            // moment it is collapsed again.
            IsVisibleChanged += PresetsTabView_IsVisibleChanged;
            PaintInks();
        }

        /// <summary>
        /// The two label inks of the 7.4.1 lift, the RowLabel mix (70% TextLight + 30% the zone hue):
        /// <c>SdPresetInk</c> for preset chip names (Studio pink) and <c>SdSessionInk</c> for session
        /// row names (SectionHueNotifications blue). Code-built chips and rows reach them through the
        /// tab's styles by DynamicResource; re-mixed each time the tab is shown, so a mod retheme lands.
        /// </summary>
        internal void PaintInks()
        {
            uint light = TryFindResource("TextLightBrush") is SolidColorBrush l ? RowHue.ToArgb(l.Color) : EmblemPalette.TextLight;
            uint Hue(string key) => TryFindResource(key) is Color c ? RowHue.ToArgb(c) : EmblemPalette.Pink;
            Resources["SdPresetInk"] = RowHue.Frozen(EmblemPalette.LabelTint(light, Hue("SectionHueStudio")));
            Resources["SdSessionInk"] = RowHue.Frozen(EmblemPalette.LabelTint(light, Hue("SectionHueNotifications")));
        }

        private void PresetsTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) PaintInks();
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnPresetsTabVisibilityChanged(IsVisible);
        }

        private void BtnCreateSession_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCreateSession_Click(sender, e);
        }
        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeletePreset_Click(sender, e);
        }
        private void BtnExportPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnExportPreset_Click(sender, e);
        }
        private void BtnImportPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnImportPreset_Click(sender, e);
        }
        private void BtnSharePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSharePreset_Click(sender, e);
        }
        private void CatalogueCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCatalogue_Click(sender, e);
        }
        private void BtnExportSession_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnExportSession_Click(sender, e);
        }
        private void BtnLoadPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLoadPreset_Click(sender, e);
        }
        private void BtnNewPreset_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnNewPreset_Click(sender, e);
        }
        private void BtnRevealSpoilers_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRevealSpoilers_Click(sender, e);
        }
        private void BtnSaveOverPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSaveOverPreset_Click(sender, e);
        }
        private void BtnSelectCornerGif_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSelectCornerGif_Click(sender, e);
        }
        private void BtnSessionHistory_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSessionHistory_Click(sender, e);
        }
        private void BtnStartSession_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnStartSession_Click(sender, e);
        }
        private void ChkCornerGifEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkCornerGifEnabled_Changed(sender, e);
        }
        // PacksScrollViewer_PreviewMouseWheel is gone with the two horizontal scrollers it served
        // (the preset strip and the Takeaway shelf, 2026-08-16). The preset rail wraps and the
        // Takeaway strip is one clipped line, so neither scrolls sideways any more. MainWindow
        // still owns the handler - the Assets tab's pack strip is its other consumer.
        private void SessionBtn_Edit(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SessionBtn_Edit(sender, e);
        }
        private void SessionBtn_Export(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SessionBtn_Export(sender, e);
        }
        private void RbCornerPos_Checked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.RbCornerPos_Checked(sender, e);
        }
        private void SessionCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SessionCard_Click(sender, e);
        }
        private void CmbRackSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbRackSort_SelectionChanged(sender, e);
        }
        private void TxtRackSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtRackSearch_TextChanged(sender, e);
        }
        private void SliderCornerGifOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderCornerGifOpacity_ValueChanged(sender, e);
        }
        private void SliderCornerGifSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderCornerGifSize_ValueChanged(sender, e);
        }
    }
}

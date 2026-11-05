using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The title bar's "Online 42%" pill (WPF MainWindow.OnlineMeter.cs PaintOnlineMeterPill / OnlineMeterPill_Click):
    /// visible from every tab while online media is in the source, consented and switched on; a click opens the Assets
    /// tab, where the card sits. Percent only, never MB.
    /// <para>ponytail: not here yet - the spent hover card
    /// with its "See plans".</para>
    /// </summary>
    public partial class MainShellWindow
    {
        private bool _onlinePillWired;

        // This window loads with AvaloniaXamlLoader.Load (no generated fields): the pill's parts by name.
        internal Border? OnlinePill => Named<Border>("OnlineMeterPill");
        internal TextBlock? OnlinePillText => Named<TextBlock>("TxtOnlineMeterPill");
        internal OnlineMeterRing? OnlinePillRing => Named<OnlineMeterRing>("OnlineMeterPillRing");

        private void WireOnlineMeterPill()
        {
            if (_onlinePillWired) return;
            _onlinePillWired = true;
            try
            {
                OnlineMeterHead.Start();
                OnlineMeterHead.Refresh += RefreshOnlineMeterPill;
                Closed += (_, _) => OnlineMeterHead.Refresh -= RefreshOnlineMeterPill;
                if (OnlinePill is not { } pill) return;
                pill.PointerPressed += (_, e) =>
                {
                    e.Handled = true;   // never a title bar drag
                    try { ShowTab("assets"); }
                    catch (Exception ex) { Log.Debug("Online meter pill click: {E}", ex.Message); }
                };
            }
            catch (Exception ex) { Log.Warning(ex, "Online meter pill wiring failed"); }
            RefreshOnlineMeterPill();
        }

        internal void RefreshOnlineMeterPill()
        {
            try { PaintOnlineMeterPill(OnlineMeterHead.Now()); }
            catch (Exception ex) { Log.Debug("Online meter pill refresh failed: {E}", ex.Message); }
        }

        internal void PaintOnlineMeterPill(OnlineMeterState st)
        {
            if (OnlinePill is not { } pill || OnlinePillText is not { } text) return;
            pill.IsVisible = st.Show;
            if (!st.Show) return;
            text.Text = OnlineMeterHead.PillText(st);
            text.Foreground = st.Spent ? new SolidColorBrush(Color.FromRgb(0xA7, 0xA7, 0xC1)) : Brushes.White;
            if (OnlinePillRing is { } ring) { ring.Percent = st.Percent; ring.Tone = st.Tone; }
            pill.Background = new SolidColorBrush(st.Tone switch
            {
                OnlineMeterTone.Spent => Color.FromRgb(0x2E, 0x2E, 0x4A),
                OnlineMeterTone.Amber => Color.FromRgb(0x2E, 0x24, 0x16),
                _ => Color.FromRgb(0x25, 0x1A, 0x38),
            });
            ToolTip.SetTip(pill, st.Spent ? Loc.Get("online_hover_body") : Loc.Get("online_meter_label"));
        }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
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
    /// <para>The ring is tracked separately in ledger row 206.</para>
    /// </summary>
    public partial class MainShellWindow
    {
        private bool _onlinePillWired;
        private Popup? _onlineHover;
        private DispatcherTimer? _onlineHoverLeave;
        internal StackPanel? OnlineHoverBody => _onlineHover?.Child is Border b ? b.Child as StackPanel : null;

        // This window loads with AvaloniaXamlLoader.Load (no generated fields): the pill's parts by name.
        internal Border? OnlinePill => Named<Border>("OnlineMeterPill");
        internal TextBlock? OnlinePillText => Named<TextBlock>("TxtOnlineMeterPill");
        private Ellipse? OnlinePillDot => Named<Ellipse>("OnlineMeterPillDot");

        private void WireOnlineMeterPill()
        {
            if (_onlinePillWired) return;
            _onlinePillWired = true;
            try
            {
                OnlineMeterHead.Start();
                OnlineMeterHead.Refresh += RefreshOnlineMeterPill;
                Closed += (_, _) => { OnlineMeterHead.Refresh -= RefreshOnlineMeterPill; CloseOnlineHover(); };
                Deactivated += (_, _) => CloseOnlineHover();
                if (OnlinePill is not { } pill) return;
                _onlineHoverLeave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                _onlineHoverLeave.Tick += (_, _) =>
                {
                    _onlineHoverLeave.Stop();
                    if (!pill.IsPointerOver && _onlineHover?.Child?.IsPointerOver != true) CloseOnlineHover();
                };
                pill.PointerEntered += (_, _) => { _onlineHoverLeave.Stop(); OpenOnlineHover(OnlineMeterHead.Now()); };
                pill.PointerExited += (_, _) => _onlineHoverLeave.Start();
                pill.PointerPressed += (_, e) =>
                {
                    CloseOnlineHover();
                    e.Handled = true;   // never a title bar drag
                    try { ShowTab("assets"); }
                    catch (Exception ex) { Log.Debug("Online meter pill click: {E}", ex.Message); }
                };
            }
            catch (Exception ex) { Log.Warning(ex, "Online meter pill wiring failed"); }
            RefreshOnlineMeterPill();
        }

        internal void OpenOnlinePlans(int tier)
        {
            CloseOnlineHover();
            if (LockdownActive) return;
            if (ConditioningControlPanel.Services.TierGate.ReconnectIsTheAnswer()) { _ = ReconnectFromGate(); return; }
            ShowVaultGate(null, tier);
        }

        internal void OpenOnlineHover(OnlineMeterState st)
        {
            if (!st.Show || !st.Spent || OnlinePill == null) return;
            if (_onlineHover == null)
            {
                var holder = new Border
                {
                    Padding = new Thickness(16, 14), CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x30)),
                    Child = new StackPanel { Width = 276, Spacing = 6 },
                };
                holder.PointerEntered += (_, _) => _onlineHoverLeave?.Stop();
                holder.PointerExited += (_, _) => _onlineHoverLeave?.Start();
                holder.PointerPressed += (_, _) => { if (!IsActive) Activate(); };
                _onlineHover = new Popup
                {
                    Child = holder, PlacementTarget = OnlinePill, Placement = PlacementMode.Bottom,
                    IsLightDismissEnabled = false, Focusable = false, Topmost = false, ShouldUseOverlayLayer = true,
                };
                if (OnlinePill.Parent is Panel host) host.Children.Add(_onlineHover);
            }
            PaintOnlineHover(st);
            _onlineHover.IsOpen = true;
        }

        private void PaintOnlineHover(OnlineMeterState st)
        {
            if (OnlineHoverBody is not { } body) return;
            body.Children.Clear();
            void Line(string key) => body.Children.Add(new TextBlock
            {
                Text = Loc.Get(key), TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = Brushes.White,
            });
            Line("online_hover_title");
            Line("online_hover_body");
            if (st.UpsellKey != null) Line(st.UpsellKey);
            if (st.SeePlansTier is int tier)
            {
                var plans = new Button { Content = Loc.Get("online_see_plans"), HorizontalAlignment = HorizontalAlignment.Left };
                plans.Click += (_, _) => OpenOnlinePlans(tier);
                body.Children.Add(plans);
            }
        }

        private void CloseOnlineHover()
        {
            _onlineHoverLeave?.Stop();
            if (_onlineHover != null) _onlineHover.IsOpen = false;
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
            if (!st.Show || !st.Spent) CloseOnlineHover();
            else if (_onlineHover?.IsOpen == true) PaintOnlineHover(st);
            if (!st.Show) return;
            text.Text = OnlineMeterHead.PillText(st);
            text.Foreground = st.Spent ? new SolidColorBrush(Color.FromRgb(0xA7, 0xA7, 0xC1)) : Brushes.White;
            if (OnlinePillDot is { } dot) dot.Fill = new SolidColorBrush(Tabs.AssetsTabView.ToneColor(st.Tone));
            pill.Background = new SolidColorBrush(st.Tone switch
            {
                OnlineMeterTone.Spent => Color.FromRgb(0x2E, 0x2E, 0x4A),
                OnlineMeterTone.Amber => Color.FromRgb(0x2E, 0x24, 0x16),
                _ => Color.FromRgb(0x25, 0x1A, 0x38),
            });
            ToolTip.SetTip(pill, st.Spent ? null : Loc.Get("online_meter_label"));
        }
    }
}

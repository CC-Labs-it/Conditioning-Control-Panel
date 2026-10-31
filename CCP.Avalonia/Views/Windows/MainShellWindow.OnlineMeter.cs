using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia;
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
    /// <para>ponytail: the ring and the spent card's "See plans" remain with their respective lanes.</para>
    /// </summary>
    public partial class MainShellWindow
    {
        private bool _onlinePillWired;
        private bool _onlinePillSpent;
        private Popup? _onlineHover;
        private readonly DispatcherTimer _onlineHoverLeave = new() { Interval = TimeSpan.FromMilliseconds(220) };
        internal Button? OnlineHoverHelp { get; private set; }

        internal void OpenOnlineMeterHover()
        {
            if (!_onlinePillSpent || OnlinePill is not { } pill || _onlineHover?.IsOpen == true) return;
            var body = new StackPanel { Width = 276, Spacing = 8 };
            var head = new DockPanel();
            var help = new Button { Content = "?", Tag = "online-help-open", Focusable = false,
                Width = 16, Height = 16, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(8), Background = Brush.Parse("#241838"),
                BorderBrush = Brush.Parse("#66B99CFF"), BorderThickness = new Thickness(1),
                Foreground = Brush.Parse("#B99CFF"), FontSize = 10, FontWeight = FontWeight.Bold,
                Cursor = new Cursor(StandardCursorType.Help) };
            OnlineHoverHelp = help;
            DockPanel.SetDock(help, Dock.Right);
            help.Click += (_, e) => { e.Handled = true; OpenOnlineMeterHelp(); };
            head.Children.Add(help);
            head.Children.Add(new TextBlock { Text = Loc.Get("online_hover_title"), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(head);
            body.Children.Add(new TextBlock { Text = Loc.Get("online_hover_body"), TextWrapping = TextWrapping.Wrap });
            var card = new Border { Child = body, Padding = new Thickness(14), CornerRadius = new CornerRadius(10), Background = Brush.Parse("#252038") };
            card.PointerEntered += (_, _) => _onlineHoverLeave.Stop();
            card.PointerExited += (_, _) => _onlineHoverLeave.Start();
            _onlineHover = new Popup { PlacementTarget = pill, Placement = PlacementMode.Bottom, Child = card,
                IsLightDismissEnabled = false, ShouldUseOverlayLayer = true, Topmost = false };
            ((ISetLogicalParent)_onlineHover).SetParent(pill);
            _onlineHover.IsOpen = true;
        }

        private void CloseOnlineMeterHover()
        {
            _onlineHoverLeave.Stop();
            if (_onlineHover == null) return;
            _onlineHover.IsOpen = false;
            ((ISetLogicalParent)_onlineHover).SetParent(null);
            _onlineHover = null;
            OnlineHoverHelp = null;
        }

        internal void OpenOnlineMeterHelp()
        {
            CloseOnlineMeterHover();
            ShowTab("assets");
            Dispatcher.UIThread.Post(() => Named<Tabs.AssetsTabView>("AssetsTab")?.PinOnlineHelp(), DispatcherPriority.Loaded);
        }

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
                Closed += (_, _) => { OnlineMeterHead.Refresh -= RefreshOnlineMeterPill; CloseOnlineMeterHover(); };
                _onlineHoverLeave.Tick += (_, _) => CloseOnlineMeterHover();
                if (OnlinePill is not { } pill) return;
                pill.PointerEntered += (_, _) => { _onlineHoverLeave.Stop(); OpenOnlineMeterHover(); };
                pill.PointerExited += (_, _) => _onlineHoverLeave.Start();
                pill.PointerPressed += (_, e) =>
                {
                    e.Handled = true;   // never a title bar drag
                    CloseOnlineMeterHover();
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
            _onlinePillSpent = st.Spent;
            if (!st.Show || !st.Spent) CloseOnlineMeterHover();
            pill.IsVisible = st.Show;
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

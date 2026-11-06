using System;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
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
    /// <para>The ring inside the pill (OnlineMeterRing) and the spent hover card with its "See plans" are both here.</para>
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
        internal OnlineMeterRing? OnlinePillRing => Named<OnlineMeterRing>("OnlineMeterPillRing");

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
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5C)), BorderThickness = new Thickness(1),
                    Child = new StackPanel { Width = 276, Spacing = 6 },
                };
                holder.PointerEntered += (_, _) => _onlineHoverLeave?.Stop();
                holder.PointerExited += (_, _) => _onlineHoverLeave?.Start();
                holder.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
                {
                    if (!IsActive) Activate();
                    e.Handled = true; // The overlay's logical parent is inside the draggable title bar.
                }, RoutingStrategies.Bubble, handledEventsToo: true);
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
            void Line(string key, Color ink) => body.Children.Add(new TextBlock
            {
                Text = Loc.Get(key), TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = new SolidColorBrush(ink),
            });
            var help = F.Pill(new TextBlock
            {
                Text = "?", FontFamily = F.Display, FontWeight = FontWeight.Bold, FontSize = 10,
                Foreground = F.Lilac, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            }, new SolidColorBrush(Color.FromRgb(0x24, 0x18, 0x38)), F.Lilac, "online-help-open",
                new SolidColorBrush(Color.FromArgb(0x66, 0xB9, 0x9C, 0xFF)));
            help.Width = help.Height = 16;
            help.MinWidth = help.MinHeight = 0;
            help.Padding = new Thickness(0);
            help.Margin = new Thickness(8, 0, 0, 0);
            help.Focusable = false;
            help.VerticalAlignment = VerticalAlignment.Top;
            help.Cursor = new Cursor(StandardCursorType.Help);
            global::Avalonia.Automation.AutomationProperties.SetName(help, Loc.Get(OnlineMeterRules.HelpTitleKey));
            help.Click += (_, e) => { e.Handled = true; OpenOnlineMeterHelp(); };
            var head = new DockPanel();
            DockPanel.SetDock(help, Dock.Right);
            head.Children.Add(help);
            head.Children.Add(new TextBlock { Text = Loc.Get("online_hover_title"), FontSize = 13, FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF5)) });
            body.Children.Add(head);
            Line("online_hover_body", Color.FromRgb(0xCC, 0xCC, 0xE0));
            if (st.UpsellKey != null) Line(st.UpsellKey, Color.FromRgb(0xE0, 0xB0, 0xFF));
            if (st.SeePlansTier is int tier)
            {
                var plans = new Button { Content = Loc.Get("online_see_plans"), HorizontalAlignment = HorizontalAlignment.Left,
                    Theme = this.FindResource("SmallPinkButton") as ControlTheme, Padding = new Thickness(12, 5), FontSize = 12 };
                plans.Click += (_, _) => OpenOnlinePlans(tier);
                body.Children.Add(plans);
            }
        }

        private void OpenOnlineMeterHelp()
        {
            CloseOnlineHover();
            ShowTab("assets");
            Dispatcher.UIThread.Post(() =>
            {
                if (Named<Tabs.AssetsTabView>("AssetsTab") is not { } tab) return;
                tab.RemoteMediaBlock.BringIntoView();
                global::ConditioningControlPanel.Avalonia.Views.Controls.Leash.LeashHelpButton.Pin(tab.OnlineHelpButton);
            }, DispatcherPriority.Loaded);
        }

        private void CloseOnlineHover()
        {
            _onlineHoverLeave?.Stop();
            if (_onlineHover != null) _onlineHover.IsOpen = false;
        }

        /// <summary>Entry for the spent hover card's help glyph and the shell's tests (WPF OpenOnlineHelp).</summary>
        internal void OpenOnlineHelp()
        {
            OpenOnlineMeterHelp();
        }

        internal void RefreshOnlineMeterPill()
        {
            try
            {
                var st = OnlineMeterHead.Now();
                PaintOnlineMeterPill(st);
                OfferOnlineLicenceCard(st);   // WPF RefreshOnlineMeter :163 (MainShellWindow.OnlineLicence.cs)
            }
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
            if (OnlinePillRing is { } ring) { ring.Percent = st.Percent; ring.Tone = st.Tone; }
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

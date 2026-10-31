using System;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _onlinePillWired;
        private Helpers.OnlineSpentToast? _onlineSpentToast;
        internal bool OnlineSpentPolling => _onlineSpentWait?.IsEnabled == true;

        // This window loads with AvaloniaXamlLoader.Load (no generated fields): the pill's parts by name.
        internal Border? OnlinePill => Named<Border>("OnlineMeterPill");
        internal TextBlock? OnlinePillText => Named<TextBlock>("TxtOnlineMeterPill");
        internal OnlineMeterRing? OnlinePillRing => Named<Decorator>("OnlineMeterPillRing")?.Child as OnlineMeterRing;
        private Popup? _onlineMeterHover;
        private DispatcherTimer? _onlineMeterHoverLeave, _onlineSpentWait;
        private readonly OnlineSpentNoticeQueue _onlineSpentQueue = new();
        internal Popup? OnlineMeterHover => _onlineMeterHover;
        private static readonly IBrush OnlineText = Brushes.White;
        private static readonly IBrush OnlineText2 = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xE0));
        private static readonly IBrush OnlineLilac = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0xFF));

        private void WireOnlineMeterPill()
        {
            if (_onlinePillWired) return;
            _onlinePillWired = true;
            try
            {
                OnlineMeterHead.Start();
                OnlineMeterHead.Refresh += RefreshOnlineMeterPill;
                ScrolllerAllowance.SpentReached += OnOnlineAllowanceSpent;
                LocalizationManager.Instance.LanguageChanged += OnOnlineMeterLanguageChanged;
                _onlineSpentWait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _onlineSpentWait.Tick += (_, _) => ReleaseOnlineSpentNotice();
                _onlineMeterHoverLeave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                _onlineMeterHoverLeave.Tick += (_, _) =>
                {
                    _onlineMeterHoverLeave.Stop();
                    if (OnlinePill?.IsPointerOver != true && _onlineMeterHover?.Child?.IsPointerOver != true)
                        CloseOnlineMeterHover();
                };
                Closed += (_, _) =>
                {
                    OnlineMeterHead.Refresh -= RefreshOnlineMeterPill;
                    ScrolllerAllowance.SpentReached -= OnOnlineAllowanceSpent;
                    LocalizationManager.Instance.LanguageChanged -= OnOnlineMeterLanguageChanged;
                    _onlineSpentWait.Stop();
                    _onlineSpentToast?.Dispose();
                    CloseOnlineMeterHover();
                };
                if (OnlinePill is not { } pill) return;
                pill.PointerPressed += (_, e) => { e.Handled = true; OnlineMeterPill_Click(); };
                pill.KeyDown += (_, e) =>
                {
                    if (e.Key is not (Key.Enter or Key.Space)) return;
                    e.Handled = true;
                    OnlineMeterPill_Click();
                };
                pill.PointerEntered += (_, _) => { _onlineMeterHoverLeave.Stop(); OpenOnlineMeterHover(); };
                pill.PointerExited += (_, _) => _onlineMeterHoverLeave.Start();
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
            if (!st.Show || !st.Spent) CloseOnlineMeterHover();
            else if (_onlineMeterHover is { IsOpen: true, Child: Border holder }) holder.Child = BuildOnlineMeterHoverCard(st);
            if (!st.Show) return;
            text.Text = OnlineMeterHead.PillText(st);
            text.Foreground = st.Spent ? new SolidColorBrush(Color.FromRgb(0xA7, 0xA7, 0xC1)) : Brushes.White;
            if (Named<Decorator>("OnlineMeterPillRing") is { } host)
            {
                if (host.Child is not OnlineMeterRing) host.Child = new OnlineMeterRing();
                var ring = (OnlineMeterRing)host.Child;
                ring.Percent = st.Percent;
                ring.Tone = st.Tone;
            }
            pill.Background = new SolidColorBrush(st.Tone switch
            {
                OnlineMeterTone.Spent => Color.FromRgb(0x2E, 0x2E, 0x4A),
                OnlineMeterTone.Amber => Color.FromRgb(0x2E, 0x24, 0x16),
                _ => Color.FromRgb(0x25, 0x1A, 0x38),
            });
            ToolTip.SetTip(pill, st.Spent ? null : Loc.Get("online_meter_label"));
        }
        private void OnOnlineAllowanceSpent(object? sender, EventArgs e) => Dispatcher.UIThread.Post(QueueOnlineSpentNotice);
        private void OnOnlineMeterLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            CloseOnlineMeterHover();
            RefreshOnlineMeterPill();
            Named<Tabs.AssetsTabView>("AssetsTab")?.RefreshOnlineMeter();
        });
        private static bool OnlineNoticeHolding()
        {
            try { return FriendsLanding.World().Holding; } catch { return true; }
        }
        internal void OnlineMeterPill_Click()
        {
            CloseOnlineMeterHover();
            try
            {
                ShowTab("assets");
                Dispatcher.UIThread.Post(() =>
                {
                    try { Named<Tabs.AssetsTabView>("AssetsTab")?.RemoteMediaBlock.BringIntoView(); } catch { /* tab gone */ }
                });
            }
            catch (Exception ex) { Log.Debug("Online meter pill click: {E}", ex.Message); }
        }
        internal void OpenOnlineMeterHover()
        {
            if (_onlineMeterHover is { IsOpen: true }) return;
            try
            {
                var st = OnlineMeterHead.Now();
                if (!st.Show || !st.Spent || Named<Border>("OnlineMeterPill") is not { } pill) return;
                var holder = new Border { Padding = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent, Child = BuildOnlineMeterHoverCard(st) };
                holder.PointerEntered += (_, _) => _onlineMeterHoverLeave?.Stop();
                holder.PointerExited += (_, _) => _onlineMeterHoverLeave?.Start();
                if (_onlineMeterHover == null)
                {
                    _onlineMeterHover = new Popup
                    {
                        PlacementTarget = pill,
                        Placement = PlacementMode.Bottom,
                        IsLightDismissEnabled = false,
                        Focusable = false,
                    };
                    if (pill.Parent is Panel bar) bar.Children.Add(_onlineMeterHover);
                }
                _onlineMeterHover.Child = holder;
                _onlineMeterHover.IsOpen = true;
            }
            catch (Exception ex) { Log.Debug("Online meter hover: {E}", ex.Message); }
        }
        internal void CloseOnlineMeterHover()
        {
            _onlineMeterHoverLeave?.Stop();
            if (_onlineMeterHover != null) _onlineMeterHover.IsOpen = false;
        }
        private Border BuildOnlineMeterHoverCard(OnlineMeterState st)
        {
            var body = new StackPanel { Width = 276 };
            TextBlock Line(string key, IBrush brush, bool title = false) => new()
            {
                Text = Loc.Get(key), FontSize = title ? 13 : 12, TextWrapping = TextWrapping.Wrap, Foreground = brush,
                FontWeight = title ? FontWeight.SemiBold : FontWeight.Normal, Margin = new Thickness(0, title ? 0 : 6, 0, 0),
            };
            body.Children.Add(Line("online_hover_title", OnlineText, true));
            body.Children.Add(Line("online_hover_body", OnlineText2));
            if (st.UpsellKey != null) body.Children.Add(Line(st.UpsellKey, OnlineLilac));
            if (st.SeePlansTier is int tier)
            {
                var btn = new Button
                {
                    Content = new TextBlock { Text = Loc.Get("online_see_plans") },
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 5, 12, 5),
                    FontSize = 12,
                    Margin = new Thickness(0, 10, 0, 0),
                };
                if (this.TryFindResource("SmallPinkButton", out var theme) && theme is global::Avalonia.Styling.ControlTheme ct) btn.Theme = ct;
                btn.Click += (_, _) => OpenOnlinePlans(tier);
                body.Children.Add(btn);
            }
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5C)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 14, 16, 14),
                Child = body,
            };
        }
        internal void OpenOnlinePlans(int tier)
        {
            if (LockdownActive) return;
            CloseOnlineMeterHover();
            try
            {
                if (TierGate.ReconnectIsTheAnswer()) { _ = ReconnectFromGate(); return; }
                ShowVaultGate(null, tier);
            }
            catch (Exception ex) { Log.Debug("Online meter plans: {E}", ex.Message); }
        }
        internal void QueueOnlineSpentNotice()
        {
            RefreshOnlineMeterPill();
            _onlineSpentQueue.Arm(ScrolllerAllowance.Now());
            ReleaseOnlineSpentNotice();
        }
        internal void ReleaseOnlineSpentNotice()
        {
            try
            {
                _onlineSpentToast?.PollHold();
                if (_onlineSpentQueue.TryRelease(ScrolllerAllowance.Now(), OnlineNoticeHolding()) && ScrolllerAllowance.Spent)
                    ShowOnlineSpentNotice();
                if (_onlineSpentQueue.Pending || _onlineSpentToast?.Active == true) _onlineSpentWait?.Start();
                else _onlineSpentWait?.Stop();
            }
            catch (Exception ex) { Log.Debug("Online spent notice queue: {E}", ex.Message); }
        }
        private void ShowOnlineSpentNotice()
        {
            try
            {
                RefreshOnlineMeterPill();
                var tier = OnlineMeterRules.SpentNoticePlansTier(ScrolllerAllowance.Plan, LockdownActive);
                _onlineSpentToast?.Dispose();
                if (Named<Panel>("NotificationHost") is not { } host) return;
                _onlineSpentToast = new Helpers.OnlineSpentToast(App.Notifications, host, OnlineNoticeHolding,
                    QueueOnlineSpentNotice, Loc.Get("online_toast_spent"),
                    tier != null ? Loc.Get("online_see_plans") : null,
                    tier is int t ? () => OpenOnlinePlans(t) : null);
            }
            catch (Exception ex) { Log.Debug("Online spent notice: {E}", ex.Message); }
        }
    }
}

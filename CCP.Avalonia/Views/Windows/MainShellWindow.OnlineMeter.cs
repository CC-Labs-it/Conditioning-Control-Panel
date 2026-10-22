// PORTED from ConditioningControlPanel/MainWindow/MainWindow.OnlineMeter.cs (WPF main 76cb2f8b0, 50a0074f1,
// 6ea63f274): "Online media today" - the title bar pill, the card in the Assets tab's Media source block and
// the once-a-day spent notice. What each shows is decided by Core OnlineMeterRules; this file only paints it.
// Percent only, never MB.
// ponytail: the official note, the stamped line, the "?" help slip and the licence card are lane
// sync7-scrolller-h. The spent notice goes through this head's in-app toast stack (App.Notifications): the
// FriendNotices corner window is not on this head, so it has no ring icon and no fold-and-return under a hold
// (the toasts never fold). The hover card stays a plain Popup: WPF's topmost drop is Win32-only.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private static readonly IBrush OnlineLilac = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0xFF));
        private static readonly IBrush OnlineText = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF5));
        private static readonly IBrush OnlineText2 = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xE0));

        /// <summary>One refresh a second at most while charges stream in.</summary>
        private DispatcherTimer? _onlineMeterThrottle;
        /// <summary>A minute tick: "Back in 6 h" counts down, and it backs up ScrolllerAllowance.Changed.</summary>
        private DispatcherTimer? _onlineMeterClock;
        private OnlineMeterRing? _onlineMeterPillRing;
        private Popup? _onlineMeterHover;
        private DispatcherTimer? _onlineMeterHoverLeave;
        /// <summary>The spent notice waits out a session / Lockdown / Strict Lock / program hold.</summary>
        private readonly OnlineSpentNoticeQueue _onlineSpentQueue = new();
        /// <summary>Polls for the hold's end, only while a notice waits (FriendsLanding's cadence).</summary>
        private DispatcherTimer? _onlineSpentWait;

        internal Popup? OnlineMeterHover => _onlineMeterHover;

        /// <summary>WPF WireOnlineMeter (MainWindow.xaml.cs:3699): the allowance events, the timers, the pill
        /// click/keys/hover. Called once from the constructor.</summary>
        private void InitializeOnlineMeter()
        {
            try
            {
                _onlineMeterThrottle = new DispatcherTimer { Interval = OnlineMeterRules.Throttle };
                _onlineMeterThrottle.Tick += (_, _) => { _onlineMeterThrottle.Stop(); RefreshOnlineMeter(); };
                _onlineMeterClock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                _onlineMeterClock.Tick += (_, _) => RefreshOnlineMeter();
                _onlineMeterClock.Start();
                _onlineSpentWait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _onlineSpentWait.Tick += (_, _) => ReleaseOnlineSpentNotice();
                _onlineMeterHoverLeave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                _onlineMeterHoverLeave.Tick += (_, _) =>
                {
                    _onlineMeterHoverLeave.Stop();
                    if (Named<Border>("OnlineMeterPill")?.IsPointerOver != true && _onlineMeterHover?.Child?.IsPointerOver != true)
                        CloseOnlineMeterHover();
                };

                ScrolllerAllowance.Changed += OnOnlineAllowanceChanged;
                ScrolllerAllowance.SpentReached += OnOnlineAllowanceSpent;
                LocalizationManager.Instance.LanguageChanged += OnOnlineMeterLanguageChanged;
                Closed += (_, _) =>
                {
                    ScrolllerAllowance.Changed -= OnOnlineAllowanceChanged;
                    ScrolllerAllowance.SpentReached -= OnOnlineAllowanceSpent;
                    LocalizationManager.Instance.LanguageChanged -= OnOnlineMeterLanguageChanged;
                    _onlineMeterThrottle?.Stop();
                    _onlineMeterClock?.Stop();
                    _onlineSpentWait?.Stop();
                    CloseOnlineMeterHover();
                };

                if (Named<Border>("OnlineMeterPill") is { } pill)
                {
                    // e.Handled: never a title bar drag or a double-click maximise.
                    pill.PointerPressed += (_, e) => { e.Handled = true; OnlineMeterPill_Click(); };
                    pill.KeyDown += (_, e) =>   // P17: keyboard-reachable like a button
                    {
                        if (e.Key is not (Key.Enter or Key.Space)) return;
                        e.Handled = true;
                        OnlineMeterPill_Click();
                    };
                    pill.PointerEntered += (_, _) =>
                    {
                        _onlineMeterHoverLeave.Stop();
                        if (ScrolllerAllowance.Spent) OpenOnlineMeterHover();
                    };
                    pill.PointerExited += (_, _) => _onlineMeterHoverLeave.Start();
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Online meter wiring failed"); }
            RefreshOnlineMeter();
        }

        private void OnOnlineAllowanceChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() => { if (_onlineMeterThrottle is { IsEnabled: false } t) t.Start(); });

        private void OnOnlineAllowanceSpent(object? sender, EventArgs e) => Dispatcher.UIThread.Post(QueueOnlineSpentNotice);

        /// <summary>The pill, the card and the hover card are filled in code: repaint on a language switch.</summary>
        private void OnOnlineMeterLanguageChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() => { CloseOnlineMeterHover(); RefreshOnlineMeter(); });

        private static OnlineMeterState OnlineMeterNow()
        {
            var s = CoreSettings.Current;
            return OnlineMeterRules.For(s.MediaSource, s.HasRemoteMediaConsent, ScrolllerPolicy.Enabled,
                ScrolllerAllowance.Percent, ScrolllerAllowance.Spent, ScrolllerAllowance.Plan, ScrolllerPolicy.Official,
                s.ScrolllerOfficialNoteSeen);
        }

        /// <summary>Repaints the pill and the card from the live allowance. UI thread.</summary>
        internal void RefreshOnlineMeter()
        {
            try
            {
                var st = OnlineMeterNow();
                PaintOnlineMeterPill(st);
                PaintOnlineMeterCard(st);
            }
            catch (Exception ex) { Log.Debug("Online meter refresh failed: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------- title bar pill

        private void PaintOnlineMeterPill(OnlineMeterState st)
        {
            if (Named<Border>("OnlineMeterPill") is not { } pill) return;
            pill.IsVisible = st.Show;
            if (!st.Show || !st.Spent) CloseOnlineMeterHover();
            // An open hover card follows a plan change (its upsell line and "See plans" tier).
            else if (_onlineMeterHover is { IsOpen: true, Child: Border holder }) holder.Child = BuildOnlineMeterHoverCard(st);
            if (!st.Show) return;

            if (_onlineMeterPillRing == null && Named<Decorator>("OnlineMeterPillRing") is { } host)
                host.Child = _onlineMeterPillRing = new OnlineMeterRing();
            if (_onlineMeterPillRing != null)
            {
                _onlineMeterPillRing.Percent = st.Percent;
                _onlineMeterPillRing.Tone = st.Tone;
            }
            if (Named<TextBlock>("TxtOnlineMeterPill") is { } text)
            {
                text.Text = st.Spent ? Loc.Get("online_pill_done") : string.Format(Loc.Get("online_pill"), st.Percent);
                text.Foreground = st.Spent ? OnlineText2 : Brushes.White;
            }
            pill.Background = new SolidColorBrush(st.Tone switch
            {
                OnlineMeterTone.Spent => Color.FromRgb(0x2E, 0x2E, 0x4A),
                OnlineMeterTone.Amber => Color.FromRgb(0x2E, 0x24, 0x16),
                _ => Color.FromRgb(0x25, 0x1A, 0x38),
            });
            // Spent: the hover card says it all; otherwise a plain tooltip names the pill.
            ToolTip.SetTip(pill, st.Spent ? null : Loc.Get("online_meter_label"));
        }

        /// <summary>Click = the Assets tab, where the Media source block (and the card) sit at the top.</summary>
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
                var st = OnlineMeterNow();
                if (!st.Show || !st.Spent || Named<Border>("OnlineMeterPill") is not { } pill) return;
                var holder = new Border { Padding = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent, Child = BuildOnlineMeterHoverCard(st) };
                holder.PointerEntered += (_, _) => _onlineMeterHoverLeave?.Stop();
                holder.PointerExited += (_, _) => _onlineMeterHoverLeave?.Start();
                if (_onlineMeterHover == null)
                {
                    // Hover-open without light dismiss; a short grace to cross onto the card (popup house rules).
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

        private void CloseOnlineMeterHover()
        {
            _onlineMeterHoverLeave?.Stop();
            if (_onlineMeterHover != null) _onlineMeterHover.IsOpen = false;
        }

        private Border BuildOnlineMeterHoverCard(OnlineMeterState st)
        {
            var body = new StackPanel { Width = 276 };
            body.Children.Add(new TextBlock
            {
                Text = Loc.Get("online_hover_title"),
                FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = OnlineText, TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(new TextBlock
            {
                Text = Loc.Get("online_hover_body"),
                FontSize = 12, Foreground = OnlineText2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            });
            if (st.UpsellKey != null)
                body.Children.Add(new TextBlock
                {
                    Text = Loc.Get(st.UpsellKey),
                    FontSize = 12, Foreground = OnlineLilac, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                });
            if (st.SeePlansTier is int tier)
            {
                var btn = new Button
                {
                    // TextBlock content: Avalonia's Button would read '_' in a label as an access key.
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
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 14, 16, 14),
                Child = body,
            };
        }

        /// <summary>"See plans": the vault gate card on the next tier up (Free sees Basic, Basic sees Prime); a
        /// patron whose grant died here is sent to Reconnect, as every gate does.</summary>
        internal void OpenOnlinePlans(int tier)
        {
            CloseOnlineMeterHover();
            try
            {
                if (TierGate.ReconnectIsTheAnswer()) { _ = ReconnectFromGate(); return; }
                ShowVaultGate(null, tier);
            }
            catch (Exception ex) { Log.Debug("Online meter plans: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------- Assets tab card

        private void PaintOnlineMeterCard(OnlineMeterState st)
        {
            if (Named<Tabs.AssetsTabView>("AssetsTab") is not { } tab) return;
            tab.OnlineMeterCard.IsVisible = st.Show;
            if (!st.Show) return;

            var tone = new SolidColorBrush(OnlineMeterRing.ColorFor(st.Tone));
            tab.TxtOnlineMeterPct.Text = string.Format(Loc.Get("online_meter_pct"), st.Percent);
            tab.TxtOnlineMeterPct.Foreground = st.Spent ? OnlineText2 : tone;
            tab.OnlineMeterBar.ColumnDefinitions[0].Width = new GridLength(st.Percent, GridUnitType.Star);
            tab.OnlineMeterBar.ColumnDefinitions[1].Width = new GridLength(100 - st.Percent, GridUnitType.Star);
            tab.OnlineMeterBarFill.Background = tone;

            var inlines = tab.TxtOnlineMeterLine.Inlines ??= new InlineCollection();
            inlines.Clear();
            if (st.Spent)
            {
                var (key, value) = OnlineMeterRules.BackIn(ScrolllerAllowance.Now(), ScrolllerAllowance.NextResetLocal);
                var inText = string.Format(Loc.Get(key), value);
                inlines.Add(new Run(string.Format(Loc.Get("online_meter_spent"), inText)) { Foreground = OnlineText, FontSize = 12.5 });
            }
            else
            {
                inlines.Add(new Run(Loc.Get(st.UnderKey)));
            }
            if (st.UpsellKey != null)
                inlines.Add(new Run("   " + Loc.Get(st.UpsellKey)) { Foreground = OnlineLilac });
            if (st.SeePlansTier is int tier)
            {
                var link = new SafeHyperlinkButton
                {
                    Content = new TextBlock
                    {
                        Text = Loc.Get("online_see_plans"),
                        FontSize = tab.TxtOnlineMeterLine.FontSize,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = this.TryFindResource("PinkBrush", out var pink) && pink is IBrush b ? b : Brushes.HotPink,
                    },
                    Padding = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                link.Click += (_, _) => OpenOnlinePlans(tier);
                inlines.Add(new Run("   "));
                inlines.Add(new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.Center });
            }
        }

        // ---------------------------------------------------------------- the spent notice

        /// <summary>SpentReached (once per local day): the notice waits while a session, Lockdown, Strict Lock or
        /// program holds (WPF: the corner stack folds away under a hold).</summary>
        internal void QueueOnlineSpentNotice()
        {
            RefreshOnlineMeter();
            _onlineSpentQueue.Arm(ScrolllerAllowance.Now());
            ReleaseOnlineSpentNotice();
        }

        internal void ReleaseOnlineSpentNotice()
        {
            try
            {
                // A notice that waited out a hold is dropped when the share is no longer spent: another
                // account signed in (the share is per account) or the plan went up.
                if (_onlineSpentQueue.TryRelease(ScrolllerAllowance.Now(), FriendsLanding.Holding()) && ScrolllerAllowance.Spent)
                    ShowOnlineSpentNotice();
                if (_onlineSpentQueue.Pending) _onlineSpentWait?.Start();
                else _onlineSpentWait?.Stop();
            }
            catch (Exception ex) { Log.Debug("Online spent notice queue: {E}", ex.Message); }
        }

        private void ShowOnlineSpentNotice()
        {
            try
            {
                RefreshOnlineMeter();
                var tier = OnlineMeterRules.SpentNoticePlansTier(ScrolllerAllowance.Plan, FriendsLanding.LockdownActive());
                App.Notifications.Show(Loc.Get("online_toast_spent"), Helpers.NotificationType.Info,
                    TimeSpan.FromMilliseconds(OnlineMeterRules.NoticeLifetimeMs),
                    tier != null ? Loc.Get("online_see_plans") : null,
                    tier is int t ? () => OpenOnlinePlans(t) : null);
            }
            catch (Exception ex) { Log.Debug("Online spent notice: {E}", ex.Message); }
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.FriendsWindows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel
{
    // "Online media today" (owner, 2026-10-19): the title bar pill, the card in the Assets tab's
    // Media source block, the one-time "official" note and the once-a-day spent notice. What each
    // shows is decided by OnlineMeterRules; this file only paints it. Percent only, never MB.
    public partial class MainWindow
    {
        private static readonly Brush OnlineLilac = Frozen(Color.FromRgb(0xE0, 0xB0, 0xFF));
        private static readonly Brush OnlineText = Frozen(Color.FromRgb(0xF0, 0xF0, 0xF5));
        private static readonly Brush OnlineText2 = Frozen(Color.FromRgb(0xCC, 0xCC, 0xE0));

        private bool _onlineMeterWired;
        /// <summary>One refresh a second at most while charges stream in (a clip charges every 256 KB).</summary>
        private DispatcherTimer? _onlineMeterThrottle;
        /// <summary>A minute tick: "Back in 6 h" counts down, and it backs up ScrolllerAllowance.Changed
        /// (raised at midnight and on a plan or limit change) should one ever be missed.</summary>
        private DispatcherTimer? _onlineMeterClock;
        private OnlineMeterRing? _onlineMeterPillRing;
        private Popup? _onlineMeterHover;
        private DispatcherTimer? _onlineMeterHoverLeave;
        private bool _onlineOfficialNoteLanded;
        /// <summary>The licence card is on the startup ladder for this run (one offer per run).</summary>
        private bool _onlineLicenceCardQueued;
        /// <summary>The spent notice waits out a session / Lockdown / Strict Lock / program hold.</summary>
        private readonly OnlineSpentNoticeQueue _onlineSpentQueue = new();
        /// <summary>Polls for the hold's end, only while a notice waits (FriendsLanding's cadence).</summary>
        private DispatcherTimer? _onlineSpentWait;
        /// <summary>The Assets card's "?" (how the daily share works).</summary>
        private Button? _onlineMeterHelp;

        /// <summary>Hooks the allowance events, the timers, the pill hover and the note's "Nice".
        /// Idempotent; called once the window has loaded.</summary>
        private void WireOnlineMeter()
        {
            if (_onlineMeterWired) return;
            _onlineMeterWired = true;
            try
            {
                _onlineMeterThrottle = new DispatcherTimer(DispatcherPriority.Normal) { Interval = OnlineMeterRules.Throttle };
                _onlineMeterThrottle.Tick += (_, _) => { _onlineMeterThrottle.Stop(); RefreshOnlineMeter(); };
                _onlineMeterClock = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMinutes(1) };
                _onlineMeterClock.Tick += (_, _) => RefreshOnlineMeter();
                _onlineMeterClock.Start();
                _onlineSpentWait = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(3) };
                _onlineSpentWait.Tick += (_, _) => ReleaseOnlineSpentNotice();

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

                // Hover-open without capture, a short grace to cross onto the card (popup house rules).
                _onlineMeterHoverLeave = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(220) };
                _onlineMeterHoverLeave.Tick += (_, _) =>
                {
                    _onlineMeterHoverLeave.Stop();
                    if (!OnlineMeterPill.IsMouseOver && !(_onlineMeterHover?.Child is UIElement c && c.IsMouseOver))
                        CloseOnlineMeterHover();
                };
                OnlineMeterPill.MouseEnter += (_, _) =>
                {
                    _onlineMeterHoverLeave.Stop();
                    if (ScrolllerAllowance.Spent) OpenOnlineMeterHover();
                };
                OnlineMeterPill.MouseLeave += (_, _) => _onlineMeterHoverLeave.Start();

                var tab = AssetsTab;
                if (tab != null)
                {
                    tab.BtnOnlineOfficialNice.Click += (_, _) => OnlineOfficialNice();
                    tab.OnlineOfficialNote.IsVisibleChanged += (_, e) =>
                    {
                        if (e.NewValue is true && !_onlineOfficialNoteLanded) { _onlineOfficialNoteLanded = true; LandOnlineOfficialNote(); }
                    };
                    // The "?" slip (owner, 2026-10-19): hover opens, a click pins, the leash slips' rules.
                    _onlineMeterHelp = Controls.Leash.LeashHelpButton.Create(() => new OnlineMediaHelpSlip(), "online-help");
                    tab.OnlineMeterHelpHost.Content = _onlineMeterHelp;
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Online meter wiring failed"); }
            RefreshOnlineMeter();
        }

        private void OnOnlineAllowanceChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (_onlineMeterThrottle is { IsEnabled: false } t) t.Start();
                }));
            }
            catch { /* dispatcher shutting down */ }
        }

        private void OnOnlineAllowanceSpent(object? sender, EventArgs e)
        {
            try { Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(QueueOnlineSpentNotice)); }
            catch { /* dispatcher shutting down */ }
        }

        /// <summary>The pill, the card and the hover card are filled in code (Loc.Get), so the
        /// live {loc:Str} bindings do not reach them: repaint on a language switch.</summary>
        private void OnOnlineMeterLanguageChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    CloseOnlineMeterHover();
                    RefreshOnlineMeter();
                }));
            }
            catch { /* dispatcher shutting down */ }
        }

        private static OnlineMeterState OnlineMeterNow()
        {
            var settings = App.Settings?.Current;
            return OnlineMeterRules.For(settings?.MediaSource, settings?.HasRemoteMediaConsent == true,
                ScrolllerPolicy.Enabled, ScrolllerAllowance.Percent,
                ScrolllerAllowance.Spent, ScrolllerAllowance.Plan, ScrolllerPolicy.Official,
                settings?.ScrolllerOfficialNoteSeen == true);
        }

        /// <summary>Repaints the pill, the card and the note from the live allowance. UI thread.</summary>
        internal void RefreshOnlineMeter()
        {
            try
            {
                var st = OnlineMeterNow();
                PaintOnlineMeterPill(st);
                PaintOnlineMeterCard(st);
                OfferOnlineLicenceCard(st);
            }
            catch (Exception ex) { App.Logger?.Debug("Online meter refresh failed: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------- title bar pill

        private void PaintOnlineMeterPill(OnlineMeterState st)
        {
            OnlineMeterPill.Visibility = st.Show ? Visibility.Visible : Visibility.Collapsed;
            if (!st.Show || !st.Spent) CloseOnlineMeterHover();
            // An open hover card follows a plan change (its upsell line and "See plans" tier).
            else if (_onlineMeterHover is { IsOpen: true, Child: Border holder }) holder.Child = BuildOnlineMeterHoverCard(st);
            if (!st.Show) return;

            if (_onlineMeterPillRing == null)
            {
                _onlineMeterPillRing = new OnlineMeterRing();
                OnlineMeterPillRing.Child = _onlineMeterPillRing;
            }
            _onlineMeterPillRing.Percent = st.Percent;
            _onlineMeterPillRing.Tone = st.Tone;
            TxtOnlineMeterPill.Text = st.Spent
                ? Loc.Get("online_pill_done")
                : string.Format(Loc.Get("online_pill"), st.Percent);
            TxtOnlineMeterPill.Foreground = st.Spent ? OnlineText2 : Brushes.White;
            OnlineMeterPill.Background = st.Tone switch
            {
                OnlineMeterTone.Spent => Frozen(Color.FromRgb(0x2E, 0x2E, 0x4A)),
                OnlineMeterTone.Amber => Frozen(Color.FromRgb(0x2E, 0x24, 0x16)),
                _ => Frozen(Color.FromRgb(0x25, 0x1A, 0x38)),
            };
            // Spent: the hover card says it all; otherwise a plain tooltip names the pill.
            OnlineMeterPill.ToolTip = st.Spent ? null : Loc.Get("online_meter_label");
        }

        /// <summary>Click = the Assets tab, where the Media source block (and the card) sit at the top.</summary>
        private void OnlineMeterPill_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;   // never a title bar drag or a double-click maximise
            CloseOnlineMeterHover();
            try
            {
                ShowTab("assets");
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try { AssetsTab?.RemoteMediaBlock?.BringIntoView(); } catch { /* tab gone */ }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("Online meter pill click: {E}", ex.Message); }
        }

        private void OpenOnlineMeterHover()
        {
            if (_onlineMeterHover is { IsOpen: true }) return;
            try
            {
                var st = OnlineMeterNow();
                if (!st.Show || !st.Spent) return;
                var holder = new Border { Padding = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent, Child = BuildOnlineMeterHoverCard(st) };
                holder.MouseEnter += (_, _) => _onlineMeterHoverLeave?.Stop();
                holder.MouseLeave += (_, _) => _onlineMeterHoverLeave?.Start();
                // A Popup never activates its owner: do it on the press, before the button acts.
                holder.PreviewMouseDown += (_, _) => { try { if (!IsActive) Activate(); } catch { } };
                _onlineMeterHover = new Popup
                {
                    Child = holder,
                    AllowsTransparency = true,
                    StaysOpen = true,
                    Placement = PlacementMode.Bottom,
                    PlacementTarget = OnlineMeterPill,
                    PopupAnimation = PopupAnimation.None,
                    Focusable = false,
                };
                _onlineMeterHover.Opened += (_, _) => DropOnlineMeterHoverTopmost();
                _onlineMeterHover.IsOpen = true;
            }
            catch (Exception ex) { App.Logger?.Debug("Online meter hover: {E}", ex.Message); }
        }

        private void CloseOnlineMeterHover()
        {
            _onlineMeterHoverLeave?.Stop();
            if (_onlineMeterHover != null) _onlineMeterHover.IsOpen = false;
            _onlineMeterHover = null;
        }

        /// <summary>A Popup is TOPMOST by default: drop it so it never floats over other apps.</summary>
        private void DropOnlineMeterHoverTopmost()
        {
            try
            {
                if (_onlineMeterHover?.Child is { } c && PresentationSource.FromVisual(c) is HwndSource src && src.Handle != IntPtr.Zero)
                    OnlineMeterSetWindowPos(src.Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
            catch (Exception ex) { App.Logger?.Debug("Online meter hover topmost drop: {E}", ex.Message); }
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
        private static extern bool OnlineMeterSetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private Border BuildOnlineMeterHoverCard(OnlineMeterState st)
        {
            var body = new StackPanel { Width = 276 };
            var head = new DockPanel();
            // The "?" here leads to the Assets card's slip: a slip popping out of this hover card
            // would fold the moment the pointer crossed onto it.
            var help = OnlineHelpGlyph();
            DockPanel.SetDock(help, Dock.Right);
            help.Margin = new Thickness(8, 0, 0, 0);
            help.Click += (_, e) => { e.Handled = true; OpenOnlineMeterHelp(); };
            head.Children.Add(help);
            head.Children.Add(new TextBlock
            {
                Text = Loc.Get("online_hover_title"),
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = OnlineText, TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(head);
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
                    Content = Loc.Get("online_see_plans"),
                    Style = TryFindResource("SmallPinkButton") as Style,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 5, 12, 5),
                    FontSize = 12,
                    Margin = new Thickness(0, 10, 0, 0),
                };
                btn.Click += (_, _) => OpenOnlinePlans(tier);
                body.Children.Add(btn);
            }
            return new Border
            {
                Background = Frozen(Color.FromRgb(0x18, 0x18, 0x30)),
                BorderBrush = Frozen(Color.FromRgb(0x3A, 0x3A, 0x5C)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 14, 16, 14),
                Child = body,
            };
        }

        /// <summary>A "?" in the leash slips' look that only clicks (no hover slip of its own).</summary>
        private static Button OnlineHelpGlyph(double size = 16)
        {
            var t = new TextBlock
            {
                Text = "?",
                FontFamily = Controls.Friends.FriendsLook.Display,
                FontWeight = FontWeights.Bold,
                FontSize = size * 0.62,
                Foreground = Controls.Friends.FriendsLook.LilacBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var b = Controls.Friends.FriendsLook.Pill(t, Frozen(Color.FromRgb(0x24, 0x18, 0x38)), Controls.Friends.FriendsLook.LilacBrush,
                Frozen(Color.FromArgb(0x66, 0xB9, 0x9C, 0xFF)), size / 2, new Thickness(0));
            b.Width = size;
            b.Height = size;
            b.Focusable = false;
            b.VerticalAlignment = VerticalAlignment.Top;
            b.Cursor = Cursors.Help;
            b.Tag = "online-help-open";
            return b;
        }

        /// <summary>The hover card's "?": the Assets tab, with the card's slip pinned open.</summary>
        private void OpenOnlineMeterHelp()
        {
            CloseOnlineMeterHover();
            try
            {
                ShowTab("assets");
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    try
                    {
                        AssetsTab?.RemoteMediaBlock?.BringIntoView();
                        if (_onlineMeterHelp is { IsVisible: true } q) Controls.Leash.LeashHelpButton.Pin(q);
                    }
                    catch { /* tab gone */ }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("Online meter help: {E}", ex.Message); }
        }

        /// <summary>"See plans": the existing vault gate card on the next tier up (Free sees Basic,
        /// Basic sees Prime); a patron whose grant died here is sent to Reconnect, as every gate does.</summary>
        private void OpenOnlinePlans(int tier)
        {
            CloseOnlineMeterHover();
            try
            {
                if (TierGate.ReconnectIsTheAnswer()) { StartPatreonReconnectFromGate(); return; }
                ShowVaultGate(null, tier);
            }
            catch (Exception ex) { App.Logger?.Debug("Online meter plans: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------- Assets tab card

        private void PaintOnlineMeterCard(OnlineMeterState st)
        {
            var tab = AssetsTab;
            if (tab == null) return;
            tab.OnlineMeterCard.Visibility = st.Show ? Visibility.Visible : Visibility.Collapsed;
            tab.OnlineMeterStamped.Visibility = st.ShowStampedLine ? Visibility.Visible : Visibility.Collapsed;
            // The linked "Media from Scrolller" in the fold stays up official or not (licence 3.2):
            // the stamped line under the meter does not carry the credit's own words.
            tab.TxtScrolllerCredit.Visibility = Visibility.Visible;
            PaintOnlineOfficialNote(st);
            if (!st.Show) return;

            var tone = new SolidColorBrush(OnlineMeterRing.ColorFor(st.Tone));
            tone.Freeze();
            tab.TxtOnlineMeterPct.Text = string.Format(Loc.Get("online_meter_pct"), st.Percent);
            tab.TxtOnlineMeterPct.Foreground = st.Spent ? OnlineText2 : tone;
            tab.OnlineMeterBarOn.Width = new GridLength(st.Percent, GridUnitType.Star);
            tab.OnlineMeterBarOff.Width = new GridLength(100 - st.Percent, GridUnitType.Star);
            tab.OnlineMeterBarFill.Background = tone;

            var line = tab.TxtOnlineMeterLine;
            line.Inlines.Clear();
            if (st.Spent)
            {
                var (key, value) = OnlineMeterRules.BackIn(DateTime.Now, ScrolllerAllowance.NextResetLocal);
                var inText = string.Format(Loc.Get(key), value);
                line.Inlines.Add(new Run(string.Format(Loc.Get("online_meter_spent"), inText)) { Foreground = OnlineText, FontSize = 12.5 });
            }
            else
            {
                line.Inlines.Add(new Run(Loc.Get(st.UnderKey)));
            }
            if (st.UpsellKey != null)
                line.Inlines.Add(new Run("   " + Loc.Get(st.UpsellKey)) { Foreground = OnlineLilac });
            if (st.SeePlansTier is int tier)
            {
                var link = new Hyperlink(new Run(Loc.Get("online_see_plans")))
                {
                    Foreground = (Brush?)TryFindResource("PinkBrush") ?? Brushes.HotPink,
                    FontWeight = FontWeights.SemiBold,
                    TextDecorations = null,
                    Cursor = Cursors.Hand,
                };
                link.Click += (_, _) => OpenOnlinePlans(tier);
                line.Inlines.Add(new Run("   "));
                line.Inlines.Add(link);
            }
        }

        // ---------------------------------------------------------------- the official note

        private void PaintOnlineOfficialNote(OnlineMeterState st)
        {
            var tab = AssetsTab;
            if (tab == null) return;
            if (!st.ShowOfficialNote)
            {
                tab.OnlineOfficialNote.Visibility = Visibility.Collapsed;
                return;
            }
            tab.TxtOnlineOfficialPlan.Text = Loc.Get(OnlineMeterRules.OfficialPlanKey(st.Plan));
            tab.OnlineOfficialNote.Visibility = Visibility.Visible;
        }

        /// <summary>The note lands once, the first time it is actually on screen: a short rise and
        /// fade in (GentleVisuals lengthens it, motion Off skips it).</summary>
        private void LandOnlineOfficialNote()
        {
            var note = AssetsTab?.OnlineOfficialNote;
            if (note == null || MotionFx.Level == MotionLevel.Off) return;
            try
            {
                var ms = GentleVisuals.RiseMs(420);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                if (note.RenderTransform is TranslateTransform slide && MotionFx.Level == MotionLevel.Full)
                    slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
                note.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
            }
            catch (Exception ex) { App.Logger?.Debug("Official note landing: {E}", ex.Message); }
        }

        /// <summary>The licence card (owner, 2026-10-21): the same gate as the note, as a pop-up on
        /// the startup ladder so it waits out games and other startup surfaces. Closing it counts as
        /// "Nice"; a card that never got its turn is offered again next launch.
        /// <para>The ladder's modal pump does NOT wait for a session or a Lockdown (only the passive
        /// route does), and the flag can flip mid-run, so the card asks the same hold the spent
        /// notice does: while it holds nothing is queued, and the minute clock offers it again once
        /// the hold ends. A card queued before a session started steps aside at show time.</para></summary>
        private void OfferOnlineLicenceCard(OnlineMeterState st)
        {
            if (!st.ShowOfficialNote || _onlineLicenceCardQueued || FriendsLanding.Holding()) return;
            _onlineLicenceCardQueued = true;
            EnqueueStartupModal("online-licence", 40, owner =>
            {
                // The flag or the source may have changed while it waited for its turn.
                var now = OnlineMeterNow();
                if (!now.ShowOfficialNote) return;
                if (FriendsLanding.Holding()) { _onlineLicenceCardQueued = false; return; }
                new OnlineLicenceCard(now.Plan) { Owner = owner ?? this }.ShowDialog();
                OnlineOfficialNice();
            });
        }

        /// <summary>"Nice": the note folds away for good on this PC; the stamped line stays.</summary>
        private void OnlineOfficialNice()
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings != null)
                {
                    settings.ScrolllerOfficialNoteSeen = true;
                    App.Settings?.Save();
                }
                RefreshOnlineMeter();
            }
            catch (Exception ex) { App.Logger?.Debug("Official note Nice: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------- the spent notice

        /// <summary>SpentReached (once per local day): the notice waits while a session, Lockdown,
        /// Strict Lock or program holds, because the corner stack folds away under a hold.</summary>
        private void QueueOnlineSpentNotice()
        {
            RefreshOnlineMeter();
            _onlineSpentQueue.Arm(DateTime.Now);
            ReleaseOnlineSpentNotice();
        }

        private void ReleaseOnlineSpentNotice()
        {
            try
            {
                // A notice that waited out a hold is dropped when the share is no longer spent:
                // another account signed in (the share is per account) or the plan went up.
                if (_onlineSpentQueue.TryRelease(DateTime.Now, FriendsLanding.Holding()) && ScrolllerAllowance.Spent)
                    ShowOnlineSpentNotice();
                if (_onlineSpentQueue.Pending) _onlineSpentWait?.Start();
                else _onlineSpentWait?.Stop();
            }
            catch (Exception ex) { App.Logger?.Debug("Online spent notice queue: {E}", ex.Message); }
        }

        /// <summary>A corner notice through the app's corner notification stack, bottom right, never
        /// over the screen. A hold that folds it before it is read puts it back in the queue.</summary>
        private void ShowOnlineSpentNotice()
        {
            try
            {
                RefreshOnlineMeter();
                var tier = OnlineMeterRules.SpentNoticePlansTier(ScrolllerAllowance.Plan, FriendsLanding.LockdownActive());
                var notice = new FriendNotice(NoticeKind.Watch, "ccp:online-media", Loc.Get("online_meter_label"),
                    DateTimeOffset.UtcNow, OnlineMeterRules.NoticeLifetimeMs);
                FriendNotices.Show(FriendsLanding.NoticeAnchor(), notice, new NoticeLook
                {
                    Line = Loc.Get("online_toast_spent"),
                    Icon = () => new Grid
                    {
                        Width = 34,
                        VerticalAlignment = VerticalAlignment.Top,
                        Children = { new OnlineMeterRing { Width = 22, Height = 22, Tone = OnlineMeterTone.Spent, Percent = 100, Margin = new Thickness(0, 4, 0, 0) } },
                    },
                    ActionLabel = tier != null ? Loc.Get("online_see_plans") : null,
                    Act = tier is int t ? _ => OpenOnlinePlans(t) : null,
                    // Missed = ran out, pushed off, or folded by a hold; only the hold brings it back.
                    Missed = _ =>
                    {
                        if (!FriendsLanding.Holding()) return;
                        _onlineSpentQueue.Arm(DateTime.Now);
                        _onlineSpentWait?.Start();
                    },
                });
            }
            catch (Exception ex) { App.Logger?.Debug("Online spent notice: {E}", ex.Message); }
        }
    }
}

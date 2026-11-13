using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/PlayTabView.xaml.cs (the host) and
    /// PlayTabView.Cards.cs (the shims).
    ///
    /// <para>The Play door (tab key <c>play</c>): a card wall over the game-shaped features.</para>
    ///
    /// <para><b>This file is the host, the painter (WPF MainWindow.PlayTab.cs RefreshPlayCards)
    /// and the shims.</b> Every click WPF answers with <c>ShowTab</c> is the shell's <c>ShowTab</c>
    /// here. Section entrances follow MainWindow/MainWindow.ChromeFx.cs:248; no ambient loop
    /// runs on this wall.</para>
    /// </summary>
    public partial class PlayTabView : UserControl
    {
        /// <summary>The one cast every shim makes - the port of WPF's
        /// <c>Window.GetWindow(this) as MainWindow</c>. Null while the view is being built and under
        /// <c>--render-view</c>, where a card that fires simply does nothing, exactly as WPF's
        /// designer case does.</summary>
        private MainShellWindow? Owner => TopLevel.GetTopLevel(this) as MainShellWindow;

        public PlayTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields.
            InitializeComponent();

            // WPF binds the two Breakout covers with x:Static BreakoutCardArt.Demo/Full.
            PlayBreakoutDemoArt.Source = BreakoutCardArt.Demo;
            PlayBreakoutArt.Source = BreakoutCardArt.Full;

            // The hero plates, and the repaint that keeps them honest across a mod switch. Not
            // deferred to attach: a card that draws its scrim first and its art a frame later flickers.
            RefreshGameDoors();
            RefreshHeroArt();
            RefreshWebcamStatus();
        }

        // Subscribed ONCE PER ATTACH, off on every detach (P41). WPF repaints the wall on arrival
        // (ShowTab "play"), on entitlement change (Patreon.cs) and on IntakePassService.PassStateChanged.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Platform.WebcamTracker.Instance.StateChanged += RefreshWebcamStatus;
            RefreshWebcamStatus();
            Gaze.ActiveChanged += OnGazeActiveChanged;
            SyncFocusGazeToggle(CoreSettings.Current.FocusGazeEnabled);
            CoreMods.ModChanged += OnModChangedRepaintArt;
            RefreshHeroArt();
            App.IntakePass.PassStateChanged += OnIntakePassStateChanged;
            LocalizationManager.Instance.LanguageChanged += OnIntakePassStateChanged;
            RefreshPlayCards();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Platform.WebcamTracker.Instance.StateChanged -= RefreshWebcamStatus;
            Gaze.ActiveChanged -= OnGazeActiveChanged;
            CoreMods.ModChanged -= OnModChangedRepaintArt;
            App.IntakePass.PassStateChanged -= OnIntakePassStateChanged;
            LocalizationManager.Instance.LanguageChanged -= OnIntakePassStateChanged;
            base.OnDetachedFromVisualTree(e);
        }

        // The shell shows a tab by flipping IsVisible (P01), which is WPF's ShowTab "play" arrival.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible && VisualRoot != null)
            {
                RefreshPlayCards();
                RefreshWebcamStatus();
            }
        }

        internal void RefreshWebcamStatus()
        {
            var state = Platform.WebcamTracker.Instance.State;
            WebcamStatusPresentation.Paint(this, "WebcamStatusChipPlayDot", "TxtWebcamStatusChipPlay", state);
            bool live = WebcamStatus.IsLive(state);
            PlayGazeCard.Opacity = PlayFocusCard.Opacity = live ? 1.0 : 0.62;
            PlayGazeNeedsTracker.IsVisible = PlayFocusNeedsTracker.IsVisible = !live;
        }

        // The pass is spent (or entitlement lands) off the UI thread.
        private void OnIntakePassStateChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() => { RefreshPlayCards(); RefreshWebcamStatus(); RefreshGameDoors(); });

        // The shell owns the one entrance: WPF MainWindow/MainWindow.ChromeFx.cs:248,375.
        // MainShellWindow.TabTransitions.cs keeps delayed sections at base values until BeginTime.

        /// <summary>
        /// Every card hero, WPF's <c>PlayTabView.xaml</c> table restated: the Border that owns the
        /// plate, the Resources-relative art, and the Stretch each one was authored with.
        /// <c>lockdown_icon.png</c> sits at the RESOURCE ROOT, the path a mod keys its override against.
        /// </summary>
        // WPF MainWindow/MainWindow.xaml.cs:2933 sizes Play hero decodes.
        // Chess stays full size as in WPF Views/Tabs/PlayTabView.xaml:447.
        private static readonly (string Plate, string Art, Stretch Fit, int? DecodeWidth)[] HeroPlates =
        {
            ("PlayBackRoomHeroPlate", "features/backroom.png", Stretch.UniformToFill, null),
            ("PlayDtrhHeroPlate", "features/dtrh.png", Stretch.UniformToFill, null),
            ("PlayArcademyHeroPlate", "features/arcademy.png", Stretch.UniformToFill, null),
            ("PlayRacingThoughtsHeroPlate", "features/race.png", Stretch.UniformToFill, null),
            ("PlayWebAppHeroPlate", "billboard/webapp.png", Stretch.UniformToFill, null),
            ("PlayGoonHeroPlate",     "features/goon_game_tile.png",    Stretch.UniformToFill, 512),
            ("PlayChessHeroPlate",    "features/piecebypiece.png",      Stretch.UniformToFill, null),
            ("PlayRemoteHeroPlate",   "features/remote_control.png",    Stretch.UniformToFill, 768),
            ("PlayGazeHeroPlate",     "features/lab_gaze_hero.png",     Stretch.UniformToFill, 512),
            ("PlayFocusHeroPlate",    "features/lab_focusgaze_hero.png",Stretch.UniformToFill, 512),
            ("PlayBlinkHeroPlate",    "features/blink_trainer.png",     Stretch.UniformToFill, 512),
            ("PlayIntakeHeroPlate",   "features/lab_quiz_hero.png",     Stretch.UniformToFill, 512),
            ("PlayFypHeroPlate",      "features/fyp.png",               Stretch.UniformToFill, 512),
            ("PlayLockdownHeroPlate", "lockdown_icon.png",              Stretch.UniformToFill, 1024),
            ("PlayLoomHeroPlate",     "features/loom.png",              Stretch.UniformToFill, 512),
        };

        private void OnModChangedRepaintArt(object? sender, ModPackage? mod) =>
            Dispatcher.UIThread.Post(RefreshHeroArt);

        /// <summary>
        /// Paints each hero plate, mod override first (<see cref="ModArt.TryLoad"/>), this head's
        /// shipped avares:// copy second. A null resolve LEAVES the plate as it is.
        /// </summary>
        private void RefreshHeroArt()
        {
            foreach (var (plateName, art, fit, decodeWidth) in HeroPlates)
            {
                try
                {
                    if (this.FindControl<Border>(plateName) is not { } plate) continue;
                    if (ModArt.TryLoad(art, out var modApplied, decodeWidth) is not { } bmp) continue;
                    var brush = new ImageBrush(bmp) { Stretch = fit };
                    // WPF MainWindow.xaml.cs:2954-2989 maps only these Play surfaces.
                    string? surface = art switch
                    {
                        "features/piecebypiece.png" or "features/dtrh.png" => null,
                        "features/remote_control.png" => ModArtFramingRegistry.SurfacePlayCardTall,
                        "features/loom.png" => ModArtFramingRegistry.SurfaceLoomStrip,
                        _ => ModArtFramingRegistry.SurfacePlayCard,
                    };
                    if (surface != null)
                    {
                        ModArtFraming? framing = null;
                        var map = modApplied ? CoreMods.ActiveModPackage?.Manifest.ArtFraming : null;
                        if (map != null)
                            foreach (var entry in map)
                            {
                                if (entry.Value == null || !string.Equals(entry.Key.Replace('\\', '/'), art, StringComparison.OrdinalIgnoreCase)) continue;
                                foreach (var item in entry.Value)
                                    if (string.Equals(item.Key, surface, StringComparison.OrdinalIgnoreCase)) { framing = item.Value; break; }
                                break;
                            }
                        var aspect = bmp.Size.Height > 0 ? bmp.Size.Width / bmp.Size.Height : 0;
                        var viewbox = ModArtFramingRegistry.ResolveViewbox(art, surface, modApplied, aspect, framing);
                        brush.SourceRect = new RelativeRect(viewbox.X, viewbox.Y, viewbox.Width, viewbox.Height, RelativeUnit.Relative);
                    }
                    plate.Background = brush;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[Play] hero art {Art} would not paint", art);
                }
            }
        }

        /// <summary>Gap kept above a zone header after a zone scroll (WPF PlayTabView.ZoneTopGap).</summary>
        internal const double ZoneTopGap = 16;

        internal Control? ZoneHeader(string? zone) => (zone ?? "").Trim().ToLowerInvariant() switch
        {
            "games" => ZoneGames,
            "sessions" => ZoneSessions,
            "eyes" => ZoneEyes,
            _ => null,
        };

        /// <summary>WPF PlayTabView.ScrollToZone (:51), after layout: Games goes to the top so the
        /// intro line shows too; any other zone's header lands ZoneTopGap under the strip, then the
        /// header glows once where the eye goes (WPF :73, ed48b817c).</summary>
        internal void ScrollToZone(string zone)
        {
            zone = (zone ?? "").Trim().ToLowerInvariant();
            if (ZoneHeader(zone) is not { } header) { Log.Debug("Play ScrollToZone({Zone}): unknown zone", zone); return; }
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    double y = 0;
                    // Measured against the viewport, so the content's own margin cannot shift the landing.
                    if (zone != "games" && header.TranslatePoint(default, WallScroll) is { } p)
                        y = Math.Max(0, WallScroll.Offset.Y + p.Y - ZoneTopGap);
                    WallScroll.Offset = new Vector(WallScroll.Offset.X, y);
                    Dispatcher.UIThread.Post(() => global::ConditioningControlPanel.Avalonia.Controls.NavGlow.Once(header,
                        global::ConditioningControlPanel.Avalonia.Views.Controls.NavStripRules.Accent(Services.UI.NavSections.Play), why: "play." + zone), DispatcherPriority.Loaded);
                }
                catch (Exception ex) { Log.Debug("Play ScrollToZone({Zone}): {E}", zone, ex.Message); }
            }, DispatcherPriority.Background);
        }

        /// <summary>WPF MainWindow.PlayTab.cs:83-120 RefreshPlayCards: the tier lockbands (same loc
        /// keys as the refusal), the FREE TODAY stamps and the Graded Intake's four pass states.
        /// Presentation only - TierGate refuses inside each door. Never throws.</summary>
        internal void RefreshPlayCards()
        {
            try
            {
                // BreakoutAccess.FullAllowed (WPF Services/BackRoom/BreakoutAccess.cs:9).
                PlayLockBreakout.IsVisible = !TierGate.RequiresLab(Loc.Get("launcher_game_breakout_title")).Allowed;
                PlayLockArcademy.IsVisible = !TierGate.RequiresLab(Loc.Get("launcher_game_arcademy_title")).Allowed;
                // WPF MainWindow.PlayTab.cs:132: Racing Thoughts requires Lab, with no daily-free key.
                PlayLockRacingThoughts.IsVisible = !TierGate.RequiresLab(Loc.Get("launcher_game_race_title")).Allowed;
                PlayLockGaze.IsVisible = !TierGate.RequiresLab(Loc.Get("label_gaze_minigame")).Allowed;
                PlayLockFocusGaze.IsVisible = !TierGate.RequiresLab(Loc.Get("label_focus_gaze")).Allowed;
                PlayLockRemote.IsVisible = !TierGate.RequiresPremium(Loc.Get("tab_remote_control"), "remote").Allowed;
                PlayLockLockdown.IsVisible = !TierGate.RequiresPremium(Loc.Get("tab_lockdown_mode")).Allowed;
                PlayLockBlink.IsVisible = !TierGate.RequiresPremium(Loc.Get("tab_blink_trainer")).Allowed;
                PlayLockFyp.IsVisible = !TierGate.RequiresPremium(Loc.Get("tab_fyp"), "fyp").Allowed;
                PlayLockDtrh.IsVisible = Games.GameWindow.DtrhLocked();

                // FREE TODAY (WPF :145): only on a door that is otherwise shut - an owner gets no gift.
                bool premium = CoreEntitlement.HasPremium;
                PlayBadgeRemote.FreeToday = !premium && CoreEntitlement.IsFreeToday("remote");
                PlayBadgeFyp.FreeToday = !premium && CoreEntitlement.IsFreeToday("fyp");
                PlayBadgeDtrh.FreeToday = !premium && CoreEntitlement.IsFreeToday("dtrh");


                RefreshPlayIntakeCard();
            }
            catch (Exception ex) { Log.Debug("RefreshPlayCards: {E}", ex.Message); }
        }

        /// <summary>WPF MainWindow.PlayTab.cs:183 RefreshPlayIntakeCard, read from the same
        /// IntakePassService the page's gate reads. Premium: nothing; Available: no band, the
        /// announcement and the "where" button; Spent / NeedsLogin: band + the page's own copy.</summary>
        private void RefreshPlayIntakeCard()
        {
            var state = App.IntakePass.State;
            PlayLockIntake.IsVisible = state is not (IntakePassState.Premium or IntakePassState.Available);
            BtnPlayIntakePassHome.IsVisible = state == IntakePassState.Available;
            var days = IntakePassService.DaysUntilNextPass;
            TxtPlayIntakeState.Text = state switch
            {
                IntakePassState.Available => Loc.Get("pl6_intake_state_available"),
                // Two keys rather than one with a {0}: "unlocks in 1 days" gets screenshotted.
                IntakePassState.Spent => days == 1 ? Loc.Get("intake_gate_spent_body_one_day") : Loc.GetF("intake_gate_spent_body", days),
                IntakePassState.NeedsLogin => Loc.Get("intake_gate_login_body"),
                _ => string.Empty,
            };
            TxtPlayIntakeState.IsVisible = state != IntakePassState.Premium;
        }

        // ==================================================================================
        // Launch shims (WPF PlayTabView.Cards.cs). Every name below is the MainWindow handler the
        // WPF card forwards to. GAMES: the Breakout demo, Piece by Piece, DtRH and Racing Thoughts open their
        // GameWindow as the launcher does (LauncherWindow.Destinations). Full Breakout uses the same host
        // with a Lab gate. Goon, Back Room and Arcademy doors activate when their hosts register.
        // ==================================================================================

        internal void RefreshGameDoors()
        {
            SetGameDoor(BtnPlayGoon, "goon");
            SetGameDoor(BtnPlayBackRoom, "backroom");
            SetGameDoor(BtnPlayArcademy, "arcademy");
        }

        private static void SetGameDoor(Button button, string id)
        {
            button.IsEnabled = Games.GameWindow.Games.ContainsKey(id);
            if (button.IsEnabled) ToolTip.SetTip(button, null);
            else ToolTip.SetTip(button, Loc.Get("exclusives_not_on_this_build"));
        }

        private void BtnPlayGoon_Click(object? sender, RoutedEventArgs e) => Owner?.LaunchPlayGoon();
        private void BtnPlayBackRoom_Click(object? sender, RoutedEventArgs e) => Owner?.LaunchPlayBackRoom();
        private void BtnPlayArcademy_Click(object? sender, RoutedEventArgs e) => Owner?.LaunchPlayLauncherGame("arcademy");

        // WPF MainWindow.PlayTab.cs:79-86.
        private void BtnPlayWebApp_Click(object? sender, RoutedEventArgs e) => Owner?.OpenPlayWebApp();

        // ---- GAMES -----------------------------------------------------------------------

        /// <summary>How a games card opens its game: the launcher's own door. Tests capture it.</summary>
        internal static Action<string> LaunchGame { get; set; } = id => Games.GameWindow.Launch(id);

        /// <summary>WPF MainWindow.PlayTab.cs:56, BreakoutHostService.cs:14-17: Lab gate.</summary>
        private void BtnPlayBreakout_Click(object? sender, RoutedEventArgs e) => Owner?.LaunchPlayBreakout();

        private void BtnPlayBreakoutDemo_Click(object? sender, RoutedEventArgs e) => LaunchGame("breakoutdemo");

        /// <summary>WPF LaunchPlayChess (PieceByPieceHostService.Launch): free for everyone.</summary>
        private void BtnPlayChess_Click(object? sender, RoutedEventArgs e) => LaunchGame(Games.GameWindow.PbpId);

        // WPF PlayTabView.Cards.cs:59-66 -> LaunchExclusiveGame: sign-in first, then DtRH's Basic gate, before any focus.
        private void BtnPlayDtrh_Click(object? sender, RoutedEventArgs e) => Owner?.LaunchPlayLauncherGame(Games.GameWindow.DtrhId);

        private void BtnPlayRacingThoughts_Click(object? sender, RoutedEventArgs e) => Owner?.LaunchPlayLauncherGame("race");

        // ---- TOGETHER --------------------------------------------------------------------

        private void BtnPlayRemoteControl_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("remotecontrol");

        // ---- EYES ------------------------------------------------------------------------

        /// <summary>WPF <c>mw.OpenDeviceSettings()</c>: Settings door, Devices section.</summary>
        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e) => Owner?.OpenDeviceSettings();

        /// <summary>
        /// RESTORED, on the condition the previous note set: "the day an entitlement seam exists".
        /// It does — <c>TierGate</c> is CCP.Core/Services/TierGate.cs over <c>CoreEntitlement</c> —
        /// so this is MainWindow.LabTab.cs:770 verbatim: Tier 2 checked BEFORE the window is
        /// constructed, because the Lab smokescreen is a tab-wide overlay and not a gate on this
        /// door. The window runs on the webcam tracker (consent, start, calibration gates as WPF).
        ///
        /// <para>The seam is seeded by Platform.AccountSeed, so a Tier 2 account opens the window.</para>
        /// </summary>
        private void BtnGazeMinigame_Click(object? sender, RoutedEventArgs e) => OpenGazeMinigame();

        /// <summary>The Play wall button and the Exclusives card (main 2e9080399) share this door and its gate.</summary>
        internal void OpenGazeMinigame()
        {
            if (!TierGate.DemandLab(Loc.Get("label_gaze_minigame"))) return;
            // A non-modal Show still needs a visible owner; the shell can be minimised to tray.
            if (Owner is not { IsVisible: true } owner) return;
            new Lab.GazeMinigame.GazeMinigameWindow().Show(owner);
        }

        // ---- Focus Gaze (WPF MainWindow.LabTab.cs:816-1001) -----------------------------
        private bool _focusGazeSyncing;
        private static Platform.GazeFocus Gaze => Platform.GazeFocus.Instance;

        private void OnGazeActiveChanged(bool _) => RefreshFocusGazeStatus();

        /// <summary>WPF SyncFocusGazeToggle: the argument is the ENABLED setting (user intent), never
        /// the engine's IsActive (#1116).</summary>
        internal void SyncFocusGazeToggle(bool enabled)
        {
            var s = CoreSettings.Current;
            if (s.FocusGazeEnabled != enabled) { s.FocusGazeEnabled = enabled; CoreSettings.Save(); }
            if (ChkPlayFocusGaze.IsChecked != enabled)
            {
                _focusGazeSyncing = true;
                try { ChkPlayFocusGaze.IsChecked = enabled; }
                finally { _focusGazeSyncing = false; }
            }
            RefreshFocusGazeStatus();
        }

        /// <summary>WPF RefreshFocusGazeStatus: the switch says ENABLED, this line says RUNNING.</summary>
        internal void RefreshFocusGazeStatus()
        {
            if (!CoreSettings.Current.FocusGazeEnabled) { TxtPlayFocusGazeStatus.Text = ""; return; }
            TxtPlayFocusGazeStatus.Text = Loc.Get(Gaze.IsActive ? "label_focus_gaze_active" : "label_focus_gaze_waiting");
        }

        /// <summary>The only Focus Gaze switch in the app (WPF ChkFocusGaze_Changed). ON is gated on the
        /// Lab tier, then consent, then a running camera, then the engine actually starting; OFF is
        /// never gated (releasing a camera consumer is not a premium act).</summary>
        private async void ChkFocusGaze_Changed(object? sender, RoutedEventArgs e)
        {
            if (_focusGazeSyncing) return;
            if (ChkPlayFocusGaze.IsChecked != true)
            {
                Gaze.MasterEnabled = false;
                SyncFocusGazeToggle(false);
                return;
            }
            try
            {
                // Revert first, then tell (WPF order): the switch never sits ON behind a refusal.
                var verdict = TierGate.RequiresLab(Loc.Get("label_focus_gaze"));
                if (!verdict.Allowed) { SyncFocusGazeToggle(false); TierGate.ShowDenied(verdict); return; }

                if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    var dlg = new Dialogs.WebcamConsentDialog();
                    await dlg.ShowDialogSafe(TopLevel.GetTopLevel(this) as Window);
                    if (!dlg.ConsentGiven)
                    {
                        SyncFocusGazeToggle(false);
                        TxtPlayFocusGazeStatus.Text = Loc.Get("label_focus_gaze_consent_required");
                        return;
                    }
                }

                // Pre-warm the camera off the UI thread; the engine only rides a running camera.
                var tracker = Platform.WebcamTracker.Instance;
                if (!tracker.IsRunning)
                {
                    TxtPlayFocusGazeStatus.Text = "Starting webcam…";   // WPF literal (LabTab.cs:963)
                    if (!await tracker.StartAsync())
                    {
                        SyncFocusGazeToggle(false);
                        TxtPlayFocusGazeStatus.Text = Loc.GetF("label_focus_gaze_webcam_failed_format", tracker.State);
                        return;
                    }
                }

                Gaze.MasterEnabled = true;
                if (Gaze.IsActive) { SyncFocusGazeToggle(true); return; }
                // Couldn't start (almost always: no calibration). Disarm so it isn't silently wanted.
                Gaze.MasterEnabled = false;
                SyncFocusGazeToggle(false);
                TxtPlayFocusGazeStatus.Text = tracker.Calibration == null
                    ? Loc.Get("label_focus_gaze_calibrate_first")
                    : Loc.GetF("label_focus_gaze_webcam_failed_format", tracker.State);
            }
            catch (Exception ex) { Log.Warning(ex, "Focus Gaze switch failed"); SyncFocusGazeToggle(false); }
        }

        /// <summary>WPF MainWindow.LabTab.cs:1061 is ShowTab("blinktrainer"); the page owns its gate.</summary>
        private void BtnLabBlinkTrainerOpenNew_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("blinktrainer");

        // ---- SESSIONS --------------------------------------------------------------------

        // All four pass states navigate: the page's own gate is what explains a spent week or a
        // missing login, so a locked click has to ARRIVE somewhere rather than be swallowed.
        private void BtnPlayGradedIntake_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("gradedintake");

        /// <summary>"Where does a pass come from?" - the Home logo tile's flip ceremony hands them
        /// out, and "settings" is Home's tab key (the Settings DOOR is "appsettings").</summary>
        private void BtnPlayIntakePassHome_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("settings");

        /// <summary>ShowTab("fyp"), never FypHostService.Launch() - that would be a second, ungated
        /// launch path. The shell intercepts the key into the feed window (MainShellWindow.TabNavigation.cs).</summary>
        private void BtnPlayFyp_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("fyp");

        private void BtnPlayLockdown_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("lockdown");

        // ---- MORE ------------------------------------------------------------------------

        /// <summary>Loom NAVIGATES to the one editor: WPF OpenStudioModule("spiral").</summary>
        private void BtnPlayLoom_Click(object? sender, RoutedEventArgs e) => Owner?.OpenStudioModule("spiral");
    }
}

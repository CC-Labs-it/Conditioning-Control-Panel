using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The Dashboard (tab key <c>settings</c>), PORTED from
    /// ConditioningControlPanel/Views/Tabs/SettingsTabView.xaml.cs.
    ///
    /// <para><b>This file is the host only.</b> On WPF all but three of its members are one-line
    /// shims that look up the hosting MainWindow and forward to a MainWindow partial of the same
    /// name - the premium rail, the mosaic wall, the browser card, the account strip and the
    /// quick-toggle pills every one of them. MainWindow is a WPF type, so on this head each shim
    /// is a stub wired to the same control with the same handler name, so the eventual wiring
    /// diffs cleanly.</para>
    ///
    /// <para><b>The three that are NOT shims are ported for real:</b> the marquee's edge-fade
    /// mask (it needs nothing but its own width), the advanced-audio disclosure (purely local -
    /// nothing below it owns state) and starting the one ambient canvas.</para>
    ///
    /// <para>The <c>IsVisibleChanged</c> hook that tells
    /// <c>MainShellWindow.OnDashboardTabVisibilityChanged</c> when the Dashboard comes and goes is
    /// ported (attach + IsVisible) in the constructor.</para>
    /// </summary>
    public partial class SettingsTabView : UserControl
    {
        /// <summary>Width of the marquee's edge fade, in the banner's own units.</summary>
        private const double MarqueeFadePx = 40;

        /// <summary>
        /// Ambient density behind the mosaic. Twin of <c>MainWindow.DashboardFx.cs</c>'s private
        /// <c>MosaicFxIntensity</c> / <c>MosaicFogPuffs</c>, kept to the digit so the wall looks
        /// the same as it does on the WPF head.
        /// </summary>
        private const double MosaicFxIntensity = 0.62;
        private const int MosaicFogPuffs = 3;

        private bool _fxComposed;

        public SettingsTabView()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            MarqueeFadeHost.SizeChanged += MarqueeFadeHost_SizeChanged;
            HomeBtnAudioAdvanced.IsCheckedChanged += HomeBtnAudioAdvanced_Changed;

            // Composed on first ATTACH rather than in the constructor: the canvas needs a live
            // visual tree to size its layers against. WPF hooked Loaded/IsVisibleChanged;
            // Avalonia's twin for "the tree is up" is AttachedToVisualTree. Views stay
            // instantiated for the app's life, so the _fxComposed guard keeps this to once.
            AttachedToVisualTree += OnDashboardAttached;

            // WPF IsVisibleChanged -> MainWindow.OnDashboardTabVisibilityChanged (the ? box and
            // One Account cards). Attach covers the launch: the app lands here with no IsVisible flip.
            AttachedToVisualTree += (_, _) => NotifyShellVisibility();
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) { NotifyShellVisibility(); SyncFoldArrowBreath(); } };

            WireStubs();
            InitFavoritesDrawer();
            InitProgramTodayCard();   // the Training Programs "Today" card (SettingsTabView.ProgramToday.cs)
            InitializeMarqueeStrip(); // SettingsTabView.Marquee.cs
        }

        // ------------------------------------------------------------------------------
        // The real ports.
        // ------------------------------------------------------------------------------

        private void NotifyShellVisibility()
        {
            try { (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.OnDashboardTabVisibilityChanged(IsVisible); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Dashboard visibility hook failed"); }
        }

        private void OnDashboardAttached(object? sender, EventArgs e)
        {
            try
            {
                if (_fxComposed) return;
                _fxComposed = true;

                // The one ambient canvas on the whole Dashboard. Config copied verbatim from
                // MainWindow.DashboardFx.cs:165.
                MosaicFx.StartLayers(new AmbientFxConfig
                {
                    Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField,
                    Intensity = MosaicFxIntensity,
                    FogPuffs = MosaicFogPuffs,
                });

                // ponytail: needs MainWindow.RegisterTabFx("settings", MosaicFx) - the
                // park/resume hook and the motion kill-switch's reach. It is MainWindow's; wired when the ambient registry and the tab
                // navigation move to Core. Until then the canvas parks itself on detach
                // (AmbientFxCanvas.Evaluate), which is why running it here is safe.
            }
            catch (Exception)
            {
                // A missing ambient layer must never take the Dashboard down with it.
            }
        }

        /// <summary>
        /// Rebuilds the marquee's edge-fade mask whenever the banner is resized.
        ///
        /// Self-contained on purpose (no MainWindow hop): it needs nothing but its own width, and
        /// the banner resizes with every window resize. The brush uses ABSOLUTE relative units so
        /// its start/end points are this element's own coordinates rather than a bounding box -
        /// the shipped relative-mapped version was stretched across the marquee text's full
        /// (thousands of pixels wide) bounds, which put its 6% fade entirely off-screen and made
        /// the effect invisible. Absolute mapping also means the fade stays a constant ~40px
        /// instead of growing with the window.
        ///
        /// WPF's <c>BrushMappingMode.Absolute</c> is Avalonia's <c>RelativeUnit.Absolute</c> on
        /// the point itself; there is no Freeze() to call.
        /// </summary>
        private void MarqueeFadeHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            try
            {
                if (sender is not Visual host) return;
                double w = e.NewSize.Width;
                if (double.IsNaN(w) || w <= 16) { host.OpacityMask = null; return; }

                // Never eat more than a quarter of the banner from each side on a narrow window.
                double fade = Math.Min(MarqueeFadePx, w / 4.0);
                double stop = fade / w;

                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(w, 0, RelativeUnit.Absolute),
                };
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
                brush.GradientStops.Add(new GradientStop(Colors.Black, stop));
                brush.GradientStops.Add(new GradientStop(Colors.Black, 1.0 - stop));
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                host.OpacityMask = brush;
            }
            catch (Exception)
            {
                // Cosmetic. A mask that cannot be built leaves the banner un-faded, not broken.
            }
        }

        /// <summary>The advanced disclosure. Purely local - nothing below it owns state, so the
        /// shell has no reason to know whether the drawer is open.</summary>
        private void HomeBtnAudioAdvanced_Changed(object? sender, RoutedEventArgs e)
        {
            if (HomeAudioAdvanced == null || HomeBtnAudioAdvanced == null) return;
            HomeAudioAdvanced.IsVisible = HomeBtnAudioAdvanced.IsChecked == true;
        }

        // ------------------------------------------------------------------------------
        // The stubs. Every one of these forwards to a MainWindow partial on the WPF head; the
        // handler NAMES are kept verbatim, because they are the behaviour-parity contract and
        // the wiring is a rename away once those partials reach Core.
        //
        // ponytail: needs MainWindow (DashboardFx, Browser, Login,
        // TeaseCard, TabNavigation) and PremiumFeature - both WPF-head. TierGate is NOT
        // among them any more: CCP.Core/Services/TierGate.cs, over the CoreEntitlement seam.
        //
        // LinkPhoneDialog and LayeredAudioWindow are the only two handlers in the WPF file that
        // do not forward. LayeredAudioWindow is wired (HomeBtnAudioLayers_Click); LinkPhoneDialog
        // is wired too (BtnLinkPhone_Click below).
        // ------------------------------------------------------------------------------
        private void WireStubs()
        {
            // Velvet mosaic. Left-click opens the half's Studio module, right-click toggles it;
            // the three diagonal combo tiles forward per-half. A = the top-left half, B = the
            // bottom-right, as authored in XAML.
            CardFlash.Click += CardFlash_Click;
            CardFlash.ToggleRequested += CardFlash_Toggle;
            CardSubliminal.Click += CardSubliminal_Click;
            CardSubliminal.ToggleRequested += CardSubliminal_Toggle;
            CardBouncingText.Click += CardBouncingText_Click;
            CardBouncingText.ToggleRequested += CardBouncingText_Toggle;
            CardBubblePop.Click += CardBubblePop_Click;
            CardBubblePop.ToggleRequested += CardBubblePop_Toggle;
            CardLockCard.Click += CardLockCard_Click;
            CardLockCard.ToggleRequested += CardLockCard_Toggle;
            ComboVideoBubble.ClickA += ComboVideoBubble_ClickA;
            ComboVideoBubble.ClickB += ComboVideoBubble_ClickB;
            ComboVideoBubble.ToggleA += ComboVideoBubble_ToggleA;
            ComboVideoBubble.ToggleB += ComboVideoBubble_ToggleB;
            ComboSpiralPink.ClickA += ComboSpiralPink_ClickA;
            ComboSpiralPink.ClickB += ComboSpiralPink_ClickB;
            ComboSpiralPink.ToggleA += ComboSpiralPink_ToggleA;
            ComboSpiralPink.ToggleB += ComboSpiralPink_ToggleB;
            ComboMindDrain.ClickA += ComboMindDrain_ClickA;
            ComboMindDrain.ClickB += ComboMindDrain_ClickB;
            ComboMindDrain.ToggleA += ComboMindDrain_ToggleA;
            ComboMindDrain.ToggleB += ComboMindDrain_ToggleB;
            CardMystery.Click += CardMystery_Click;
            MysteryRevealFace.PointerReleased += MysteryRevealFace_Click;
            CardVault.Click += CardVault_Click;
            CardJustDrop.Click += CardJustDrop_Click;
            LogoBrandFrame.PointerPressed += ImgLogo_MouseLeftButtonDown;
            IntakePassFace.PointerPressed += IntakePassFace_MouseLeftButtonDown;

            // Browser card.
            BrowserLoadingText.PointerPressed += BrowserLoadingText_Click;
            RbBambiCloud.Click += BrowserSiteToggle_Click;
            RbHypnoTube.Click += BrowserSiteToggle_Click;
            BtnReloadBrowser.Click += BtnReloadBrowser_Click;
            BrowserWebHost.NavigationCompleted += url => Shell?.OnBrowserNavigationCompleted(url);
            BtnWebcamTracking.Click += BtnWebcamTracking_Click;
            BtnMuteBrowser.Click += BtnMuteBrowser_Click;
            BtnPopOutBrowser.Click += BtnPopOutBrowser_Click;
            ToggleEnhanceIfPossible.IsCheckedChanged += ToggleEnhanceIfPossible_Changed;
            ChkForceShowBambiCloud.IsCheckedChanged += ChkForceShowBambiCloud_Changed;
            HookBrowserCard();   // SettingsTabView.BrowserCard.cs

            // Home audio card: the same binder as Settings · Audio, so both surfaces write through
            // one path and repaint each other (WPF MainWindow.HomeAudio.cs mirrors instead).
            _ = new Controls.AppSettings.AudioSettingsBinder(this, HomeSliderMaster, HomeTxtMaster, HomeSliderVideoVolume,
                HomeTxtVideoVolume, HomeChkAudioDuck, HomeSliderDuck, HomeTxtDuck, HomeChkExcludeBambiCloudDucking,
                HomeCmbAudioOutputDevice, HomeBtnAudioOutputRefresh, HomeBtnTestAudio);
            HomeBtnAudioLayers.Click += HomeBtnAudioLayers_Click;

            // Account strip (the companion strip went in WPF 210e0e262: the rail has Companion).
            BtnUnifiedLogin.Click += BtnUnifiedLogin_Click;
            BtnQuickLogout.Click += BtnQuickLogout_Click;
            BtnLinkPhone.Click += BtnLinkPhone_Click;
            BtnDiscord.Click += BtnDiscord_Click;
            SyncQuickRichPresence();
            ChkQuickDiscordRichPresence.IsCheckedChanged += ChkDiscordRichPresence_Changed;
            // The Profile privacy switch moved: repaint (WPF syncs both boxes, AccountShell.cs:300).
            AttachedToVisualTree += (_, _) => { App.RichPresenceChanged -= SyncQuickRichPresence; App.RichPresenceChanged += SyncQuickRichPresence; };
            DetachedFromVisualTree += (_, _) => App.RichPresenceChanged -= SyncQuickRichPresence;

            // Quick-toggles row.
            VelvetBtnWebcam.Click += VelvetBtnWebcam_Click;
            VelvetBtnSystem.Click += VelvetBtnSystem_Click;
            VelvetBtnAppInfo.Click += VelvetBtnAppInfo_Click;
            VelvetBtnSchedulerRamp.Click += VelvetBtnSchedulerRamp_Click;
            VelvetBtnCatalogue.Click += VelvetBtnCatalogue_Click;
        }

        /// <summary>WPF's <c>Window.GetWindow(this) is MainWindow mw</c>: the view forwards, the shell decides.</summary>
        private Windows.MainShellWindow? Shell => TopLevel.GetTopLevel(this) as Windows.MainShellWindow;
        // -- velvet mosaic (4x4 hybrid wall) --------------------------------------------
        private void CardFlash_Click(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("flash");
        private void CardFlash_Toggle(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("flash");
        private void CardSubliminal_Click(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("subliminal");
        private void CardSubliminal_Toggle(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("subliminal");
        private void CardBouncingText_Click(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("bouncingtext");
        private void CardBouncingText_Toggle(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("bouncingtext");
        private void CardBubblePop_Click(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("bubbles");
        private void CardBubblePop_Toggle(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("bubbles");
        private void CardLockCard_Click(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("lockcard");
        private void CardLockCard_Toggle(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("lockcard");
        private void ComboVideoBubble_ClickA(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("video");
        private void ComboVideoBubble_ClickB(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("bubblecount");
        private void ComboVideoBubble_ToggleA(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("video");
        private void ComboVideoBubble_ToggleB(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("bubblecount");
        private void ComboSpiralPink_ClickA(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("spiral");
        private void ComboSpiralPink_ClickB(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("pinkfilter");
        private void ComboSpiralPink_ToggleA(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("spiral");
        private void ComboSpiralPink_ToggleB(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("pinkfilter");
        private void ComboMindDrain_ClickA(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("mindwipe");
        private void ComboMindDrain_ClickB(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("braindrain");
        private void ComboMindDrain_ToggleA(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("mindwipe");
        private void ComboMindDrain_ToggleB(object? sender, RoutedEventArgs e) => Shell?.ToggleWallFeature("braindrain");
        /// <summary>WPF CardMystery_Click (MainWindow.Presets.cs:1084): the ? box navigates to today's free
        /// feature. Navigate, never launch: the gate stays with the destination.</summary>
        internal void CardMystery_Click(object? sender, RoutedEventArgs e)
        {
            if (MysteryTabFor(TodayFreeKey()) is { } tab) Shell?.ShowTab(tab);
        }
        /// <summary>Clicking the revealed face is clicking the box - one navigation, two faces.</summary>
        private void MysteryRevealFace_Click(object? sender, PointerReleasedEventArgs e) => CardMystery_Click(sender, e);

        /// <summary>The rotation's keys. The DailyFreeService instance is private to App, so today's key is
        /// read back through the provider TierGate asks (CoreEntitlement.IsFreeToday).</summary>
        private static readonly string[] MysteryKeys = { "takeover", "awareness", "haptics", "voice", "fyp", "remote", "dtrh" };

        internal static string? TodayFreeKey() => MysteryKeys.FirstOrDefault(CoreEntitlement.IsFreeToday);

        /// <summary>WPF's key -> tab table, verbatim; Drop days go to the Play door, where the game's card lives.</summary>
        internal static string? MysteryTabFor(string? key) => key switch
        {
            "takeover" => "bambitakeover",
            "awareness" => "awareness",
            "haptics" => "haptics",
            "voice" => "shelistening",
            "fyp" => "fyp",
            "remote" => "remotecontrol",
            "dtrh" => "play",
            _ => null,
        };
        private void CardVault_Click(object? sender, RoutedEventArgs e) => Shell?.BtnPatreonExclusives_Click(sender, e);
        private void CardJustDrop_Click(object? sender, RoutedEventArgs e) { }        // mw.CardJustDrop_Click(...)
        /// <summary>
        /// WIRED (the bark and the easter egg). WPF's handler
        /// (MainWindow.UiUpdates.cs:1210) does four things: an achievement track, a bark
        /// notify, the 100-clicks-in-60-seconds easter egg, and a click pulse. The bark now
        /// crosses on <see cref="CoreBark"/> and fires FIRST, before the egg's early return,
        /// which is where WPF fires it - the rolling 60s click count it feeds is what drives
        /// the click-escalation eggs, so a click swallowed by the return would be a lost count.
        ///
        /// The counter lives on this view and not on the shell because this view owns the
        /// click: the shell's copy would be a second set of fields nothing increments.
        ///
        /// ponytail: still needs App.Achievements.TrackAvatarClick
        /// (ConditioningControlPanel/Services/Progression/AchievementService.cs), which is not in
        /// Core. The click pulse is a ScaleTransform animation on ImgLogo and is left out with it.
        /// </summary>
        private async void ImgLogo_MouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
        {
            // Bark hook: rolling 60s click count drives the click-escalation eggs.
            CoreBark.NotifyAvatarClicked();

            if (_easterEggTriggered) return;

            var now = DateTime.Now;
            if (_easterEggFirstClick == DateTime.MinValue || (now - _easterEggFirstClick).TotalSeconds > 60)
            {
                _easterEggFirstClick = now;
                _easterEggClickCount = 1;
                return;
            }

            _easterEggClickCount++;
            if (_easterEggClickCount < 100) return;

            _easterEggTriggered = true;
            await ShowEasterEgg();
        }

        /// <summary>
        /// PORTED from MainWindow.UiUpdates.ShowEasterEgg. Deviations, all of them a service
        /// this head does not have:
        ///  - <c>App.ProfileSync.RecordEasterEggReadAsync</c> is head-side, so the reader count
        ///    stays -1, which is exactly what the WPF path passes when ProfileSync is null. The
        ///    window hides its reader line on a non-positive count, so nothing invents a number.
        ///  - <c>App.AvatarWindow.PlayNoteClip</c> plus the <c>NewYearNoteReactionSeen</c> latch
        ///    are skipped together. Skipping BOTH is deliberate: latching the flag without the
        ///    clip actually playing would burn a once-ever moment on silence.
        ///  - <c>ShowDialog</c> is async here and needs a VISIBLE owner - a shell minimised to
        ///    the tray is loaded but not visible and Avalonia throws on it.
        /// </summary>
        private async System.Threading.Tasks.Task ShowEasterEgg()
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is not Window owner || !owner.IsVisible) return;
                await new Views.Windows.EasterEggWindow(-1).ShowDialogSafe(owner);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Easter egg window failed to open");
            }
        }

        private int _easterEggClickCount;
        private DateTime _easterEggFirstClick = DateTime.MinValue;
        private bool _easterEggTriggered;
        /// <summary>WPF IntakePassFace_MouseLeftButtonDown (MainWindow.UiUpdates.cs:1911): the pass face sits INSIDE
        /// LogoBrandFrame, so the click is marked handled (never a logo poke); a click on its "(?)" belongs to the
        /// help popover; anything else launches through the intake page's own handler, which owns every gate.</summary>
        private void IntakePassFace_MouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
            LaunchIntakeFromFace(e.Source);
        }

        internal void LaunchIntakeFromFace(object? source)
        {
            if (source is Visual src && (ReferenceEquals(src, BtnIntakePassHelp) || BtnIntakePassHelp.IsVisualAncestorOf(src))) return;
            try { Shell?.Named<GradedIntakeTabView>("GradedIntakeTab")?.BtnStartIntake_Click(this, new RoutedEventArgs()); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Intake pass tile: launch from Dashboard card failed"); }
        }

        // -- browser card ---------------------------------------------------------------
        private void BrowserLoadingText_Click(object? sender, PointerPressedEventArgs e) => OnBrowserPromptClick();   // SettingsTabView.BrowserCard.cs
        private void BrowserSiteToggle_Click(object? sender, RoutedEventArgs e) => Shell?.BrowserSiteToggle_Click(sender, e);
        private void BtnReloadBrowser_Click(object? sender, RoutedEventArgs e) => Shell?.BtnReloadBrowser_Click(sender, e);
        // Webcam, Mute, Pop out, Enhance and the BambiCloud override: SettingsTabView.BrowserCard.cs.

        // -- home audio card ------------------------------------------------------------
        /// <summary>Self-contained on the old dashboard too - it only opens a window.</summary>
        private void HomeBtnAudioLayers_Click(object? sender, RoutedEventArgs e)
            => Windows.LayeredAudioWindow.Open(this);

        // -- account strip ---------------------------------------------------------------
        private async void BtnUnifiedLogin_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is Windows.MainShellWindow mw) await mw.OpenUnifiedLoginDialog();
        }
        private void BtnQuickLogout_Click(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.Logout();
        /// <summary>WPF SettingsTabView.BtnLinkPhone_Click: the modal one-time phone-link code.</summary>
        private async void BtnLinkPhone_Click(object? sender, RoutedEventArgs e)
            => await new Dialogs.LinkPhoneDialog().ShowDialogSafe(TopLevel.GetTopLevel(this) as Window);
        private void BtnDiscord_Click(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.BtnDiscord_Click(sender, e);

        private bool _syncingRichPresence;

        /// <summary>Seeds the quick checkbox from settings without running its handler.</summary>
        internal void SyncQuickRichPresence() => SyncQuickRichPresenceTo(CoreSettings.Current.DiscordRichPresenceEnabled);

        /// <summary>WPF ChkDiscordRichPresence_Changed (MainWindow.AccountShell.cs:279-316): refused, with
        /// the reason, unless a Discord is linked; otherwise the setting is written and the presence
        /// client armed or dropped (App.ApplyRichPresence), which repaints the Profile switch.</summary>
        internal void ChkDiscordRichPresence_Changed(object? sender, RoutedEventArgs e)
        {
            if (_syncingRichPresence) return;
            try
            {
                var s = CoreSettings.Current;
                bool on = ChkQuickDiscordRichPresence.IsChecked == true;
                if (on && !s.HasLinkedDiscord)
                {
                    SyncQuickRichPresenceTo(false);
                    if (TopLevel.GetTopLevel(this) is Window { IsVisible: true } owner && !SuppressDialogsForTest)
                        _ = Dialogs.MessageDialog.ShowAsync(owner, "Discord Not Linked",   // WPF's literal title
                            ConditioningControlPanel.Localization.Loc.Get("msg_discord_rich_presence_requires_a_linked_disco"));
                    return;
                }
                if (s.DiscordRichPresenceEnabled == on) return;
                s.DiscordRichPresenceEnabled = on;
                CoreSettings.Save();
                App.ApplyRichPresence();
                Serilog.Log.Information("Discord Rich Presence {Status}", on ? "enabled" : "disabled");
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "ChkDiscordRichPresence_Changed failed"); }
        }

        private void SyncQuickRichPresenceTo(bool value)
        {
            _syncingRichPresence = true;
            try { ChkQuickDiscordRichPresence.IsChecked = value; }
            finally { _syncingRichPresence = false; }
        }

        /// <summary>Test seam: a headless run answers no dialogs.</summary>
        internal static bool SuppressDialogsForTest;

        // -- quick-toggles row -----------------------------------------------------------
        /// <summary>WPF VelvetBtnWebcam_Click (MainWindow.Presets.cs:1475): the bark this pill always fired, then Settings > Devices.</summary>
        internal void VelvetBtnWebcam_Click(object? sender, RoutedEventArgs e)
        {
            try { CoreBark.NotifyFeatureOpened("Webcam"); } catch (Exception ex) { Serilog.Log.Debug("webcam bark: {E}", ex.Message); }
            Shell?.OpenDeviceSettings();
        }
        /// <summary>Quick-toggles row · "System". This pill is the ONLY route to
        /// <c>MainWindow.CardSystem_Click</c> since the mosaic tile was deleted; since the nav rework
        /// it opens Settings > Monitors with the bark it always fired (WPF MainWindow.Presets.cs:1450).</summary>
        internal void VelvetBtnSystem_Click(object? sender, RoutedEventArgs e)
        {
            try { CoreBark.NotifyFeatureOpened("System"); } catch (Exception ex) { Serilog.Log.Debug("system bark: {E}", ex.Message); }
            Shell?.OpenAppSettingsSection("monitors");
        }

        /// <summary>WPF VelvetBtnAppInfo_Click (MainWindow.Presets.cs:1482): About + support forms in the feature
        /// popup; a second click replaces the first.</summary>
        internal void VelvetBtnAppInfo_Click(object? sender, RoutedEventArgs e)
        {
            AppInfoPopup?.Close();
            var popup = new Features.FeaturePopupWindow(new Features.AppInfoFeatureControl(),
                ConditioningControlPanel.Localization.Loc.Get("label_app_info"), glyph: IconKind.Info);
            popup.Closed += (_, _) => { if (ReferenceEquals(AppInfoPopup, popup)) AppInfoPopup = null; };
            AppInfoPopup = popup;
            if (TopLevel.GetTopLevel(this) is Window { IsVisible: true } owner) popup.Show(owner);
            else popup.Show();
        }

        internal Features.FeaturePopupWindow? AppInfoPopup { get; private set; }
        private void VelvetBtnSchedulerRamp_Click(object? sender, RoutedEventArgs e) => Shell?.OpenStudioModule("scheduler");
        private void VelvetBtnCatalogue_Click(object? sender, RoutedEventArgs e) => Shell?.BtnCatalogue_Click(sender, e);

        /// <summary>
        /// Opens the head's diagnostics window. A second window rather than a swap, so the shell
        /// keeps its state and the diagnostics window's own Back button is just a Close.
        /// Re-entrant: a second click focuses the window that is already open instead of stacking
        /// duplicates.
        /// </summary>
        private void BtnOpenDiagnostics_Click(object? sender, RoutedEventArgs e)
        {
            if (_diagnostics is { } open)
            {
                open.Activate();
                return;
            }

            var owner = TopLevel.GetTopLevel(this) as Window;
            _diagnostics = new MainWindow();
            _diagnostics.Closed += (_, _) => _diagnostics = null;

            if (owner is not null) _diagnostics.Show(owner);
            else _diagnostics.Show();
        }

        private MainWindow? _diagnostics;
    }
}

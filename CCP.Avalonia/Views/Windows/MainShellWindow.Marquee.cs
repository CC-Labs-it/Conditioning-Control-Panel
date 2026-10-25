// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Marquee.cs (1,170 lines) - the
// header banner's rotation, which is the part of that file this head can actually run.
//
// WHAT IS REAL HERE: the two-to-four beat rotation over the fixed, web and optional pool beats.
// The optional authored pool beat is shared with WPF through Core BannerPoolService.
// The beat array is built from CoreSettings (the One Account beat rides SeenFeatureIntros and
// drops out once spent), the welcome-back line is written from CoreSettings' offline name /
// display name, the 4s DispatcherTimer crossfades between beats, SetBannerAnnouncement replaces
// the welcome line, and RetireWebBannerBeat spends the key and hands the beat's slot back with
// the same 500ms fade rather than blinking empty.
//
// The crossfade is an Avalonia Transitions entry on Opacity, not a WPF DoubleAnimation:
// BeginAnimation/Storyboard have no twin here, and a DoubleTransition attached once and driven
// by plain Opacity writes is the same 500ms quadratic curve with none of the clock bookkeeping
// (no FillBehavior, no BeginAnimation(prop, null) afterwards to release the animated value).
//
// This partial owns OnLoaded on MainShellWindow. It has to: the window's constructor is in
// MainShellWindow.axaml.cs, which this layer does not own, so there is no other hook to start
// the rotation from. OnOpened is already taken by MainShellWindow.WorkAreaFit.cs.
//
// Beat text and {loc:Str}: TxtBannerSecondary carries {loc:Str} in the XAML, and writing .Text
// over a binding is undone on the next language change (the porting rule). The welcome line is
// a FORMATTED string (Loc.GetF with a name), so there is no key to bind instead - so
// UpdateBannerWelcomeMessage re-runs on LocalizationManager.LanguageChanged, which is what
// actually keeps it honest.
//
// Remaining startup gaps: priority-30 What's New modal queue/version stamp and the
// server-confirmed monthly season snapshot/rollover service + sync caller. Season recaps are
// silent bookkeeping in WPF now; ShowOgWelcomePopup has no live WPF caller at the pinned base.
//   (RefreshMarqueeFromSettings / CheckServerUpdateBanner are PORTED: MainShellWindow.ServerBanners.cs over Core
//     V2AuthService, as CheckServerAnnouncement is in MainShellWindow.Announcement.cs.)
//   (CheckIntakePassNudge / StartIntakeFromNudge / _serverAnnouncementShownThisLaunch are PORTED:
//     MainShellWindow.IntakeNudge.cs.)
//   (BannerWebLink_Click is ported below: the beat's inline SafeHyperlinkButton opens through
//   ExternalOpener; FailurePurpose enables WPF's clipboard + no-default-browser prompt.)
//   (StartMarqueeAnimation is PORTED onto the page that carries the strip: SettingsTabView.Marquee.cs.)
//   Banner FX are now live in MainShellWindow.BannerFx.cs on the shared frame clock.

using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Seen-list key for the One Account banner beat (and the surfaces that retire it). It
        /// rides <see cref="Models.AppSettings.SeenFeatureIntros"/> rather than a new bool - the
        /// same registry the intro cards spend - but it is NOT a card key: FeatureIntros.All has
        /// no entry for it, so nothing can ever open a modal for it.
        /// </summary>
        internal const string WebBannerSeenKey = "banner-web";

        /// <summary>
        /// True while a startup modal owns the screen. FeatureIntroPopup reads this to refuse to
        /// stack a card on a modal. Set around the first-run wizard (FirstRunWizard.axaml.cs);
        /// WPF's other startup modals have no ladder on this head yet.
        /// </summary>
        public static bool IsStartupDialogShowing { get; set; }

        private const int BannerFadeMs = 500;
        private static readonly TimeSpan BannerRotationInterval = TimeSpan.FromSeconds(4);

        private DispatcherTimer? _bannerRotationTimer;
        private int _bannerCurrentIndex;          // 0 = Primary (support), 1 = Secondary (welcome)
        private TextBlock[] _bannerBeats = Array.Empty<TextBlock>();

        // Resolved through Named<T> on every read, never through a generated x:Name field: this
        // window loads with AvaloniaXamlLoader.Load, so its generated fields are permanently null.
        private TextBlock? BannerPrimary => Named<TextBlock>("TxtBannerPrimary");
        private TextBlock? BannerSecondary => Named<TextBlock>("TxtBannerSecondary");
        private TextBlock? BannerWeb => Named<TextBlock>("TxtBannerWeb");
        private TextBlock? BannerPool => Named<TextBlock>("TxtBannerPool");

        /// <summary>
        /// The only hook this layer has into the window's lifetime - see the header. Guarded as a
        /// whole: a banner that cannot build must never stop the shell from opening.
        /// </summary>
        protected override void OnLoaded(global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnLoaded(e);
            try
            {
                InitializeBannerRotation();
                UpdateQuickLoginUI();
                RefreshSessionFeatureLock();

                // Both of these write .Text over a control that carries {loc:Str}, which a
                // language change would otherwise undo - see the header and
                // MainShellWindow.Login.cs.
                EventHandler onLanguage = (_, _) =>
                {
                    UpdateBannerWelcomeMessage();
                    UpdateQuickLoginUI();
                };
                LocalizationManager.Instance.LanguageChanged += onLanguage;
                // A closed shell must not keep answering language changes (it also pinned the window,
                // and a test shell on another dispatcher thread then threw on the next SetLanguage).
                Closed += (_, _) => LocalizationManager.Instance.LanguageChanged -= onLanguage;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Shell OnLoaded (banner rotation / login state / session lock) failed");
            }
        }

        private void InitializeBannerRotation()
        {
            _bannerBeats = BuildBannerBeats();
            foreach (var beat in _bannerBeats) EnsureFadeTransition(beat);

            UpdateBannerWelcomeMessage();

            _bannerRotationTimer?.Stop();
            _bannerRotationTimer = new DispatcherTimer { Interval = BannerRotationInterval };
            _bannerRotationTimer.Tick += BannerRotationTimer_Tick;
            _bannerRotationTimer.Start();
            Closed += (_, _) => _bannerRotationTimer.Stop();
            InitializeBannerPool();
            InitializeBannerFx();
        }

        /// <summary>
        /// Support + welcome-back always; the One Account beat only while unspent. The array is
        /// what the tick indexes, so a beat that is not in it simply never fades in - its
        /// TextBlock stays exactly as MainShellWindow.axaml authored it (Opacity 0, hit-test off).
        /// A beat the XAML does not carry is dropped rather than crashing the rotation.
        /// </summary>
        private TextBlock[] BuildBannerBeats()
        {
            var spent = CoreSettings.Current.SeenFeatureIntros.Contains(WebBannerSeenKey);
            var beats = new List<TextBlock?> { BannerPrimary, BannerSecondary };
            if (!spent) beats.Add(BannerWeb);
            if (_bannerPoolActive) beats.Add(BannerPool);
            return beats.Where(b => b is not null).Select(b => b!).ToArray();
        }

        /// <summary>Preserve the currently visible beat when optional slots join or leave.</summary>
        private void RebuildBannerBeats()
        {
            var current = _bannerCurrentIndex < _bannerBeats.Length ? _bannerBeats[_bannerCurrentIndex] : null;
            _bannerBeats = BuildBannerBeats();
            var index = current is null ? -1 : Array.IndexOf(_bannerBeats, current);
            _bannerCurrentIndex = index >= 0 ? index : 0;
            if (index < 0 && current is not null && BannerPrimary is { } primary)
                Crossfade(current, primary);
        }

        /// <summary>
        /// One DoubleTransition on Opacity, attached once. Every later fade is then a plain
        /// Opacity write - which is why <see cref="RetireWebBannerBeat"/> needs no animation code
        /// of its own.
        /// </summary>
        private static void EnsureFadeTransition(TextBlock beat)
        {
            if (!global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowTransitions)
            {
                beat.Transitions = null;
                return;
            }
            var transitions = beat.Transitions ??= new Transitions();
            foreach (var t in transitions)
                if (t is DoubleTransition dt && dt.Property == OpacityProperty) return;

            transitions.Add(new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(BannerFadeMs),
                Easing = new global::Avalonia.Animation.Easings.QuadraticEaseInOut(),
            });
        }

        private void UpdateBannerWelcomeMessage()
        {
            var secondary = BannerSecondary;
            if (secondary is null) return;

            var s = CoreSettings.Current;

            if (s.OfflineMode && !string.IsNullOrWhiteSpace(s.OfflineUsername))
            {
                secondary.Text = Loc.GetF("label_welcome_back_0_offline_mode", s.OfflineUsername);
                return;
            }

            // WPF Marquee.cs:135: unified name, then the provider names. UpdateQuickLoginUI re-runs
            // this on sign-in, restore and logout, as WPF's login paths do (Login.cs:57/123/324).
            var displayName = s.UserDisplayName
                              ?? Platform.AccountSeed.Patreon?.DisplayName
                              ?? Platform.AccountSeed.Discord?.DisplayName;
            secondary.Text = string.IsNullOrEmpty(displayName)
                ? Loc.Get("label_welcome_consider_logging_in_with_patreon_for")
                : Loc.GetF("label_welcome_back_0", displayName!);
        }

        private void BannerRotationTimer_Tick(object? sender, EventArgs e)
        {
            var banners = _bannerBeats;
            if (banners.Length < 2) return;
            if (_bannerCurrentIndex >= banners.Length) _bannerCurrentIndex = 0;

            var nextIndex = (_bannerCurrentIndex + 1) % banners.Length;
            ApplyPendingBannerPoolText(banners[_bannerCurrentIndex], banners[nextIndex]);
            var outgoing = banners[_bannerCurrentIndex];
            Crossfade(outgoing, banners[nextIndex]);
            _bannerCurrentIndex = nextIndex;

            OnBannerBeatChanged(outgoing, banners[nextIndex]);
        }

        /// <summary>
        /// Hands the banner from one beat to the next. Hit-testing follows the fade rather than
        /// the opacity, for the reason WPF spells out: a link parked at Opacity 0 still eats
        /// clicks.
        /// </summary>
        private static void Crossfade(TextBlock outgoing, TextBlock incoming)
        {
            EnsureFadeTransition(outgoing);
            EnsureFadeTransition(incoming);

            outgoing.Opacity = 0;
            incoming.Opacity = 1;
            outgoing.IsHitTestVisible = false;
            incoming.IsHitTestVisible = true;
        }

        /// <summary>
        /// Set a temporary announcement message to display in the banner rotation. It takes the
        /// welcome beat's slot, exactly as in WPF, so the rotation length does not change.
        /// </summary>
        public void SetBannerAnnouncement(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            var secondary = BannerSecondary;
            if (secondary is not null) secondary.Text = message;
            SweepBannerSheen(force: true);

            if (_bannerRotationTimer is { IsEnabled: false }) _bannerRotationTimer.Start();
        }

        /// <summary>WPF BannerWebLink_Click (MainWindow.Marquee.cs:464): the beat's own link opens the
        /// web app (SafeHyperlinkButton -> ExternalOpener) and, having worked, retires the beat.</summary>
        private void BannerWebLink_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => RetireWebBannerBeat();

        /// <summary>
        /// Spends the One Account banner beat and takes it out of the live rotation. Called from
        /// every surface that counts as "the nudge worked". Idempotent - once the key is spent
        /// the rebuild is a two-beat array either way.
        ///
        /// <para>If the beat is on screen at the moment it is retired, it hands its slot to the
        /// support beat with the same fade the rotation uses, so the banner never blinks empty.
        /// Any other beat on screen keeps its turn: the index is re-found in the rebuilt array
        /// rather than reset.</para>
        /// </summary>
        internal void RetireWebBannerBeat()
        {
            try
            {
                var settings = CoreSettings.Current;
                if (!settings.SeenFeatureIntros.Contains(WebBannerSeenKey))
                {
                    settings.SeenFeatureIntros.Add(WebBannerSeenKey);
                    CoreSettings.Save();
                }

                RebuildBannerBeats();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RetireWebBannerBeat failed; the beat rotates on until next launch");
            }
        }
    }
}

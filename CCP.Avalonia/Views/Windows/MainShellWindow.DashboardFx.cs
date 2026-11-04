// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.DashboardFx.cs (920 lines).
//
// LIVE: InitializeDashboardFx (ctor) enrols the mosaic fog in the tab park/resume and owns WPF's
// re-evaluation funnel, ApplyDashboardFxLoops (:171): a motion-level or performance change
// (Env.MotionGateChanged) and window activation/minimise re-run every dashboard card's RefreshFx and
// the vault CTA breath (ApplyVaultCtaBreath, :213). WPF reaches the same funnel through
// ApplyChromeFxLoops; this head has no chrome-FX funnel yet, so the dashboard hooks its own.
//
// STILL MISSING (each a WPF member of MainWindow.DashboardFx.cs):
//   the "? box" doorbell + hover flip - ApplyMysteryFx/ApplyMysteryBreath/ApplyMysteryBadgeVisibility/
//     EnsureMysteryTileFx/RequestMysteryFace/RunMysteryFlip/Open/SettleMysteryPlate/StartMysteryPop;
//   the centre wordmark's sheen - ApplyLogoSheenTimer/SweepLogoSheen (ApplyLogoDrift is LIVE for the logo's
//     AmbientAllowed gate; its Ken Burns drift only runs for a mod's still logo in WPF, and is not ported);
// LIVE: browser frame sweep and localized loading-status pulse (ApplyBrowserFx), shared BeatLoop.
// Not to port: PrepareRailArtNudge/ApplyRailHover/ApplyRailDotPulse were retired with the premium
// quick-toggle rail on WPF (2026-09-11); the dashboard column is the favorites rail now
// (MainShellWindow.FavoritesRail.cs).

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Features;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const double VaultCtaBreathTo = 1.07;      // WPF :200
        private const double VaultCtaBreathSeconds = 2.2;  // WPF :201

        private bool _dashboardFxInitialized;
        private BeatLoop? _vaultCtaBreath;

        /// <summary>True while the vault CTA's breath clock runs (test seam).</summary>
        internal bool VaultCtaBreathing => _vaultCtaBreath?.IsRunning == true;

        /// <summary>WPF InitializeDashboardFx (:122), called once from the constructor.</summary>
        private void InitializeDashboardFx()
        {
            if (_dashboardFxInitialized) return;
            _dashboardFxInitialized = true;
            try
            {
                var tab = Named<Tabs.SettingsTabView>("SettingsTab");
                // SettingsTabView composed the fog itself; this only enrols it in the tab park.
                RegisterTabFx("settings", tab?.FindControl<AmbientFxCanvas>("MosaicFx"));
                // The favorites drawer's motes (the view starts them itself; this parks them).
                RegisterTabFx("settings", tab?.FindControl<AmbientFxCanvas>("FavoritesDrawerFx"));

                LoadLogo();
                Activated += OnDashboardFxWindowStateish;
                Deactivated += OnDashboardFxWindowStateish;
                PropertyChanged += OnDashboardFxWindowProperty;
                if (tab != null)
                {
                    tab.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) ApplyVaultCtaBreath(); };
                    // The browser writes status from many paths. Watch the text as WPF does,
                    // so connected and failed statuses immediately stop the progress cue.
                    if (tab.FindControl<TextBlock>("TxtBrowserStatus") is { } status)
                        status.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) ApplyBrowserFx(); };
                }
                // A static event: subscribe while open, never root a closed shell (P41).
                Opened += (_, _) => { Env.MotionGateChanged -= ApplyDashboardFxLoops; Env.MotionGateChanged += ApplyDashboardFxLoops; ApplyDashboardFxLoops(); };
                Closed += (_, _) => { Env.MotionGateChanged -= ApplyDashboardFxLoops; StopVaultCtaBreath(); StopBrowserFx(); };
            }
            catch (Exception ex) { Log.Warning(ex, "InitializeDashboardFx failed"); }
        }

        private void OnDashboardFxWindowStateish(object? sender, EventArgs e) => ApplyDashboardFxLoops();

        private void OnDashboardFxWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty) ApplyDashboardFxLoops();
        }

        /// <summary>
        /// WPF ApplyDashboardFxLoops (:171): re-evaluates every dashboard loop against the current
        /// motion/tier/focus gate - both ways, so Full re-arms what Reduced/Off parked.
        /// </summary>
        private void ApplyDashboardFxLoops()
        {
            if (!_dashboardFxInitialized) return;
            try
            {
                var tab = Named<Tabs.SettingsTabView>("SettingsTab");
                if (tab == null) return;
                foreach (var card in tab.GetLogicalDescendants().OfType<FeatureCard>()) card.RefreshFx();
                foreach (var combo in tab.GetLogicalDescendants().OfType<SplitFeatureCard>()) combo.RefreshFx();
                ApplyVaultCtaBreath();
                ApplyBrowserFx();
                tab.SyncMysteryGiftFx();
                // WPF ApplyLogoDrift (:753): the logo animates under the chrome gate; BeatLoop parks it hidden.
                if (tab.FindControl<AnimatedLogoImage>("ImgLogo") is { } logo)
                    logo.AmbientAllowed = Env.AllowAmbientLoops && IsActive && WindowState != WindowState.Minimized;
                tab.ApplyHostShown(WindowState != WindowState.Minimized);   // drawer + fold breaths
            }
            catch (Exception ex) { Log.Debug("ApplyDashboardFxLoops: {E}", ex.Message); }
        }

        /// <summary>
        /// WPF ApplyVaultCtaBreath (:213): the vault stamp's slow 1.0-1.07 scale breath. Parks at
        /// 1.0 unless ambient motion is allowed, the window is focused and the stamp is on screen.
        /// </summary>
        private void ApplyVaultCtaBreath()
        {
            try
            {
                var cta = Named<Tabs.SettingsTabView>("SettingsTab")?.FindControl<TextBlock>("VaultCta");
                if (cta?.RenderTransform is not TransformGroup { Children: [ScaleTransform scale, ..] }) return;
                bool run = Env.AllowAmbientLoops && IsActive && WindowState != WindowState.Minimized
                           && cta.IsEffectivelyVisible;
                if (run == VaultCtaBreathing) return;
                StopVaultCtaBreath();
                scale.ScaleX = scale.ScaleY = 1.0;
                if (!run) return;
                // Animation.RunAsync on the ScaleTransform threw InvalidCastException, so the breath was
                // inert; it now rides the 30 fps beat (trunk 66ca21fd3). Alternate + SineEaseInOut.
                _vaultCtaBreath = new BeatLoop(cta, t =>
                    scale.ScaleX = scale.ScaleY = 1.0 + (VaultCtaBreathTo - 1.0) * BeatLoop.Breath(t, VaultCtaBreathSeconds));
                _vaultCtaBreath.Start();
            }
            catch (Exception ex) { Log.Debug("ApplyVaultCtaBreath: {E}", ex.Message); }
        }

        /// <summary>WPF LoadLogo (MainWindow.xaml.cs:2277): a mod's own logo.png / logo2.png stays still;
        /// the built-in wordmark becomes the animated CCP logo.</summary>
        private void LoadLogo()
        {
            try
            {
                var logo = Named<Tabs.SettingsTabView>("SettingsTab")?.FindControl<AnimatedLogoImage>("ImgLogo");
                if (logo == null) return;
                var file = CoreMods.IsCCPDefault || CoreSettings.Current.IsSissyMode ? "logo2.png" : "logo.png";
                var custom = CoreModArt.OverridePath(file);
                logo.SetArtwork(custom == null ? null : new global::Avalonia.Media.Imaging.Bitmap(custom));
            }
            catch (Exception ex) { Log.Warning("Failed to load logo: {Error}", ex.Message); }
        }

        // WPF MainWindow.DashboardFx.cs:823-919. Frame/header only: never draw over or
        // transform the native browser surface. Both loops use the shared 30 fps clock.
        private BeatLoop? _browserFrameSweep;
        private BeatLoop? _browserStatusPulse;

        private void ApplyBrowserFx()
        {
            try
            {
                var tab = Named<Tabs.SettingsTabView>("SettingsTab");
                var frame = tab?.FindControl<Border>("BrowserCardFrame");
                var status = tab?.FindControl<TextBlock>("TxtBrowserStatus");
                bool ambient = Env.AllowAmbientLoops && IsActive && WindowState != WindowState.Minimized;
                if (frame?.BorderBrush is LinearGradientBrush { GradientStops.Count: 3 } brush)
                {
                    if (!ambient)
                    {
                        _browserFrameSweep?.Stop();
                        _browserFrameSweep = null;
                        for (int i = 0; i < 3; i++) brush.GradientStops[i].Offset = -0.30 + i * 0.15;
                    }
                    else if (_browserFrameSweep == null)
                    {
                        _browserFrameSweep = new BeatLoop(frame, t =>
                        {
                            double phase = t % 14.0;
                            // WPF holds the end position until the discrete reset at 2.45 s,
                            // then rests until 14 s. Interpolating that reset causes a flash.
                            double travel = phase < 2.45 ? 1.35 * Math.Min(phase / 2.4, 1) : 0;
                            for (int i = 0; i < 3; i++) brush.GradientStops[i].Offset = -0.30 + i * 0.15 + travel;
                        });
                        _browserFrameSweep.Start();
                    }
                }
                if (status == null) return;
                bool loading = string.Equals(IconText.Bare(status.Text).Trim(), IconText.BareLoc("label_loading").Trim(), StringComparison.OrdinalIgnoreCase);
                bool pulse = loading && ambient && Env.AllowTransitions;
                if (!pulse)
                {
                    _browserStatusPulse?.Stop();
                    _browserStatusPulse = null;
                    status.Opacity = 1;
                }
                else if (_browserStatusPulse == null)
                {
                    _browserStatusPulse = new BeatLoop(status, t => status.Opacity = 0.35 + 0.65 * BeatLoop.Breath(t, 1.6));
                    _browserStatusPulse.Start();
                }
            }
            catch (Exception ex) { Log.Debug("ApplyBrowserFx: {E}", ex.Message); }
        }

        private void StopBrowserFx()
        {
            _browserFrameSweep?.Stop();
            _browserFrameSweep = null;
            _browserStatusPulse?.Stop();
            _browserStatusPulse = null;
        }

        private void StopVaultCtaBreath()
        {
            _vaultCtaBreath?.Stop();
            _vaultCtaBreath = null;
        }
    }
}

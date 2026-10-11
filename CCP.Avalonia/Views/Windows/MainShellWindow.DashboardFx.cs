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
//   the centre wordmark's drift and sheen - ApplyLogoDrift/ApplyLogoSheenTimer/SweepLogoSheen;
//   the browser card's frame sweep and status pulse - ApplyBrowserFrameSweep/ApplyBrowserStatusPulse.
// Not to port: PrepareRailArtNudge/ApplyRailHover/ApplyRailDotPulse were retired with the premium
// quick-toggle rail on WPF (2026-09-11); the dashboard column is the favorites rail now
// (MainShellWindow.FavoritesRail.cs).

using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation.Easings;
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
        private TransformTween? _vaultCtaBreath;

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

                Activated += OnDashboardFxWindowStateish;
                Deactivated += OnDashboardFxWindowStateish;
                PropertyChanged += OnDashboardFxWindowProperty;
                if (tab != null)
                    tab.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) ApplyVaultCtaBreath(); };
                // A static event: subscribe while open, never root a closed shell (P41).
                Opened += (_, _) => { Env.MotionGateChanged -= ApplyDashboardFxLoops; Env.MotionGateChanged += ApplyDashboardFxLoops; ApplyDashboardFxLoops(); };
                Closed += (_, _) => { Env.MotionGateChanged -= ApplyDashboardFxLoops; StopVaultCtaBreath(); };
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
                // inert. One 2 x 2.2 s loop eased by (1 - cos 2 pi p) / 2 is Alternate + SineEaseInOut.
                _vaultCtaBreath = TransformTween.Run(scale, TimeSpan.FromSeconds(2 * VaultCtaBreathSeconds),
                    new (double, AvaloniaProperty, double)[]
                    {
                        (0, ScaleTransform.ScaleXProperty, 1.0), (1, ScaleTransform.ScaleXProperty, VaultCtaBreathTo),
                        (0, ScaleTransform.ScaleYProperty, 1.0), (1, ScaleTransform.ScaleYProperty, VaultCtaBreathTo),
                    }, new AlternateSineEase(), loop: true);
            }
            catch (Exception ex) { Log.Debug("ApplyVaultCtaBreath: {E}", ex.Message); }
        }

        private void StopVaultCtaBreath()
        {
            _vaultCtaBreath?.Stop();
            _vaultCtaBreath = null;
        }

        /// <summary>Alternate + SineEaseInOut folded into one forward pass: 0 -> 1 -> 0.</summary>
        private sealed class AlternateSineEase : Easing
        {
            public override double Ease(double p) => (1 - Math.Cos(2 * Math.PI * p)) / 2;
        }
    }
}

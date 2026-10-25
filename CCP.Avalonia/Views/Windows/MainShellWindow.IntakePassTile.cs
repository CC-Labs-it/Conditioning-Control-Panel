// PORTED from WPF 7.1.5 ConditioningControlPanel/MainWindow/MainWindow.UiUpdates.cs, the Intake Pass
// tile (:1300-1925): RefreshIntakePassTile, SetIntakePassFace, ApplyIntakePassFaceState,
// StartIntakePassFlipLoop, HoldIntakePassFace, RunIntakePassSpinPhase, FinishIntakePassSpin,
// CancelIntakePassSpin, and Services/Quiz/IntakeNiche.PassCardImage.
//
// The Home wall's centre logo doubles as the weekly Graded Intake pass card: while a free account has
// an unspent run (IntakePassState.Available, the ONLY state that shows the card, as WPF), the tile
// holds the logo for 8 s, turns on its Y axis to the pass card, holds, and turns back. Premium,
// signed-out and spent-week accounts only ever see the logo.
//
// The turn is the WPF fake: ScaleX 1 -> 0 -> 1 with a 6 degree skew at the thin point, five half
// turns (105, 115, 130, 150, 560 ms, the last one eased), the face swapping at each thin point.
// Flip and CTA breath use the existing shared BeatLoop. Leaving Home, minimising, a state
// change or Motion Off parks them immediately; no dispatcher/animation loop keeps composing.
// Motion Off (no ambient loops) shows the card still, as WPF's off-screen branch does.
//
// The click (SettingsTabView.IntakePassFace_MouseLeftButtonDown) and the (?) popover content
// (MainShellWindow.HelpButtons.cs) were already live.
// Adapted from parity/trunk a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; CTA breath restored.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Quiz;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF IntakePassFaceHoldMs: how long each face rests between turns.</summary>
        private const int IntakePassFaceHoldMs = 8000;
        private static readonly int[] IntakePassHalfTurnMs = { 105, 115, 130, 150, 560 };
        private const double IntakePassSkewDeg = 6.0;

        private bool _intakePassHooked;
        private bool _intakePassLoopRunning;
        private bool _intakePassClosed;
        private BeatLoop? _intakePassFlip, _intakePassCta;

        /// <summary>True while the pass card is the face showing (tests).</summary>
        internal bool IntakePassShowingCard { get; private set; }

        /// <summary>True while the tile is alternating (tests).</summary>
        internal bool IntakePassLoopRunning => _intakePassLoopRunning;

        /// <summary>WPF IntakeNiche.PassCardImage: a mod's own intake/pass_card.png wins, else the
        /// niche card this build ships; null hides the art and leaves the vector card.</summary>
        internal static Bitmap? IntakePassCardImage()
        {
            try
            {
                const string generic = "intake/pass_card.png";
                if (CoreModArt.HasOverride(generic)) return ModArt.TryLoad(generic, 768);
                var niche = IntakeRun.ResolveNiche(CoreMods.ActiveModId, CoreMods.ActiveModPackage?.Manifest?.Tags,
                    CoreSettings.Current.ContentMode == ContentMode.SissyHypno);
                return ModArt.TryLoad($"intake/pass_card_{niche}.png", 768);
            }
            catch { return null; }
        }

        /// <summary>WPF RefreshIntakePassTile: decide the face from the pass state, from scratch.</summary>
        internal void RefreshIntakePassTile()
        {
            try
            {
                if (_intakePassClosed || SettingsPage is not { } dash) return;
                var face = dash.FindControl<Border>("IntakePassFace");
                var logo = dash.FindControl<Border>("LogoFaceLogo");
                if (face == null || logo == null) return;

                if (!_intakePassHooked)
                {
                    _intakePassHooked = true;
                    var pass = App.IntakePass;
                    EventHandler onState = (_, _) => Dispatcher.UIThread.Post(RefreshIntakePassTile);
                    pass.PassStateChanged += onState;
                    EventHandler<ModPackage> onMod = (_, _) => Dispatcher.UIThread.Post(RefreshIntakePassTile);
                    CoreMods.ModChanged += onMod;
                    PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) RefreshIntakePassTile(); };
                    Action onMotion = () => Dispatcher.UIThread.Post(RefreshIntakePassTile);
                    AmbientFxCanvas.Env.MotionGateChanged += onMotion;
                    // Leaving Home stops the turn; coming back re-reads the state.
                    dash.PropertyChanged += (_, e) =>
                    {
                        if (e.Property != IsVisibleProperty) return;
                        if (!dash.IsVisible) { CancelIntakePassSpin(); StopIntakePassCtaPulse(); }
                        else RefreshIntakePassTile();
                    };
                    Opened += (_, _) => RefreshIntakePassTile();   // the first paint ran before the page was on screen
                    Closed += (_, _) =>
                    {
                        _intakePassClosed = true;
                        CoreMods.ModChanged -= onMod;
                        pass.PassStateChanged -= onState;
                        AmbientFxCanvas.Env.MotionGateChanged -= onMotion;
                        CancelIntakePassSpin();
                        StopIntakePassCtaPulse();
                    };
                }

                var state = App.IntakePass.State;
                if (state != IntakePassState.Available)
                {
                    CancelIntakePassSpin();
                    SetIntakePassFace(false);
                    return;
                }

                ApplyIntakePassFaceState();

                var onScreen = dash.IsVisible && dash.IsEffectivelyVisible && WindowState != WindowState.Minimized;
                if (!onScreen || !AmbientFxCanvas.Env.AllowAmbientLoops)
                {
                    // A hidden page or Motion Off: the card, still. Nothing turns.
                    CancelIntakePassSpin();
                    SetIntakePassFace(true);
                    return;
                }
                if (_intakePassLoopRunning) return;   // already alternating; do not restart it
                StartIntakePassFlipLoop();
            }
            catch (Exception ex) { Log.Debug("RefreshIntakePassTile: {E}", ex.Message); }
        }

        /// <summary>WPF SetIntakePassFace: exactly one face is ever visible.</summary>
        private void SetIntakePassFace(bool showCard)
        {
            if (SettingsPage is not { } dash) return;
            if (dash.FindControl<Border>("IntakePassFace") is { } face) face.IsVisible = showCard;
            if (dash.FindControl<Border>("LogoFaceLogo") is { } logo) logo.IsVisible = !showCard;
            IntakePassShowingCard = showCard;
            if (showCard && dash.IsEffectivelyVisible && WindowState != WindowState.Minimized
                && AmbientFxCanvas.Env.AllowAmbientLoops) StartIntakePassCtaPulse();
            else StopIntakePassCtaPulse();
        }

        /// <summary>WPF ApplyIntakePassFaceState: the niche art and the state's copy.</summary>
        private void ApplyIntakePassFaceState()
        {
            if (SettingsPage is not { } dash) return;
            var art = IntakePassCardImage();
            var hasArt = art != null;

            if (dash.FindControl<Border>("IntakePassArt") is { } artHost)
            {
                artHost.Background = hasArt
                    ? new ImageBrush(art) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center }
                    : null;
                artHost.IsVisible = hasArt;
                artHost.Opacity = 1;
            }
            if (dash.FindControl<Border>("IntakePassScrim") is { } scrim) scrim.IsVisible = hasArt;
            if (dash.FindControl<StackPanel>("IntakePassVectorBody") is { } vector) vector.IsVisible = !hasArt;

            if (dash.FindControl<TextBlock>("IntakePassHeadline") is { } h) h.Text = Loc.Get("intake_pass_card_headline");
            if (dash.FindControl<TextBlock>("IntakePassBody") is { } b) b.Text = Loc.Get("intake_pass_card_body");
            if (dash.FindControl<TextBlock>("IntakePassCta") is { } c) c.Text = Loc.Get("intake_pass_card_cta");
            if (dash.FindControl<Border>("IntakePassFace") is { } face) ToolTip.SetTip(face, Loc.Get("tooltip_intake_pass_card"));
        }

        private void StartIntakePassCtaPulse()
        {
            if (SettingsPage?.FindControl<TextBlock>("IntakePassCta") is not { } cta) return;
            _intakePassCta ??= new BeatLoop(cta, t =>
            {
                var breath = BeatLoop.Breath(t, 1.9); // WPF: 1900 ms each direction, SineEaseInOut
                cta.Opacity = 0.65 + 0.35 * breath;
                if (cta.RenderTransform is ScaleTransform scale) scale.ScaleX = scale.ScaleY = 1 + 0.05 * breath;
            });
            _intakePassCta.Start();
        }

        private void StopIntakePassCtaPulse()
        {
            _intakePassCta?.Stop();
            if (SettingsPage?.FindControl<TextBlock>("IntakePassCta") is not { } cta) return;
            cta.Opacity = 1;
            if (cta.RenderTransform is ScaleTransform scale) scale.ScaleX = scale.ScaleY = 1;
        }

        // ---- the turn -----------------------------------------------------------------------

        private (ScaleTransform? Scale, SkewTransform? Skew) IntakePassFlipTransforms()
        {
            var group = SettingsPage?.FindControl<Grid>("LogoFlipHost")?.RenderTransform as TransformGroup;
            ScaleTransform? scale = null;
            SkewTransform? skew = null;
            if (group != null)
                foreach (var t in group.Children)
                {
                    if (t is ScaleTransform s) scale = s;
                    else if (t is SkewTransform k) skew = k;
                }
            return (scale, skew);
        }

        private void CancelIntakePassSpin()
        {
            _intakePassLoopRunning = false;
            _intakePassFlip?.Stop();
            var (scale, skew) = IntakePassFlipTransforms();
            if (scale != null) scale.ScaleX = 1;
            if (skew != null) skew.AngleY = 0;
        }

        private void StartIntakePassFlipLoop()
        {
            CancelIntakePassSpin();
            SetIntakePassFace(false);
            _intakePassLoopRunning = true;
            _intakePassFlip ??= new BeatLoop(SettingsPage!, StepIntakePassFlip);
            _intakePassFlip.Start();
        }

        // WPF's five half-turns, with the face swapped exactly at each thin point. Sampling the
        // shared clock also handles a missed frame: it cannot strand the tile edge-on.
        internal void StepIntakePassFlip(double seconds)
        {
            if (!_intakePassLoopRunning) return;
            var (scale, skew) = IntakePassFlipTransforms();
            if (scale == null || skew == null) return;
            double cycle = IntakePassFaceHoldMs;
            foreach (var duration in IntakePassHalfTurnMs) cycle += 2 * (duration / 2);
            var ms = Math.Max(0, seconds * 1000);
            var card = (long)(ms / cycle) % 2 == 1;
            var phase = ms % cycle - IntakePassFaceHoldMs;
            scale.ScaleX = 1;
            skew.AngleY = 0;
            for (var i = 0; i < IntakePassHalfTurnMs.Length && phase >= 0; i++)
            {
                var half = IntakePassHalfTurnMs[i] / 2;
                if (phase >= half * 2) { card = !card; phase -= half * 2; continue; }
                var closing = phase < half;
                if (!closing) card = !card;
                var t = closing ? phase / half : (phase - half) / half;
                if (i == IntakePassHalfTurnMs.Length - 1)
                    t = closing ? t * t * t : 1 - Math.Pow(1 - t, 3);
                scale.ScaleX = closing ? 1 - t : t;
                skew.AngleY = IntakePassSkewDeg * (closing ? t : t - 1);
                break;
            }
            if (card != IntakePassShowingCard) SetIntakePassFace(card);
        }
    }
}

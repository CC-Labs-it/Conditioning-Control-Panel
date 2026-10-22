// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.Fx.cs: the ground and the hero's
// art - FxOnShown/FxPark's loops (:129-165, :252-270), BuildSpiral/StartSpiralTurn/HoldSpiral
// (:276-314), the hero sheen (:316-338), the art drift (:340-360), the tag swing (:362-381) and the
// art plate's fade (:383-404) - plus RefreshTag (ChasterTabView.xaml.cs:431-445).
// WPF runs three storyboards (10/10/20 fps); here one BeatLoop (the window's shared 30 fps beat)
// steps all three on real elapsed time, only while the page is on screen and AllowAmbientLoops says yes, as WPF gates them.
// ponytail: the LockTitle letters, GlowDigits, the card stagger and the event Fx are not here
// (row views-tab-chaster names them under wire-views-tab-chaster-b-next).
using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private const double SpiralTurns = 6.5, SpiralInnerRadius = 8, SpiralTurnSeconds = 90;
        private const double ArtDriftTo = 1.06, ArtDriftSeconds = 38;
        internal const double TagSwingFrom = -7, TagSwingTo = -3, TagSwingSeconds = 3.6;
        private const double HeroRadius = 18;

        private BeatLoop _stageClock = null!;
        internal readonly RotateTransform SpiralTurn = new();
        internal readonly RotateTransform TagSwing = new() { Angle = -6 };
        internal readonly ScaleTransform HeroArtScale = new();
        private CardSheenAdorner? _heroSheen;
        private bool _stageStarted, _spiralBuilt;
        private double _stageLast;
        private double _driftT, _swingT;

        internal bool StageTicking => _stageClock.IsRunning;
        internal CardSheenAdorner? HeroSheen => _heroSheen;

        private void StageInit()
        {
            SpiralLayer.RenderTransform = SpiralTurn;
            HeroArt.RenderTransform = HeroArtScale;
            PaperTag.RenderTransform = TagSwing;
            PaperTag.PointerReleased += (_, _) => ToggleBill();   // WPF MouseLeftButtonUp="StatRun_Click"
            HeroArtPlate.SizeChanged += (_, _) => PaintArtFade();
            _stageClock = new BeatLoop(this, t =>
            {
                double dt = t - _stageLast;
                _stageLast = t;
                if (dt > 0) StepStage(dt);
            });
        }

        /// <summary>FxOnShown (on) / FxPark (off): called with the page's on-screen state.</summary>
        private void Stage(bool on)
        {
            try
            {
                if (!on)
                {
                    Ambient.Pause();
                    _stageClock.Stop();                   // HoldSpiral: the angle stays where it is
                    CardSheenAdorner.Detach(_heroSheen);
                    _heroSheen = null;
                    StopArtDrift();
                    StopTagSwing();
                    return;
                }
                if (!_spiralBuilt)
                {
                    _spiralBuilt = true;
                    SpiralPath.Data = BuildSpiral(SpiralLayer.Width, SpiralTurns, SpiralInnerRadius);
                }
                if (!_stageStarted)
                {
                    _stageStarted = true;
                    Ambient.StartLayers(new AmbientFxConfig
                    {
                        Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField,
                        Intensity = 0.85,
                        FogPuffs = 3,
                    });
                }
                else Ambient.Resume();
                PaintArtFade();
                CardSheenAdorner.Detach(_heroSheen);
                _heroSheen = null;
                // The card's adorner layer is reachable only once the whole subtree is attached.
                Dispatcher.UIThread.Post(() =>
                {
                    if (_heroSheen == null && _attached && IsVisible && Env.AllowAmbientLoops)
                        _heroSheen = CardSheenAdorner.Attach(HeroCard, HeroRadius);
                }, DispatcherPriority.Loaded);
                if (!Env.AllowAmbientLoops) { _stageClock.Stop(); StopArtDrift(); StopTagSwing(); return; }
                if (_stageClock.IsRunning) return;
                _stageLast = 0;
                _stageClock.Start();
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] stage failed"); }
        }

        /// <summary>One step of the three loops, on real elapsed seconds (P40). Test seam.</summary>
        internal void StepStage(double dt)
        {
            if (!Env.AllowAmbientLoops) { _stageClock.Stop(); StopArtDrift(); StopTagSwing(); return; }
            SpiralTurn.Angle = (SpiralTurn.Angle + dt * 360 / SpiralTurnSeconds) % 360;
            _driftT = (_driftT + dt) % (2 * ArtDriftSeconds);
            HeroArtScale.ScaleX = HeroArtScale.ScaleY = 1 + (ArtDriftTo - 1) * SineBounce(_driftT, ArtDriftSeconds);
            _swingT = (_swingT + dt) % (2 * TagSwingSeconds);
            TagSwing.Angle = TagSwingFrom + (TagSwingTo - TagSwingFrom) * SineBounce(_swingT, TagSwingSeconds);
        }

        /// <summary>WPF's DoubleAnimation with AutoReverse and a SineEase in-out: 0 -> 1 -> 0 over 2 x span.</summary>
        private static double SineBounce(double t, double span)
        {
            double u = t / span;
            if (u > 1) u = 2 - u;
            return (1 - Math.Cos(Math.PI * u)) / 2;
        }

        private void StopArtDrift() { _driftT = 0; HeroArtScale.ScaleX = HeroArtScale.ScaleY = 1; }

        private void StopTagSwing() { _swingT = 0; TagSwing.Angle = (TagSwingFrom + TagSwingTo) / 2; }

        /// <summary>An Archimedean spiral (r grows linearly with the angle) as one polyline. The launcher's own recipe.</summary>
        private static Geometry BuildSpiral(double size, double turns, double innerRadius)
        {
            var geometry = new StreamGeometry();
            double c = size / 2, maxTheta = turns * Math.PI * 2, growth = (c - innerRadius - 4) / maxTheta;
            const double step = Math.PI / 45;
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(c + innerRadius, c), false);
                for (double theta = step; theta <= maxTheta; theta += step)
                {
                    double r = innerRadius + growth * theta;
                    ctx.LineTo(new Point(c + r * Math.Cos(theta), c + r * Math.Sin(theta)));
                }
                ctx.EndFigure(false);
            }
            return geometry;
        }

        /// <summary>The fade from the card's own surface on the art's left edge, so the picture emerges
        /// instead of starting (the rounded right corners are the plate's CornerRadius).</summary>
        private void PaintArtFade()
        {
            var surface = Brush("SurfaceBgBrush") is ISolidColorBrush s ? s.Color : Color.FromRgb(0x1A, 0x12, 0x2E);
            HeroArtFade.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(surface, 0),
                    new GradientStop(Color.FromArgb(0xD0, surface.R, surface.G, surface.B), 0.28),
                    new GradientStop(Color.FromArgb(0x40, surface.R, surface.G, surface.B), 0.62),
                    new GradientStop(Colors.Transparent, 1),
                },
            };
        }

        /// <summary>The paper tag: the tab's amount in ink, when it lands, and a stamp that says
        /// UNPAID while there is something to pay, CLEAR at zero, CREDIT below it.</summary>
        internal void RefreshTag(int balance)
        {
            var ink = balance > 0 ? Color.FromRgb(0xC8, 0x24, 0x4A) : balance < 0 ? Color.FromRgb(0x1E, 0x8A, 0x6E) : Color.FromRgb(0x24, 0x1A, 0x2E);
            TxtTagAmount.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
            TxtTagAmount.Foreground = new SolidColorBrush(ink);
            var chaster = ChasterHead.Service;
            var line = TabPageText.Tag(balance, chaster?.PushableTodaySeconds ?? 0, chaster?.IsPaused == true,
                !string.IsNullOrEmpty(CoreSettings.Current.ChasterLockId) && chaster?.LockLookup != LockLookup.Ambiguous,
                chaster?.AddsBlocked == true);
            TxtTagLands.Text = line.Today == null ? Loc.Get(line.Key) : Loc.GetF(line.Key, line.Today, line.Later!);
            TxtTagStamp.Text = Loc.Get(balance > 0 ? "chaster_tag_unpaid" : balance < 0 ? "chaster_tag_credit" : "chaster_tag_clear");
            var stamp = new SolidColorBrush(balance > 0 ? Color.FromRgb(0xC8, 0x24, 0x4A) : balance < 0 ? Color.FromRgb(0x1E, 0x8A, 0x6E) : Color.FromRgb(0x6E, 0x66, 0x86));
            TagStamp.BorderBrush = stamp;
            TxtTagStamp.Foreground = stamp;
        }
    }
}

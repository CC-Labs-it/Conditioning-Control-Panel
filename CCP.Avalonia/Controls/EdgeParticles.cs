using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Services.UI.EdgeEffects;
using NavStripRules = ConditioningControlPanel.Avalonia.Views.Controls.NavStripRules;
namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>What the edge strips run at a motion level and tier (WPF EdgeParticles.cs:12).</summary>
    public enum EdgeFogMode { None, Reduced, Full }

    /// <summary>WPF Controls/NavRail/EdgeParticles.cs: four 56 px strips in the section hue
    /// running the fog (Reduced: half the puffs at half the speed) and, at Full, the embers kept
    /// to their 30 px band.</summary>
    public class EdgeParticles : Grid
    {
        public const double StripThickness = EdgeFogMath.StripPx;
        public const double EmberBandPx = 30;
        public const double StripIntensity = 0.55;
        public const double StripDustDensity = 0.35;

        private static readonly EdgeSide[] Sides = { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left };

        private readonly List<AmbientFxCanvas> _strips = new();
        private Color _hue = Color.FromRgb(0xB7, 0x9C, 0xFF);
        private EdgeFogMode _mode = EdgeFogMode.None;
        private bool _mountAsked;

        internal MotionLevel? MotionOverride { get; set; }
        internal bool? TierAllowsParticlesOverride { get; set; }

        public EdgeParticles()
        {
            IsHitTestVisible = false;
            Focusable = false;
            UseLayoutRounding = true;
            DetachedFromVisualTree += (_, _) => Teardown();
            AttachedToVisualTree += (_, _) => Refresh();
        }

        public IReadOnlyList<AmbientFxCanvas> Strips => _strips;

        public Color Hue => _hue;

        public EdgeFogMode Mode => _mode;

        /// <summary>True while the fog runs: the section edge thins its static band (WPF :75).</summary>
        public bool FogLive => _mode != EdgeFogMode.None && _strips.Count > 0;

        public static bool ShouldMount(MotionLevel level, bool tierAllowsParticles) =>
            level == MotionLevel.Full && tierAllowsParticles;

        public static EdgeFogMode ModeFor(MotionLevel level, bool tierAllowsParticles) =>
            !tierAllowsParticles || level == MotionLevel.Off ? EdgeFogMode.None
            : level == MotionLevel.Reduced ? EdgeFogMode.Reduced
            : EdgeFogMode.Full;

        public static AmbientFxLayers LayersFor(EdgeFogMode mode) => mode switch
        {
            EdgeFogMode.Full => AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift,
            EdgeFogMode.Reduced => AmbientFxLayers.EdgeFog,
            _ => AmbientFxLayers.None,
        };

        /// <summary>WPF SectionEdgeRules.FogGain: the band's balance relative to its 0.20 centre.</summary>
        public static double FogGain(Color hue) => NavStripRules.EdgeGlowAlpha(hue) / (0.20 * 255);

        private EdgeFogMode CurrentMode()
        {
            var level = MotionOverride ?? AmbientFxCanvas.Env.Level;
            var current = AmbientFxCanvas.Env.CurrentTier;
            bool tier = TierAllowsParticlesOverride
                ?? (AmbientFxCanvas.Env.AllowAmbientMotion(current) && AmbientFxCanvas.Env.MaxAmbientParticles(current) > 0);
            return ModeFor(level, tier);
        }

        /// <summary>Re-checks the gate: a level or tier change builds, rebuilds or tears down the
        /// strips; otherwise the live strips crossfade to the hue (WPF :115).</summary>
        public void Mount(Color hue, int fadeMs = 0)
        {
            _hue = hue;
            _mountAsked = true;
            try
            {
                var mode = CurrentMode();
                if (mode == EdgeFogMode.None)
                {
                    Teardown();
                    return;
                }
                if (mode != _mode || _strips.Count == 0) { Teardown(); _mode = mode; Build(); }
                else Retint(hue, fadeMs);
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("EdgeParticles.Mount: {E}", ex.Message);
            }
        }

        public void Refresh()
        {
            if (_mountAsked) Mount(_hue);
        }

        /// <summary>Crossfade the fog over <paramref name="fadeMs"/>; the embers swap at once (WPF :150).</summary>
        public void Retint(Color hue, int fadeMs = 0)
        {
            _hue = hue;
            foreach (var strip in _strips)
            { strip.Retint(hue); strip.SetEdgeFog(hue, FogGain(hue), fadeMs); }
        }

        private void Build()
        {
            var layers = LayersFor(_mode);
            foreach (var side in Sides)
            {
                var strip = new AmbientFxCanvas();
                bool horizontal = side is EdgeSide.Top or EdgeSide.Bottom;
                if (horizontal)
                {
                    strip.Height = StripThickness;
                    strip.HorizontalAlignment = HorizontalAlignment.Stretch;
                    strip.VerticalAlignment = side == EdgeSide.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                }
                else
                {
                    strip.Width = StripThickness;
                    strip.VerticalAlignment = VerticalAlignment.Stretch;
                    strip.HorizontalAlignment = side == EdgeSide.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                }
                Children.Add(strip);
                _strips.Add(strip);
                strip.StartLayers(new AmbientFxConfig
                {
                    Layers = layers,
                    EdgeSide = side,
                    Tint = _hue,
                    Intensity = StripIntensity,
                    DustDensity = StripDustDensity,
                    EdgeDriftBandPx = EmberBandPx,
                    EdgeFogReduced = _mode == EdgeFogMode.Reduced,
                    EdgeFogGain = FogGain(_hue),
                });
            }
        }

        private void Teardown()
        {
            _mode = EdgeFogMode.None;
            if (_strips.Count == 0) return;
            foreach (var strip in _strips)
            {
                try { strip.Stop(); } catch { }
            }
            _strips.Clear();
            Children.Clear();
        }
    }
}

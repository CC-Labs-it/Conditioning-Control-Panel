using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
namespace ConditioningControlPanel.Avalonia.Controls
{
    public class EdgeParticles : Grid
    {
        public const double StripThickness = 30;
        public const double StripIntensity = 0.55;
        public const double StripDustDensity = 0.35;

        private static readonly EdgeSide[] Sides = { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left };

        private readonly List<AmbientFxCanvas> _strips = new();
        private Color _hue = Color.FromRgb(0xB7, 0x9C, 0xFF);
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

        public static bool ShouldMount(MotionLevel level, bool tierAllowsParticles) =>
            level == MotionLevel.Full && tierAllowsParticles;

        private bool Allowed()
        {
            if (MotionOverride is null && TierAllowsParticlesOverride is null)
                return AmbientFxCanvas.Env.AllowParticles;
            var level = MotionOverride ?? AmbientFxCanvas.Env.Level;
            var current = AmbientFxCanvas.Env.CurrentTier;
            bool tier = TierAllowsParticlesOverride
                ?? (AmbientFxCanvas.Env.AllowAmbientMotion(current) && AmbientFxCanvas.Env.MaxAmbientParticles(current) > 0);
            return ShouldMount(level, tier);
        }

        public void Mount(Color hue)
        {
            _hue = hue;
            _mountAsked = true;
            try
            {
                if (!Allowed())
                {
                    Teardown();
                    return;
                }
                if (_strips.Count == 0) Build();
                else Retint(hue);
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

        public void Retint(Color hue)
        {
            _hue = hue;
            foreach (var strip in _strips)
                strip.Retint(hue);
        }

        private void Build()
        {
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
                    Layers = AmbientFxLayers.EdgeDrift,
                    EdgeSide = side,
                    Tint = _hue,
                    Intensity = StripIntensity,
                    DustDensity = StripDustDensity,
                });
            }
        }

        private void Teardown()
        {
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

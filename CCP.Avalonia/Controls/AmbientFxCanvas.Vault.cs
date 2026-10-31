using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Controls
{
    public readonly record struct VaultZone(Rect Bounds, Color Color, bool Diamond);

    public partial class AmbientFxCanvas
    {
        private struct VMote
        {
            public float X, Y, VX, VY, Life, Max, SizePx, Phase, PhaseSpd, Spin;
            public uint Rgb;
            public bool Diamond;
        }

        private VMote[] _vault = Array.Empty<VMote>();
        private int _vaultN;
        private float _vaultT;
        private VaultZone[] _vaultZones = Array.Empty<VaultZone>();
        private readonly Dictionary<uint, (IBrush Halo, IBrush Core)> _vaultTints = new();

        /// <summary>Motes alive right now (tests).</summary>
        internal int VaultMoteCount => _vaultN;

        private bool VaultLayer => (_config.Layers & AmbientFxLayers.VaultMotes) != 0;

        /// <summary>Where the motes rise from. The page calls this after layout and on scroll, so the
        /// gold stays near the Basic cards and the cyan near the Prime ones.</summary>
        public void SetVaultZones(IReadOnlyList<VaultZone> zones)
        {
            var copy = new VaultZone[zones?.Count ?? 0];
            for (int i = 0; i < copy.Length; i++) copy[i] = zones![i];
            _vaultZones = copy;
        }

        /// <summary>The Reduced level may tick for this layer alone (the page starts no other layer then).</summary>
        private bool ReducedVaultMayRun() =>
            VaultLayer && Env.Level == MotionLevel.Reduced
            && Env.AllowAmbientMotion(_tier) && _particleBudget > 0;

        internal void StepVault(float dt)
        {
            if (!VaultLayer)
            {
                _vaultN = 0;
                return;
            }
            var level = Env.Level;
            int target = _fogOnly ? 0 : VaultMoteRules.Target(level, _liveBudget);
            if (_vault.Length < VaultMoteRules.Cap(MotionLevel.Full)) _vault = new VMote[VaultMoteRules.Cap(MotionLevel.Full)];

            float speed = (float)VaultMoteRules.SpeedScale(level);
            for (int i = _vaultN - 1; i >= 0; i--)
            {
                var m = _vault[i];
                m.X += m.VX * dt * speed;
                m.Y += m.VY * dt * speed;
                m.Phase += m.PhaseSpd * dt;
                m.Life -= dt;
                if (m.Life <= 0f || m.Y < -0.05f) _vault[i] = _vault[--_vaultN];
                else _vault[i] = m;
            }
            if (_vaultN > target) _vaultN = target;

            double every = 1.0 / Math.Max(0.001, VaultMoteRules.SpawnPerSecond(level));
            _vaultT = _vaultN >= target ? Math.Min(_vaultT + dt, (float)every) : _vaultT + dt;
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 1 || h <= 1) return;
            while (_vaultN < target && _vaultT > every)
            {
                _vaultT -= (float)every;
                SpawnVaultMote(w, h);
            }
        }

        private static readonly Color[] LooseHues =
        {
            Color.FromRgb(0xFF, 0xD2, 0x7A),   // gold
            Color.FromRgb(0x8C, 0xF0, 0xFF),   // ice cyan
            Color.FromRgb(0xD8, 0xC6, 0xFF),   // lilac
        };

        private void SpawnVaultMote(double w, double h)
        {
            Point at;
            Color c;
            bool diamond;
            if (_vaultZones.Length > 0 && _rng.NextDouble() < VaultMoteRules.ZoneShare)
            {
                var z = _vaultZones[_rng.Next(_vaultZones.Length)];
                var p = VaultMoteRules.SpawnOnRing(z.Bounds.X, z.Bounds.Y, z.Bounds.Width, z.Bounds.Height,
                    _rng.NextDouble(), _rng.NextDouble(), _rng.NextDouble());
                at = new Point(p.X, p.Y);
                c = z.Color;
                diamond = z.Diamond;
            }
            else
            {
                at = new Point(_rng.NextDouble() * w, h * (0.15 + _rng.NextDouble() * 0.85));
                int k = _rng.Next(LooseHues.Length);
                c = LooseHues[k];
                diamond = k == 1;
            }
            if (at.X < 0 || at.X > w || at.Y < 0 || at.Y > h) return;

            float life = (float)(VaultMoteRules.LifeMin + _rng.NextDouble() * (VaultMoteRules.LifeMax - VaultMoteRules.LifeMin));
            _vault[_vaultN++] = new VMote
            {
                X = (float)(at.X / w),
                Y = (float)(at.Y / h),
                VX = (float)((_rng.NextDouble() - 0.5) * 0.012),
                VY = -(float)(0.012 + _rng.NextDouble() * 0.030),
                Life = life, Max = life,
                SizePx = (float)(diamond ? 3.0 + _rng.NextDouble() * 3.5 : 2.2 + _rng.NextDouble() * 3.0),
                Phase = (float)(_rng.NextDouble() * Math.PI * 2),
                PhaseSpd = (float)(3.0 + _rng.NextDouble() * 5.0),
                Spin = (float)(_rng.NextDouble() * 0.6 - 0.3),
                Rgb = (uint)((c.R << 16) | (c.G << 8) | c.B),
                Diamond = diamond,
            };
        }

        private void DrawVault(DrawingContext ctx, float w, float h)
        {
            for (int i = 0; i < _vaultN; i++)
            {
                var m = _vault[i];
                double env = Math.Sin(Math.PI * Math.Clamp(1 - m.Life / m.Max, 0, 1));
                double twinkle = 0.55 + 0.45 * Math.Sin(m.Phase), a = env * twinkle;
                if (a <= 0.01) continue;
                if (!_vaultTints.TryGetValue(m.Rgb, out var tint))
                {
                    var c = Color.FromRgb((byte)(m.Rgb >> 16), (byte)(m.Rgb >> 8), (byte)m.Rgb);
                    _vaultTints[m.Rgb] = tint = (MakeDot(c, 0), new SolidColorBrush(c).ToImmutable());
                }
                float x = m.X * w, y = m.Y * h, s = m.SizePx;
                DrawSprite(ctx, tint.Halo, x, y, s * 4.2f, s * 4.2f, (float)(0.42 * a));
                using (ctx.PushOpacity(0.95 * a))
                {
                    if (m.Diamond)
                    {
                        // Rotate a rectangle into the tall, slowly turning Prime rhombus.
                        var transform = Matrix.CreateRotation(Math.PI / 4) * Matrix.CreateScale(0.62 * Math.Sqrt(2), Math.Sqrt(2))
                            * Matrix.CreateRotation(m.Spin * m.Phase * 0.15) * Matrix.CreateTranslation(x, y);
                        using (ctx.PushTransform(transform))
                            ctx.DrawRectangle(tint.Core, null, new Rect(-s / 2, -s / 2, s, s));
                    }
                    else ctx.DrawEllipse(tint.Core, null, new Point(x, y), s * 0.45, s * 0.45);
                }
                if (twinkle > 0.93)
                {
                    double g = s * (m.Diamond ? 2.6 : 2);
                    using (ctx.PushOpacity(0.75 * a))
                    {
                        ctx.DrawLine(_vaultGlint, new Point(x - g, y), new Point(x + g, y));
                        ctx.DrawLine(_vaultGlint, new Point(x, y - g), new Point(x, y + g));
                    }
                }
            }
        }
        private readonly Pen _vaultGlint = new(Brushes.White, 1);
    }
}

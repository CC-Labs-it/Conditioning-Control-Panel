using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Controls;

public readonly record struct VaultZone(Rect Bounds, Color Color, bool Diamond);

public partial class AmbientFxCanvas
{
    // Adapted from parity/trunk 073afa0aa: keep this head's DrawingContext renderer and shared clock.
    private sealed class VaultMote
    {
        internal double X, Y, VX, VY, Life, Max, Size, Phase, PhaseSpeed, Spin;
        internal bool Diamond;
        internal Color Hue;
    }
    private readonly List<VaultMote> _vault = new();
    private VaultZone[] _vaultZones = Array.Empty<VaultZone>();
    private double _vaultT;
    private readonly Dictionary<Color, IBrush> _vaultHalos = new();
    internal int VaultMoteCount => _vault.Count;
    internal IReadOnlyList<VaultZone> VaultZones => _vaultZones;
    public void SetVaultZones(IReadOnlyList<VaultZone> zones)
    {
        _vaultZones = new VaultZone[zones.Count];
        for (int i = 0; i < zones.Count; i++) _vaultZones[i] = zones[i];
    }
    private bool ReducedVaultMayRun => (_config.Layers & AmbientFxLayers.VaultMotes) != 0
        && Env.Level == MotionLevel.Reduced && Env.AllowAmbientMotion(_tier) && _particleBudget > 0;

    internal void StepVault(double dt)
    {
        int target = (_config.Layers & AmbientFxLayers.VaultMotes) == 0 || _fogOnly ? 0 : VaultMoteMath.Target(Env.Level, _liveBudget);
        double speed = VaultMoteMath.SpeedScale(Env.Level);
        for (int i = _vault.Count - 1; i >= 0; i--)
        {
            var m = _vault[i];
            m.X += m.VX * dt * speed; m.Y += m.VY * dt * speed;
            m.Phase += m.PhaseSpeed * dt; m.Life -= dt;
            if (m.Life <= 0 || m.Y < -0.05) _vault.RemoveAt(i);
        }
        if (_vault.Count > target) _vault.RemoveRange(target, _vault.Count - target);
        double every = 1 / Math.Max(0.001, VaultMoteMath.SpawnPerSecond(Env.Level));
        _vaultT = Math.Min(_vaultT + dt, every * Math.Max(1, target));
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 1 || h <= 1) return;
        while (_vault.Count < target && _vaultT > every)
        {
            _vaultT -= every;
            Point at; Color hue; bool diamond;
            if (_vaultZones.Length > 0 && _rng.NextDouble() < VaultMoteMath.ZoneShare)
            {
                var z = _vaultZones[_rng.Next(_vaultZones.Length)];
                var p = VaultMoteMath.SpawnOnRing(new RectD(z.Bounds.X, z.Bounds.Y, z.Bounds.Width, z.Bounds.Height), _rng.NextDouble(), _rng.NextDouble(), _rng.NextDouble());
                at = new Point(p.X, p.Y); hue = z.Color; diamond = z.Diamond;
            }
            else
            {
                int k = _rng.Next(3);
                at = new Point(_rng.NextDouble() * w, h * (0.15 + _rng.NextDouble() * 0.85));
                hue = k == 0 ? Color.FromRgb(255, 210, 122) : k == 1 ? Color.FromRgb(140, 240, 255) : Color.FromRgb(216, 198, 255);
                diamond = k == 1;
            }
            if (at.X < 0 || at.X > w || at.Y < 0 || at.Y > h) continue;
            double life = VaultMoteMath.LifeMin + _rng.NextDouble() * (VaultMoteMath.LifeMax - VaultMoteMath.LifeMin);
            _vault.Add(new VaultMote { X = at.X / w, Y = at.Y / h, VX = (_rng.NextDouble() - 0.5) * 0.012,
                VY = -(0.012 + _rng.NextDouble() * 0.030), Life = life, Max = life, Hue = hue, Diamond = diamond,
                Size = diamond ? 3 + _rng.NextDouble() * 3.5 : 2.2 + _rng.NextDouble() * 3,
                Phase = _rng.NextDouble() * Math.PI * 2, PhaseSpeed = 3 + _rng.NextDouble() * 5, Spin = _rng.NextDouble() * 0.6 - 0.3 });
        }
    }

    private void DrawVault(DrawingContext dc, double w, double h)
    {
        foreach (var m in _vault)
        {
            double twinkle = 0.55 + 0.45 * Math.Sin(m.Phase);
            double a = Math.Sin(Math.PI * Math.Clamp(1 - m.Life / m.Max, 0, 1)) * twinkle;
            if (a <= 0.01) continue;
            var p = new Point(m.X * w, m.Y * h);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * 0.95), m.Hue.R, m.Hue.G, m.Hue.B));
            if (!_vaultHalos.TryGetValue(m.Hue, out var halo)) _vaultHalos[m.Hue] = halo = MakeDot(m.Hue, 0);
            using (dc.PushOpacity(0.42 * a)) dc.DrawEllipse(halo, null, p, m.Size * 4.2, m.Size * 4.2);
            using (dc.PushOpacity(a))
            {
                if (!m.Diamond) dc.DrawEllipse(brush, null, p, m.Size * 0.45, m.Size * 0.45);
                else
                {
                    using (dc.PushTransform(Matrix.CreateRotation(m.Spin * m.Phase * 0.15) * Matrix.CreateTranslation(p.X, p.Y)))
                    {
                        var g = new StreamGeometry();
                        using (var c = g.Open())
                        { c.BeginFigure(new Point(0, -m.Size), true); c.LineTo(new Point(m.Size * 0.62, 0)); c.LineTo(new Point(0, m.Size)); c.LineTo(new Point(-m.Size * 0.62, 0)); c.EndFigure(true); }
                        dc.DrawGeometry(brush, null, g);
                    }
                }
                if (twinkle > 0.93)
                {
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb(191, 255, 255, 255)), Math.Max(1, m.Size * 0.22));
                    double r = m.Size * (m.Diamond ? 2.6 : 2);
                    dc.DrawLine(pen, p.WithX(p.X - r), p.WithX(p.X + r));
                    dc.DrawLine(pen, p.WithY(p.Y - r), p.WithY(p.Y + r));
                }
            }
        }
    }
}

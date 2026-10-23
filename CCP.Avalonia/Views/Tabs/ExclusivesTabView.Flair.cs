using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ExclusivesTabView
{
    private sealed class CardMotion
    {
        internal readonly ScaleTransform Scale = new();
        internal readonly TranslateTransform Slide = new();
        internal long Started, HoverStarted;
        internal int Index;
        internal Border? Rim, HoverGlow;
        internal IconGlyph? Glint;
        internal readonly ScaleTransform GlintScale = new();
        internal readonly RotateTransform GlintTurn = new();
        internal double From = 1, To = 1, HoverMs = 150;
    }
    private readonly Dictionary<Control, CardMotion> _cards = new();
    private readonly List<DispatcherTimer> _clicks = new();
    private BeatLoop? _flair;
    private long _entranceStarted;
    private bool _zonesQueued, _pageActive;

    // WPF PlayVaultEntrance: groups take a slot too; cap the stagger, never the number of cards.
    internal static (double Opacity, double Y) EntranceAt(double ms, int index, MotionLevel level)
    {
        if (level == MotionLevel.Off) return (1, 0);
        bool full = level == MotionLevel.Full;
        double since = ms - (full ? 38 : 20) * Math.Min(index, full ? 16 : 8);
        double fade = Math.Clamp(since / (full ? 320 : 200), 0, 1);
        double u = 1 - Math.Clamp(since / 420, 0, 1);
        return (1 - (1 - fade) * (1 - fade), full ? 18 * (u * u * u - u * 0.35 * Math.Sin(Math.PI * u)) : 0);
    }

    private void AddCardMotion(Control card)
    {
        var rig = new CardMotion { Started = _entranceStarted, Index = _cards.Count,
            Rim = card.GetVisualDescendants().OfType<Border>().FirstOrDefault(c => c.Name == "OwnedRim"),
            HoverGlow = card.GetVisualDescendants().OfType<Border>().FirstOrDefault(c => c.Name == "HoverGlow"),
            Glint = card.GetVisualDescendants().OfType<IconGlyph>().FirstOrDefault(c => c.Name == "OwnedGlint") };
        if (rig.Glint is { } glint)
        {
            glint.RenderTransformOrigin = RelativePoint.Center;
            glint.RenderTransform = new TransformGroup { Children = { rig.GlintScale, rig.GlintTurn } };
        }
        _cards[card] = rig;
        card.RenderTransformOrigin = RelativePoint.Center;
        card.RenderTransform = new TransformGroup { Children = { rig.Scale, rig.Slide } };
        EnsureFlair();
        FlairFrame();
        QueueVaultZones();
    }

    private void UpdateFlair()
    {
        bool want = !PlansMode && IsVisible && this.IsAttachedToVisualTree()
            && AmbientFxCanvas.Env.Level != MotionLevel.Off;
        if (want && !_pageActive)
        {
            _entranceStarted = FxAdorner.Time.GetTimestamp();
            foreach (var r in _cards.Values) r.Started = _entranceStarted;
        }
        _pageActive = want;
        if (want) { EnsureFlair(); return; }
        _flair?.Stop(); _flair = null;
        CancelPendingClicks();
        foreach (var (card, rig) in _cards) { card.Opacity = 1; rig.Slide.Y = 0; rig.Scale.ScaleX = rig.Scale.ScaleY = 1; }
        FlairFrame();
    }

    private void EnsureFlair()
    {
        if (!_pageActive || PlansMode || !IsEffectivelyVisible || !this.IsAttachedToVisualTree() || !AmbientFxCanvas.Env.AllowTransitions) return;
        if (!AmbientFxCanvas.Env.AllowAmbientMotion(AmbientFxCanvas.Env.CurrentTier) && FiniteMotionDone) return;
        _flair ??= new BeatLoop(this, _ => FlairFrame());
        _flair.Start();
    }

    internal void FlairFrame()
    {
        foreach (var (card, rig) in _cards)
        {
            var level = _flair == null ? MotionLevel.Off : AmbientFxCanvas.Env.Level;
            var decor = AmbientFxCanvas.Env.AllowAmbientMotion(AmbientFxCanvas.Env.CurrentTier) ? level : MotionLevel.Off;
            double t = FxAdorner.Time.GetElapsedTime(rig.Started).TotalSeconds;
            var entry = EntranceAt(t * 1000, rig.Index, level);
            card.Opacity = Math.Clamp(entry.Opacity, 0, 1);
            rig.Slide.Y = entry.Y;
            double hover = Math.Clamp(FxAdorner.Time.GetElapsedTime(rig.HoverStarted).TotalMilliseconds / rig.HoverMs, 0, 1);
            rig.Scale.ScaleX = rig.Scale.ScaleY = rig.From + (rig.To - rig.From) * (1 - (1 - hover) * (1 - hover));
            if (card.DataContext is ExclusiveGroupRow group && card.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Rectangle>().FirstOrDefault(c => c.Name == "GroupSignSheen") is { } sign)
            {
                sign.IsVisible = decor != MotionLevel.Off;
                double cross = level == MotionLevel.Full ? 1.1 : 2.2, period = level == MotionLevel.Full ? 4.2 : 9;
                double since = t - (group.Group == PremiumGroup.Basic ? 0.5 : 1.2);
                double u = since < 0 ? 0 : Math.Clamp(since % period / cross, 0, 1);
                double x = -1.2 + 2.4 * (1 - Math.Cos(Math.PI * u)) / 2;
                var band = (LinearGradientBrush)sign.Fill!;
                band.StartPoint = new RelativePoint(x, 0, RelativeUnit.Relative); band.EndPoint = new RelativePoint(x + 1, 0.35, RelativeUnit.Relative);
            }
            if (card.DataContext is not ExclusiveCardRow row) continue;
            var rim = rig.Rim;
            if (rim == null) continue;
            rim.IsVisible = row.IsMine;
            int seed = (rig.Index + 1) * 7 + row.Feature.Key.Length;
            double jitter = Math.Abs(seed) % 7 * 0.13;
            rim.Opacity = decor == MotionLevel.Off ? 0.6 : Breath(t + jitter * 3, (level == MotionLevel.Full ? 2.6 : 5.2) + jitter,
                level == MotionLevel.Full ? 0.32 : 0.5, level == MotionLevel.Full ? 0.95 : 0.72);
            if (rig.Glint is { } glint)
            {
                double period = 5 + Math.Abs(seed * 37) % 100 / 100.0 * 4;
                double phase = t - (0.6 + Math.Abs(seed * 13) % 100 / 100.0 * 2.4);
                double ms = phase < 0 ? -1 : phase % period * 1000;
                glint.Opacity = row.IsMine && decor == MotionLevel.Full && ms >= 0 && ms < 620
                    ? ms <= 140 ? 1 - Math.Pow(1 - ms / 140, 2) : 1 - Math.Pow((ms - 140) / 480, 2) : 0;
                double u = 1 - Math.Clamp(ms / 180, 0, 1);
                double size = ms < 180 ? 0.2 + 0.95 * (1 - (u * u * u - u * 0.4 * Math.Sin(Math.PI * u))) : 1.15 - 0.65 * Math.Clamp((ms - 180) / 440, 0, 1);
                rig.GlintScale.ScaleX = rig.GlintScale.ScaleY = size;
                rig.GlintTurn.Angle = 90 * Math.Clamp(ms / 620, 0, 1);
            }
        }
        // Performance mode keeps finite hover/entrance transitions, then parks the shared beat.
        if (!AmbientFxCanvas.Env.AllowAmbientMotion(AmbientFxCanvas.Env.CurrentTier) && FiniteMotionDone)
        { _flair?.Stop(); _flair = null; }
    }

    private bool FiniteMotionDone => _cards.Values.All(r =>
        FxAdorner.Time.GetElapsedTime(r.Started).TotalMilliseconds >= (AmbientFxCanvas.Env.Level == MotionLevel.Full ? 1028 : 360)
        && FxAdorner.Time.GetElapsedTime(r.HoverStarted).TotalMilliseconds >= r.HoverMs);

    private void ScaleCard(Control card, double scale, double ms)
    {
        if (!_cards.TryGetValue(card, out var r)) return;
        r.From = r.Scale.ScaleX; r.To = scale; r.HoverMs = ms; r.HoverStarted = FxAdorner.Time.GetTimestamp();
        if (!AmbientFxCanvas.Env.AllowTransitions) r.From = scale;
        else EnsureFlair();
        FlairFrame();
    }

    internal void HoverCard(Control card, bool on)
    {
        ScaleCard(card, on ? 1.02 : 1, 150);
        if (_cards.TryGetValue(card, out var rig) && rig.HoverGlow is { } glow)
            glow.BoxShadow = on && AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier)
                ? Glow(VaultAccent(), 0.55, Math.Min(24, AmbientFxCanvas.Env.MaxGlowBlurRadius(AmbientFxCanvas.Env.CurrentTier))) : default;
        if (on) HoverPop.Enter(CardArt(card)); else HoverPop.Leave(CardArt(card));
    }

    internal void ClickCard(Control card, ExclusiveCardRow row)
    {
        if (!row.IsAvailable || !IsEffectivelyVisible || Windows.MainShellWindow.LockdownActive) return;
        if (!AmbientFxCanvas.Env.AllowParticles || card.Bounds.Width <= 0 || card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), _ambientFx) is not { } at)
        { Open(row.Feature); return; }
        _ambientFx.Burst(at.X, at.Y, row.Hue, 90);
        ScaleCard(card, 0.97, 80);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        timer.Tick += (_, _) =>
        {
            timer.Stop(); if (!_clicks.Remove(timer)) return;
            ScaleCard(card, card.IsPointerOver ? 1.02 : 1, 80);
            if (IsEffectivelyVisible) Open(row.Feature);
        };
        _clicks.Add(timer); timer.Start();
    }

    private void QueueVaultZones()
    {
        if (_zonesQueued || PlansMode) return;
        _zonesQueued = true;
        Dispatcher.UIThread.Post(() => { _zonesQueued = false; UpdateVaultZones(); }, DispatcherPriority.Background);
    }
    internal void UpdateVaultZones()
    {
        if (!_pageActive || PlansMode || !IsEffectivelyVisible) return;
        var zones = new List<VaultZone>();
        foreach (var (card, _) in _cards)
        {
            if (!card.IsEffectivelyVisible || card.Bounds.Width <= 0 || card.TranslatePoint(default, _ambientFx) is not { } p) continue;
            var rect = new Rect(p, card.Bounds.Size);
            if (rect.Bottom < 0 || rect.Top > _ambientFx.Bounds.Height) continue;
            if (card.DataContext is ExclusiveCardRow row) Add(rect, row.Hue, PremiumShelfOrder.GroupOf(row.Feature.Key, row.Tier) == PremiumGroup.Prime, row.IsMine ? 2 : 1);
            else if (card.DataContext is ExclusiveGroupRow group && group.HasSign && card.GetVisualDescendants().OfType<Image>().FirstOrDefault() is { } sign && sign.TranslatePoint(default, _ambientFx) is { } at)
                Add(new Rect(at, sign.Bounds.Size), group.Group == PremiumGroup.Basic ? Color.FromRgb(255, 200, 90) : Color.FromRgb(111, 232, 255), group.Group == PremiumGroup.Prime, 2);
        }
        _ambientFx.SetVaultZones(zones);
        void Add(Rect rect, Color hue, bool diamond, int weight) { for (int i = 0; i < weight; i++) zones.Add(new VaultZone(rect, hue, diamond)); }
    }

    // A queued door is work a panic must cancel, even though this inline view owns no window.
    internal void CancelPendingClicks()
    {
        foreach (var t in _clicks) t.Stop();
        _clicks.Clear();
        foreach (var r in _cards.Values) { r.From = r.To = 1; r.Scale.ScaleX = r.Scale.ScaleY = 1; }
    }
    internal int PendingClickCount => _clicks.Count;
}

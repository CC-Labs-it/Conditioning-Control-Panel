// WPF Services/Possession/Effects/DropEffect.cs:61-310: target safety, falling glyphs and undo.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class DropEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] DropRoles = { PossessionRole.TabHeader, PossessionRole.Title, PossessionRole.Label };
    private static readonly List<DropEffect> Live = new();
    private sealed class Glyph
    {
        public required Image Image;
        public required TranslateTransform Translation;
        public required RotateTransform Rotation;
        public double Start, Duration, Dx, Dy, Angle, ReturnX, ReturnY, ReturnAngle, HomeX, HomeY;
        public bool InRubble;
    }
    private readonly List<Glyph> _glyphs = new();
    private readonly HashSet<int> _dropped = new();
    private TextBlock? _text;
    private string? _original;
    private InlineCollection? _originalInlines, _ours;
    private Canvas? _layer, _floor;
    private Rect? _floorBounds;
    private RenderTargetBitmap? _snapshot;
    private IFrameSource? _frames;
    private Random _rng = Random.Shared;
    private CancellationTokenRegistration _cancellation;
    private TimeSpan? _start, _returnStart;
    private double _nextDrop, _returnDuration, _scaleX, _scaleY, _originX, _originY, _startX;
    private int _maximum, _generation;
    private bool _photosafe, _returning, _subscribed;
    private TaskCompletionSource? _returned;

    public override string Id => "drop";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override bool IsBig => true;
    public override double Weight => 4;
    public override TimeSpan HoldFor => TimeSpan.Zero;
    public override IReadOnlyList<PossessionRole> Roles => DropRoles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        ctx.Host is IAvaloniaPossessionHost { GhostLayer: not null } && FindTarget(target) is { } text && Fits(text, out _);

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer is not { } layer ||
            FindTarget(target) is not { } text || !Fits(text, out _startX) ||
            host.BoundsOnGhostLayer(text) is not { } bounds || bounds.Width <= 0 || bounds.Height <= 0)
            return Task.CompletedTask;
        var pixels = PixelSize.FromSize(text.Bounds.Size, TopLevel.GetTopLevel(text)?.RenderScaling ?? 1);
        if (pixels.Width <= 0 || pixels.Height <= 0 || pixels.Width > 8192 || pixels.Height > 8192) return Task.CompletedTask;
        var scale = TopLevel.GetTopLevel(text)?.RenderScaling ?? 1;
        var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        try { bitmap.Render(text); }
        catch { bitmap.Dispose(); return Task.CompletedTask; }
        _snapshot = bitmap;
        _text = text; _original = text.Text; _originalInlines = text.Inlines;
        _layer = layer; _floor = host.RubbleFloor; _floorBounds = _floor == null ? null : host.BoundsOnGhostLayer(_floor);
        _frames = host.PossessionFrames; _rng = ctx.Rng; _photosafe = ctx.Photosafe;
        _originX = bounds.X; _originY = bounds.Y;
        _scaleX = bounds.Width / text.Bounds.Width; _scaleY = bounds.Height / text.Bounds.Height;
        int eligible = 0;
        for (int i = 1; i < _original!.Length; i++) if (!char.IsWhiteSpace(_original[i])) eligible++;
        _maximum = Math.Max(1, (int)(eligible * 0.4));
        _nextDrop = 2000 + _rng.NextDouble() * 2000;
        Live.Add(this);
        _frames.Subscribe(OnFrame); _subscribed = true;
        int generation = ++_generation;
        _cancellation = ct.Register(() => Dispatcher.UIThread.Post(() =>
        { if (generation == _generation && IsLive && !_returning) _ = UndoAsync(TimeSpan.Zero); }));
        return Task.CompletedTask;
    }

    private static double Width(TextBlock text, string value) => value.Length == 0 ? 0 :
        new FormattedText(value, CultureInfo.CurrentUICulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
            text.Foreground ?? Brushes.White).WidthIncludingTrailingWhitespace;

    private static bool Fits(TextBlock text, out double start)
    {
        start = 0;
        if (text.Text == null || text.Bounds.Height <= 0) return false;
        double width = Width(text, text.Text), inner = text.Bounds.Width - text.Padding.Left - text.Padding.Right;
        if (width <= 0 || inner <= 0 || width > inner + 1.5) return false;
        start = text.Padding.Left;
        if (text.TextAlignment == TextAlignment.Center) start += Math.Max(0, (inner - width) / 2);
        else if (text.TextAlignment == TextAlignment.Right) start += Math.Max(0, inner - width);
        return true;
    }

    private bool StillOurs() => _text != null && _original != null &&
        string.Equals(_text.Text, _original, StringComparison.Ordinal) &&
        (_ours == null || ReferenceEquals(_text.Inlines, _ours));

    private void OnFrame(TimeSpan now)
    {
        if (_returning)
        {
            _returnStart ??= now;
            double f = Math.Clamp((now - _returnStart.Value).TotalMilliseconds / _returnDuration, 0, 1);
            double k = 1 - HeadEffect.CubicInOut(f);
            foreach (var g in _glyphs) { g.Translation.X = g.ReturnX * k; g.Translation.Y = g.ReturnY * k; g.Rotation.Angle = g.ReturnAngle * k; }
            if (f >= 1) Finish();
            return;
        }
        _start ??= now;
        double ms = (now - _start.Value).TotalMilliseconds;
        foreach (var g in _glyphs)
        {
            double f = Math.Clamp((ms - g.Start) / g.Duration, 0, 1);
            g.Translation.X = g.Dx * HeadEffect.CubicOut(f);
            g.Translation.Y = g.Dy * f * f;
            g.Rotation.Angle = g.Angle * HeadEffect.CubicOut(f);
            double bounce = ms - g.Start - g.Duration;
            if (!_photosafe && bounce >= 0 && bounce < 370)
                g.Translation.Y -= bounce < 170 ? 10 * HeadEffect.CubicOut(bounce / 170) : 10 * (1 - Math.Pow((bounce - 170) / 200, 2));
            if (!g.InRubble && _floor != null && _floorBounds is { } floor && bounce >= (_photosafe ? 20 : 370))
            {
                _layer?.Children.Remove(g.Image); Canvas.SetLeft(g.Image, g.HomeX - floor.X);
                Canvas.SetTop(g.Image, g.HomeY - floor.Y); _floor.Children.Add(g.Image); g.InRubble = true;
            }
        }
        bool ours = StillOurs();
        if (ours && _dropped.Count < _maximum && ms >= _nextDrop)
        {
            int priorCount = _glyphs.Count;
            DropOne(ms);
            double fall = _glyphs.Count > priorCount ? _glyphs[^1].Duration + (_photosafe ? 20 : 400) : 0;
            _nextDrop = ms + fall + 2000 + _rng.NextDouble() * 2000;
        }
        if ((!ours || _dropped.Count >= _maximum) && (_glyphs.Count == 0 ||
            ms >= _glyphs[^1].Start + _glyphs[^1].Duration + (_photosafe ? 20 : 370))) Park();
    }

    private void DropOne(double ms)
    {
        if (_text == null || _original == null || _layer == null || _snapshot == null) return;
        var choices = new List<int>();
        for (int i = 1; i < _original.Length; i++) if (!char.IsWhiteSpace(_original[i]) && !_dropped.Contains(i)) choices.Add(i);
        if (choices.Count == 0) return;
        int index = choices[_rng.Next(choices.Count)];
        double x0 = _startX + Width(_text, _original[..index]);
        double x1 = _startX + Width(_text, _original[..(index + 1)]);
        double w = Math.Max(2, x1 - x0), h = _text.Bounds.Height;
        if (x0 < 0 || x0 + w > _text.Bounds.Width + 1) return;
        var image = new Image
        {
            Source = new CroppedBitmap { Source = _snapshot, SourceRect = new PixelRect((int)(x0 * _snapshot.PixelSize.Width / _text.Bounds.Width), 0, Math.Max(1, Math.Min(_snapshot.PixelSize.Width - (int)(x0 * _snapshot.PixelSize.Width / _text.Bounds.Width), (int)Math.Ceiling(w * _snapshot.PixelSize.Width / _text.Bounds.Width))), _snapshot.PixelSize.Height) },
            Width = w * _scaleX, Height = h * _scaleY, Stretch = Stretch.Fill, IsHitTestVisible = false,
            RenderTransformOrigin = RelativePoint.Center,
        };
        var tr = new TranslateTransform(); var rot = new RotateTransform();
        var group = new TransformGroup(); group.Children.Add(rot); group.Children.Add(tr); image.RenderTransform = group;
        double homeY = _originY;
        Canvas.SetLeft(image, _originX + x0 * _scaleX); Canvas.SetTop(image, homeY);
        _layer.Children.Add(image);
        double floorY = _floorBounds is { } floor ? floor.Y + Math.Max(0, floor.Height - h * _scaleY) * _rng.NextDouble()
            : Math.Max(homeY + 80, _layer.Bounds.Height - 48);
        double dy = Math.Max(30, floorY - homeY);
        double amplitude = _photosafe ? 0.5 : 1;
        var glyph = new Glyph { Image = image, Translation = tr, Rotation = rot, Start = ms,
            Dx = (_rng.NextDouble() * 28 - 14) * amplitude, Dy = dy,
            Angle = (_rng.NextDouble() * 70 - 35) * amplitude, Duration = Math.Clamp(560 + dy * 0.6, 560, 1100),
            HomeX = _originX + x0 * _scaleX, HomeY = homeY };
        _glyphs.Add(glyph); _dropped.Add(index);
        var runs = new InlineCollection();
        for (int i = 0; i < _original.Length;)
        {
            bool gone = _dropped.Contains(i);
            int end = i + 1;
            while (end < _original.Length && _dropped.Contains(end) == gone) end++;
            var run = new Run(_original.Substring(i, end - i));
            if (gone) run.Foreground = Brushes.Transparent;
            runs.Add(run);
            i = end;
        }
        _ours = runs; _text.Inlines = runs;
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        if (_glyphs.Count == 0 || duration <= TimeSpan.Zero) { Finish(); return; }
        _returning = true; _returnDuration = Math.Min(600, Math.Max(400, duration.TotalMilliseconds));
        if (!_subscribed && _frames != null) { _frames.Subscribe(OnFrame); _subscribed = true; }
        foreach (var g in _glyphs)
        {
            if (g.InRubble && _layer != null)
            {
                _floor?.Children.Remove(g.Image); Canvas.SetLeft(g.Image, g.HomeX); Canvas.SetTop(g.Image, g.HomeY);
                _layer.Children.Add(g.Image); g.InRubble = false;
            }
            g.ReturnX = g.Translation.X; g.ReturnY = g.Translation.Y; g.ReturnAngle = g.Rotation.Angle;
        }
        var returned = _returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (await Task.WhenAny(returned.Task, Task.Delay(TimeSpan.FromMilliseconds(_returnDuration + 100))).ConfigureAwait(true) != returned.Task) Finish();
        await returned.Task.ConfigureAwait(true);
    }

    private void Park()
    {
        if (!_subscribed) return;
        _frames?.Unsubscribe(OnFrame); _subscribed = false;
    }

    private void Finish()
    {
        ++_generation;
        Park();
        foreach (var g in _glyphs) { _layer?.Children.Remove(g.Image); _floor?.Children.Remove(g.Image); }
        if (StillOurs() && _text != null) _text.Inlines = _originalInlines;
        _cancellation.Dispose(); _snapshot?.Dispose();
        _snapshot = null; _text = null; _original = null; _ours = _originalInlines = null;
        _layer = _floor = null; _floorBounds = null; _frames = null; _start = _returnStart = null;
        _glyphs.Clear(); _dropped.Clear(); _returning = false; Live.Remove(this);
        _returned?.TrySetResult(); _returned = null;
    }

    internal static void UndoAllLive()
    {
        foreach (var effect in Live.ToArray())
            if (effect._returning) effect.Finish(); else _ = effect.UndoAsync(TimeSpan.Zero);
    }

    internal static TextBlock? FindTarget(PossessionTarget? target)
    {
        if (target == null || (target.Role != PossessionRole.TabHeader &&
            target.Role != PossessionRole.Title && target.Role != PossessionRole.Label)) return null;
        var text = FindText(target.Visual as Visual, 0);
        if (text == null || BindingOperations.GetBindingExpressionBase(text, TextBlock.TextProperty) != null)
            return null;
        var value = text.Text;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4 || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            return null;
        if (text.Inlines is { Count: > 1 } ||
            (text.Inlines is { Count: 1 } inlines && inlines[0] is not Run)) return null;
        for (int i = 1; i < value.Length; i++)
            if (!char.IsWhiteSpace(value[i])) return text;
        return null;
    }

    private static TextBlock? FindText(Visual? root, int depth)
    {
        if (root == null || depth > 12) return null;
        if (root is TextBlock text) return text;
        foreach (var child in root.GetVisualChildren())
            if (FindText(child, depth + 1) is { } found) return found;
        return null;
    }
}

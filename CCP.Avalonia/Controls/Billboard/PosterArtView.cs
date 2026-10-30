using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Services.Billboard.Motion;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard;

// WPF 80a8cdfc9: one image, one matrix, on the shared 30 fps beat.
public sealed class PosterArtView : Grid, IBillboardArtView
{
    private readonly PosterCoverImage _image;
    private readonly Control _pointerScope;
    private readonly MatrixTransform _matrix = new();
    private readonly Helpers.BeatLoop _loop;
    private Bitmap? _bitmap;
    private double _last, _phase, _energy, _land = -1;
    private Vector _pointer;
    private Point _aim = new(.5, .5);
    private bool _hovered, _landed, _released;
    internal bool IsAnimating => _loop.IsRunning;
    internal Matrix CurrentMatrix => _matrix.Matrix;
    internal bool Hovered => _hovered;
    internal Point Aim => _aim;

    public PosterArtView(string? path, Control? pointerScope = null)
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0d, 0x1a));
        _image = new PosterCoverImage { RenderTransform = _matrix,
            RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative) };
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.HighQuality);
        try
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + path));
                _image.Source = _bitmap = new Bitmap(stream);
            }
        }
        catch (Exception ex) { Log.Debug("Billboard poster {Path} did not load: {E}", path, ex.Message); }
        Children.Add(_image);
        _loop = new Helpers.BeatLoop(this, seconds =>
        {
            if (!Env.AllowAmbientLoops) { Run(); return; }
            Step(seconds - _last, _hovered, _aim);
            _last = seconds;
        });
        // Observe the whole card, including handled events over its overlay buttons.
        _pointerScope = pointerScope ?? this;
        _pointerScope.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _pointerScope.PointerExited += OnPointerExited;
        SizeChanged += (_, _) => Apply();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        _hovered = p.X >= 0 && p.Y >= 0 && p.X <= Bounds.Width && p.Y <= Bounds.Height;
        if (_hovered) _aim = new Point(p.X / Math.Max(1, Bounds.Width), p.Y / Math.Max(1, Bounds.Height));
    }
    private void OnPointerExited(object? sender, PointerEventArgs e) => _hovered = false;

    public void Run()
    {
        if (_released) return;
        if (!Env.AllowAmbientLoops)
        {
            Hold();
            _phase = _energy = 0; _land = -1; _pointer = default;
            Apply();
            return;
        }
        if (_loop.IsRunning) return;
        if (!_landed) { _landed = true; if (Env.AllowTransitions) _land = 0; }
        _last = 0;
        _loop.Start();
    }
    public void Hold() => _loop.Stop();
    public void Release()
    {
        Hold();
        _released = true;
        _pointerScope.RemoveHandler(PointerMovedEvent, OnPointerMoved);
        _pointerScope.PointerExited -= OnPointerExited;
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Hold();
        base.OnDetachedFromVisualTree(e);
    }
    internal void Step(double dt, bool hovered, Point aim)
    {
        dt = Math.Clamp(dt, 0, .1);
        double target = hovered ? 1 : 0;
        _energy += (target - _energy) * (1 - Math.Exp(-dt / (target > _energy ? .18 : .48)));
        var pointer = hovered ? new Vector(aim.X * 2 - 1, aim.Y * 2 - 1) : default;
        _pointer += (pointer - _pointer) * (1 - Math.Exp(-dt / .22));
        _phase = (_phase + dt * AnimatedLogoImage.PhaseRate(_energy)) % Math.Tau;
        if (_land >= 0) { _land += dt; if (_land >= PosterMotion.LandSeconds) _land = -1; }
        Apply();
    }
    private void Apply()
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (!(w > 0 && h > 0)) return;
        var pose = _loop.IsRunning || _land >= 0 || _energy > 0
            ? PosterMotion.At(_phase, (_pointer.X, _pointer.Y), _energy, PosterMotion.Land(_land), w, h)
            : PosterMotion.Still;
        var m = PosterMotion.Matrix(pose, w, h);
        _matrix.Matrix = new Matrix(m.A, m.B, m.C, m.D, m.X, m.Y);
    }
}

// WPF LivingPosterArt.CoverImage: keep more of the right side of wide artwork.
internal sealed class PosterCoverImage : Control
{
    private IImage? _source;
    public IImage? Source
    {
        get => _source;
        set { _source = value; InvalidateVisual(); }
    }
    public PosterCoverImage() { IsHitTestVisible = false; ClipToBounds = true; }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double w = Bounds.Width, h = Bounds.Height;
        if (_source == null || !(w > 0 && h > 0)) return;
        double iw = Math.Max(1, _source.Size.Width), ih = Math.Max(1, _source.Size.Height);
        double k = Math.Max(w / iw, h / ih), dw = iw * k, dh = ih * k;
        context.DrawImage(_source, new Rect(_source.Size), new Rect((w - dw) * .62, (h - dh) * .5, dw, dh));
    }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Preview.Scenes;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace ConditioningControlPanel.Controls.Preview
{
    /// <summary>
    /// The live preview in the side-art card of a Studio effect page (pilot, owner 2026-11-24): a framed
    /// SLICE of a plain desktop filling the whole card, where the effect plays with fake pictures and
    /// answers clicks. No buttons, no caption, no words.
    ///
    /// <para>The desktop is static: painted once per size into a cached bitmap. The scene draws over it on
    /// an <see cref="FxSurface"/> (half resolution, Bgra8888) that covers ONLY the band of the slice that is
    /// on screen (the card is as tall as the options column and scrolls with it), so a frame rasters the
    /// visible part and nothing else. New pictures land in that band.</para>
    ///
    /// <para>Wakes when touched (<see cref="PreviewDriver"/>): a watched setting changes (through a
    /// <see cref="SettingsHook"/> on AppSettings, no per-page plumbing), a click in it, the page opening.
    /// The 30 fps <see cref="FrameClock"/> runs only while awake, loaded, visible, the window active and the
    /// motion gate open (<see cref="PreviewGate"/>; copied from AmbientFxCanvas), and re-checks on
    /// MotionFx.AmbientChanged and on a mod change. While the engine or a session runs it holds still.</para>
    /// </summary>
    public sealed class EffectPreview : FrameworkElement
    {
        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
            nameof(Scene), typeof(string), typeof(EffectPreview),
            new PropertyMetadata(null, (d, _) => ((EffectPreview)d).OnSceneChanged()));

        /// <summary>Which scene plays: "flash", or any id whose class is Scenes.&lt;Id&gt;PreviewScene.</summary>
        public string? Scene
        {
            get => (string?)GetValue(SceneProperty);
            set => SetValue(SceneProperty, value);
        }

        public static readonly DependencyProperty WakeOnPageInputProperty = DependencyProperty.Register(
            nameof(WakeOnPageInput), typeof(bool), typeof(EffectPreview), new PropertyMetadata(false));

        /// <summary>
        /// On a page whose settings raise no change event of their own (the Haptics tab keeps most of its
        /// values in a plain nested object), the preview wakes whenever a slider, switch or dropdown of
        /// its own page is touched. Off by default: Studio pages wake on the settings their scene watches.
        /// </summary>
        public bool WakeOnPageInput
        {
            get => (bool)GetValue(WakeOnPageInputProperty);
            set => SetValue(WakeOnPageInputProperty, value);
        }

        /// <summary>Tests: the pixel scale the desktop is painted at, instead of the element's DPI.</summary>
        internal static double? DesktopScaleOverride;

        private const int FaultLimit = 5;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly Brush BezelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x06, 0x05, 0x0d)));
        private static readonly Pen EdgeLine = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x45, 0x40, 0x6e))), 1.5));
        private static readonly Pen EdgeBand = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x10, 0x0e, 0x20))), 3.5));

        private readonly FxSurface _fx;
        private readonly DeskOverlay _over = new() { IsHitTestVisible = false };
        private WriteableBitmap? _furniture;
        private (int W, int H) _furnitureSize;
        private readonly FrameClock _clock;
        private readonly SettingsHook _hook;
        private PreviewDriver? _driver;
        private IPreviewScene? _scene;
        private Window? _window;
        private ScrollViewer? _scroll;
        private WriteableBitmap? _desk;
        private (int W, int H) _deskSize;
        private Rect _screen, _fxRect;
        private int _faults;
        private bool _hooked, _wasVisible;
        private readonly System.Collections.Generic.List<INotifyPropertyChanged> _nested = new();
        private FrameworkElement? _inputRoot;
        private RoutedEventHandler? _inputHandler;

        public EffectPreview()
        {
            ClipToBounds = true;
            Focusable = false;
            _fx = new FxSurface { ResolutionScale = 0.5 };
            _fx.PaintSurface += OnPaintSurface;
            AddVisualChild(_fx);
            AddVisualChild(_over);
            _clock = new FrameClock { Interval = TimeSpan.FromMilliseconds(33) };
            _clock.Tick += (_, _) => OnFrame();
            _hook = new SettingsHook(OnSettingChanged);
            AutomationProperties.SetName(this, Loc.Get("section_preview"));
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += (_, _) => OnVisibilityMaybeChanged();
        }

        /// <summary>The driver (tests).</summary>
        internal PreviewDriver? Driver => _driver;
        /// <summary>True while the frame clock runs (tests).</summary>
        internal bool IsTicking => _clock.IsEnabled;
        /// <summary>Share of the screen's pixels the scene rasters at (half by default; tests mimic a 150% screen).</summary>
        internal double RasterScale { get => _fx.ResolutionScale; set => _fx.ResolutionScale = value; }
        /// <summary>The scene's raster window inside the element (tests).</summary>
        internal Rect RasterBand => _fxRect;

        /// <summary>
        /// A scene id names its class by convention: "bubbles" is Scenes.BubblesPreviewScene (case ignored),
        /// so a new scene is one new file and needs no edit here.
        /// </summary>
        private static IPreviewScene? Create(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (id.Trim().Equals("flash", StringComparison.OrdinalIgnoreCase)) return new FlashPreviewScene();
            var type = typeof(FlashPreviewScene).Assembly.GetType(
                typeof(FlashPreviewScene).Namespace + "." + id.Trim() + "PreviewScene", throwOnError: false, ignoreCase: true);
            if (type == null || type.IsAbstract || !typeof(IPreviewScene).IsAssignableFrom(type)) return null;
            return Activator.CreateInstance(type, nonPublic: true) as IPreviewScene;
        }

        private void OnSceneChanged()
        {
            _scene?.Dispose();
            _scene = Create(Scene);
            _driver = _scene == null ? null : new PreviewDriver(_scene, () => Clock.Elapsed.TotalSeconds);
            InvalidateArrange();
            InvalidateVisual();
        }

        /// <summary>Use this scene (tests and pages that build their own).</summary>
        internal void UseScene(IPreviewScene scene, Func<double>? clock = null)
        {
            _scene?.Dispose();
            _scene = scene;
            _driver = new PreviewDriver(scene, clock ?? (() => Clock.Elapsed.TotalSeconds));
            InvalidateArrange();
            InvalidateVisual();
        }

        // ---------------------------------------------------------------- layout

        protected override int VisualChildrenCount => 2;
        protected override Visual GetVisualChild(int index) => index == 0 ? _fx : _over;

        protected override Size MeasureOverride(Size availableSize)
        {
            _fx.Measure(new Size(0, 0));
            _over.Measure(new Size(0, 0));
            return new Size(0, 0);   // the card's size comes from the options column, never from the preview
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var b = PreviewDesktop.Bezel;
            _screen = new Rect(b, b, Math.Max(0, finalSize.Width - 2 * b), Math.Max(0, finalSize.Height - 2 * b));
            var band = Rect.Intersect(_screen, VisibleInSelf(finalSize));
            _fxRect = band.IsEmpty ? new Rect(_screen.X, _screen.Y, 0, 0) : band;
            _fx.Arrange(_fxRect);
            _over.Arrange(new Rect(finalSize));
            _scene?.Layout(new SKRect(0, 0, (float)_screen.Width, (float)_screen.Height), VisibleInScreen());
            Evaluate();
            _fx.Redraw();
            return finalSize;
        }

        /// <summary>The part of this element its page ScrollViewer shows right now (own coordinates).</summary>
        private Rect VisibleInSelf(Size size)
        {
            var full = new Rect(size);
            if (_scroll == null) return full;
            try
            {
                var viewport = new Rect(0, 0, _scroll.ActualWidth, _scroll.ActualHeight);
                var seen = _scroll.TransformToDescendant(this).TransformBounds(viewport);
                seen.Intersect(full);
                return seen;
            }
            catch (InvalidOperationException) { return full; }
        }

        private SKRect VisibleInScreen()
        {
            if (_fxRect.Height <= 0) return new SKRect(0, 0, (float)_screen.Width, 0);
            return new SKRect((float)(_fxRect.X - _screen.X), (float)(_fxRect.Y - _screen.Y),
                (float)(_fxRect.Right - _screen.X), (float)(_fxRect.Bottom - _screen.Y));
        }

        // ---------------------------------------------------------------- drawing

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRoundedRectangle(BezelBrush, null, new Rect(0, 0, w, h), PreviewDesktop.OuterRadius, PreviewDesktop.OuterRadius);
            dc.DrawRoundedRectangle(null, EdgeBand, Inset(new Rect(0, 0, w, h), 3.25), PreviewDesktop.OuterRadius - 3, PreviewDesktop.OuterRadius - 3);
            dc.DrawRoundedRectangle(null, EdgeLine, Inset(new Rect(0, 0, w, h), 0.75), PreviewDesktop.OuterRadius - 0.75, PreviewDesktop.OuterRadius - 0.75);
            if (_screen.Width <= 0 || _screen.Height <= 0) return;
            var desk = Desk();
            if (desk == null) return;
            dc.PushClip(new RectangleGeometry(_screen, PreviewDesktop.ScreenRadius, PreviewDesktop.ScreenRadius));
            dc.DrawImage(desk, _screen);
            dc.Pop();
            // A scene that paints its own wallpaper gets the windows, icons and taskbar laid over it.
            var over = _scene is IPreviewWallpaperScene ? Bake(false, ref _furniture, ref _furnitureSize) : null;
            if (!ReferenceEquals(_over.Image, over) || _over.Screen != _screen)
            {
                _over.Image = over;
                _over.Screen = _screen;
                _over.InvalidateVisual();
            }
        }

        private WriteableBitmap? Desk() => Bake(true, ref _desk, ref _deskSize);

        private WriteableBitmap? Bake(bool wallpaper, ref WriteableBitmap? cache, ref (int W, int H) cacheSize)
        {
            double scale = DesktopScaleOverride ?? Dpi();
            int pw = Math.Max(1, (int)Math.Ceiling(_screen.Width * scale)), ph = Math.Max(1, (int)Math.Ceiling(_screen.Height * scale));
            if (cache != null && cacheSize == (pw, ph)) return cache;
            try
            {
                var bmp = new WriteableBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32, null);
                bmp.Lock();
                try
                {
                    var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using var surface = SKSurface.Create(info, bmp.BackBuffer, bmp.BackBufferStride);
                    if (surface != null)
                    {
                        surface.Canvas.Clear(SKColors.Transparent);
                        surface.Canvas.Scale(pw / (float)_screen.Width, ph / (float)_screen.Height);
                        PreviewDesktop.Paint(surface.Canvas, (float)_screen.Width, (float)_screen.Height, wallpaper);
                        surface.Canvas.Flush();
                    }
                    bmp.AddDirtyRect(new Int32Rect(0, 0, pw, ph));
                }
                finally { bmp.Unlock(); }
                bmp.Freeze();
                cache = bmp;
                cacheSize = (pw, ph);
            }
            catch (Exception ex) { App.Logger?.Debug("EffectPreview desktop: {E}", ex.Message); }
            return cache;
        }

        /// <summary>The desktop's windows, icons and taskbar over a wallpaper scene (never hit-tested).</summary>
        private sealed class DeskOverlay : FrameworkElement
        {
            public ImageSource? Image;
            public Rect Screen;

            protected override void OnRender(DrawingContext dc)
            {
                if (Image == null || Screen.Width <= 0 || Screen.Height <= 0) return;
                dc.PushClip(new RectangleGeometry(Screen, PreviewDesktop.ScreenRadius, PreviewDesktop.ScreenRadius));
                dc.DrawImage(Image, Screen);
                dc.Pop();
            }
        }

        private double Dpi()
        {
            try { return VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { return 1.0; }
        }

        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var scene = _scene;
            if (scene == null || _fxRect.Width <= 0 || _fxRect.Height <= 0) return;
            try
            {
                var c = e.Surface.Canvas;
                c.Save();
                c.Scale(e.Info.Width / (float)_fxRect.Width, e.Info.Height / (float)_fxRect.Height);
                c.Translate((float)(_screen.X - _fxRect.X), (float)(_screen.Y - _fxRect.Y));
                var screen = new SKRect(0, 0, (float)_screen.Width, (float)_screen.Height);
                c.ClipRoundRect(new SKRoundRect(screen, PreviewDesktop.ScreenRadius, PreviewDesktop.ScreenRadius), antialias: true);
                scene.Render(c, screen, VisibleInScreen());
                c.Restore();
            }
            catch (Exception ex) { Fault(ex); }
        }

        /// <summary>Paint the scene band now (tests and one-off repaints).</summary>
        internal void Repaint() => _fx.Redraw();

        // ---------------------------------------------------------------- lifecycle

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                HookWindow(Window.GetWindow(this));
                _scroll = FindScroll(this);
                if (_scroll != null) _scroll.ScrollChanged += OnScrollChanged;
                if (!_hooked)
                {
                    _hooked = true;
                    if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
                    MotionFx.AmbientChanged += OnAmbientChanged;
                    if (App.Settings != null) App.Settings.CurrentReplaced += OnSettingsReplaced;
                    _hook.Rebind();
                    HookNested();
                }
                if (WakeOnPageInput) HookPageInput(FindPage(this));
                OnVisibilityMaybeChanged();
            }
            catch (Exception ex) { App.Logger?.Debug("EffectPreview.OnLoaded: {E}", ex.Message); }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _clock.Stop();
                UnhookWindow();
                if (_scroll != null) _scroll.ScrollChanged -= OnScrollChanged;
                _scroll = null;
                if (_hooked)
                {
                    _hooked = false;
                    if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
                    MotionFx.AmbientChanged -= OnAmbientChanged;
                    if (App.Settings != null) App.Settings.CurrentReplaced -= OnSettingsReplaced;
                    _hook.Unhook();
                    UnhookNested();
                }
                UnhookPageInput();
                _wasVisible = false;
            }
            catch (Exception ex) { App.Logger?.Debug("EffectPreview.OnUnloaded: {E}", ex.Message); }
        }

        /// <summary>The page was opened (loaded and shown): the preview plays its first burst.</summary>
        private void OnVisibilityMaybeChanged()
        {
            bool now = IsLoaded && IsVisible;
            if (now && !_wasVisible && _driver != null) _driver.Open();
            _wasVisible = now;
            Evaluate();
            if (now) _fx.Redraw();
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0 || e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0) InvalidateArrange();
        }

        private void OnAmbientChanged()
        {
            if (Dispatcher.CheckAccess()) Evaluate();
            else Dispatcher.BeginInvoke(new Action(Evaluate));
        }

        private void OnModChanged(object? sender, ModPackage mod)
        {
            try { Dispatcher.BeginInvoke(new Action(Evaluate)); } catch { }
        }

        private void OnSettingsReplaced()
        {
            try { Dispatcher.BeginInvoke(new Action(() => { _hook.Rebind(); HookNested(); })); } catch { }
        }

        /// <summary>
        /// Settings objects that live INSIDE AppSettings and raise their own change events (Haptics and
        /// its audio sync) reach the scene through the same handler, under their own property names.
        /// </summary>
        private void HookNested()
        {
            UnhookNested();
            var haptics = App.Settings?.Current?.Haptics;
            if (haptics is INotifyPropertyChanged h) { h.PropertyChanged += OnSettingChanged; _nested.Add(h); }
            if (haptics?.AudioSync is INotifyPropertyChanged a) { a.PropertyChanged += OnSettingChanged; _nested.Add(a); }
        }

        private void UnhookNested()
        {
            foreach (var n in _nested) n.PropertyChanged -= OnSettingChanged;
            _nested.Clear();
        }

        private static FrameworkElement? FindPage(DependencyObject from)
        {
            for (var d = VisualTreeHelper.GetParent(from); d != null; d = VisualTreeHelper.GetParent(d))
                if (d is UserControl page) return page;
            return null;
        }

        /// <summary>Wake on any slider, switch or dropdown of <paramref name="page"/> (see <see cref="WakeOnPageInput"/>).</summary>
        internal void HookPageInput(FrameworkElement? page)
        {
            UnhookPageInput();
            if (page == null) return;
            _inputRoot = page;
            _inputHandler = (_, _) => Poke();
            page.AddHandler(System.Windows.Controls.Primitives.RangeBase.ValueChangedEvent, _inputHandler, true);
            page.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, _inputHandler, true);
            page.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, _inputHandler, true);
            page.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, _inputHandler, true);
        }

        private void UnhookPageInput()
        {
            if (_inputRoot != null && _inputHandler != null)
            {
                _inputRoot.RemoveHandler(System.Windows.Controls.Primitives.RangeBase.ValueChangedEvent, _inputHandler);
                _inputRoot.RemoveHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, _inputHandler);
                _inputRoot.RemoveHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, _inputHandler);
                _inputRoot.RemoveHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, _inputHandler);
            }
            _inputRoot = null;
            _inputHandler = null;
        }

        /// <summary>Start the wake window without naming a setting (a control of the page was touched).</summary>
        private void Poke()
        {
            if (_driver == null) return;
            try
            {
                Evaluate();
                if (_driver.Wake()) { Evaluate(); _fx.Redraw(); }
            }
            catch (Exception ex) { Fault(ex); }
        }

        private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            var name = e.PropertyName;
            if (name == null || _driver == null || !_driver.Watches(name)) return;
            void Apply()
            {
                try
                {
                    Evaluate();
                    if (_driver.SettingChanged(name)) { Evaluate(); _fx.Redraw(); }
                }
                catch (Exception ex) { Fault(ex); }
            }
            if (Dispatcher.CheckAccess()) Apply();
            else Dispatcher.BeginInvoke(new Action(Apply));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            // Never a window drag from inside the preview, and nothing below it sees the click.
            e.Handled = true;
            if (_driver == null) return;
            var p = e.GetPosition(this);
            if (!_screen.Contains(p)) return;
            try
            {
                Evaluate();
                if (_driver.Click(new SKPoint((float)(p.X - _screen.X), (float)(p.Y - _screen.Y)))) { Evaluate(); _fx.Redraw(); }
            }
            catch (Exception ex) { Fault(ex); }
        }

        private void HookWindow(Window? window)
        {
            if (ReferenceEquals(_window, window)) return;
            UnhookWindow();
            _window = window;
            if (_window == null) return;
            _window.Activated += OnWindowStateish;
            _window.Deactivated += OnWindowStateish;
            _window.StateChanged += OnWindowStateish;
        }

        private void UnhookWindow()
        {
            if (_window == null) return;
            _window.Activated -= OnWindowStateish;
            _window.Deactivated -= OnWindowStateish;
            _window.StateChanged -= OnWindowStateish;
            _window = null;
        }

        private void OnWindowStateish(object? sender, EventArgs e) => Evaluate();

        /// <summary>The single gate: pace from the motion rules, the clock only while everything says it may run.</summary>
        private void Evaluate()
        {
            var d = _driver;
            if (d == null) { _clock.Stop(); return; }
            try
            {
                d.Pace = PreviewGate.Pace(MotionFx.Level, MotionFx.PanelQuiet, MotionFx.AllowAmbientLoops,
                    PerformanceProfile.AllowAmbientMotion(PerformanceProfile.CurrentTier));
                var w = _window;
                d.CanShow = _faults < FaultLimit && _fxRect.Height > 0
                    && PreviewGate.CanShow(IsLoaded, IsVisible, w == null || w.IsActive, w?.WindowState == WindowState.Minimized);
                if (d.WantsClock) _clock.Start();
                else _clock.Stop();
            }
            catch (Exception ex) { _clock.Stop(); App.Logger?.Debug("EffectPreview.Evaluate: {E}", ex.Message); }
        }

        private void OnFrame()
        {
            try
            {
                if (_driver == null || !_driver.Frame()) { _clock.Stop(); return; }
                _fx.Redraw();
            }
            catch (Exception ex) { Fault(ex); }
        }

        private void Fault(Exception ex)
        {
            _faults++;
            App.Logger?.Debug("EffectPreview: {E}", ex.Message);
            if (_faults >= FaultLimit) _clock.Stop();
        }

        private static ScrollViewer? FindScroll(DependencyObject from)
        {
            for (var node = VisualTreeHelper.GetParent(from); node != null; node = VisualTreeHelper.GetParent(node))
                if (node is ScrollViewer sv) return sv;
            return null;
        }

        private static Rect Inset(Rect r, double by) => new(r.X + by, r.Y + by, Math.Max(0, r.Width - 2 * by), Math.Max(0, r.Height - 2 * by));
        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}

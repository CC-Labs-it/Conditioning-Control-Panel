// PORTED from ConditioningControlPanel/Services/Possession/EmberAttribution.cs (WPF, 484 lines): the
// attribution grammar every possession effect speaks - the ember CHARGE before anything moves, the
// possessed OUTLINE (+ the refcounted cursor RING) while it stays moved, and the window-EDGE PULSE for
// tripwires and rung changes. Ember #FF8A5C is reserved for Possession; never reuse it for theme.
//
// WPF drove every overlay with DoubleAnimations and a 100 ms follow DispatcherTimer. Here one frame
// subscription on the host window's frame source steps every overlay from frame timestamps (P40, P82:
// no Animation objects), and stops when nothing is left to step. Same numbers: charge 0 -> 0.95 at
// 35% -> 0 over max(80, duration) ms with a quadratic ease out; still mode (photosafe, Gentle, or motion
// Off) is a flat tint held max(duration, 600) ms; the pulse is 0 -> strength at 40% -> 0 over 500 ms
// (still: 900 ms, strength capped at 0.5), at most three live; the ring fades over 250 ms.
//
// Divergences, by house rule: WPF's charge glow (DropShadowEffect) and the pulse's BlurEffect are
// Effects, which this head never puts under a looping FX layer; the charge keeps its flat ember fill
// and the pulse draws a soft inset BoxShadow instead.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Possession
{
    internal sealed class EmberAttribution : IPossessionAttribution
    {
        /// <summary>Ember. Possession only. Never reuse for theme (that is crimson #DC143C).</summary>
        public static readonly Color Ember = Color.FromRgb(0xFF, 0x8A, 0x5C);

        private static readonly IBrush EmberSolid = new SolidColorBrush(Ember);
        private static readonly IBrush EmberOutline = new SolidColorBrush(Color.FromArgb(191, Ember.R, Ember.G, Ember.B)); // 75%
        private static readonly IBrush EmberTint12 = new SolidColorBrush(Color.FromArgb(31, Ember.R, Ember.G, Ember.B));   // 12%
        private static readonly IBrush EmberTint08 = new SolidColorBrush(Color.FromArgb(20, Ember.R, Ember.G, Ember.B));   // 8%

        internal const int MaxLivePulses = 3;
        private const double RingFadeMs = 250;
        /// <summary>How long past its own length a charge may wait for frames before it ends on the wall clock.</summary>
        internal const int ChargeFallbackMs = 5000;

        private readonly IAvaloniaPossessionHost _host;
        private readonly Func<bool> _photosafe;
        private readonly Action<TimeSpan> _onFrame;
        private bool _subscribed;

        private readonly List<Outline> _outlines = new();
        private readonly List<Overlay> _pulses = new();
        private readonly List<Overlay> _charges = new();
        private Ellipse? _ring;
        private double _ringFrom, _ringTo;
        private TimeSpan? _ringFadeStart;
        private bool _ringHooked;
        private int _possessCount;

        /// <param name="photosafe">Read LIVE (LockdownPhotosafe) so flipping the toggle mid-lockdown takes
        /// effect on the very next charge instead of at the next lockdown.</param>
        public EmberAttribution(IAvaloniaPossessionHost host, Func<bool>? photosafe = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _photosafe = photosafe ?? (() => false);
            _onFrame = OnFrame;
        }

        public bool AnyPossessed => _possessCount > 0;

        /// <summary>Overlays live on the ghost layer right now (tests).</summary>
        internal int LiveOverlayCount => _charges.Count + _pulses.Count + _outlines.Count;
        internal int LivePulseCount => _pulses.Count;
        internal Ellipse? Ring => _ring;

        /// <summary>WPF StillMotion: the user asked for no flashing (photosafe, which Gentle implies), or
        /// motion is switched off.</summary>
        private bool StillMotion
        {
            get
            {
                try
                {
                    return GentleVisuals.Photosafe(_photosafe())
                           || CoreSettings.Service?.Current?.MotionLevel == MotionLevel.Off;
                }
                catch { return true; }
            }
        }

        // -----------------------------------------------------------------------------------------
        //  The charge
        // -----------------------------------------------------------------------------------------

        public Task ChargeAsync(object control, CancellationToken ct, int durationMs = 400)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (control is not Control target) { tcs.TrySetResult(false); return tcs.Task; }
            OnUi(() =>
            {
                try { StartCharge(target, ct, durationMs, tcs); }
                catch (Exception ex)
                {
                    Serilog.Log.Warning("Possession charge failed: {Error}", ex.Message);
                    tcs.TrySetResult(false);
                }
            });
            return tcs.Task;
        }

        private void StartCharge(Control target, CancellationToken ct, int durationMs, TaskCompletionSource<bool> tcs)
        {
            var layer = _host.GhostLayer;
            if (layer == null || !target.IsEffectivelyVisible) { tcs.TrySetResult(false); return; }
            var bounds = BoundsOf(target);
            if (bounds is not { } b || b.Width <= 1 || b.Height <= 1) { tcs.TrySetResult(false); return; }

            var still = StillMotion;
            var border = new Border
            {
                Width = b.Width,
                Height = b.Height,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = EmberSolid,
                Background = EmberTint12,
                IsHitTestVisible = false,
                Opacity = still ? 1.0 : 0.0,
            };
            Canvas.SetLeft(border, b.X);
            Canvas.SetTop(border, b.Y);
            layer.Children.Add(border);

            var overlay = new Overlay(border)
            {
                DurationMs = still ? Math.Max(durationMs, 600) : Math.Max(80, durationMs),
                Still = still,
                Peak = 0.95,
                PeakAt = 0.35,
            };
            CancellationTokenRegistration reg = default;
            IDisposable? fallback = null;
            overlay.OnDone = () =>
            {
                try { reg.Dispose(); } catch { }
                try { fallback?.Dispose(); } catch { }
                tcs.TrySetResult(true);
            };
            _charges.Add(overlay);
            if (ct.CanBeCanceled) reg = ct.Register(() => OnUi(() => Finish(_charges, overlay)));
            // Frames stop while the window is minimized or covered; the director awaits this charge before
            // the effect moves and keeps its pick slot until then, so a charge that never ends would stall the
            // haunt for the rest of the lockdown. WPF's composition clock never stops; this is that belt.
            fallback = DispatcherTimer.RunOnce(() => Finish(_charges, overlay),
                TimeSpan.FromMilliseconds(overlay.DurationMs + ChargeFallbackMs));
            EnsureFrames();
        }

        // -----------------------------------------------------------------------------------------
        //  The possessed outline (+ the cursor ring it refcounts)
        // -----------------------------------------------------------------------------------------

        public IDisposable Possess(object control)
        {
            var handle = new Outline(this, control as Control);
            OnUi(handle.Start);
            return handle;
        }

        private void AddOutline(Outline handle)
        {
            var layer = _host.GhostLayer;
            if (layer == null || handle.Element == null) return;
            handle.Border = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = EmberOutline,
                Background = EmberTint08,
                CornerRadius = new CornerRadius(6),
                IsHitTestVisible = false,
                Width = 0,
                Height = 0,
            };
            layer.Children.Add(handle.Border);
            _outlines.Add(handle);
            PositionOutline(handle);
            _possessCount++;
            UpdateRing();
            EnsureFrames();
        }

        private void RemoveOutline(Outline handle)
        {
            if (_outlines.Remove(handle))
            {
                _possessCount = Math.Max(0, _possessCount - 1);
                UpdateRing();
            }
            RemoveChild(handle.Border);
            handle.Border = null;
        }

        /// <summary>Effects MOVE the control under the outline, so it chases it every frame (WPF: 100 ms).</summary>
        private void PositionOutline(Outline handle)
        {
            var border = handle.Border;
            var el = handle.Element;
            if (border == null || el == null) return;
            if (BoundsOf(el) is not { } b || b.Width <= 0 || b.Height <= 0 || !el.IsEffectivelyVisible)
            {
                border.IsVisible = false;
                return;
            }
            border.IsVisible = true;
            border.Width = b.Width;
            border.Height = b.Height;
            Canvas.SetLeft(border, b.X);
            Canvas.SetTop(border, b.Y);
        }

        private sealed class Outline : IDisposable
        {
            private readonly EmberAttribution _owner;
            private bool _disposed, _started;
            public Control? Element;
            public Border? Border;

            public Outline(EmberAttribution owner, Control? element)
            {
                _owner = owner;
                Element = element;
            }

            public void Start()
            {
                if (_disposed || _started || Element == null) return;
                _started = true;
                try { _owner.AddOutline(this); }
                catch (Exception ex) { Serilog.Log.Warning("Possession outline failed: {Error}", ex.Message); }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.OnUi(() =>
                {
                    try { if (_started) _owner.RemoveOutline(this); }
                    catch (Exception ex) { Serilog.Log.Warning("Possession outline release failed: {Error}", ex.Message); }
                    Element = null;
                });
            }
        }

        // -----------------------------------------------------------------------------------------
        //  The cursor ring
        // -----------------------------------------------------------------------------------------

        private void UpdateRing()
        {
            var layer = _host.GhostLayer;
            if (layer == null) return;
            if (_ring == null)
            {
                _ring = new Ellipse
                {
                    Width = 22,
                    Height = 22,
                    Stroke = EmberSolid,
                    StrokeThickness = 1.5,
                    Fill = null,
                    IsHitTestVisible = false,
                    Opacity = 0,
                };
            }
            if (!layer.Children.Contains(_ring)) layer.Children.Add(_ring);
            var p = _host.PointerPosition;
            Canvas.SetLeft(_ring, p.X - _ring.Width / 2);
            Canvas.SetTop(_ring, p.Y - _ring.Height / 2);
            HookRing();
            FadeRing(_possessCount > 0 ? 1.0 : 0.0);
        }

        private void HookRing()
        {
            if (_ringHooked || _host.PossessionWindow is not { } win) return;
            win.AddHandler(InputElement.PointerMovedEvent, OnRingPointerMoved, global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            win.PointerExited += OnRingPointerExited;
            _ringHooked = true;
        }

        private void UnhookRing()
        {
            if (!_ringHooked) return;
            if (_host.PossessionWindow is { } win)
            {
                win.RemoveHandler(InputElement.PointerMovedEvent, OnRingPointerMoved);
                win.PointerExited -= OnRingPointerExited;
            }
            _ringHooked = false;
        }

        private void OnRingPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_ring == null || _possessCount <= 0 || _host.GhostLayer is not { } layer) return;
            try
            {
                var p = e.GetPosition(layer);
                Canvas.SetLeft(_ring, p.X - _ring.Width / 2);
                Canvas.SetTop(_ring, p.Y - _ring.Height / 2);
                if (_ringTo <= 0) FadeRing(1.0);
            }
            catch { }
        }

        private void OnRingPointerExited(object? sender, PointerEventArgs e) => FadeRing(0);

        private void FadeRing(double to)
        {
            if (_ring == null) return;
            _ringFrom = _ring.Opacity;
            _ringTo = to;
            _ringFadeStart = null;   // stamped on the next frame
            EnsureFrames();
        }

        // -----------------------------------------------------------------------------------------
        //  The edge pulse
        // -----------------------------------------------------------------------------------------

        public void EdgePulse(double strength)
        {
            if (strength <= 0) return;
            OnUi(() =>
            {
                try { StartPulse(strength); }
                catch (Exception ex) { Serilog.Log.Warning("Possession edge pulse failed: {Error}", ex.Message); }
            });
        }

        private void StartPulse(double strength)
        {
            var layer = _host.GhostLayer;
            if (layer == null) return;
            if (_pulses.Count >= MaxLivePulses) return;   // overlapping scares stack, but never into a strobe
            var w = layer.Bounds.Width;
            var h = layer.Bounds.Height;
            if (w <= 0 || h <= 0) return;

            var still = StillMotion;
            strength = Math.Clamp(strength, 0, still ? 0.5 : 1.0);
            var border = new Border
            {
                Width = w,
                Height = h,
                BorderThickness = new Thickness(10),
                BorderBrush = EmberSolid,
                BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, Blur = 24, Spread = 6, Color = Ember }),
                IsHitTestVisible = false,
                Opacity = 0,
            };
            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, 0);
            layer.Children.Add(border);
            _pulses.Add(new Overlay(border) { DurationMs = still ? 900 : 500, Peak = strength, PeakAt = 0.4 });
            EnsureFrames();
        }

        // -----------------------------------------------------------------------------------------
        //  The frame step
        // -----------------------------------------------------------------------------------------

        private sealed class Overlay
        {
            public Overlay(Border border) => Border = border;
            public Border Border { get; }
            public TimeSpan? Start;
            public double DurationMs, Peak, PeakAt;
            public bool Still;
            public Action? OnDone;
        }

        private void EnsureFrames()
        {
            if (_subscribed) return;
            _subscribed = true;
            _host.PossessionFrames.Subscribe(_onFrame);
        }

        private void StopFrames()
        {
            if (!_subscribed) return;
            _subscribed = false;
            _host.PossessionFrames.Unsubscribe(_onFrame);
        }

        private void OnFrame(TimeSpan now)
        {
            try
            {
                Step(_charges, now);
                Step(_pulses, now);
                for (int i = _outlines.Count - 1; i >= 0; i--)
                {
                    try { PositionOutline(_outlines[i]); }
                    catch { /* a control that vanished mid-haunt just loses its outline */ }
                }
                StepRing(now);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Possession attribution frame failed"); }

            bool ringMoving = _ring != null && _ringFadeStart != null && Math.Abs(_ring.Opacity - _ringTo) > 0.001;
            bool ringPending = _ring != null && _ringFadeStart == null && Math.Abs(_ring.Opacity - _ringTo) > 0.001;
            if (_charges.Count == 0 && _pulses.Count == 0 && _outlines.Count == 0 && !ringMoving && !ringPending) StopFrames();
        }

        private void Step(List<Overlay> list, TimeSpan now)
        {
            foreach (var o in list.ToArray())
            {
                o.Start ??= now;
                var t = (now - o.Start.Value).TotalMilliseconds / o.DurationMs;
                if (t >= 1) { Finish(list, o); continue; }
                if (o.Still) { o.Border.Opacity = 1; continue; }
                o.Border.Opacity = t < o.PeakAt
                    ? o.Peak * QuadOut(t / o.PeakAt)
                    : o.Peak * (1 - QuadOut((t - o.PeakAt) / (1 - o.PeakAt)));
            }
        }

        private void StepRing(TimeSpan now)
        {
            if (_ring == null) return;
            if (Math.Abs(_ring.Opacity - _ringTo) <= 0.001) { _ring.Opacity = _ringTo; return; }
            _ringFadeStart ??= now;
            var t = Math.Min(1, (now - _ringFadeStart.Value).TotalMilliseconds / RingFadeMs);
            _ring.Opacity = _ringFrom + (_ringTo - _ringFrom) * t;
        }

        private static double QuadOut(double f) => 1 - (1 - f) * (1 - f);

        private void Finish(List<Overlay> list, Overlay o)
        {
            if (!list.Remove(o)) return;
            RemoveChild(o.Border);
            try { o.OnDone?.Invoke(); } catch { }
        }

        // -----------------------------------------------------------------------------------------
        //  Teardown
        // -----------------------------------------------------------------------------------------

        /// <summary>Rip every ember overlay off the window. The reassembly exit and the crash-safe
        /// UndoAll both end here, so it must never throw and must leave nothing behind.</summary>
        public void ReleaseAll()
        {
            OnUi(() =>
            {
                try
                {
                    foreach (var h in _outlines.ToArray()) RemoveChild(h.Border);
                    _outlines.Clear();
                    foreach (var p in _pulses.ToArray()) RemoveChild(p.Border);
                    _pulses.Clear();
                    foreach (var c in _charges.ToArray()) Finish(_charges, c);
                    _charges.Clear();
                    _possessCount = 0;
                    if (_ring != null)
                    {
                        _ring.Opacity = 0;
                        _ringTo = 0;
                        _ringFadeStart = null;
                        RemoveChild(_ring);
                    }
                    UnhookRing();
                    StopFrames();
                }
                catch (Exception ex) { Serilog.Log.Warning("Possession ReleaseAll failed: {Error}", ex.Message); }
            });
        }

        // -----------------------------------------------------------------------------------------
        //  Helpers
        // -----------------------------------------------------------------------------------------

        /// <summary>The element's bounds in ghost-layer coordinates, WITH its render transform applied - an
        /// effect that already slid the control has to be outlined where it actually sits now.</summary>
        private Rect? BoundsOf(Control el) => _host.BoundsOnGhostLayer(el);

        private void RemoveChild(Control? child)
        {
            if (child == null) return;
            try { _host.GhostLayer?.Children.Remove(child); }
            catch { }
        }

        private void OnUi(Action a)
        {
            if (Dispatcher.UIThread.CheckAccess()) a();
            else Dispatcher.UIThread.Post(a);
        }
    }
}

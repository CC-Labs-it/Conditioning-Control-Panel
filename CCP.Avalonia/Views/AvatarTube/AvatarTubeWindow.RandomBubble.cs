// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs:1712-1834 (random bubble timer)
// and AvatarRandomBubble.cs (the clickable bubble). Each bubble is its own small topmost window floating up
// from her; WPF's keep-alive window pool (a Win32 layered-window deadlock fix, AvatarRandomBubble.cs:36-44)
// has no X11 cause and is dropped. Panic closes every live bubble (PanicSilence, registry 'tube-speech').

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DispatcherTimer? _randomBubbleTimer;
        private DispatcherTimer? _randomBubbleSpawn;
        private readonly List<RandomBubble> _randomBubbles = new();
        internal IReadOnlyList<RandomBubble> RandomBubbles => _randomBubbles;
        internal bool RandomBubbleTimerRunning => _randomBubbleTimer != null;

        /// <summary>Seam: one of this app's windows has focus (WPF compares the foreground window's pid, Speech.cs:1762).</summary>
        internal static Func<bool> AppHasFocus = static () =>
            (Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.Windows.Any(w => w.IsActive) == true;

        private void StartRandomBubbleTimer()
        {
            if (!CoreSettings.Current.RandomBubbleEnabled) return;
            _randomBubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_random.Next(180, 301)) };
            _randomBubbleTimer.Tick += (_, _) => RandomBubbleTick();
            _randomBubbleTimer.Start();
        }

        private void StopRandomBubbles()
        {
            _randomBubbleTimer?.Stop();
            _randomBubbleTimer = null;
            CloseRandomBubbles();
        }

        /// <summary>Panic and close: the pending spawn and every bubble on screen go, nothing is paid.</summary>
        internal void CloseRandomBubbles()
        {
            _randomBubbleSpawn?.Stop();
            _randomBubbleSpawn = null;
            foreach (var b in _randomBubbles.ToArray()) b.Destroy();
        }

        internal void RandomBubbleTick()
        {
            if (_randomBubbleTimer != null) _randomBubbleTimer.Interval = TimeSpan.FromSeconds(_random.Next(180, 301));
            if (PanicAftermath.TriggersSilenced()) return;
            if (!AppHasFocus()) { Log.Debug("RandomBubble: skipped, app not in focus"); return; }
            GiggleFromCategory("RandomBubble");
            // The line shows first, the bubble a second later (WPF Speech.cs:1782).
            _randomBubbleSpawn?.Stop();
            _randomBubbleSpawn = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _randomBubbleSpawn.Tick += (_, _) => { _randomBubbleSpawn?.Stop(); _randomBubbleSpawn = null; SpawnRandomBubble(); };
            _randomBubbleSpawn.Start();
        }

        internal void SpawnRandomBubble()
        {
            if (PanicAftermath.TriggersSilenced()) return;
            try
            {
                var center = _avatarBorder.TranslatePoint(new Point(_avatarBorder.Bounds.Width / 2, _avatarBorder.Bounds.Height / 2), this) ?? default;
                var px = this.PointToScreen(center);
                _randomBubbles.Add(new RandomBubble(this, px, DesktopScaling, _random, OnRandomBubblePopped));
            }
            catch (Exception ex) { Log.Warning("RandomBubble: failed to spawn - {Error}", ex.Message); }
        }

        private void OnRandomBubblePopped()
        {
            // WPF PlayBubblePopSound (Speech.cs:1810-1830).
            try
            {
                var s = CoreSettings.Current;
                var pop = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "bubbles", new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" }[_random.Next(3)]);
                var vol = Math.Max(0.05f, (float)Math.Pow(s.BubblesVolume / 100f * (s.MasterVolume / 100f), 1.5));
                if (File.Exists(pop)) CoreAudio.PlayOneShot(pop, vol, "avatar-bubble-pop");
            }
            catch (Exception ex) { Log.Debug("RandomBubble: pop sound failed - {Error}", ex.Message); }
            CoreProgression.AddXP(5, "AvatarInteraction");
            Giggle("Good girl! *giggles*");
        }

        /// <summary>WPF AvatarRandomBubble: floats up at 20 fps with one of four wobbles; a click pops it
        /// (grow and fade), floating off the top ends it unpaid.</summary>
        internal sealed class RandomBubble
        {
            private const int WindowDim = 200;   // AvatarRandomBubble.cs:41
            private readonly AvatarTubeWindow _owner;
            private readonly Action _onPop;
            internal readonly Window Window;
            private readonly Control _art;
            private readonly BeatLoop _anim;
            private readonly double _k, _speed, _wobbleOffset, _startX, _top;
            private readonly int _size, _animType;
            private double _posX, _posY, _timeAlive, _angle, _scale = 1.0, _fade = 1.0;
            internal bool IsPopping { get; private set; }
            internal bool IsAlive { get; private set; } = true;

            internal RandomBubble(AvatarTubeWindow owner, PixelPoint avatarPx, double scaling, Random r, Action onPop)
            {
                _owner = owner;
                _onPop = onPop;
                _k = scaling;
                _size = r.Next(100, 150);
                _speed = 1.0 + r.NextDouble();
                _animType = r.Next(4);
                _wobbleOffset = r.NextDouble() * 100;
                _angle = r.Next(360);
                _startX = avatarPx.X / _k + 50 + r.Next(-30, 30);
                _posX = _startX;
                _posY = avatarPx.Y / _k;
                _top = -_size - 50;
                _art = Helpers.ModArt.TryLoad("bubble.png") is { } bmp
                    ? new Image { Source = bmp, Stretch = Stretch.Uniform } as Control
                    : new global::Avalonia.Controls.Shapes.Ellipse { Fill = new RadialGradientBrush { GradientStops = { new(Color.FromArgb(180, 200, 220, 255), 0), new(Color.FromArgb(80, 255, 255, 255), 1) } }, Stroke = Brushes.White, StrokeThickness = 2 };
                _art.Width = _art.Height = _size;
                _art.RenderTransformOrigin = RelativePoint.Center;
                _art.Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand);
                // Alpha 1, not transparent, so a click between the art's own gaps still lands (AvatarRandomBubble.cs:120).
                var grid = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Children = { _art } };
                grid.PointerPressed += (_, e) => { e.Handled = true; Pop(); };
                Window = new Window
                {
                    WindowDecorations = WindowDecorations.None, TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                    CanResize = false, Width = WindowDim, Height = WindowDim, Title = "", Content = grid,
                };
                Place();
                Window.Show();
                _anim = new BeatLoop(Window, elapsed => { if (elapsed > 0) Step(); }, TimeSpan.FromSeconds(0.05));   // AvatarRandomBubble.cs:150, 50 ms frames
                _anim.Start();
            }

            internal void Pop()
            {
                if (!IsAlive || IsPopping) return;
                IsPopping = true;
                _onPop();
            }

            internal void Step()
            {
                if (!IsAlive) return;
                if (IsPopping)
                {
                    _scale += 0.06; _fade -= 0.1; _angle += 3;
                    if (_fade <= 0) { Destroy(); return; }
                }
                else
                {
                    _timeAlive += 0.03;
                    _posY -= _speed;
                    double offset;
                    switch (_animType)
                    {
                        case 0: offset = Math.Sin(_timeAlive * 2) * 25; _angle = (_angle + 0.5) % 360; break;
                        case 1: offset = Math.Sin(_timeAlive * 2.5) * 30; _angle = (_angle + 0.2) % 360; break;
                        case 2: offset = Math.Cos(_timeAlive * 1.8) * 25; _angle = (_angle - 1.0) % 360; break;
                        default: offset = Math.Sin(_timeAlive) * 30 + Math.Cos(_timeAlive * 2) * 15; _angle = (_angle + 0.8) % 360; break;
                    }
                    _posX = _startX + offset;
                    if (_posY < _top) { Destroy(); return; }
                }
                var s = _scale + 0.06 * Math.Sin(_timeAlive * 2.5 + _wobbleOffset);
                _art.RenderTransform = new TransformGroup { Children = { new ScaleTransform(s, s), new RotateTransform(_angle) } };
                Window.Opacity = Math.Max(0, _fade);
                Place();
            }

            // AvatarRandomBubble.cs:257-261: the art is centred in the fixed window.
            private void Place() => Window.Position = new PixelPoint(
                (int)Math.Round((_posX + _size / 2.0 - 78) * _k), (int)Math.Round((_posY + _size / 2.0 - 100) * _k));

            internal void Destroy()
            {
                if (!IsAlive) return;
                IsAlive = false;
                _anim.Stop();
                _owner._randomBubbles.Remove(this);
                try { Window.Close(); } catch (Exception ex) { Log.Debug("RandomBubble close: {E}", ex.Message); }
            }
        }
    }
}

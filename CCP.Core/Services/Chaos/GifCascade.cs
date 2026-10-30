using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos
{
    /// <summary>
    /// The maths of the Chaos "GifCascade" rain (WPF Chaos/ChaosGifCascadeOverlay.cs), without a window:
    /// clips spawn at the top on a fixed interval for the spawn window, fall with a per-clip speed, grow
    /// from <see cref="StartScale"/> to full by 75% of the height and despawn below the bottom. The head
    /// draws one Image per <see cref="Faller"/> and calls <see cref="Step"/> once per frame.
    /// </summary>
    public sealed class GifCascade
    {
        /// <summary>WPF MAX_CONCURRENT: clips alive at once, so a pile-up can never exhaust memory.</summary>
        public const int MaxConcurrent = 14;
        /// <summary>WPF MAX_ANIMATED / ANIMATED_MAX_BYTES: past this budget clips fall as stills.</summary>
        public const int MaxAnimated = 3;
        public const long AnimatedMaxBytes = 3_000_000;

        // WPF Services/Chaos/EffectPayload.cs GifCascadePayload: the named tunables every caller uses.
        public const double PayloadSpawnRatePerSec = 1.67;
        public const double PayloadDurationSec = 6.0;
        public const double PayloadGifSize = 400;
        public const double PayloadStartScale = 0.45;
        public const double PayloadFallSpeed = 3.6;
        public const double PayloadOpacity = 0.9;

        /// <summary>WPF motion is "DIPs per 16 ms frame"; a frame's delta is capped at 100 ms.</summary>
        private const double FrameSec = 0.016;
        private const double MaxFrameSec = 0.1;

        public sealed class Faller
        {
            public int FileIndex;
            public double CenterX;
            public double Y;
            public double Speed;
            public bool Animated;
        }

        private readonly Random _rng;
        private readonly List<Faller> _fallers = new();
        private int _fileCount;
        private double _interval = 1;
        private double _duration = 1;
        private double _elapsed;
        private double _nextSpawn;

        public GifCascade(Random? rng = null) => _rng = rng ?? new Random();

        public double Size { get; private set; } = 200;
        public double FallSpeed { get; private set; } = 4;
        public double Opacity { get; private set; } = 1;
        public double StartScale { get; private set; } = 1;
        public double Height { get; private set; }
        public double SpawnLeft { get; private set; }
        public double SpawnWidth { get; private set; }
        public bool Spawning { get; private set; }
        public IReadOnlyList<Faller> Fallers => _fallers;

        /// <summary>WPF IsRaining: spawning, or clips still falling.</summary>
        public bool IsRaining => Spawning || _fallers.Count > 0;

        public int AnimatedAlive
        {
            get { int n = 0; foreach (var f in _fallers) if (f.Animated) n++; return n; }
        }

        /// <summary>Raised for every new clip, before it first moves; the head builds its Image.</summary>
        public event Action<Faller>? Spawned;
        /// <summary>Raised when a clip leaves the bottom (or is cleared); the head drops its Image.</summary>
        public event Action<Faller>? Gone;

        /// <summary>WPF Show: the image batch drawn per cascade, sampled with replacement.</summary>
        public static int BatchSize(double spawnRatePerSec, double durationSec) =>
            (int)Math.Clamp(Math.Ceiling(spawnRatePerSec * Math.Max(1.0, durationSec)) + 6, 8, 24);

        /// <summary>WPF SpawnOne's budget: a gif / animated webp animates only while fewer than
        /// <see cref="MaxAnimated"/> do and its file is non-empty and at most <see cref="AnimatedMaxBytes"/>.</summary>
        public bool CanAnimate(bool animatedFormat, long fileBytes) =>
            animatedFormat && AnimatedAlive < MaxAnimated && fileBytes > 0 && fileBytes <= AnimatedMaxBytes;

        /// <summary>WPF SetSpawnSpread: the window-local band clips spawn in, from the resolved screens'
        /// left/right edges (same units as the window). Empty or degenerate input errs to the whole window.</summary>
        public static (double Left, double Width) SpawnBand(IEnumerable<(double Left, double Right)> screens, double windowLeft, double windowWidth)
        {
            double min = double.MaxValue, max = double.MinValue;
            foreach (var (l, r) in screens)
            {
                if (l < min) min = l;
                if (r > max) max = r;
            }
            if (max <= min) return (0, windowWidth);
            var left = Math.Max(0, min - windowLeft);
            var width = Math.Min(max - min, Math.Max(1, windowWidth - left));
            return (left, Math.Max(1, width));
        }

        /// <summary>WPF Restart: any clips in flight are replaced, knobs clamped, one clip spawns now.</summary>
        public void Restart(int fileCount, double spawnRatePerSec, double durationSec, double gifSize, double fallSpeed,
                            double opacity, double startScale, double height, double spawnLeft, double spawnWidth)
        {
            Clear();
            _fileCount = Math.Max(0, fileCount);
            Size = Math.Clamp(gifSize, 40, 600);
            FallSpeed = Math.Clamp(fallSpeed, 0.5, 30);
            Opacity = Math.Clamp(opacity, 0.05, 1.0);
            StartScale = Math.Clamp(startScale, 0.1, 1.0);
            Height = height;
            SpawnLeft = spawnLeft;
            SpawnWidth = spawnWidth;
            _interval = 1.0 / Math.Max(0.05, spawnRatePerSec);
            _duration = Math.Max(1.0, durationSec);
            _elapsed = 0;
            _nextSpawn = _interval;
            Spawning = _fileCount > 0;
            SpawnOne();
        }

        /// <summary>One frame, <paramref name="dtSec"/> after the last. Spawn and spawn-window timing run on
        /// real time (WPF DispatcherTimers); motion uses the capped delta at the 16 ms tuning (WPF OnRender).</summary>
        public void Step(double dtSec)
        {
            if (dtSec <= 0) return;
            double frameScale = Math.Min(dtSec, MaxFrameSec) / FrameSec;
            for (int i = _fallers.Count - 1; i >= 0; i--)
            {
                var f = _fallers[i];
                f.Y += f.Speed * frameScale;
                if (f.Y > Height + Size)
                {
                    _fallers.RemoveAt(i);
                    Gone?.Invoke(f);
                }
            }
            _elapsed += dtSec;
            while (Spawning && _nextSpawn <= _elapsed && _nextSpawn < _duration)
            {
                SpawnOne();
                _nextSpawn += _interval;
            }
            if (_elapsed >= _duration) Spawning = false;   // stop spawning; in-flight clips fall out
        }

        /// <summary>WPF ScaleAt: <see cref="StartScale"/> at the top easing to full by 75% of the way down.</summary>
        public double ScaleAt(double y)
        {
            if (StartScale >= 1.0) return 1.0;
            double p = Math.Clamp(y / Math.Max(1.0, Height * 0.75), 0, 1);
            return StartScale + (1.0 - StartScale) * p;
        }

        /// <summary>WPF StopAndClear: spawning stops and every clip goes.</summary>
        public void Clear()
        {
            Spawning = false;
            var gone = _fallers.ToArray();
            _fallers.Clear();
            foreach (var f in gone) Gone?.Invoke(f);
        }

        private void SpawnOne()
        {
            if (!Spawning || _fallers.Count >= MaxConcurrent) return;
            var f = new Faller
            {
                FileIndex = _rng.Next(_fileCount),
                CenterX = SpawnLeft + Size / 2 + _rng.NextDouble() * Math.Max(1, SpawnWidth - Size),
                Y = -Size,
                Speed = FallSpeed * (0.7 + _rng.NextDouble() * 0.6),
            };
            _fallers.Add(f);
            try { Spawned?.Invoke(f); }
            catch
            {
                // A failed surface never reaches normal teardown. Drop its slot and surface now.
                _fallers.Remove(f);
                f.Animated = false;
                Gone?.Invoke(f);
                throw;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace ConditioningControlPanel.Services
{
    /// <summary>Building one moving collage: off the render thread, below normal priority, handed over
    /// finished. Static's layout plan, every slot its own file, the moving tiles spread out.</summary>
    public sealed partial class DynamicWallpaperService
    {
        private const double HeroAnimateBonus = 0.25;   // hero is +25pp likelier to move than a tile (still a roll)
        private const int SpreadChoices = 3;            // next moving tile = random one of the 3 farthest from the rest
        private const int HeroAnimCap = 512;            // moving frames are baked at most this big (px) ...
        private const int TileAnimCap = 320;            // ... frozen tiles are baked at full tile size

        private Task? _buildTask;
        private int _building;
        private string? _lastPack;
        private readonly Random _random = new();

        /// <summary>Snapshot what the build needs on the UI thread, then build off-thread. Single-flighted:
        /// a tick that lands mid-build is skipped, like the static collage.</summary>
        private void RequestBuild()
        {
            if (!_active || _runCts == null) return;
            if (Interlocked.CompareExchange(ref _building, 1, 0) != 0) return;

            BuildInput input;
            try
            {
                input = new BuildInput
                {
                    Sources = CollageSources.Snapshot(),
                    Layout = App.Wallpaper?.PickCollageLayout() ?? CollageLayout.SideHero,
                    AccentHex = App.Mods?.GetAccentColorHex() ?? "#E84393",
                    BackgroundHex = App.Mods?.GetBackgroundColorHex() ?? "#08080C",
                };
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _building, 0);
                App.Logger?.Warning("[DynamicWallpaper] Could not read the build settings: {Error}", ex.Message);
                return;
            }

            var token = _runCts.Token;
            _buildTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;   // LongRunning: our own thread
                    Build(input, token);
                }
                catch (OperationCanceledException) { } // swallow: stopped mid-build
                catch (Exception ex) { App.Logger?.Warning("[DynamicWallpaper] Build failed: {Error}", ex.Message); }
                finally
                {
                    Interlocked.Exchange(ref _building, 0);
                    // A builder from an earlier run outlived Stop and held the flag, so a newer Start's
                    // first build was skipped: ask again now that the flag is free.
                    if (token.IsCancellationRequested && _active)
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(RequestBuild));
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void Build(BuildInput input, CancellationToken token)
        {
            if (!_gpuReady.Wait(15000, token) || _gpuFailed) return;

            var sw = Stopwatch.StartNew();
            var active = CollageSources.ActiveImages(input.Sources);
            if (active.Count < 2)
            {
                App.Logger?.Warning("[DynamicWallpaper] Only {N} selected image(s) in the assets folder - need at least 2", active.Count);
                return;
            }

            // One collage per monitor at its own pixel size (#1544 follow-up: a primary-sized collage cropped
            // or squashed on a monitor of another shape). Bounds are physical pixels: the process is PerMonitorV2.
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                input.Monitors.Add((b.X, b.Y, Math.Max(16, b.Width), Math.Max(16, b.Height)));
            }

            // One theme folder per collage, drawn only from the SELECTED assets (the rule Static shares).
            var pack = CollageSources.PickPack(active, input.Sources.ImagesRoot, ref _lastPack, _random);
            var collage = Compose(pack, input, token);
            if (collage == null && pack.Count != active.Count)
                collage = Compose(active, input, token);   // the folder had too few decodable images
            if (collage == null) return;

            lock (_gpuLock)
            {
                if (_gpuGone || token.IsCancellationRequested) { collage.Dispose(); return; }
                Interlocked.Exchange(ref _pending, collage)?.Dispose();
            }
            App.Logger?.Debug(
                "[DynamicWallpaper] Built {Layout}: {Screens} monitor(s), {Tiles} tiles, {Moving} moving (gif {Gif}, webp {Webp}), {Frames} frames, {Mb} MB of frames in {Ms}ms",
                input.Layout, collage.Screens.Count, collage.Screens.Sum(c => c.Tiles.Count), collage.MovingGif + collage.MovingWebp, collage.MovingGif, collage.MovingWebp,
                collage.FrameTotal, (DynamicFrameBudget.Bytes - collage.BudgetLeft) / (1024 * 1024), sw.ElapsedMilliseconds);
        }

        /// <summary>Plan one collage PER MONITOR at that monitor's own pixel size (the process is
        /// PerMonitorV2, so <c>Screen.Bounds</c> is physical pixels), give every slot its own file, roll which
        /// tiles move, then decode + bake + upload everything. A file that lands on two monitors is decoded
        /// once. Null when there was nothing usable.</summary>
        private CollageSet? Compose(List<string> files, BuildInput input, CancellationToken token)
        {
            var rng = new Random();
            var monitors = input.Monitors.Count > 0 ? input.Monitors : new List<(int X, int Y, int W, int H)> { (0, 0, 0, 0) };
            var plans = new List<List<TileSlot>>();
            foreach (var m in monitors)
            {
                var (w, h) = m.W > 0 && m.H > 0 ? (m.W, m.H) : CollageComposer.CanvasSize();
                plans.Add(CollageComposer.PlanLayout(input.Layout, w, h, rng).Select(p => new TileSlot { Plan = p }).ToList());
            }
            var sources = PickSources(files, plans.Sum(p => p.Count), rng, token);
            if (sources.Count < 2) return null;

            // Hero = the largest source that actually decodes (sharpest when blown up), like Static; each
            // monitor's hero its own file while there are enough. Every other slot gets the next unused file;
            // only a pack smaller than all the layouts together wraps around.
            var heroes = new HashSet<Source>();
            foreach (var heroSlot in plans.SelectMany(p => p).Where(sl => sl.Plan.Kind == CollageSlotKind.Hero))
            {
                foreach (var candidate in sources.OrderByDescending(src => (long)src.W * src.H).ToList())
                {
                    if (heroes.Contains(candidate)) continue;
                    token.ThrowIfCancellationRequested();
                    using (var probe = CollageComposer.DecodeRepresentativeFrame(candidate.Path))
                        if (probe != null) { heroSlot.Src = candidate; break; }
                    sources.Remove(candidate);   // header read but no picture: never let it leave a hole
                }
                heroSlot.Src ??= heroes.OrderByDescending(src => (long)src.W * src.H).FirstOrDefault();
                if (heroSlot.Src == null || sources.Count < 2) return null;
                heroes.Add(heroSlot.Src);
            }
            var rest = sources.Where(src => !heroes.Contains(src)).ToList();
            if (rest.Count == 0) rest = sources;
            int next = 0;
            foreach (var sl in plans.SelectMany(p => p))
                if (sl.Plan.Kind != CollageSlotKind.Hero) sl.Src = rest[next++ % rest.Count];

            foreach (var slots in plans) RollMoving(slots, rng);

            var set = new CollageSet();
            try
            {
                var accent = CollageComposer.HexToColor(input.AccentHex, CollageComposer.DefaultAccent);
                var background = CollageComposer.HexToColor(input.BackgroundHex, CollageComposer.DefaultBackground);

                // Under everything, per monitor: Static's backdrop (gradient + blurred atmosphere from one source).
                for (int i = 0; i < plans.Count; i++)
                {
                    var m = monitors[i];
                    var (w, h) = m.W > 0 && m.H > 0 ? (m.W, m.H) : CollageComposer.CanvasSize();
                    var collage = new Collage { MonX = m.X, MonY = m.Y, MonW = m.W, MonH = m.H, RefW = w, RefH = h };
                    set.Screens.Add(collage);
                    using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                    if (surface == null) { set.Dispose(); return null; }
                    using (var atmosphere = CollageComposer.DecodeRepresentativeFrame(sources[rng.Next(sources.Count)].Path))
                        CollageComposer.DrawBackdrop(surface.Canvas, w, h, background, atmosphere);
                    collage.Base = Upload(surface, w, h);
                }

                // Bake round-robin across monitors so the shared frame budget is spread over all of them,
                // one decode per file however many monitors show it.
                var order = new List<TileSlot>();
                for (int k = 0, most = plans.Max(p => p.Count); k < most; k++)
                    foreach (var slots in plans)
                        if (k < slots.Count) order.Add(slots[k]);
                foreach (var group in order.GroupBy(sl => sl.Src!.Path, StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    BakeGroup(group.ToList(), set, token);
                }

                for (int i = 0; i < plans.Count; i++)
                {
                    var collage = set.Screens[i];
                    foreach (var sl in plans[i])   // plan order is draw order: the hero over the fill
                    {
                        var tile = sl.Baked;
                        if (tile == null) continue;
                        sl.Baked = null;
                        collage.Tiles.Add(tile);
                        if (tile.Frames.Count > 1)
                        {
                            if (sl.Src!.Path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) set.MovingWebp++;
                            else set.MovingGif++;
                        }
                        set.FrameTotal += tile.Frames.Count;
                    }

                    // Over everything: Static's finish (mod-accent grade + vignette), pre-blended into one layer.
                    int w = (int)collage.RefW, h = (int)collage.RefH;
                    using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                    if (surface == null) { set.Dispose(); return null; }
                    surface.Canvas.Clear(SKColors.Transparent);
                    CollageComposer.DrawFinish(surface.Canvas, w, h, accent);
                    collage.Overlay = Upload(surface, w, h);
                }

                if (set.Screens.All(c => c.Tiles.Count == 0)) { set.Dispose(); return null; }
                return set;
            }
            catch
            {
                foreach (var sl in plans.SelectMany(p => p)) sl.Baked?.Dispose();
                set.Dispose();
                throw;
            }
        }

        /// <summary>Which tiles of one monitor's collage move: gif/webp tiles up to the cap, the hero
        /// weighted +25pp (still a roll). SPREAD, never bunched: each next one comes from the few candidates
        /// farthest from everything already moving, so the movement covers the whole collage.</summary>
        private static void RollMoving(List<TileSlot> slots, Random rng)
        {
            var heroSlot = slots.FirstOrDefault(sl => sl.Plan.Kind == CollageSlotKind.Hero);
            var capable = slots.Where(sl => sl.Src!.FrameCount > 1).ToList();
            double tileRate = capable.Count > 0 ? Math.Min(1.0, (double)AnimatedCap / capable.Count) : 0;
            int budget = AnimatedCap;
            var moving = new List<TileSlot>();
            if (heroSlot != null && heroSlot.Src!.FrameCount > 1 && rng.NextDouble() < Math.Min(0.9, tileRate + HeroAnimateBonus))
            {
                heroSlot.Animated = true;
                moving.Add(heroSlot);
                budget--;
            }
            var pool = capable.Where(sl => sl.Plan.Kind != CollageSlotKind.Hero).ToList();
            while (budget > 0 && pool.Count > 0)
            {
                TileSlot pick;
                if (moving.Count == 0)
                {
                    pick = pool[rng.Next(pool.Count)];
                }
                else
                {
                    var farthest = pool
                        .OrderByDescending(sl => moving.Min(m => DistanceSq(sl.Plan, m.Plan)))
                        .Take(SpreadChoices)
                        .ToList();
                    pick = farthest[rng.Next(farthest.Count)];
                }
                pick.Animated = true;
                moving.Add(pick);
                pool.Remove(pick);
                budget--;
            }
        }

        /// <summary>Shuffle the files and read headers (size + frame count, no pixels) until there is one
        /// usable file per slot. Unreadable files are skipped.</summary>
        private static List<Source> PickSources(List<string> files, int needed, Random rng, CancellationToken token)
        {
            var picked = new List<Source>(needed);
            foreach (var path in files.OrderBy(_ => rng.Next()))
            {
                if (picked.Count >= needed) break;
                token.ThrowIfCancellationRequested();
                try
                {
                    using var codec = SKCodec.Create(path);
                    if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0) continue;
                    picked.Add(new Source { Path = path, W = codec.Info.Width, H = codec.Info.Height, FrameCount = codec.FrameCount });
                }
                catch (Exception ex) { Diag.Swallowed(ex, "dynamic header read"); }
            }
            return picked;
        }

        /// <summary>Bake every slot that shows one file (on any monitor) from ONE decode: cover-crop each to
        /// its tile, feather its edges like <c>CollageComposer.DrawTileCore</c>, and upload it. A moving tile
        /// takes every kept frame (baked at most <see cref="HeroAnimCap"/>/<see cref="TileAnimCap"/> px, scaled
        /// up by the GPU) while the set's shared frame budget lasts; a frozen tile is baked once, at full tile
        /// size, from Static's mid-timeline frame. Sets each slot's <see cref="TileSlot.Baked"/>.</summary>
        private void BakeGroup(List<TileSlot> group, CollageSet set, CancellationToken token)
        {
            var src = group[0].Src!;
            var bakers = new List<TileBaker>(group.Count);
            try
            {
                foreach (var sl in group)
                {
                    var b = new TileBaker(this, sl);
                    if (sl.Animated)
                    {
                        long cost = b.MoveCost(src.FrameCount);
                        b.Animate = DynamicFrameBudget.MayMove(src.FrameCount, cost, set.BudgetLeft);
                        if (b.Animate) set.BudgetLeft -= cost;
                    }
                    bakers.Add(b);
                }

                if (bakers.Any(b => b.Animate))
                {
                    using var codec = SKCodec.Create(src.Path);
                    if (codec != null)
                    {
                        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                        using var full = new SKBitmap(info);
                        var frameInfos = codec.FrameInfo;
                        int stride = DynamicFrameBudget.Stride(codec.FrameCount);
                        for (int i = 0; i < codec.FrameCount; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            // Sequential into one buffer: Skia composites delta frames, so a skipped frame
                            // still has to be decoded.
                            var opts = new SKCodecOptions(i);
                            if (i < frameInfos.Length && frameInfos[i].RequiredFrame != -1)
                                opts = new SKCodecOptions(i, frameInfos[i].RequiredFrame);
                            var result = codec.GetPixels(info, full.GetPixels(), opts);
                            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) break;
                            int d = i < frameInfos.Length ? frameInfos[i].Duration : 0;
                            if (d <= 0) d = 80;
                            foreach (var b in bakers)
                            {
                                if (!b.Animate) continue;
                                if (i % stride == 0) b.BakeFrame(full, d);
                                else b.HoldLonger(d);   // the kept frame holds for what was skipped
                            }
                        }
                    }
                }

                if (bakers.Any(b => b.FrameCount == 0))   // frozen, or a moving one that would not decode
                {
                    using var still = CollageComposer.DecodeRepresentativeFrame(src.Path);
                    foreach (var b in bakers)
                        if (b.FrameCount == 0 && still != null) b.BakeFrame(still, 0);
                }

                foreach (var b in bakers) b.Slot.Baked = b.Finish();
            }
            catch
            {
                foreach (var b in bakers) b.Discard();
                throw;
            }
            finally
            {
                foreach (var b in bakers) b.Dispose();
            }
        }

        /// <summary>One slot's bake: its tile geometry, a reusable bake surface and the frames so far.</summary>
        private sealed class TileBaker : IDisposable
        {
            private readonly DynamicWallpaperService _owner;
            private readonly int _tw, _th;
            private readonly bool _hero;
            private readonly List<int> _delays = new();
            private SKSurface? _surface;
            private SKImageFilter? _sharpen;
            private bool _feathered;
            private float _fL, _fT, _fR, _fB, _k;
            private Tile? _tile;
            public readonly TileSlot Slot;
            public bool Animate;

            public TileBaker(DynamicWallpaperService owner, TileSlot slot)
            {
                _owner = owner;
                Slot = slot;
                _hero = slot.Plan.Kind == CollageSlotKind.Hero;
                (_tw, _th) = slot.Plan.TileSize(slot.Src!.W, slot.Src.H);
            }

            public int FrameCount => _tile?.Frames.Count ?? 0;

            private int MoveCap => _hero ? HeroAnimCap : TileAnimCap;

            private (int w, int h) BakeSize(int cap)
            {
                float f = Math.Min(1f, cap / (float)Math.Max(_tw, _th));
                return (Math.Max(1, (int)Math.Round(_tw * f)), Math.Max(1, (int)Math.Round(_th * f)));
            }

            public long MoveCost(int frameCount)
            {
                var (mw, mh) = BakeSize(MoveCap);
                return DynamicFrameBudget.Cost(mw, mh, frameCount);
            }

            public void HoldLonger(int ms)
            {
                if (_delays.Count > 0) _delays[^1] += ms;
            }

            public void BakeFrame(SKBitmap frame, int delayMs)
            {
                if (_tile == null)
                {
                    // Moving: capped bake size, scaled up by the GPU. Frozen: full tile size.
                    int cap = Animate ? MoveCap : Math.Max(_tw, _th);
                    _k = Math.Min(1f, cap / (float)Math.Max(_tw, _th));
                    var (bw, bh) = BakeSize(cap);
                    _surface = SKSurface.Create(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
                    if (_surface == null) return;
                    _sharpen = _hero ? CollageComposer.HeroSharpen() : null;
                    var plan = Slot.Plan;
                    _tile = new Tile
                    {
                        Cx = plan.Cx, Cy = plan.Cy, AngleRad = plan.AngleDeg * MathF.PI / 180f,
                        BakeW = bw, BakeH = bh, ScaleX = _tw / (float)bw, ScaleY = _th / (float)bh,
                    };
                }
                if (_surface == null) return;
                if (!_feathered)
                {
                    if (_hero) Slot.Plan.HeroFeather(out _fL, out _fT, out _fR, out _fB);
                    else CollageComposer.PerEdgeFeather(CollageComposer.ComputeFocus(frame), frame.Width, frame.Height, _tw, _th,
                        out _fL, out _fT, out _fR, out _fB);
                    _feathered = true;
                }
                var canvas = _surface.Canvas;
                int w = _tile.BakeW, h = _tile.BakeH;
                canvas.Clear(SKColors.Transparent);
                var srcRect = CollageComposer.CoverSrcRect(frame.Width, frame.Height, _tw, _th);
                using (var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High, ImageFilter = _sharpen })
                    canvas.DrawBitmap(frame, srcRect, new SKRect(0, 0, w, h), paint);
                CollageComposer.ApplyEdgeFeather(canvas, w, h, _fL * _k, _fT * _k, _fR * _k, _fB * _k);
                _tile.Frames.Add(_owner.Upload(_surface, w, h));
                _delays.Add(delayMs);
            }

            /// <summary>The finished tile (ownership passes to the caller), or null when nothing baked.</summary>
            public Tile? Finish()
            {
                var t = _tile;
                _tile = null;
                if (t == null || t.Frames.Count == 0) { t?.Dispose(); return null; }
                t.Delays = _delays.ToArray();
                return t;
            }

            public void Discard()
            {
                _tile?.Dispose();
                _tile = null;
            }

            public void Dispose()
            {
                _sharpen?.Dispose();
                _surface?.Dispose();
            }
        }

        /// <summary>Copy a baked surface to the GPU. Under <see cref="_gpuLock"/> so it can never race the
        /// render thread's teardown; throws <see cref="OperationCanceledException"/> once the GPU is gone.</summary>
        private ID2D1Bitmap Upload(SKSurface surface, int w, int h)
        {
            surface.Canvas.Flush();
            using var pixmap = surface.PeekPixels();
            lock (_gpuLock)
            {
                if (_gpuGone || _loadCtx == null) throw new OperationCanceledException();
                return _loadCtx.CreateBitmap(new SizeI(w, h), pixmap.GetPixels(), (uint)pixmap.RowBytes, FrameProps);
            }
        }

        /// <summary>Squared distance between two slot centres (for spreading the moving tiles).</summary>
        private static float DistanceSq(CollageSlot a, CollageSlot b)
            => (a.Cx - b.Cx) * (a.Cx - b.Cx) + (a.Cy - b.Cy) * (a.Cy - b.Cy);

        private sealed class BuildInput
        {
            public CollageSources.Selection Sources = new();
            public CollageLayout Layout;
            public string AccentHex = "", BackgroundHex = "";
            public List<(int X, int Y, int W, int H)> Monitors = new();   // physical pixels (PerMonitorV2)
        }

        /// <summary>One planned collage slot plus what Dynamic adds to it: its own file and whether it moves.</summary>
        private sealed class TileSlot
        {
            public CollageSlot Plan = null!;
            public Source? Src;
            public bool Animated;
            public Tile? Baked;
        }

        private sealed class Source
        {
            public string Path = "";
            public int W, H, FrameCount;
        }
    }
}

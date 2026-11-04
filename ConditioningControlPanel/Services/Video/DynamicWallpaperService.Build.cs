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
                "[DynamicWallpaper] Built {Layout}: {Tiles} tiles, {Moving} moving (gif {Gif}, webp {Webp}), {Frames} frames, {Mb} MB of frames in {Ms}ms",
                input.Layout, collage.Tiles.Count, collage.MovingGif + collage.MovingWebp, collage.MovingGif, collage.MovingWebp,
                collage.FrameTotal, (DynamicFrameBudget.Bytes - collage.BudgetLeft) / (1024 * 1024), sw.ElapsedMilliseconds);
        }

        /// <summary>Plan the static collage's layout, give every slot its own file, roll which tiles move,
        /// then decode + bake + upload everything. Null when there was nothing usable.</summary>
        private Collage? Compose(List<string> files, BuildInput input, CancellationToken token)
        {
            var (w, h) = CollageComposer.CanvasSize();
            var rng = new Random();

            var slots = CollageComposer.PlanLayout(input.Layout, w, h, rng).Select(p => new TileSlot { Plan = p }).ToList();
            var sources = PickSources(files, slots.Count, rng, token);
            if (sources.Count < 2) return null;

            // Hero = the largest source that actually decodes (sharpest when blown up), like Static; every
            // other slot gets the next unique file. Only a pack smaller than the layout wraps around.
            var heroSlot = slots.FirstOrDefault(sl => sl.Plan.Kind == CollageSlotKind.Hero);
            var rest = sources;
            if (heroSlot != null)
            {
                foreach (var candidate in sources.OrderByDescending(src => (long)src.W * src.H).ToList())
                {
                    token.ThrowIfCancellationRequested();
                    using (var probe = CollageComposer.DecodeRepresentativeFrame(candidate.Path))
                        if (probe != null) { heroSlot.Src = candidate; break; }
                    sources.Remove(candidate);   // header read but no picture: never let it leave a hole
                }
                if (heroSlot.Src == null || sources.Count < 2) return null;
                rest = sources.Where(src => !ReferenceEquals(src, heroSlot.Src)).ToList();
            }
            int next = 0;
            foreach (var sl in slots)
                if (sl.Plan.Kind != CollageSlotKind.Hero) sl.Src = rest[next++ % rest.Count];

            // Which tiles move: gif/webp tiles up to the cap, the hero weighted +25pp (still a roll). SPREAD,
            // never bunched: each next one comes from the few candidates farthest from everything already
            // moving, so the movement covers the whole collage.
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

            var collage = new Collage { RefW = w, RefH = h };
            try
            {
                var accent = CollageComposer.HexToColor(input.AccentHex, CollageComposer.DefaultAccent);
                var background = CollageComposer.HexToColor(input.BackgroundHex, CollageComposer.DefaultBackground);

                // Under everything: Static's backdrop (gradient + blurred atmosphere from one random source).
                using (var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul)))
                {
                    if (surface == null) { collage.Dispose(); return null; }
                    using (var atmosphere = CollageComposer.DecodeRepresentativeFrame(sources[rng.Next(sources.Count)].Path))
                        CollageComposer.DrawBackdrop(surface.Canvas, w, h, background, atmosphere);
                    collage.Base = Upload(surface, w, h);
                }

                foreach (var sl in slots)
                {
                    token.ThrowIfCancellationRequested();
                    var tile = BakeTile(sl, collage, token);
                    if (tile == null) continue;
                    collage.Tiles.Add(tile);
                    if (tile.Frames.Count > 1)
                    {
                        if (sl.Src!.Path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) collage.MovingWebp++;
                        else collage.MovingGif++;
                    }
                    collage.FrameTotal += tile.Frames.Count;
                }

                // Over everything: Static's finish (mod-accent grade + vignette), pre-blended into one layer.
                using (var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul)))
                {
                    if (surface == null) { collage.Dispose(); return null; }
                    surface.Canvas.Clear(SKColors.Transparent);
                    CollageComposer.DrawFinish(surface.Canvas, w, h, accent);
                    collage.Overlay = Upload(surface, w, h);
                }

                if (collage.Tiles.Count == 0) { collage.Dispose(); return null; }
                return collage;
            }
            catch
            {
                collage.Dispose();
                throw;
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

        /// <summary>Decode one slot's image, cover-crop it to the tile, feather its edges like
        /// <c>CollageComposer.DrawTileCore</c>, and upload it. A moving tile does this for every kept frame
        /// (baked at most <see cref="HeroAnimCap"/>/<see cref="TileAnimCap"/> px, scaled up by the GPU), until
        /// the collage's frame budget runs out; a frozen tile does it once, at full tile size, from Static's
        /// mid-timeline frame.</summary>
        private Tile? BakeTile(TileSlot sl, Collage collage, CancellationToken token)
        {
            var src = sl.Src!;
            var plan = sl.Plan;
            bool hero = plan.Kind == CollageSlotKind.Hero;
            var (tw, th) = plan.TileSize(src.W, src.H);

            int MoveCap() => hero ? HeroAnimCap : TileAnimCap;
            (int w, int h) BakeSize(int cap)
            {
                float f = Math.Min(1f, cap / (float)Math.Max(tw, th));
                return (Math.Max(1, (int)Math.Round(tw * f)), Math.Max(1, (int)Math.Round(th * f)));
            }

            bool animate = sl.Animated;
            if (animate)
            {
                var (mw, mh) = BakeSize(MoveCap());
                long cost = DynamicFrameBudget.Cost(mw, mh, src.FrameCount);
                animate = DynamicFrameBudget.MayMove(src.FrameCount, cost, collage.BudgetLeft);
                if (animate) collage.BudgetLeft -= cost;
            }

            int cap = animate ? MoveCap() : Math.Max(tw, th);
            float k = Math.Min(1f, cap / (float)Math.Max(tw, th));
            var (bw, bh) = BakeSize(cap);
            var tile = new Tile
            {
                Cx = plan.Cx, Cy = plan.Cy, AngleRad = plan.AngleDeg * MathF.PI / 180f,
                BakeW = bw, BakeH = bh, ScaleX = tw / (float)bw, ScaleY = th / (float)bh,
            };

            using var bake = SKSurface.Create(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (bake == null) return null;
            using var sharpen = hero ? CollageComposer.HeroSharpen() : null;
            bool feathered = false;
            float fL = 0, fT = 0, fR = 0, fB = 0;
            var delays = new List<int>();

            void BakeFrame(SKBitmap frame, int delayMs)
            {
                if (!feathered)
                {
                    if (hero)
                    {
                        plan.HeroFeather(out fL, out fT, out fR, out fB);
                    }
                    else
                    {
                        CollageComposer.PerEdgeFeather(CollageComposer.ComputeFocus(frame), frame.Width, frame.Height, tw, th,
                            out fL, out fT, out fR, out fB);
                    }
                    feathered = true;
                }
                var canvas = bake.Canvas;
                canvas.Clear(SKColors.Transparent);
                var srcRect = CollageComposer.CoverSrcRect(frame.Width, frame.Height, tw, th);
                using (var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High, ImageFilter = sharpen })
                    canvas.DrawBitmap(frame, srcRect, new SKRect(0, 0, bw, bh), paint);
                CollageComposer.ApplyEdgeFeather(canvas, bw, bh, fL * k, fT * k, fR * k, fB * k);
                tile.Frames.Add(Upload(bake, bw, bh));
                delays.Add(delayMs);
            }

            try
            {
                if (animate)
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
                            if (i % stride == 0) BakeFrame(full, d);
                            else delays[^1] += d;   // the kept frame holds for what was skipped
                        }
                    }
                }

                if (tile.Frames.Count == 0)   // frozen, or a moving one that would not decode: Static's frame
                {
                    using var still = CollageComposer.DecodeRepresentativeFrame(src.Path);
                    if (still == null) return null;
                    BakeFrame(still, 0);
                }
                tile.Delays = delays.ToArray();
                return tile;
            }
            catch
            {
                tile.Dispose();
                throw;
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
        }

        /// <summary>One planned collage slot plus what Dynamic adds to it: its own file and whether it moves.</summary>
        private sealed class TileSlot
        {
            public CollageSlot Plan = null!;
            public Source? Src;
            public bool Animated;
        }

        private sealed class Source
        {
            public string Path = "";
            public int W, H, FrameCount;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace ConditioningControlPanel.Services
{
    /// <summary>The collage arrangement to render. The user picks which are in rotation (or "Surprise
    /// me" = all three); <see cref="WallpaperService"/> rolls one per compose and passes it in.</summary>
    public enum CollageLayout
    {
        /// <summary>One hero bleeding off a side, filler on the other, side swapping (the original look).</summary>
        SideHero,
        /// <summary>No hero - tilted, feathered tiles edge to edge across the whole screen.</summary>
        FillerOnly,
        /// <summary>Hero centred with filler flanking each side, feathered into the middle.</summary>
        CenterHero,
    }

    /// <summary>What a planned collage slot holds: a packed filler tile, the hero, or a tile spilled
    /// across the hero's inner edge.</summary>
    internal enum CollageSlotKind { Fill, Hero, Spill }

    /// <summary>
    /// One planned tile of a collage (<see cref="CollageComposer.PlanLayout"/>): where it goes, how big and
    /// how tilted, before an image is assigned. Static and Dynamic both draw from these, so the geometry
    /// lives in one place.
    /// </summary>
    internal sealed class CollageSlot
    {
        public CollageSlotKind Kind;
        public int Index;                        // order within its fill region / spill run (Static's image pick)
        public float Cx, Cy, AngleDeg;
        public float Base;                       // Fill: oversized cell size; Spill: tile width
        public int HeroW, HeroH, Feather;        // Hero only
        public bool HardL, HardT, HardR, HardB;  // Hero only: edges that stay crisp (the off-screen bleed)

        /// <summary>The tile's size for a source image of this size: the hero's own box, otherwise the
        /// planned width with the source's (clamped) aspect.</summary>
        public (int W, int H) TileSize(int srcW, int srcH)
        {
            if (Kind == CollageSlotKind.Hero) return (HeroW, HeroH);
            float aspect = CollageComposer.ClampAspect(srcW, srcH);
            return (Math.Max(1, (int)Base), Math.Max(1, (int)(Base / aspect)));
        }

        /// <summary>The hero's uniform feather, 0 on its hard edges.</summary>
        public void HeroFeather(out float fL, out float fT, out float fR, out float fB)
        {
            fL = HardL ? 0 : Feather;
            fT = HardT ? 0 : Feather;
            fR = HardR ? 0 : Feather;
            fB = HardB ? 0 : Feather;
        }
    }

    public static partial class CollageComposer
    {
        /// <summary>
        /// Compose one collage from <paramref name="sources"/> - the user's SELECTED images, already
        /// chosen by <see cref="CollageSources"/> - and write it as a JPEG into <paramref name="outputDir"/>.
        /// Returns the written file path, or null if there was nothing usable to compose (the caller then
        /// leaves the desktop alone).
        /// </summary>
        public static string? Compose(IReadOnlyList<string> sources, string outputDir, string accentHex, string backgroundHex,
            CollageLayout layout = CollageLayout.SideHero)
        {
            var files = sources?.ToList() ?? new List<string>();
            if (files.Count == 0)
            {
                App.Logger?.Warning("[Collage] No usable images to compose");
                return null;
            }

            var rng = new Random();
            Shuffle(files, rng);

            // Decode a working set at tile size. Gifs/webp contribute a mid-timeline frame (skips the title
            // card). Sorted by SOURCE size: the capped decodes all look alike.
            var decoded = new List<(SKBitmap Bmp, long Pixels, string Path)>();
            foreach (var f in files)
            {
                if (decoded.Count >= DecodeBudget) break;
                var bmp = DecodeCapped(f, TileDecodeCap, out var pixels);
                if (bmp != null && bmp.Width > 0 && bmp.Height > 0) decoded.Add((bmp, pixels, f));
                else bmp?.Dispose();
            }
            decoded.Sort((a, b) => b.Pixels.CompareTo(a.Pixels));
            var images = decoded.Select(d => d.Bmp).ToList();

            if (images.Count < 2)
            {
                foreach (var b in images) b.Dispose();
                App.Logger?.Warning("[Collage] Only {N} decodable image(s) - need at least 2", images.Count);
                return null;
            }

            try
            {
                var (w, h) = CanvasSize();
                var accent = HexToColor(accentHex, DefaultAccent);
                var background = HexToColor(backgroundHex, DefaultBackground);

                using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                if (surface == null)
                {
                    App.Logger?.Error("[Collage] Could not create a {W}x{H} surface", w, h);
                    return null;
                }
                var canvas = surface.Canvas;

                // Hero gets the sharpest (largest) source at screen size; the rest stay tile sized.
                var heroBmp = DecodeCapped(decoded[0].Path, Math.Max(w, h), out _);
                if (heroBmp != null) { images[0].Dispose(); images[0] = heroBmp; }

                // Estimate each image's focus once, aligned to the sorted list, so feathering can protect
                // the subject/text per edge. Cheap: a 40px thumbnail's edge energy per image.
                var focus = new List<SKRect>(images.Count);
                foreach (var b in images) focus.Add(ComputeFocus(b));

                // Under everything: gradient base + a pool image blown up as blurred atmosphere.
                DrawBackdrop(canvas, w, h, background, images[rng.Next(images.Count)]);

                // The layout-specific arrangement. The colour passes around it are identical for every
                // layout, so the active mod still leads the look regardless of arrangement.
                DrawPlan(canvas, PlanLayout(layout, w, h, rng), images, focus);

                // Over everything: one grade (the mod accent), then pull the edges down.
                DrawFinish(canvas, w, h, accent);

                canvas.Flush();

                // Encode + write.
                Directory.CreateDirectory(outputDir);
                var outPath = Path.Combine(outputDir, "ccp_collage_" + Guid.NewGuid().ToString("N") + ".jpg");
                using (var image = surface.Snapshot())
                using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 92))
                using (var fs = File.Create(outPath))
                {
                    data.SaveTo(fs);
                }

                App.Logger?.Information("[Collage] Composed {W}x{H} from {N} images -> {File}", w, h, images.Count, Path.GetFileName(outPath));
                return outPath;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[Collage] Compose failed");
                return null;
            }
            finally
            {
                foreach (var b in images) b.Dispose();
            }
        }

        // ---------------------------------------------------------------- layout plan (shared with Dynamic)

        /// <summary>
        /// Plan a layout as slots, in draw order - where each tile goes, how big, how tilted - before any
        /// image is assigned. Static (<see cref="DrawPlan"/>) and Dynamic (DynamicWallpaperService) both draw
        /// from this one plan, so the two can never drift apart. Filler is always drawn first and the hero
        /// over it, so the hero's soft inner edge dissolves over the collage.
        /// <list type="bullet">
        /// <item>SideHero: one hero bleeding off a random side, the open half filled, a few tiles spilled
        /// across the hero's inner edge.</item>
        /// <item>FillerOnly: no hero - tilted, feathered tiles across the whole screen. ~9 columns keeps each
        /// tile the same visual scale as SideHero's half-screen fill (so the compose is no heavier).</item>
        /// <item>CenterHero: hero centred and full-height, feathered on BOTH vertical edges; a filler flank on
        /// each side, with a few tiles spilled across both inner edges.</item>
        /// </list>
        /// </summary>
        internal static List<CollageSlot> PlanLayout(CollageLayout layout, int w, int h, Random rng)
        {
            var slots = new List<CollageSlot>();
            switch (layout)
            {
                case CollageLayout.FillerOnly:
                    PlanFill(slots, new SKRect(0, 0, w, h), rng, cols: 9);
                    break;

                case CollageLayout.CenterHero:
                {
                    int heroW = (int)(w * Range(rng, 0.44f, 0.50f));
                    int heroH = (int)(h * Range(rng, 0.90f, 1.0f));
                    int heroX = (w - heroW) / 2;
                    int heroY = (h - heroH) / 2;
                    int feather = Math.Max(30, heroW / 11);
                    bool tallHero = heroH >= h;
                    int leftEdge = heroX, rightEdge = heroX + heroW;

                    // Each flank is extended under the hero by one feather so the hero covers the seam.
                    PlanFill(slots, new SKRect(0, 0, leftEdge + feather, h), rng, cols: 2);
                    PlanFill(slots, new SKRect(rightEdge - feather, 0, w, h), rng, cols: 2);
                    slots.Add(new CollageSlot
                    {
                        Kind = CollageSlotKind.Hero, Cx = heroX + heroW / 2f, Cy = heroY + heroH / 2f,
                        HeroW = heroW, HeroH = heroH, Feather = feather,
                        HardL = false, HardT = tallHero, HardR = false, HardB = tallHero,
                    });
                    // Break both straight inner edges: spill a few tilted tiles, alternating sides.
                    for (int k = 0; k < 6; k++)
                    {
                        float edge = (k % 2 == 0) ? leftEdge : rightEdge;
                        slots.Add(new CollageSlot
                        {
                            Kind = CollageSlotKind.Spill, Index = k,
                            Cx = edge + Range(rng, -0.04f, 0.04f) * w,
                            Cy = Range(rng, 0.07f, 0.93f) * h,
                            Base = Math.Max(1, (int)(heroW * Range(rng, 0.18f, 0.26f))),
                            AngleDeg = Range(rng, -MaxTiltDeg, MaxTiltDeg),
                        });
                    }
                    break;
                }

                default:   // SideHero
                {
                    bool heroLeft = rng.NextDouble() < 0.5;
                    int heroW = (int)(w * Range(rng, 0.40f, 0.48f));
                    int heroH = (int)(h * Range(rng, 0.90f, 1.0f));
                    float bleed = Range(rng, 0.06f, 0.18f);
                    int heroX = heroLeft ? -(int)(heroW * bleed) : w - heroW + (int)(heroW * bleed);
                    int heroY = (h - heroH) / 2;
                    int feather = Math.Max(30, heroW / 11);
                    int heroInner = heroLeft ? heroX + heroW : heroX;   // the edge that faces the collage
                    bool tallHero = heroH >= h;

                    // Fill the OPEN half edge-to-edge, extended under the hero by one feather (hero covers the seam).
                    PlanFill(slots, heroLeft ? new SKRect(heroInner - feather, 0, w, h) : new SKRect(0, 0, heroInner + feather, h),
                        rng, cols: 5);
                    // Only the off-screen (bleeding) edge stays hard.
                    slots.Add(new CollageSlot
                    {
                        Kind = CollageSlotKind.Hero, Cx = heroX + heroW / 2f, Cy = heroY + heroH / 2f,
                        HeroW = heroW, HeroH = heroH, Feather = feather,
                        HardL = heroLeft, HardT = tallHero, HardR = !heroLeft, HardB = tallHero,
                    });
                    // Break the hero's straight inner edge: spill a few tilted tiles across it.
                    for (int k = 0; k < 6; k++)
                    {
                        slots.Add(new CollageSlot
                        {
                            Kind = CollageSlotKind.Spill, Index = k,
                            Cx = heroInner + Range(rng, -0.05f, 0.05f) * w,
                            Cy = Range(rng, 0.07f, 0.93f) * h,
                            Base = Math.Max(1, (int)(heroW * Range(rng, 0.20f, 0.30f))),
                            AngleDeg = Range(rng, -MaxTiltDeg, MaxTiltDeg),
                        });
                    }
                    break;
                }
            }
            return slots;
        }

        /// <summary>Pack a region with filler slots: a grid of oversized (1.81×), jittered cells so the tiles
        /// overlap and leave no gaps, a third kept upright and the rest tilted.</summary>
        private static void PlanFill(List<CollageSlot> slots, SKRect region, Random rng, int cols)
        {
            const float coverage = 1.81f;
            const float upright = 0.34f;
            if (cols < 1) cols = 1;
            float rw = region.Width, rh = region.Height;
            if (rw <= 8) return;

            float cellW = rw / cols;
            int rows = Math.Max(1, (int)Math.Round(rh / cellW));
            float cellH = rh / rows;
            int i = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    slots.Add(new CollageSlot
                    {
                        Kind = CollageSlotKind.Fill, Index = i++,
                        Cx = region.Left + (c + 0.5f) * cellW + Range(rng, -0.28f, 0.28f) * cellW,
                        Cy = region.Top + (r + 0.5f) * cellH + Range(rng, -0.28f, 0.28f) * cellH,
                        Base = Math.Max(cellW, cellH) * coverage * Range(rng, 0.92f, 1.18f),
                        AngleDeg = rng.NextDouble() < upright ? 0f : Range(rng, -MaxTiltDeg, MaxTiltDeg),
                    });
                }
            }
        }

        /// <summary>
        /// Draw a planned layout with Static's image assignment: the hero gets images[0] (the largest);
        /// filler cells cycle through the rest (all of them when there is no hero); the spill tiles reuse a
        /// spread of the pool. Tiles feather per edge from each image's focus - an edge the subject hugs
        /// stays crisp, an edge with empty space feathers fully (~20%); the hero keeps its own uniform
        /// feather and a light sharpen.
        /// </summary>
        private static void DrawPlan(SKCanvas canvas, List<CollageSlot> slots, List<SKBitmap> images, List<SKRect> focus)
        {
            int baseIdx = slots.Exists(s => s.Kind == CollageSlotKind.Hero) ? 1 : 0;   // the hero reserves images[0]
            int n = Math.Max(1, images.Count - baseIdx);
            foreach (var s in slots)
            {
                if (s.Kind == CollageSlotKind.Hero)
                {
                    s.HeroFeather(out var hL, out var hT, out var hR, out var hB);
                    using var sharpen = HeroSharpen();
                    DrawTileCore(canvas, images[0], s.Cx, s.Cy, s.HeroW, s.HeroH, hL, hT, hR, hB, 0f, sharpen);
                    continue;
                }

                int idx = s.Kind == CollageSlotKind.Fill
                    ? baseIdx + s.Index % n
                    : 1 + (s.Index * 5 + 2) % (images.Count - 1);
                var src = images[idx];
                var (tw, th) = s.TileSize(src.Width, src.Height);
                PerEdgeFeather(focus[idx], src.Width, src.Height, tw, th, out var fL, out var fT, out var fR, out var fB);
                DrawTileCore(canvas, src, s.Cx, s.Cy, tw, th, fL, fT, fR, fB, s.AngleDeg, null);
            }
        }
    }
}

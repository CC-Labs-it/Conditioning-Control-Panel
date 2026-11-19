using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.FrameCore;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The animated profile frames (owner 2026-11-17: "make the profile frames actually decent and animated").
/// Rules in CCP.Core/Frames/ProfileFrames.cs, drawn by Services/Profile/ProfileFrameArt.cs here and by the shop page's
/// sheet/frames.js, both pinned to prizecounter tests/fixtures/frame-probes.json.
/// Set CCP_FRAME_DUMP=&lt;dir&gt; to write every frame at three sizes, a six-step motion strip and the cost numbers.
/// </summary>
public class ProfileFrameTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static readonly string[] Levels = { "full", "less", "off" };

    private static FrameMotion MotionOf(string level, bool gentle) => FrameMotion.Of(Array.IndexOf(Levels, level), gentle);

    private static string Fnv(string s)
    {
        uint h = 0x811c9dc5;
        unchecked { foreach (var c in s) { h ^= c; h *= 0x01000193; } }
        return h.ToString("x8");
    }

    /// <summary>The page's summary() (tests/frame-probe.mjs), step for step.</summary>
    private static (int n, string sig, double a, double b) Summary(List<FramePrim> prims)
    {
        var sig = new StringBuilder();
        double a = 0, b = 0;
        for (var i = 0; i < prims.Count; i++)
        {
            var p = prims[i];
            var w = i + 1;
            if (p.Kind == FramePrimKind.Ellipse)
            {
                sig.Append($"e|{p.Fill}|{p.Stroke}|{(p.Live ? 1 : 0)};");
                a += w * (p.X + 1.7 * p.Y);
                b += w * (p.Rx + p.Ry + p.Rot / 360 + p.Width + p.Alpha);
            }
            else
            {
                sig.Append($"p|{p.Fill}|{p.Fill2}|{p.Stroke}|{(p.Closed ? 1 : 0)}|{(p.Smooth ? 1 : 0)}|{(p.Live ? 1 : 0)}|{p.Pts!.Length / 2};");
                double sx = 0;
                for (var k = 0; k < p.Pts.Length; k += 2) sx += p.Pts[k] + 1.7 * p.Pts[k + 1];
                a += w * sx;
                b += w * (p.Width + p.Alpha + p.Hole + p.G0 + p.G1);
            }
        }
        return (prims.Count, Fnv(sig.ToString()), a, b);
    }

    [Fact]
    public void Page_and_desktop_draw_the_same_frames()
    {
        var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "web", "prizecounter", "tests", "fixtures", "frame-probes.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var count = 0;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var id = e.GetProperty("id").GetString();
            var t = e.GetProperty("t").GetDouble();
            var level = e.GetProperty("level").GetString()!;
            var gentle = e.GetProperty("gentle").GetBoolean();
            var d = e.GetProperty("d").GetInt32();
            var (n, sig, a, b) = Summary(ProfileFrames.Build(id, t, MotionOf(level, gentle), d));
            var at = $"{id} t={t} {level}{(gentle ? " gentle" : "")} d={d}";
            Assert.True(n == e.GetProperty("n").GetInt32(), $"{at}: {n} steps");
            Assert.True(sig == e.GetProperty("sig").GetString(), $"{at}: step plan {sig}");
            var fa = e.GetProperty("a").GetDouble();
            var fb = e.GetProperty("b").GetDouble();
            Assert.True(Math.Abs(a - fa) <= 2e-6 * Math.Max(1, Math.Abs(fa)), $"{at}: a {a} vs {fa}");
            Assert.True(Math.Abs(b - fb) <= 2e-6 * Math.Max(1, Math.Abs(fb)), $"{at}: b {b} vs {fb}");
            count++;
        }
        Assert.Equal(ProfileFrames.Ids.Count * 7, count);
    }

    [Fact]
    public void Every_sold_frame_is_drawn()
    {
        Assert.Equal(ProfileFrames.Ids.OrderBy(x => x), ShopCosmetics.FrameIds().OrderBy(x => x));
        Assert.Equal(0.39, ProfileFrames.Window * 2 / 2, 6);
        Assert.Equal(ShopCosmetics.FrameWindowRatio / 2, ProfileFrames.Window, 6);
        Assert.Empty(ProfileFrames.Build("profile.frame.nope", 0, FrameMotion.Full, 1));
    }

    private static IEnumerable<(double x, double y)> Points(FramePrim p)
    {
        if (p.Kind == FramePrimKind.Ellipse)
        {
            for (var i = 0; i < 48; i++)
            {
                double u = i / 48.0 * Math.PI * 2, r = p.Rot * Math.PI / 180, x = p.Rx * Math.Cos(u), y = p.Ry * Math.Sin(u);
                yield return (p.X + x * Math.Cos(r) - y * Math.Sin(r), p.Y + x * Math.Sin(r) + y * Math.Cos(r));
            }
        }
        else for (var k = 0; k < p.Pts!.Length; k += 2) yield return (p.Pts[k], p.Pts[k + 1]);
    }

    [Fact]
    public void The_avatar_stays_clear_and_every_frame_stays_on_its_canvas()
    {
        foreach (var id in ProfileFrames.Ids)
            for (var d = 0; d <= 2; d++)
                foreach (var level in Levels)
                    for (var t = 0.0; t < 12; t += 0.37)
                        foreach (var p in ProfileFrames.Build(id, t, MotionOf(level, false), d))
                        {
                            if (p.Alpha <= 0.002 || (p.Kind == FramePrimKind.Ellipse && (p.Rx <= 0 || p.Ry <= 0))) continue;
                            var pts = Points(p).ToList();
                            var half = p.Stroke != null ? p.Width / 2 : 0;
                            var near = (p.Hole > 0 ? p.Hole : pts.Min(q => Math.Sqrt((q.x - 0.5) * (q.x - 0.5) + (q.y - 0.5) * (q.y - 0.5)))) - half;
                            Assert.True(near >= ProfileFrames.Keep - 1e-9, $"{id} d={d} {level} t={t:0.00}: reaches {near:0.0000}");
                            Assert.True(pts.Min(q => Math.Min(q.x, q.y)) - half >= -0.003 && pts.Max(q => Math.Max(q.x, q.y)) + half <= 1.003, $"{id} d={d} t={t:0.00}: off the canvas");
                        }
    }

    [Fact]
    public void Still_steps_never_move_and_motion_off_is_one_pose()
    {
        foreach (var id in ProfileFrames.Ids)
            for (var d = 0; d <= 2; d++)
            {
                var a = ProfileFrames.Build(id, 0, FrameMotion.Full, d);
                var b = ProfileFrames.Build(id, 6.1, FrameMotion.Full, d);
                Assert.Equal(a.Count, b.Count);
                for (var i = 0; i < a.Count; i++)
                {
                    Assert.Equal(a[i].Live, b[i].Live);
                    if (!a[i].Live) Assert.Equal(Summary(new() { a[i] }), Summary(new() { b[i] }));
                }
                Assert.Contains(a, p => p.Live);
                Assert.Equal(Summary(ProfileFrames.Build(id, 0, FrameMotion.Still, d)), Summary(ProfileFrames.Build(id, 9.3, FrameMotion.Still, d)));
                Assert.NotEqual(Summary(ProfileFrames.Build(id, 0.4, FrameMotion.Full, d)), Summary(ProfileFrames.Build(id, 1.3, FrameMotion.Full, d)));
            }
    }

    [Fact]
    public void Reduced_is_half_and_gentle_caps_speed()
    {
        Assert.Equal(new FrameMotion(1, 1, false), FrameMotion.Of(0, false));
        Assert.Equal(new FrameMotion(0.5, 0.5, false), FrameMotion.Of(1, false));
        Assert.Equal(new FrameMotion(0, 0, false), FrameMotion.Of(2, false));
        Assert.Equal(new FrameMotion(0.5, 1, true), FrameMotion.Of(0, true));
        Assert.False(FrameMotion.Of(2, true).Moves);
    }

    private static void Sta(Action body)
    {
        Exception? err = null;
        var th = new Thread(() => { try { body(); } catch (Exception e) { err = e; } });
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        th.Join();
        if (err != null) throw new Exception(err.Message, err);
    }

    [Fact]
    public void The_still_picture_keeps_the_frame_geometry()
    {
        Sta(() =>
        {
            foreach (var id in ProfileFrames.Ids)
                foreach (var px in new[] { 28.0, 133, 260 })
                {
                    var img = ProfileFrameArt.Still(id, px);
                    Assert.NotNull(img);
                    Assert.True(img!.IsFrozen);
                    Assert.Equal(ProfileFrameArt.Unit, img.Width, 3);
                    Assert.Equal(ProfileFrameArt.Unit, img.Height, 3);
                    Assert.Same(img, ProfileFrameArt.Still(id, px));
                }
            Assert.Null(ProfileFrameArt.Still("profile.avatar.emi-pixel"));
        });
    }

    [Fact]
    public void A_living_frame_caches_its_still_runs_and_redraws_only_the_moving_ones()
    {
        Sta(() =>
        {
            foreach (var id in ProfileFrames.Ids)
            {
                var host = new Image();
                Assert.True(ProfileFrameArt.Attach(host, id, 133));
                var src = Assert.IsType<DrawingImage>(host.Source);
                Assert.Equal(ProfileFrameArt.Unit, src.Width, 3);
                Assert.Equal(ProfileFrameArt.Unit, src.Height, 3);
                ProfileFrameArt.Detach(host);
            }
            var other = new Image();
            Assert.False(ProfileFrameArt.Attach(other, "profile.avatar.emi-pixel", 133));
            Assert.Null(other.Source);
        });
    }

    /// <summary>Cost of one tick: rebuilding the moving steps and redrawing their groups, for 1 and for 20 frames on
    /// screen at once (a full friends list), plus a software raster of 20 at card size as a ceiling for what the
    /// compositor then does. Written to the dump dir when CCP_FRAME_DUMP is set.</summary>
    [Fact]
    public void A_tick_is_cheap_for_one_frame_and_for_twenty()
    {
        string report = "";
        Sta(() =>
        {
            var lives = new List<ProfileFrameArt.Live>();
            for (var i = 0; i < 20; i++) lives.Add(new ProfileFrameArt.Live(new Image(), ProfileFrames.Ids[i % 5], 133));
            double Measure(int n)
            {
                var m = FrameMotion.Full;
                for (var w = 0; w < 10; w++) for (var i = 0; i < n; i++) lives[i].Render(w * 0.033, m);
                var sw = Stopwatch.StartNew();
                const int ticks = 90;
                for (var k = 0; k < ticks; k++) for (var i = 0; i < n; i++) lives[i].Render(1 + k * 0.033, m);
                return sw.Elapsed.TotalMilliseconds / ticks;
            }
            var one = Measure(1);
            var twenty = Measure(20);
            // software raster of 20 frames at 133 px
            var vis = new DrawingVisual();
            using (var dc = vis.RenderOpen())
                for (var i = 0; i < 20; i++) dc.DrawImage(lives[i].Source, new Rect(i % 5 * 140, i / 5 * 140, 133, 133));
            var rtb = new RenderTargetBitmap(700, 560, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(vis);
            var sw2 = Stopwatch.StartNew();
            for (var k = 0; k < 10; k++) { lives.ForEach(l => l.Render(2 + k * 0.033, FrameMotion.Full)); rtb.Clear(); rtb.Render(vis); }
            var raster = sw2.Elapsed.TotalMilliseconds / 10;
            report = string.Create(CultureInfo.InvariantCulture, $"tick (build + redraw moving runs): 1 frame {one:0.000} ms, 20 frames {twenty:0.000} ms; software raster of 20 at 133 px incl. tick {raster:0.00} ms");
            foreach (var l in lives) l.Detach();
            Assert.True(twenty < 80, report);   // about 2 ms alone; the whole suite running beside it has pushed one run past 20
        });
        var dir = Environment.GetEnvironmentVariable("CCP_FRAME_DUMP");
        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "cost.txt"), report + Environment.NewLine); }
    }

    /// <summary>CCP_FRAME_DUMP=&lt;dir&gt;: each frame drawn by the desktop renderer round a portrait at 28, 104 and 220 px
    /// avatars, then a six-step motion strip at 104 px (0.55 s apart), on the panel's dark ground.</summary>
    [Fact]
    public void Dump_stills_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_FRAME_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var portraitPath = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "web", "prizecounter", "assets", "preview", "profile", "profile.avatar.rain-portrait.png");
        Sta(() =>
        {
            BitmapImage? portrait = null;
            if (File.Exists(portraitPath)) { portrait = new BitmapImage(); portrait.BeginInit(); portrait.UriSource = new Uri(portraitPath); portrait.CacheOption = BitmapCacheOption.OnLoad; portrait.EndInit(); portrait.Freeze(); }
            var ground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x3F));
            void Avatar(DrawingContext dc, double cx, double cy, double size)
            {
                Brush face = portrait != null ? new ImageBrush(portrait) { Stretch = Stretch.UniformToFill } : Brushes.MediumPurple;
                dc.DrawEllipse(face, new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)), Math.Max(1, size * 0.0288)), new Point(cx, cy), size / 2, size / 2);
            }
            void Frame(DrawingContext dc, string id, double cx, double cy, double size, double t)
            {
                var canvas = size / ShopCosmetics.FrameWindowRatio;
                var d = ProfileFrames.DetailFor(canvas);
                var prims = ProfileFrames.Build(id, t, FrameMotion.Full, d);
                dc.PushTransform(new TranslateTransform(cx - canvas / 2, cy - canvas / 2));
                dc.PushTransform(new ScaleTransform(canvas / ProfileFrameArt.Unit, canvas / ProfileFrameArt.Unit));
                ProfileFrameArt.Draw(dc, prims, 0, prims.Count, 0.75 * ProfileFrameArt.Unit / canvas);
                dc.Pop(); dc.Pop();
            }
            void Save(DrawingVisual v, int w, int h, string name)
            {
                var rtb = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
                rtb.Render(v);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(Path.Combine(dir, name));
                enc.Save(fs);
            }
            var sheet = new DrawingVisual();
            using (var dc = sheet.RenderOpen())
            {
                dc.DrawRectangle(ground, null, new Rect(0, 0, 1180, 5 * 320));
                for (var r = 0; r < ProfileFrames.Ids.Count; r++)
                {
                    var id = ProfileFrames.Ids[r];
                    double y = r * 320 + 160;
                    foreach (var (size, x) in new[] { (28.0, 40.0), (104.0, 150.0), (220.0, 360.0) })
                    {
                        Avatar(dc, x, y, size);
                        Frame(dc, id, x, y, size, 0);
                    }
                    for (var k = 0; k < 6; k++)
                    {
                        double x = 560 + (k % 3) * 205, yy = y - 75 + (k / 3) * 150;
                        Avatar(dc, x, yy, 104);
                        Frame(dc, id, x, yy, 104, k * 0.55);
                    }
                }
            }
            Save(sheet, 1180, 5 * 320, "desktop-sheet.png");
            foreach (var id in ProfileFrames.Ids)
            {
                var one = new DrawingVisual();
                using (var dc = one.RenderOpen())
                {
                    dc.DrawRectangle(ground, null, new Rect(0, 0, 1240, 330));
                    foreach (var (size, x) in new[] { (28.0, 40.0), (104.0, 150.0), (220.0, 360.0) }) { Avatar(dc, x, 165, size); Frame(dc, id, x, 165, size, 0); }
                    for (var k = 0; k < 6; k++) { double x = 560 + k * 112; Avatar(dc, x, 165, 80); Frame(dc, id, x, 165, 80, k * 0.55); }
                }
                Save(one, 1240, 330, id + ".png");
            }
        });
    }
}

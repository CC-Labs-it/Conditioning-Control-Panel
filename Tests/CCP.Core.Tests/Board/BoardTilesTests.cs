using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Tonight Board's pixel board (2026-10-07): the wire block, the link allowlist, the audience
/// and expiry rules, the picture, every effect curve ported from the approved mockup, the tile
/// layout and the renderer's frame budget. The download, the card and the view are WPF-side
/// (BoardServiceTests) until BoardService moves.
/// </summary>
[Collection(BoardFrameBudgetCollection.Name)]
public class BoardTilesTests
{
    private static string Loc(string key) => "[" + key + "]";

    private static BoardPost Post(int v = 3, string[]? fx = null, DateTime? until = null, string? link = "lobby",
        BoardAudience aud = BoardAudience.Everyone, int frames = 1, int fps = 6, int w = 64, int h = 36) =>
        new(v, fx ?? Array.Empty<string>(), until, link, aud, frames, fps, w, h);

    private static int Argb(int a, int r, int g, int b) => unchecked((a << 24) | (r << 16) | (g << 8) | b);

    // ---- wire --------------------------------------------------------------------------------

    [Fact]
    public void A_full_block_parses_into_a_post()
    {
        var p = BoardWire.Parse("""
            {"message":"hi","board":{"v":3,"fx":["ola","Twinkle","sparkle","ola"],"until":"2026-10-11T21:59:00Z",
             "link":"lobby","aud":"patrons","frames":2,"fps":8,"w":32,"h":20}}
            """);
        Assert.NotNull(p);
        Assert.Equal(3, p!.Version);
        Assert.Equal(new[] { "ola", "twinkle" }, p.Fx);
        Assert.Equal(new DateTime(2026, 10, 11, 21, 59, 0, DateTimeKind.Utc), p.UntilUtc);
        Assert.Equal(DateTimeKind.Utc, p.UntilUtc!.Value.Kind);
        Assert.Equal("lobby", p.Link);
        Assert.Equal(BoardAudience.Patrons, p.Audience);
        Assert.Equal((2, 8, 32, 20), (p.Frames, p.Fps, p.Width, p.Height));
    }

    [Theory]
    [InlineData("""{"message":"hi"}""")]
    [InlineData("""{"message":"hi","board":null}""")]
    [InlineData("""{"board":{"fx":[]}}""")]
    [InlineData("""{"board":{"v":0}}""")]
    [InlineData("""{"board":{"v":4,"aud":"prime"}}""")]
    [InlineData("""{"board":{"v":4,"until":"next week"}}""")]
    [InlineData("""{"board":{"v":4,"until":12}}""")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData(null)]
    public void No_board_or_an_untrustworthy_block_reads_as_no_post(string? json) => Assert.Null(BoardWire.Parse(json));

    [Fact]
    public void Missing_fields_take_the_defaults_and_out_of_range_numbers_are_clamped()
    {
        var p = BoardWire.Parse("""{"board":{"v":"5"}}""")!;
        Assert.Equal((5, 1, 6, 64, 36), (p.Version, p.Frames, p.Fps, p.Width, p.Height));
        Assert.Null(p.UntilUtc);
        Assert.Null(p.Link);
        Assert.Equal(BoardAudience.Everyone, p.Audience);
        Assert.Empty(p.Fx);

        var q = BoardWire.Parse("""{"board":{"v":6,"frames":40,"fps":0,"w":900,"h":-3}}""")!;
        Assert.Equal((8, 1, 64, 1), (q.Frames, q.Fps, q.Width, q.Height));
    }

    [Fact]
    public void An_offset_expiry_is_read_as_utc()
    {
        var p = BoardWire.Parse("""{"board":{"v":7,"until":"2026-10-11T23:59:00+02:00"}}""")!;
        Assert.Equal(new DateTime(2026, 10, 11, 21, 59, 0, DateTimeKind.Utc), p.UntilUtc);
    }

    // ---- links -------------------------------------------------------------------------------

    [Theory]
    [InlineData("lobby", "lobby")]
    [InlineData("PREMIUM", "premium")]
    [InlineData("discord", "discord")]
    [InlineData("tab:availablesubjects", "tab:availablesubjects")]
    [InlineData("tab:Chaster", "tab:chaster")]
    [InlineData("https://cclabs.app/locktober-rules", "https://cclabs.app/locktober-rules")]
    [InlineData("https://www.cclabs.app/x?y=1", "https://www.cclabs.app/x?y=1")]
    public void Allowed_links_survive(string raw, string expected) => Assert.Equal(expected, BoardLinks.Normalise(raw));

    [Theory]
    [InlineData("http://cclabs.app/")]
    [InlineData("https://evil.example/")]
    [InlineData("https://cclabs.app.evil.example/")]
    [InlineData("https://user@cclabs.app/")]
    [InlineData("https://cclabs.app:8443/")]
    [InlineData("https://cclabs.app/\"&calc")]
    [InlineData("https://cclabs.app/a b")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///c:/windows")]
    [InlineData("tab:")]
    [InlineData("tab:../../x")]
    [InlineData("none")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_no_button(string? raw)
    {
        Assert.Null(BoardLinks.Normalise(raw));
        Assert.Equal(BillboardActionKind.None, BoardLinks.ToAction(raw, Loc).Kind);
    }

    [Fact]
    public void Each_link_maps_to_its_button()
    {
        var lobby = BoardLinks.ToAction("lobby", Loc);
        Assert.Equal((BillboardActionKind.Tab, "availablesubjects", "[board_btn_lobby]"), (lobby.Kind, lobby.Target, lobby.Label));
        var premium = BoardLinks.ToAction("premium", Loc);
        Assert.Equal((BillboardActionKind.Tab, "premium"), (premium.Kind, premium.Target));
        var discord = BoardLinks.ToAction("discord", Loc);
        Assert.Equal((BillboardActionKind.Link, ConditioningControlPanel.Services.DiscordLinks.Invite), (discord.Kind, discord.Target));
        var tab = BoardLinks.ToAction("tab:chaster", Loc);
        Assert.Equal((BillboardActionKind.Tab, "chaster", "[board_btn_open]"), (tab.Kind, tab.Target, tab.Label));
        var web = BoardLinks.ToAction("https://cclabs.app/x", Loc);
        Assert.Equal((BillboardActionKind.Link, "https://cclabs.app/x"), (web.Kind, web.Target));
    }

    // ---- audience and expiry -----------------------------------------------------------------

    [Theory]
    [InlineData(BoardAudience.Everyone, BillboardTier.Free, true)]
    [InlineData(BoardAudience.Everyone, BillboardTier.Prime, true)]
    [InlineData(BoardAudience.Free, BillboardTier.Free, true)]
    [InlineData(BoardAudience.Free, BillboardTier.Basic, false)]
    [InlineData(BoardAudience.Free, BillboardTier.Prime, false)]
    [InlineData(BoardAudience.Patrons, BillboardTier.Free, false)]
    [InlineData(BoardAudience.Patrons, BillboardTier.Basic, true)]
    [InlineData(BoardAudience.Patrons, BillboardTier.Prime, true)]
    public void Audience_matrix(BoardAudience aud, BillboardTier tier, bool sees) => Assert.Equal(sees, BoardRules.Sees(aud, tier));

    [Fact]
    public void A_post_lives_until_its_time_and_forever_without_one()
    {
        var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(BoardRules.IsLive(Post(until: null), now));
        Assert.True(BoardRules.IsLive(Post(until: now.AddSeconds(1)), now));
        Assert.False(BoardRules.IsLive(Post(until: now), now));
        Assert.False(BoardRules.IsLive(Post(until: now.AddHours(-1)), now));
    }

    // ---- the picture -------------------------------------------------------------------------

    [Fact]
    public void A_small_picture_sits_centred_and_reads_its_own_colours()
    {
        int w = 4, h = 2;
        var px = new int[w * h];
        px[0] = Argb(255, 255, 255, 255); // bright
        px[1] = Argb(255, 255, 0, 128);   // saturated
        px[2] = Argb(0, 255, 0, 0);       // transparent
        px[3] = Argb(255, 10, 10, 10);    // near black: drawn, not lit
        px[4] = Argb(255, 128, 128, 128); // grey: lit, not bright, not saturated
        for (int i = 5; i < 8; i++) px[i] = Argb(255, 0, 200, 255);
        var pic = BoardPicture.FromPixels(px, w, h, Post(w: w, h: h));
        Assert.Equal((30, 17, 4, 2), (pic.X0, pic.Y0, pic.PicW, pic.PicH));
        int at(int x, int y) => (pic.Y0 + y) * 64 + pic.X0 + x;
        Assert.True(pic.Bright[0][at(0, 0)]);
        Assert.NotNull(pic.CycleRgb[0][at(1, 0)]);
        Assert.False(pic.Lit[0][at(2, 0)]);
        Assert.Equal(BoardPicture.BaseRgb, pic.Rgb[0][at(2, 0)]);
        Assert.False(pic.Lit[0][at(3, 0)]);
        Assert.True(pic.Lit[0][at(0, 1)]);
        Assert.False(pic.Bright[0][at(0, 1)]);
        Assert.Null((int[]?)pic.CycleRgb[0][at(0, 1)]);
        Assert.Equal(BoardPicture.BaseRgb, pic.Rgb[0][0]); // outside the picture
    }

    [Fact]
    public void A_strip_splits_into_frames_and_a_too_big_picture_is_cropped()
    {
        var strip = new int[6 * 2];
        for (int y = 0; y < 2; y++) for (int x = 0; x < 6; x++) strip[y * 6 + x] = x < 3 ? Argb(255, 255, 0, 0) : Argb(255, 0, 0, 255);
        var pic = BoardPicture.FromPixels(strip, 6, 2, Post(frames: 2, w: 3, h: 2));
        Assert.Equal(2, pic.FrameCount);
        int i = pic.Y0 * 64 + pic.X0;
        Assert.Equal(0xFF0000, pic.Rgb[0][i]);
        Assert.Equal(0x0000FF, pic.Rgb[1][i]);

        var big = BoardPicture.FromPixels(new int[100 * 50], 100, 50, Post(w: 64, h: 36));
        Assert.Equal((0, 0, 64, 36), (big.X0, big.Y0, big.PicW, big.PicH));
    }

    // ---- effect curves -----------------------------------------------------------------------

    [Fact]
    public void Ola_is_a_crest_about_nine_tiles_wide_every_three_seconds()
    {
        Assert.InRange(BoardFxMath.OlaCrestTiles(), 9, 10.5);
        Assert.InRange(BoardFxMath.OlaEverySeconds, 2.8, 3.3);
        double t = 1.0;
        var lifted = Enumerable.Range(0, 64).Select(x => BoardFxMath.OlaLift(t, x, 0)).ToArray();
        Assert.All(lifted, z => Assert.InRange(z, 0, 1));
        int count = lifted.Count(z => z > 0);
        Assert.InRange(count, 5, 14); // ragged: each tile keeps its own timer
        // The crest moves right as time goes on (its centre, across all rows).
        double Centre(double tt)
        {
            double sx = 0, n = 0;
            for (int y = 0; y < 36; y++)
                for (int x = 0; x < 64; x++)
                {
                    double z = BoardFxMath.OlaLift(tt, x, y);
                    sx += z * x; n += z;
                }
            return sx / n;
        }
        Assert.True(Centre(1.2) > Centre(1.0) + 3);
    }

    [Fact]
    public void Ola_is_a_stadium_wave_each_tile_on_its_own_timer()
    {
        // Per-tile timing stays inside the owner's bounds and never changes between calls.
        for (int i = 0; i < BoardPicture.Tiles; i++)
        {
            var (off, rise, height) = BoardFxMath.OlaTile(i);
            Assert.InRange(off, -0.12, 0.12);
            Assert.InRange(rise, 0.8, 1.2);
            Assert.InRange(height, 0.75, 1.0);
            Assert.Equal((off, rise, height), BoardFxMath.OlaTile(i));
        }
        Assert.Equal(BoardFxMath.TileNoise(77, 1), BoardFxMath.TileNoise(77, 1));
        Assert.NotEqual(BoardFxMath.TileNoise(77, 1), BoardFxMath.TileNoise(77, 2));

        // Two neighbours in one column at the crest never move as one: in a run of crest frames
        // some pair differs clearly, and the same call twice gives the same lift.
        int x = 30, y = 17;
        bool differs = false;
        for (double t = 0; t < BoardFxMath.OlaEverySeconds; t += 1 / 30.0)
        {
            double a = BoardFxMath.OlaLift(t, x, y), b = BoardFxMath.OlaLift(t, x, y + 1);
            Assert.Equal(a, BoardFxMath.OlaLift(t, x, y));
            if ((a > 0.3 || b > 0.3) && Math.Abs(a - b) > 0.1) differs = true;
        }
        Assert.True(differs);

        // The front is ragged: the first frame each tile of a column leaves the floor is spread out
        // (a clean straight wave would differ only by the 0.18 slant, about 0.7 s over the column).
        var starts = new List<double>();
        for (int yy = 0; yy < 36; yy++)
        {
            double tPrev = BoardFxMath.OlaLift(0, x, yy);
            for (double t = 1 / 120.0; t < 2 * BoardFxMath.OlaEverySeconds; t += 1 / 120.0)
            {
                double z = BoardFxMath.OlaLift(t, x, yy);
                if (tPrev == 0 && z > 0) { starts.Add(t - yy * BoardFxMath.OlaSlantY / 36 / BoardFxMath.OlaSpeed); break; }
                tPrev = z;
            }
        }
        // Remove the straight-line slant and look at what is left: neighbours a beat early or late.
        double spread = starts.Max() - starts.Min();
        Assert.InRange(spread, 0.08, 0.5);
    }

    [Fact]
    public void The_ripple_ring_is_ragged_too_but_stable()
    {
        Assert.Equal(BoardFxMath.RippleLiftAt(5, 0.3, 7), BoardFxMath.RippleLiftAt(5, 0.3, 7));
        var values = Enumerable.Range(0, 40).Select(i => BoardFxMath.RippleLiftAt(i, 0.3, 7)).Distinct().Count();
        Assert.True(values > 10);
        Assert.All(Enumerable.Range(0, 40), i => Assert.InRange(BoardFxMath.RippleLiftAt(i, 0.3, 7), 0, 1));
    }

    [Fact]
    public void Ripple_is_a_ring_that_spreads_and_fades()
    {
        Assert.Equal(0, BoardFxMath.RippleLift(-0.1, 0));
        Assert.Equal(0, BoardFxMath.RippleLift(0.5, 20));            // not reached yet (front at 15)
        Assert.True(BoardFxMath.RippleLift(0.5, 13) > 0.5);         // just behind the front
        Assert.Equal(0, BoardFxMath.RippleLift(0.5, 5));             // the ring has passed
        Assert.True(BoardFxMath.RippleLift(1.0, 28) < BoardFxMath.RippleLift(0.2, 4));
        Assert.Equal(0, BoardFxMath.RippleLift(1.7, 49));            // faded out
    }

    [Fact]
    public void Arrival_hides_flashes_lifts_then_lands_everything()
    {
        Assert.True(BoardFxMath.BuildHidden(0.9, 0.5));
        Assert.False(BoardFxMath.BuildHidden(0.2, 0.5));
        Assert.True(BoardFxMath.BuildFlash(0.47, 0.5));
        Assert.False(BoardFxMath.BuildFlash(0.3, 0.5));
        Assert.True(BoardFxMath.BuildLift(0.45, 0.5) > 0);
        Assert.Equal(0, BoardFxMath.BuildLift(0.3, 0.5));
        Assert.Equal(1.0, BoardFxMath.BuildProgress(0.9), 6);
        double done = BoardFxMath.BuildProgress(BoardFxMath.BuildDoneSeconds);
        for (double h = 0; h < 1; h += 0.01)
        {
            Assert.False(BoardFxMath.BuildHidden(h, done));
            Assert.Equal(0, BoardFxMath.BuildLift(h, done));
        }
    }

    [Fact]
    public void Twinkle_cycle_chase_shine_wave_stay_in_range()
    {
        for (double t = 0; t < 5; t += 0.07)
        {
            Assert.InRange(BoardFxMath.Twinkle(t, 0.37), 0.3, 1.0);
            Assert.InRange(BoardFxMath.CycleStep(t, 17), 0, 2);
            Assert.InRange(BoardFxMath.WaveOffset(t, 9), -1, 1);
            Assert.InRange(BoardFxMath.Shine(t, 10, 10), 0, 0.75);
        }
        // Chase: 3 of every 8 edge tiles lit at any time.
        Assert.Equal(3, Enumerable.Range(0, 8).Count(p => BoardFxMath.ChaseOn(p, 0.4)));
    }

    [Fact]
    public void Cycle_walks_a_saturated_colour_round_the_wheel_and_leaves_greys_alone()
    {
        Assert.Null(BoardFxMath.CycleColours(128, 128, 128));
        Assert.Null(BoardFxMath.CycleColours(40, 0, 10)); // too dark
        var three = BoardFxMath.CycleColours(255, 0, 0)!;
        Assert.Equal(new[] { 0xFF0000, 0x00FF00, 0x0000FF }, three);
    }

    [Fact]
    public void Perimeter_walks_the_picture_edge_clockwise()
    {
        Assert.Equal(0, BoardFxMath.Perimeter(10, 5, 10, 5, 4, 3));
        Assert.Equal(3, BoardFxMath.Perimeter(13, 5, 10, 5, 4, 3));
        Assert.Equal(5, BoardFxMath.Perimeter(13, 7, 10, 5, 4, 3));
        Assert.Equal(8, BoardFxMath.Perimeter(10, 7, 10, 5, 4, 3));
        Assert.Equal(9, BoardFxMath.Perimeter(10, 6, 10, 5, 4, 3));
        Assert.Equal(-1, BoardFxMath.Perimeter(11, 6, 10, 5, 4, 3));
        Assert.Equal(-1, BoardFxMath.Perimeter(0, 0, 10, 5, 4, 3));
    }

    [Fact]
    public void Fx_names_become_flags()
    {
        var set = BoardFxSet.From(new[] { "ola", "CRT" });
        Assert.True(set.Ola && set.Crt && set.Animates);
        Assert.False(set.Twinkle || set.Cycle || set.Shine || set.Chase || set.Wave);
        Assert.False(BoardFxSet.From(null).Animates);
    }

    // ---- the scene ---------------------------------------------------------------------------

    [Fact]
    public void A_still_scene_is_the_whole_picture_flat()
    {
        var pic = BoardPicture.Solid(Post(fx: BoardFx.All.ToArray()), Argb(255, 255, 79, 168));
        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles];
        BoardScene.Compute(pic, 0, BoardFxSet.From(pic.Post.Fx), 7.3, 0, new[] { new BoardRipple(5, 5, 7.0) }, 7.3, true, c, z);
        Assert.All(z, v => Assert.Equal(0f, v));
        Assert.Contains(0xFF4FA8, c);
    }

    [Fact]
    public void At_the_start_of_the_arrival_every_tile_is_still_blank()
    {
        var pic = BoardPicture.Solid(Post(), Argb(255, 255, 79, 168));
        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles];
        BoardScene.Compute(pic, 0, default, 0, 0, null, 0, false, c, z);
        Assert.All(c, v => Assert.Equal(BoardPicture.BaseRgb, v));
        BoardScene.Compute(pic, 0, default, 5, 5, null, 5, false, c, z);
        Assert.All(c, v => Assert.Equal(0xFF4FA8, v));
        Assert.All(z, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void The_ola_lifts_a_band_and_a_ripple_lifts_a_ring()
    {
        var pic = BoardPicture.Solid(Post(fx: new[] { "ola" }), Argb(255, 200, 200, 200));
        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles];
        BoardScene.Compute(pic, 0, BoardFxSet.From(pic.Post.Fx), 1.0, 10, null, 1.0, false, c, z);
        int lifted = z.Count(v => v > 0.02f);
        Assert.InRange(lifted, 36 * 6, 36 * 12);

        BoardScene.Compute(pic, 0, default, 99, 10, new[] { new BoardRipple(32, 18, 2.0) }, 2.3, false, c, z); // effect time frozen, ripple clock runs
        Assert.True(z[18 * 64 + 32 + 7] > 0.1f);  // on the ring (front at 9 tiles)
        Assert.Equal(0f, z[18 * 64 + 32]);         // the centre has settled
    }

    // ---- layout and raster -------------------------------------------------------------------

    [Fact]
    public void Tile_layout_has_grout_a_resting_shadow_and_lifts_up_left()
    {
        var l = new BoardTileLayout(10);
        Assert.Equal((1, 9), (l.Gap, l.TileSize));
        Assert.Equal((640, 360), (l.Width, l.Height));
        Assert.Equal((10 * 3, 10 * 2), l.TileOrigin(3, 2));
        var rest = l.Lifted(3, 2, 0);
        Assert.Equal((30, 20, 9), (rest.X, rest.Y, rest.Size));
        var top = l.Lifted(3, 2, 1);
        Assert.True(top.Size > rest.Size);
        Assert.True(top.X < rest.X && top.Y < rest.Y);
        Assert.True(top.ShadowDy > rest.ShadowDy && top.ShadowAlpha > rest.ShadowAlpha);
        Assert.True(top.Highlight > 0 && rest.Highlight == 0);
    }

    [Theory]
    [InlineData(640, 10)]
    [InlineData(700, 11)]
    [InlineData(100, 4)]
    [InlineData(5000, 14)]
    [InlineData(0, 10)]
    public void Pitch_follows_the_art_width_within_budget(double px, int pitch) => Assert.Equal(pitch, BoardTileLayout.PitchFor(px));

    [Fact]
    public void The_bevel_is_lit_top_left_and_shaded_bottom_right()
    {
        var (mul, add) = BoardRaster.BuildBevel(10);
        int Lit(int i) => (128 * mul[i] >> 8) + add[i];
        Assert.True(Lit(0) > 128);
        Assert.True(Lit(99) < 128);
        Assert.True(Lit(0) > Lit(55));
    }

    [Fact]
    public void A_drawn_tile_wears_its_colour_and_the_grout_stays_dark()
    {
        var pic = BoardPicture.Solid(Post(), Argb(255, 0, 255, 0));
        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles];
        BoardScene.Compute(pic, 0, default, 0, 10, null, 0, true, c, z);
        var r = new BoardRaster(10);
        r.Draw(c, z, crt: false, 0);
        int centre = r.Pixels[5 * r.Width + 5];
        Assert.True(((centre >> 8) & 0xFF) > 150 && ((centre >> 16) & 0xFF) < 60);
        int grout = r.Pixels[5 * r.Width + 9]; // the grout column right of tile 0
        Assert.True((grout & 0xFFFFFF) < 0x101010);
        Assert.Equal(unchecked((int)0xFF000000), centre & unchecked((int)0xFF000000));
    }

    [Fact]
    public void A_frame_fits_the_budget()
    {
#if DEBUG
        // Debug code measured 12-13 ms a frame on the Linux runner; CI runs this suite in Release.
        Assert.Skip("The frame budget is a Release measurement.");
#endif
        // Every effect on, a ripple running, the biggest pitch: scene + raster per frame.
        var px = new int[64 * 36];
        for (int i = 0; i < px.Length; i++) px[i] = i % 3 == 0 ? Argb(255, 255, 255, 255) : Argb(255, 255, 79, 168);
        var pic = BoardPicture.FromPixels(px, 64, 36, Post(fx: BoardFx.All.ToArray()));
        var fx = BoardFxSet.From(pic.Post.Fx);
        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles];
        var ro = new BoardTileRole[BoardPicture.Tiles];
        var r = new BoardRaster(BoardTileLayout.MaxPitch);
        r.SetPicture(pic); // the message glow is baked once, outside the frame
        Assert.True(pic.HasInk && r.GlowPixels > 0);
        var ripples = new[] { new BoardRipple(20, 10, 0.1) };
        // Warm past tier-up (~30 calls + the background delay): with 10 frames CI measured Tier-0 code.
        for (int k = 0; k < 60; k++) { BoardScene.Compute(pic, 0, fx, 0.3 + k / 30.0, 10, ripples, 0.3 + k / 30.0, false, c, z, ro); r.Draw(c, z, true, 0.02, ro, 0.9); }
        var sw = Stopwatch.StartNew();
        const int frames = 60;
        for (int k = 0; k < frames; k++)
        {
            double t = 0.5 + k / 30.0;
            BoardScene.Compute(pic, 0, fx, t, 10, ripples, t, false, c, z, ro);
            r.Draw(c, z, true, 0.02, ro, BoardFxMath.GlowStrength(t, 10, false, true));
        }
        double ms = sw.Elapsed.TotalMilliseconds / frames;
        // The design budget is 2 ms a frame. Measured 2026-10-07 (Release, warmed up, every effect on
        // and a ripple running): pitch 10 0.52 ms, pitch 10 + CRT 0.92 ms, pitch 14 + CRT 1.67 ms.
        // With the message glow, field/message passes, face cache and packed CRT (same day, watch-party
        // sample, every effect): pitch 14 + CRT 1.6-1.7 ms (1.93 ms before on the same machine).
        // This guard is loose (Debug, cold JIT, a loaded CI runner) and only catches a gross regression.
        Assert.True(ms < 12, $"a board frame took {ms:F2} ms");
    }
}

/// <summary>
/// Runs <see cref="BoardTilesTests"/> alone. Its frame-budget guard timed the renderer while other
/// classes ran on the same 4-vCPU CI runner (process at ~1.7-2.0 cores during the timed window,
/// single frames up to 23.7 ms), so the guard failed on contention, not on the renderer.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BoardFrameBudgetCollection
{
    public const string Name = "BoardFrameBudget";
}

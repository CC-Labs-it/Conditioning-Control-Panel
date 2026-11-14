using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Tonight Board's WPF half (2026-10-07): the once-per-version download and disk cache, the
/// card, the PNG codec behind Core's <see cref="BoardPicture.PngDecoder"/> and the art view. The
/// pure rules (wire, picture, effects, raster) are pinned in CCP.Core.Tests' BoardTilesTests.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BoardServiceTests
{
    public BoardServiceTests() => BoardPng.Install(); // App startup does this in the app

    private static string Loc(string key) => "[" + key + "]";

    private static BoardPost Post(int v = 3, string[]? fx = null, DateTime? until = null, string? link = "lobby",
        BoardAudience aud = BoardAudience.Everyone, int frames = 1, int fps = 6, int w = 64, int h = 36) =>
        new(v, fx ?? Array.Empty<string>(), until, link, aud, frames, fps, w, h);

    private static BillboardContext Ctx(BillboardTier tier = BillboardTier.Free, DateTime? now = null)
    {
        var n = now ?? new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        return new BillboardContext(tier, n, n.ToLocalTime());
    }

    private static int Argb(int a, int r, int g, int b) => unchecked((a << 24) | (r << 16) | (g << 8) | b);

    private static byte[] Png(int w, int h, Func<int, int, int> argb)
    {
        var px = new int[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = argb(x, y);
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    // ---- the card ----------------------------------------------------------------------------

    [Fact]
    public void The_card_is_a_board_card_new_once_and_never_snoozable()
    {
        var pic = BoardPicture.Solid(Post(v: 9, link: "premium"), Argb(255, 255, 79, 168));
        var fresh = BoardProvider.Decide(pic, Ctx(), seenVersion: 8, Loc)!;
        Assert.Equal(BillboardCardKind.Board, fresh.Kind);
        Assert.Equal("board:9", fresh.Id);
        Assert.Equal(BillboardBadge.New, fresh.Badge);
        Assert.False(fresh.Snoozable);
        Assert.Equal("board", fresh.ArtKey);
        Assert.Same(pic, Assert.IsType<BoardArtData>(fresh.ArtData).Picture);
        Assert.Equal((BillboardActionKind.Tab, "premium"), (fresh.Action.Kind, fresh.Action.Target));

        var seen = BoardProvider.Decide(pic, Ctx(), seenVersion: 9, Loc)!;
        Assert.Equal(BillboardBadge.None, seen.Badge);
    }

    [Fact]
    public void No_card_when_nothing_is_ready_expired_or_meant_for_someone_else()
    {
        var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        Assert.Null(BoardProvider.Decide(null, Ctx(now: now), 0, Loc));
        var expired = BoardPicture.Solid(Post(until: now.AddMinutes(-1)), Argb(255, 1, 2, 3));
        Assert.Null(BoardProvider.Decide(expired, Ctx(now: now), 0, Loc));
        var patrons = BoardPicture.Solid(Post(aud: BoardAudience.Patrons), Argb(255, 1, 2, 3));
        Assert.Null(BoardProvider.Decide(patrons, Ctx(BillboardTier.Free, now), 0, Loc));
        Assert.NotNull(BoardProvider.Decide(patrons, Ctx(BillboardTier.Basic, now), 0, Loc));
    }

    // ---- the service: once per version, cached, cleared -------------------------------------

    private sealed class FakeServer
    {
        public readonly Dictionary<int, byte[]?> Files = new();
        public readonly List<int> Calls = new();
        public Task<byte[]?> Fetch(int v, CancellationToken _)
        {
            lock (Calls) Calls.Add(v);
            return Task.FromResult(Files.TryGetValue(v, out var b) ? b : null);
        }
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ccp-board-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public async Task A_version_is_fetched_once_and_kept_on_disk()
    {
        var dir = TempDir();
        var server = new FakeServer();
        server.Files[3] = Png(8, 4, (x, y) => Argb(255, 255, 0, 0));
        var svc = new BoardService(dir, server.Fetch);
        int changed = 0;
        svc.Changed += (_, _) => changed++;

        await svc.Apply(Post(v: 3, w: 8, h: 4));
        await svc.Apply(Post(v: 3, w: 8, h: 4));
        Assert.Equal(new[] { 3 }, server.Calls);
        Assert.Equal(1, changed);
        Assert.Equal(3, svc.Current!.Post.Version);
        Assert.True(File.Exists(svc.PathFor(3)));

        // A restart reads the cache: no download at all.
        var again = new BoardService(dir, server.Fetch);
        await again.Apply(Post(v: 3, w: 8, h: 4));
        Assert.Single(server.Calls);
        Assert.NotNull(again.Current);
    }

    [Fact]
    public async Task A_new_version_replaces_the_old_file_and_a_clear_drops_everything()
    {
        var dir = TempDir();
        var server = new FakeServer();
        server.Files[3] = Png(4, 4, (x, y) => Argb(255, 0, 255, 0));
        server.Files[4] = Png(4, 4, (x, y) => Argb(255, 0, 0, 255));
        var svc = new BoardService(dir, server.Fetch);
        await svc.Apply(Post(v: 3, w: 4, h: 4));
        await svc.Apply(Post(v: 4, w: 4, h: 4));
        Assert.Equal(4, svc.Current!.Post.Version);
        Assert.False(File.Exists(svc.PathFor(3)));
        Assert.True(File.Exists(svc.PathFor(4)));

        int changed = 0;
        svc.Changed += (_, _) => changed++;
        await svc.Apply(null);
        Assert.Null(svc.Current);
        Assert.Equal(1, changed);
        await Task.Delay(200); // the prune runs off the caller
        Assert.False(File.Exists(svc.PathFor(4)));
    }

    [Fact]
    public async Task A_failed_download_shows_nothing_and_is_retried_on_the_next_poll()
    {
        var dir = TempDir();
        var server = new FakeServer();
        var svc = new BoardService(dir, server.Fetch);
        await svc.Apply(Post(v: 5));
        Assert.Null(svc.Current);
        server.Files[5] = Png(64, 36, (x, y) => Argb(255, 200, 200, 200));
        await svc.Apply(Post(v: 5));
        Assert.Equal(new[] { 5, 5 }, server.Calls);
        Assert.NotNull(svc.Current);
    }

    [Fact]
    public async Task A_damaged_cache_file_is_fetched_again()
    {
        var dir = TempDir();
        var server = new FakeServer();
        server.Files[6] = Png(4, 4, (x, y) => Argb(255, 9, 9, 200));
        var svc = new BoardService(dir, server.Fetch);
        File.WriteAllBytes(svc.PathFor(6), new byte[] { 1, 2, 3 });
        await svc.Apply(Post(v: 6, w: 4, h: 4));
        Assert.Equal(new[] { 6 }, server.Calls);
        Assert.NotNull(svc.Current);
    }

    [Fact]
    public async Task Not_a_png_is_refused()
    {
        var server = new FakeServer();
        server.Files[7] = System.Text.Encoding.UTF8.GetBytes("<html>not found</html>");
        var svc = new BoardService(TempDir(), server.Fetch);
        await svc.Apply(Post(v: 7));
        Assert.Null(svc.Current);
    }

    [Fact]
    public void The_seen_marker_survives_a_restart_and_never_goes_back()
    {
        var dir = TempDir();
        var svc = new BoardService(dir, new FakeServer().Fetch);
        Assert.True(svc.IsNew(1));
        svc.MarkShown(4);
        svc.MarkShown(2);
        Assert.Equal(4, svc.SeenVersion);
        var again = new BoardService(dir, new FakeServer().Fetch);
        Assert.Equal(4, again.SeenVersion);
        Assert.False(again.IsNew(4));
        Assert.True(again.IsNew(5));
    }

    [Fact]
    public async Task The_marquee_hand_off_reads_the_board_block()
    {
        var server = new FakeServer();
        server.Files[11] = Png(4, 4, (x, y) => Argb(255, 1, 200, 1));
        var svc = new BoardService(TempDir(), server.Fetch);
        var tcs = new TaskCompletionSource();
        svc.Changed += (_, _) => tcs.TrySetResult();
        svc.OnMarquee("""{"message":"x","board":{"v":11,"w":4,"h":4,"fx":["shine"]}}""");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(11, svc.Current!.Post.Version);
        Assert.Equal(new[] { "shine" }, svc.Current.Post.Fx);
    }

    [Fact]
    public void A_real_png_decodes()
    {
        var png = Png(64, 36, (x, y) => x < 32 ? Argb(255, 255, 79, 168) : Argb(0, 0, 0, 0));
        var pic = BoardPicture.Decode(png, Post());
        Assert.NotNull(pic);
        Assert.Equal(0xFF4FA8, pic!.Rgb[0][0]);
        Assert.True(pic.Lit[0][0]);
        Assert.False(pic.Lit[0][63]);
        Assert.Null(BoardPicture.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Post()));
    }

    // ---- the view ----------------------------------------------------------------------------

    [Fact]
    public void The_art_view_registers_and_lives_through_its_lifecycle() => WpfRenderHarness.OnStaThread(() =>
    {
        BoardArtRegistration.Register();
        Assert.True(BillboardArt.IsRegistered("board"));

        var pic = BoardPicture.Solid(Post(fx: BoardFx.All.ToArray()), Argb(255, 255, 79, 168));
        int shown = 0;
        var view = BillboardArt.Create("board", new BoardArtData(pic, v => shown = v));
        var tiles = Assert.IsType<BoardTileView>(view);
        var host = new System.Windows.Controls.Border { Width = 640, Height = 360, Child = tiles };
        host.Measure(new Size(640, 360));
        host.Arrange(new Rect(0, 0, 640, 360));
        host.UpdateLayout();

        tiles.Play();
        tiles.Touch(new Point(0.5, 0.5));
        tiles.Touch(new Point(-1, 2));
        tiles.Pause();
        tiles.Release();
        tiles.Play(); // after release: ignored, no throw
        _ = shown;
    });

    [Theory]
    [InlineData(0.5, 0.5, 640, 360, 32, 18)]
    [InlineData(0.0, 0.0, 640, 360, 0, 0)]
    [InlineData(0.999, 0.999, 640, 360, 63, 35)]
    [InlineData(0.5, 0.05, 640, 480, -1, -1)] // in the letterbox above the grid
    public void A_touch_maps_through_the_letterbox(double nx, double ny, double w, double h, int tx, int ty)
    {
        bool hit = BoardTouch.TileAt(new Point(nx, ny), w, h, out var x, out var y);
        if (tx < 0) { Assert.False(hit); return; }
        Assert.True(hit);
        Assert.Equal((tx, ty), ((int)x, (int)y));
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Billboard.Board;
using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BoardCacheServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-board-" + Guid.NewGuid().ToString("N"));
    private readonly Func<byte[], (int[] Pixels, int Width, int Height)?>? _decoder = BoardPicture.PngDecoder;
    private static readonly byte[] Png = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static BoardPost Post(int v = 3) => new(v, Array.Empty<string>(), null, "lobby", BoardAudience.Everyone, 1, 6, 4, 2);

    public BoardCacheServiceTests()
    {
        Directory.CreateDirectory(_dir);
        BoardPicture.PngDecoder = _ => (new int[8], 4, 2);
    }

    public void Dispose()
    {
        BoardPicture.PngDecoder = _decoder;
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Fetches_once_then_reads_disk_after_restart_and_dispatches_changes()
    {
        int calls = 0, dispatched = 0, changed = 0;
        Task<byte[]?> Fetch(int _, System.Threading.CancellationToken ct) { calls++; return Task.FromResult<byte[]?>(Png); }
        var service = new BoardCacheService(_dir, Fetch, a => { dispatched++; a(); });
        service.Changed += (_, _) => changed++;
        await service.Apply(Post());
        await service.Apply(Post());
        await new BoardCacheService(_dir, Fetch).Apply(Post());
        Assert.Equal(1, calls);
        Assert.Equal(1, changed);
        Assert.Equal(1, dispatched);
        Assert.Equal(3, service.Current!.Post.Version);
        Assert.Equal(Png, File.ReadAllBytes(service.PathFor(3)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Damaged_cache_is_refetched_in_the_same_apply(bool invalidPixels)
    {
        int calls = 0;
        var service = new BoardCacheService(_dir, (_, _) => { calls++; return Task.FromResult<byte[]?>(Png); });
        if (invalidPixels)
        {
            var bad = (byte[])Png.Clone();
            Array.Resize(ref bad, 9);
            File.WriteAllBytes(service.PathFor(3), bad);
            BoardPicture.PngDecoder = bytes => bytes.Length == 9 ? null : (new int[8], 4, 2);
        }
        else File.WriteAllBytes(service.PathFor(3), new byte[] { 1, 2, 3 });
        await service.Apply(Post());
        Assert.Equal(1, calls);
        Assert.NotNull(service.Current);
        Assert.Equal(Png, File.ReadAllBytes(service.PathFor(3)));
    }

    [Fact]
    public async Task Failure_retries_and_an_obsolete_download_cannot_restore_a_cleared_post()
    {
        var pending = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var service = new BoardCacheService(_dir, (_, _) => ++calls == 1 ? Task.FromResult<byte[]?>(null) : pending.Task);
        await service.Apply(Post());
        Assert.Null(service.Current);
        var loading = service.Apply(Post());
        await service.Apply(null);
        pending.SetResult(Png);
        await loading;
        Assert.Equal(2, calls);
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task Replacement_prunes_old_picture_and_invalid_download_drops_current()
    {
        byte[]? bytes = Png;
        var service = new BoardCacheService(_dir, (_, _) => Task.FromResult<byte[]?>(bytes));
        await service.Apply(Post(3));
        await service.Apply(Post(4));
        Assert.False(File.Exists(service.PathFor(3)));
        Assert.Equal(4, service.Current!.Post.Version);
        bytes = new byte[BoardCacheService.MaxPngBytes + 1];
        await service.Apply(Post(5));
        Assert.Null(service.Current);
        bytes = Png;
        await service.Apply(Post(5));
        Assert.NotNull(service.Current);
    }

    [Fact]
    public async Task Expiry_notifies_the_deck_through_the_supplied_dispatcher()
    {
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int changes = 0;
        var service = new BoardCacheService(_dir, (_, _) => Task.FromResult<byte[]?>(Png));
        service.Changed += (_, _) => { if (System.Threading.Interlocked.Increment(ref changes) == 2) expired.TrySetResult(); };
        await service.Apply(Post() with { UntilUtc = DateTime.UtcNow.AddSeconds(-2) });
        await expired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Seen_version_persists_and_never_moves_backwards()
    {
        var service = new BoardCacheService(_dir, (_, _) => Task.FromResult<byte[]?>(null));
        Assert.True(service.IsNew(3));
        service.MarkShown(3);
        service.MarkShown(2);
        var restarted = new BoardCacheService(_dir, (_, _) => Task.FromResult<byte[]?>(null));
        Assert.Equal(3, restarted.SeenVersion);
        Assert.False(restarted.IsNew(3));
        Assert.True(restarted.IsNew(4));
    }
}

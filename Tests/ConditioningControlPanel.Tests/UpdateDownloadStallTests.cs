using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The installer download must not wait forever on a connection that went quiet (ccp-bugs #1413
/// and a support post: an update sat on its window for four hours).
///
/// HttpClient.Timeout stops counting once the response headers arrive, and the body was read with
/// no timeout and no cancel button while the main window was hidden. The copy now fails an attempt
/// when one read gets nothing for the stall window; the retry loop treats that as transient and
/// the third failure reaches the "update failed, get it by hand" dialog.
/// </summary>
public class UpdateDownloadStallTests
{
    /// <summary>Hands out <c>good</c> bytes, then never answers again (until cancelled).</summary>
    private sealed class StallingStream : Stream
    {
        private int _left;
        public StallingStream(int good) => _left = good;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_left > 0)
            {
                var n = Math.Min(_left, buffer.Length);
                buffer.Span.Slice(0, n).Fill(7);
                _left -= n;
                return n;
            }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AQuietConnectionFailsTheAttemptInsteadOfWaitingForever()
    {
        using var source = new StallingStream(good: 1000);
        using var target = new MemoryStream();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            UpdateService.CopyWithStallTimeoutAsync(source, target, TimeSpan.FromMilliseconds(150), null,
                TestContext.Current.CancellationToken));

        Assert.Contains("stalled", ex.Message);
        Assert.Equal(1000, target.Length);   // what arrived before the stall was written
    }

    [Fact]
    public void AStallIsRetriedLikeAnyOtherNetworkHiccup()
    {
        Assert.True(UpdateService.IsTransientNetworkError(new TimeoutException("The download stalled")));
    }

    [Fact]
    public async Task TheCallersCancelStaysACancel()
    {
        using var source = new StallingStream(good: 0);
        using var target = new MemoryStream();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateService.CopyWithStallTimeoutAsync(source, target, TimeSpan.FromSeconds(30), null, cts.Token));
    }

    [Fact]
    public async Task AMovingDownloadCopiesEverythingAndReportsProgress()
    {
        var data = new byte[300_000];
        new Random(5).NextBytes(data);
        using var source = new MemoryStream(data);
        using var target = new MemoryStream();
        long last = 0;

        var copied = await UpdateService.CopyWithStallTimeoutAsync(source, target, TimeSpan.FromSeconds(30),
            n => last = n, TestContext.Current.CancellationToken);

        Assert.Equal(data.Length, copied);
        Assert.Equal(data.Length, last);
        Assert.Equal(data, target.ToArray());
    }

    [Fact]
    public void TheStallWindowIsLongEnoughForASlowLineAndShortEnoughToNotice()
    {
        Assert.InRange(UpdateService.DownloadStallTimeout, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3));
    }
}

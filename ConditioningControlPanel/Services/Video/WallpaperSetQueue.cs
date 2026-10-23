using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Runs desktop wallpaper writes one at a time on a worker, never on the caller (ccp-bugs
    /// #1453). <c>SystemParametersInfo(SPI_SETDESKWALLPAPER)</c> decodes and transcodes the image,
    /// and with <c>SPIF_SENDCHANGE</c> it also broadcasts to every top-level window and waits for
    /// each one, so a Takeover shuffle on the UI thread froze the app for seconds, or for good
    /// when a game window was busy. Order is kept (a restore queued after a shuffle lands last),
    /// a throwing write counts as a failure, and <see cref="Flush"/> waits a bounded time.
    /// </summary>
    internal sealed class WallpaperSetQueue
    {
        private readonly Func<string, bool> _apply;
        private readonly object _gate = new();
        private Task _tail = Task.CompletedTask;
        private int _pending;

        public WallpaperSetQueue(Func<string, bool> apply)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        /// <summary>True while a write is queued or running.</summary>
        public bool IsBusy => Volatile.Read(ref _pending) > 0;

        /// <summary>Queue a write and return at once. The task says whether Windows took it.</summary>
        public Task<bool> Enqueue(string path)
        {
            lock (_gate)
            {
                Interlocked.Increment(ref _pending);
                var next = _tail.ContinueWith(_ =>
                {
                    try { return _apply(path); }
                    catch { return false; }
                    finally { Interlocked.Decrement(ref _pending); }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                _tail = next;
                return next;
            }
        }

        /// <summary>Wait up to <paramref name="timeout"/> for every queued write. False on timeout.</summary>
        public bool Flush(TimeSpan timeout)
        {
            Task tail;
            lock (_gate) tail = _tail;
            try { return tail.Wait(timeout); }
            catch { return true; }
        }
    }
}

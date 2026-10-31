using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>Head-neutral BoardService cache. Fetch and UI dispatch come from the head.</summary>
    public sealed class BoardCacheService
    {
        public const int MaxPngBytes = 256 * 1024;
        private readonly string _dir;
        private readonly Func<int, CancellationToken, Task<byte[]?>> _fetch;
        private readonly Action<Action> _post;
        private readonly object _gate = new();
        private BoardPicture? _current;
        private int _loadingVersion;
        private Task _loading = Task.CompletedTask;
        private int _generation;
        private int _seenVersion = -1;
        private CancellationTokenSource? _expiryCts;
        /// <param name="cacheDir">Where pictures and the seen marker live.</param>
        /// <param name="fetch">Downloads version v's PNG, or null on any failure.</param>
        /// <param name="post">Runs Changed handlers (the UI thread in the app, inline in tests).</param>
        public BoardCacheService(string cacheDir, Func<int, CancellationToken, Task<byte[]?>> fetch, Action<Action>? post = null)
        {
            _dir = cacheDir;
            _fetch = fetch;
            _post = post ?? (a => a());
        }
        /// <summary>The post whose picture is ready, or null. Includes its <see cref="BoardPicture.Post"/>.</summary>
        public BoardPicture? Current { get { lock (_gate) return _current; } }
        /// <summary>The board changed: a new picture is ready, it was cleared, or it just expired.</summary>
        public event EventHandler? Changed;
        /// <summary>Hands over a raw <c>GET /config/marquee</c> body. Never throws.</summary>
        public void OnMarquee(string? marqueeJson)
        {
            try
            {
                _ = Apply(BoardWire.Parse(marqueeJson));
            }
            catch (Exception ex) { Log.Warning("Board: marquee hand-off failed: {Error}", ex.Message); }
        }
        /// <summary>Applies a parsed post. Returns when its picture is loaded (or failed). Test seam.</summary>
        public Task Apply(BoardPost? post)
        {
            lock (_gate)
            {
                if (post == null)
                {
                    _generation++;
                    _loadingVersion = 0;
                    if (_current == null) return Task.CompletedTask;
                    _current = null;
                    CancelExpiry();
                    _ = Task.Run(() => Prune(keepVersion: 0));
                    RaiseChanged();
                    return Task.CompletedTask;
                }
                if (_current != null && _current.Post.Version == post.Version) return Task.CompletedTask;
                if (_loadingVersion == post.Version) return _loading;
                int gen = ++_generation;
                _loadingVersion = post.Version;
                _loading = LoadAsync(post, gen);
                return _loading;
            }
        }
        private async Task LoadAsync(BoardPost post, int gen)
        {
            BoardPicture? picture = null;
            try
            {
                var path = PathFor(post.Version);
                byte[]? bytes = null;
                if (File.Exists(path))
                {
                    try { bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false); }
                    catch (IOException) { bytes = null; }
                    if (bytes != null && bytes.Length > 0 && bytes.Length <= MaxPngBytes)
                        picture = await Task.Run(() => BoardPicture.Decode(bytes, post)).ConfigureAwait(false);
                    if (picture == null) TryDelete(path); // a damaged cache file: fetch it again
                }
                if (picture == null)
                {
                    bytes = await _fetch(post.Version, CancellationToken.None).ConfigureAwait(false);
                    if (bytes != null && bytes.Length > 0 && bytes.Length <= MaxPngBytes)
                        picture = await Task.Run(() => BoardPicture.Decode(bytes, post)).ConfigureAwait(false);
                    if (picture != null) Save(path, bytes!);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Board: loading post {Version} failed: {Error}", post.Version, ex.Message);
            }
            lock (_gate)
            {
                if (gen != _generation) return; // a newer post or a clear arrived meanwhile
                _loadingVersion = 0;
                if (picture == null)
                {
                    // The server has moved on from whatever we showed; show nothing and retry next poll.
                    if (_current == null) return;
                    _current = null;
                    CancelExpiry();
                    RaiseChanged();
                    return;
                }
                _current = picture;
                ArmExpiry(post);
                RaiseChanged();
            }
            Prune(keepVersion: post.Version);
        }
        /// <summary>True until a post of this version has been on screen once.</summary>
        public bool IsNew(int version) => version > SeenVersion;
        /// <summary>The newest version that has been on screen (persisted beside the cache).</summary>
        public int SeenVersion
        {
            get
            {
                lock (_gate)
                {
                    if (_seenVersion < 0) _seenVersion = ReadSeen();
                    return _seenVersion;
                }
            }
        }
        /// <summary>The board card for <paramref name="version"/> reached the screen.</summary>
        public void MarkShown(int version)
        {
            lock (_gate)
            {
                if (_seenVersion < 0) _seenVersion = ReadSeen();
                if (version <= _seenVersion) return;
                _seenVersion = version;
            }
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(Path.Combine(_dir, "seen.txt"), version.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Log.Debug("Board: could not save the seen marker: {Error}", ex.Message); }
        }
        private int ReadSeen()
        {
            try
            {
                var p = Path.Combine(_dir, "seen.txt");
                if (File.Exists(p) && int.TryParse(File.ReadAllText(p).Trim(), out var v)) return v;
            }
            catch (Exception) { }
            return 0;
        }
        public string PathFor(int version) => Path.Combine(_dir, $"board-{version}.png");
        private void Save(string path, byte[] bytes)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex) { Log.Debug("Board: could not cache the picture: {Error}", ex.Message); }
        }
        /// <summary>Deletes every cached picture except <paramref name="keepVersion"/>.</summary>
        private void Prune(int keepVersion)
        {
            try
            {
                if (!Directory.Exists(_dir)) return;
                var keep = keepVersion > 0 ? Path.GetFileName(PathFor(keepVersion)) : null;
                foreach (var f in Directory.GetFiles(_dir, "board-*.png*"))
                    if (!string.Equals(Path.GetFileName(f), keep, StringComparison.OrdinalIgnoreCase)) TryDelete(f);
            }
            catch (Exception) { }
        }
        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (Exception) { }
        }
        private void ArmExpiry(BoardPost post)
        {
            CancelExpiry();
            if (post.UntilUtc is not { } until) return;
            var wait = until - DateTime.UtcNow;
            if (wait > TimeSpan.FromDays(24)) return; // Task.Delay's ceiling; the next poll re-arms it
            var cts = new CancellationTokenSource();
            _expiryCts = cts;
            var delay = wait < TimeSpan.Zero ? TimeSpan.Zero : wait + TimeSpan.FromSeconds(1);
            _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                RaiseChanged();
            }, TaskScheduler.Default);
        }
        private void CancelExpiry()
        {
            _expiryCts?.Cancel();
            _expiryCts = null;
        }
        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;
            _post(() =>
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception ex) { Log.Warning("Board: a Changed handler threw: {Error}", ex.Message); }
            });
        }
    }
}

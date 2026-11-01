using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Flash;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// One surface's content-pack half of a media pool (WPF FlashService <c>_packImageList</c> + PackBag +
    /// <c>_tempPackFiles</c> :173-191; VideoService <c>_packVideoQueue</c> + <c>_tempPackFiles</c> :44/:56;
    /// BubbleCountService <c>_packVideos</c> :33): the active pack entries of one type, dealt from a
    /// <see cref="ShuffleBag{T}"/> keyed on <see cref="ContentPackStore.SourceKey"/> (never a temp path).
    ///
    /// <para>The entry list is re-read from <c>ContentPackStore.Current</c> on every draw (an in-memory
    /// walk of the loaded manifests), so an activation, an uninstall or a per-file opt-out shows on the
    /// next pick, the way the flash overlay re-lists its folder per burst. Decrypts stay on record and
    /// are deleted past <see cref="TempCap"/> and at <see cref="CleanupTemps"/>. Thread-safe.</para>
    /// </summary>
    public sealed class PackMediaPool
    {
        /// <summary>WPF FlashService.cs:3504: <c>if (_tempPackFiles.Count &gt; 50) CleanupTempPackFiles()</c>.</summary>
        public const int TempCap = 50;

        private readonly string _type;
        private readonly Func<ContentPackStore?> _store;
        private readonly Random _rng;
        private readonly object _lock = new();
        private readonly ShuffleBag<(string PackId, PackFileEntry File)> _bag;
        private readonly MediaTempFileLedger _temps;

        /// <param name="type"><see cref="ContentPackStore.ImageType"/> or <see cref="ContentPackStore.VideoType"/>.</param>
        public PackMediaPool(string type, Random? rng = null, Func<ContentPackStore?>? store = null)
        {
            _store = store ?? (() => ContentPackStore.Current);
            _temps = new MediaTempFileLedger(TempCap, path =>
            {
                var owner = _store();
                if (owner != null) owner.DeleteTempFile(path);
                else return MediaTempFileLedger.TryDeleteFile(path);
                return !System.IO.File.Exists(path);
            });
            _type = type;
            _rng = rng ?? new Random();
            _bag = new ShuffleBag<(string PackId, PackFileEntry File)>(e => ContentPackStore.SourceKey(e.PackId, e.File), _rng);
        }

        /// <summary>Decrypts on record (tests, diagnostics).</summary>
        public int TempCount => _temps.LiveCount + _temps.HeldCount;

        /// <summary>
        /// WPF GetNextImages (FlashService.cs:3555-3563), GetNextVideo (VideoService.cs:8017-8026) and
        /// BubbleCountService.cs:684-693: with local and pack entries both present a pack entry wins in
        /// proportion to its share; with packs only, always; with no packs, never.
        /// </summary>
        public static bool ShouldDrawPack(int localCount, int packCount, Random rng)
        {
            if (packCount <= 0) return false;
            if (localCount <= 0) return true;
            return rng.Next(localCount + packCount) >= localCount;
        }

        /// <summary>
        /// The whole WPF pack branch in one call: roll <see cref="ShouldDrawPack"/> against
        /// <paramref name="localCount"/>, take the next entry of the shuffled walk and decrypt it. Null
        /// when the roll picks local, no active pack holds this type, or the decrypt failed - the caller
        /// then falls back to its local pick, as WPF does. <paramref name="remaining"/>: weigh the pack
        /// entries still to come this cycle, for callers whose <paramref name="localCount"/> is what is
        /// left in a dealt queue (WPF VideoService.cs:8017 / BubbleCountService.cs:684 weigh queue
        /// lengths); flash weighs whole lists (FlashService.cs:3555).
        /// </summary>
        public string? TryDraw(int localCount, bool remaining = false)
        {
            List<(string PackId, PackFileEntry File)> list;
            (string PackId, PackFileEntry File) entry;
            lock (_lock)
            {
                list = Entries();
                var packCount = remaining && list.Count > 0 && _bag.Remaining > 0 ? Math.Min(_bag.Remaining, list.Count) : list.Count;
                if (!ShouldDrawPack(localCount, packCount, _rng) || !_bag.TryNext(list, out entry)) return null;
            }
            return Decrypt(entry);
        }

        /// <summary>
        /// WPF GetPackFileTempPath + <c>_tempPackFiles.Add</c>: decrypt to a FRESH temp file and keep it on
        /// record. Past <see cref="TempCap"/> the oldest are swept; refused deletes stay on record.
        /// </summary>
        private string? Decrypt((string PackId, PackFileEntry File) entry)
        {
            var store = _store();
            if (store == null || entry.File == null) return null;
            var temp = store.GetPackFileTempPath(entry.PackId, entry.File);
            if (string.IsNullOrEmpty(temp))
            {
                // WPF VideoService.cs:8044 logs at Information (#1135): a pool that LOOKS full handed back nothing.
                Log.Information("ContentPacks: '{Name}' from pack {PackId} could not be decrypted", entry.File.OriginalName, entry.PackId);
                return null;
            }
            _temps.Track(temp);
            return temp;
        }

        /// <summary>WPF CleanupTempPackFiles: delete every decrypt this pool has on record.</summary>
        public void CleanupTemps() => _temps.SweepAll();

        // Caller holds _lock.
        private List<(string PackId, PackFileEntry File)> Entries()
        {
            try
            {
                var store = _store();
                if (store == null) return new List<(string, PackFileEntry)>();
                return _type == ContentPackStore.VideoType ? store.GetAllActivePackVideos() : store.GetAllActivePackImages();
            }
            catch (Exception ex)
            {
                Log.Debug("ContentPacks: pool read failed: {E}", ex.Message);
                return new List<(string, PackFileEntry)>();
            }
        }
    }
}

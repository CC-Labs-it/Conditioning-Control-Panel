using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Studio > Wallpaper: a collage composed from the user's own images (or one picture they picked),
    /// up while a session runs, or the whole time CCP is open with "Run with CCP". Panic always takes it
    /// down (App.KillAllAudio); a plain Stop leaves it only under "Run with CCP". Independent of the
    /// Takeover shuffle in the other half of this class, but it shares the write queue and the #692
    /// breadcrumb, so a crash still puts the user's own wallpaper back on the next launch.
    /// </summary>
    public partial class WallpaperService
    {
        private System.Windows.Threading.DispatcherTimer? _collageTimer;
        private bool _collageRunning;
        private string? _collageCurrentFile;
        private int _collageComposing;   // 0/1 via Interlocked: one compose at a time
        private string? _lastCollagePack; // avoid picking the same theme folder twice in a row
        private bool _ownImageRunning;    // Own image mode: the user's picture is up (used in place, never deleted)

        /// <summary>Stored as the "original" when the desktop is a solid colour: restoring clears the
        /// wallpaper rather than setting a file.</summary>
        internal const string SolidColorMarker = "solid-colour";

        /// <summary>The frozen copy of the user's wallpaper, kept only until the real one is back.</summary>
        internal const string FrozenOriginalName = "ccp_original_wallpaper.jpg";

        /// <summary>Clamped recompose interval in seconds (10 s to 24 h). Shared with Dynamic mode.</summary>
        internal static int CollageInterval()
            => Math.Clamp(App.Settings?.Current?.CollageWallpaperIntervalSeconds ?? 60, 10, 86400);

        /// <summary>Whether the collage cycles on the timer (vs composing one and holding it). Shared with
        /// Dynamic mode.</summary>
        internal static bool RotateOn() => App.Settings?.Current?.CollageWallpaperRotate ?? true;

        // ---------------------------------------------------------------- what to restore

        private static string FrozenOriginalPath() => Path.Combine(App.GetMediaTempPath(), FrozenOriginalName);

        /// <summary>What to hand the write queue to put <paramref name="original"/> back: "" clears the
        /// wallpaper for a solid colour, then the user's own file while it exists, then the frozen copy.
        /// The own file comes first on purpose: pointing Windows at a copy in our temp folder would leave
        /// the desktop hanging off CCP's assets folder for good.</summary>
        internal static string RestoreTarget(string original, Func<string, bool> exists, string frozenCopy)
        {
            if (original == SolidColorMarker) return "";
            if (exists(original)) return original;
            return exists(frozenCopy) ? frozenCopy : original;
        }

        /// <summary>Can <paramref name="original"/> be put back at all? Never hand Windows an empty or
        /// missing path unless it is the solid-colour clear (#692).</summary>
        internal static bool IsRestorable(string? original, Func<string, bool> exists, string frozenCopy)
            => original == SolidColorMarker
               || (!string.IsNullOrWhiteSpace(original) && (exists(original) || exists(frozenCopy)));

        private static string RestoreTarget(string original) => RestoreTarget(original, File.Exists, FrozenOriginalPath());

        private static bool IsRestorable(string? original) => IsRestorable(original, File.Exists, FrozenOriginalPath());

        /// <summary>The real wallpaper is back: the frozen copy has done its job. Kept when Windows is
        /// pointing at it (the source was gone), since Windows re-reads that path on logon.</summary>
        private static void DropFrozenOriginal(string restoredTo)
        {
            try
            {
                var frozen = FrozenOriginalPath();
                if (!string.Equals(restoredTo, frozen, StringComparison.OrdinalIgnoreCase) && File.Exists(frozen))
                    File.Delete(frozen);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "drop frozen wallpaper"); }
        }

        /// <summary>HKCU\Control Panel\Desktop WallpaperStyle as a word: Center, Tile, Stretch, Fit, Fill,
        /// Span, or the raw value. Read only, for the log; CCP never changes the user's fit.</summary>
        internal static string ReadWallpaperStyle()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var style = key?.GetValue("WallpaperStyle")?.ToString() ?? "";
                var tile = key?.GetValue("TileWallpaper")?.ToString() ?? "";
                return WallpaperStyleName(style, tile);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "wallpaper style"); return "unknown"; }
        }

        internal static string WallpaperStyleName(string style, string tile) => style.Trim() switch
        {
            "0" => tile.Trim() == "1" ? "Tile" : "Center",
            "2" => "Stretch",
            "6" => "Fit",
            "10" => "Fill",
            "22" => "Span",
            "" => "unknown",
            var other => "style " + other,
        };

        /// <summary>BackgroundType: 0 picture, 1 solid colour, 2 slideshow, 3 Spotlight. -1 unknown.</summary>
        private static int ReadBackgroundType()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers");
                if (key?.GetValue("BackgroundType") is int bt) return bt;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "wallpaper background type"); }
            return -1;
        }

        /// <summary>True when one wallpaper write would destroy what the desktop has: a slideshow,
        /// Spotlight, or a different picture per monitor. SPI can only put ONE file back, so Static and Own
        /// image stay off there; a skipped effect is far cheaper than a desktop the user can never get
        /// back (#692). Dynamic never writes, so it is fine on all of them.</summary>
        public static bool WriteWouldLoseDesktop() => WouldLoseDesktop(ReadBackgroundType(), MonitorWallpapers());

        internal static bool WouldLoseDesktop(int backgroundType, IReadOnlyList<string> monitorWallpapers)
        {
            if (backgroundType >= 2) return true;
            for (int i = 1; i < monitorWallpapers.Count; i++)
                if (!string.Equals(monitorWallpapers[0], monitorWallpapers[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static List<string> MonitorWallpapers()
        {
            var paths = new List<string>();
            try
            {
                var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
                try
                {
                    uint count = dw.GetMonitorDevicePathCount();
                    for (uint i = 0; i < count; i++)
                    {
                        try { paths.Add(dw.GetWallpaper(dw.GetMonitorDevicePathAt(i)) ?? ""); }
                        catch (COMException) { }   // a monitor that just went away
                    }
                }
                finally { Marshal.ReleaseComObject(dw); }
            }
            catch (Exception ex) { App.Logger?.Debug("[Collage] IDesktopWallpaper query failed: {Error}", ex.Message); }
            return paths;
        }

        /// <summary>The path to remember for the restore, with a frozen copy beside it in case the source
        /// goes away during the session. Null when nothing restorable exists.</summary>
        private static string? CaptureOriginalWallpaper()
        {
            // Solid colour first: there is no image, and probing the sources below would grab a stale one.
            if (ReadBackgroundType() == 1)
            {
                App.Logger?.Information("[Collage] Original desktop is a solid colour - will clear the wallpaper on stop");
                return SolidColorMarker;
            }

            var path = GetLiveWallpaperPath(out var source);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                App.Logger?.Warning("[Collage] Could not find a restorable current wallpaper - not taking over");
                return null;
            }

            // Windows' own transcoded cache has no stable path: our collage overwrites that very file.
            bool mustUseCopy = source == "transcoded-cache";
            try
            {
                var frozen = FrozenOriginalPath();
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(frozen), StringComparison.OrdinalIgnoreCase))
                    File.Copy(path, frozen, overwrite: true);
                App.Logger?.Information("[Collage] Captured original wallpaper ({Source}): {Path}", source, path);
                return mustUseCopy ? frozen : path;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Collage] Could not snapshot original wallpaper: {Error}", ex.Message);
                return mustUseCopy ? null : path;
            }
        }

        /// <summary>The wallpaper on the desktop RIGHT NOW: IDesktopWallpaper, then the registry, then the
        /// path Windows records of what it shows, then its transcoded cache (last resort).</summary>
        private static string? GetLiveWallpaperPath(out string source)
        {
            source = "none";
            var monitors = MonitorWallpapers();
            if (monitors.Count > 0 && IsForeignImage(monitors[0])) { source = "IDesktopWallpaper"; return monitors[0]; }

            var reg = ReadCurrentWallpaper();
            if (IsForeignImage(reg)) { source = "registry"; return reg; }

            var cached = ReadTranscodedImageCachePath();
            if (cached != null) { source = "image-cache"; return cached; }

            try
            {
                var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
                if (File.Exists(cache)) { source = "transcoded-cache"; return cache; }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "transcoded wallpaper probe"); }
            return null;
        }

        /// <summary>HKCU\Control Panel\Desktop\TranscodedImageCache: a blob whose tail is the UTF-16 path of
        /// the source Windows is showing. A real image file, so no desktop icons can get baked in.</summary>
        private static string? ReadTranscodedImageCachePath()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                if (key?.GetValue("TranscodedImageCache") is byte[] data && data.Length > 24)
                {
                    var s = System.Text.Encoding.Unicode.GetString(data, 24, data.Length - 24);
                    int z = s.IndexOf('\0');
                    if (z >= 0) s = s.Substring(0, z);
                    s = s.Trim();
                    if (IsForeignImage(s)) return s;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Collage] TranscodedImageCache read failed: {Error}", ex.Message); }
            return null;
        }

        /// <summary>A real image we did NOT create. Our own collage and frozen copies must never be captured
        /// back as "the original", or a stop-then-start loop keeps restoring our own stale output.</summary>
        private static bool IsForeignImage(string? p)
        {
            if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) return false;
            var name = Path.GetFileName(p);
            return !name.StartsWith("ccp_collage_", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("ccp_original_wallpaper", StringComparison.OrdinalIgnoreCase);
        }

        // IDesktopWallpaper: only the first four vtable slots are declared, in order, since only reads are used.
        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID,
                              [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
        }

        [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperClass { }

        // ---------------------------------------------------------------- run / stop

        /// <summary>
        /// Begin the wallpaper in the selected mode. No-op unless it is switched on. Captures the real
        /// wallpaper first (the #692 path). UI thread; the compose itself runs off-thread.
        /// </summary>
        public void StartCollage()
        {
            var s = App.Settings?.Current;
            if (s == null || !s.CollageWallpaperEnabled) return;

            // Dynamic mode: the moving collage owns the desktop instead, and it never writes a wallpaper.
            if (DynamicWallpaperService.ShouldRun(s))
            {
                App.DynamicWallpaper?.Start();
                return;
            }

            // Own image: one picture, held. Moved or deleted since it was picked: leave the desktop alone.
            string? ownImage = null;
            if (s.CollageMode == Models.WallpaperMode.OwnImage)
            {
                ownImage = s.CollageOwnImagePath;
                if (string.IsNullOrWhiteSpace(ownImage) || !File.Exists(ownImage))
                {
                    App.Logger?.Warning("[Collage] Own image mode, but the picked image is missing - desktop left alone");
                    return;
                }
            }

            // A write still in flight (a restore after a crash, or a mode switch) means the desktop may not
            // show the user's own wallpaper yet: capturing now would save the collage as "the original"
            // (#692). Wait a bounded moment, the same 3 s as Dispose; still busy: skip this start.
            if (!_isActive && _setQueue.IsBusy && !_setQueue.Flush(TimeSpan.FromSeconds(3)))
            {
                App.Logger?.Warning("[Collage] A wallpaper write is still running - not taking over this time");
                return;
            }

            lock (_lock)
            {
                if (_collageRunning) return;

                if (!_isActive)
                {
                    if (WriteWouldLoseDesktop())
                    {
                        App.Logger?.Warning("[Collage] Slideshow, Spotlight or per-monitor wallpapers: not writing over them");
                        return;
                    }
                    var original = CaptureOriginalWallpaper();
                    if (original == null) return;
                    PersistOriginal(original);
                    _originalWallpaperPath = original;
                    _isActive = true;
                }
                _collageRunning = true;
                _ownImageRunning = ownImage != null;
            }

            if (ownImage != null)
            {
                // Used where it is: never copied, and never held as _collageCurrentFile (Stop deletes that).
                SetWallpaper(ownImage, "own image");
                App.Logger?.Information("[Collage] Own image up");
                return;
            }

            // Recolour at once when the active mod changes.
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;

            // Static rides the user's own Windows fit (Fill/Fit/Stretch/Span): CCP never writes it. Logged so a
            // "stretched on my vertical monitor" report (#1544) shows whether that fit was Stretch.
            App.Logger?.Information("[Collage] Started (rotate={Rotate}, every {Sec}s, desktop fit {Style})",
                RotateOn(), CollageInterval(), ReadWallpaperStyle());
            TriggerCollageCompose();   // the first one immediately
            RefreshCollageSchedule();  // the rotation timer only if rotation is on
        }

        /// <summary>Stop composing and restore the user's original wallpaper. Safe anytime; never waits, so
        /// a panic is not held up.</summary>
        public void StopCollage()
        {
            App.DynamicWallpaper?.Stop();

            bool wasRunning;
            lock (_lock)
            {
                wasRunning = _collageRunning;
                _collageRunning = false;
                _ownImageRunning = false;
            }
            if (!wasRunning) return;

            try { _collageTimer?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex, "collage timer stop"); }
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
            try { Deactivate(); }
            catch (Exception ex) { App.Logger?.Warning("[Collage] Restore on stop failed: {Error}", ex.Message); }

            string? held;
            lock (_lock) { held = _collageCurrentFile; _collageCurrentFile = null; }
            TryDeleteCollageFile(held);
            App.Logger?.Information("[Collage] Stopped; original wallpaper restored");
        }

        /// <summary>Exit and Windows logoff: take everything down and wait for the restore to land.</summary>
        public void StopAndFlush(TimeSpan timeout)
        {
            StopCollage();
            Deactivate();   // a Takeover wallpaper is up without the collage: same rule at logoff
            App.DynamicWallpaper?.WaitStopped(timeout);
            _setQueue.Flush(timeout);
        }

        /// <summary>Arm or disarm the rotation timer to match the Rotate + interval settings, live.</summary>
        public void RefreshCollageSchedule()
        {
            App.DynamicWallpaper?.RefreshSchedule();
            if (!_collageRunning) return;
            if (_ownImageRunning) { _collageTimer?.Stop(); return; }
            if (_collageTimer == null)
            {
                _collageTimer = new System.Windows.Threading.DispatcherTimer();
                _collageTimer.Tick += (_, _) => TriggerCollageCompose();
            }
            if (RotateOn())
            {
                _collageTimer.Interval = TimeSpan.FromSeconds(CollageInterval());
                _collageTimer.Start();
            }
            else
            {
                _collageTimer.Stop();
            }
        }

        private void OnModChanged(object? sender, ConditioningControlPanel.Models.ModPackage e)
        {
            if (_collageRunning) TriggerCollageCompose();
        }

        private void TriggerCollageCompose()
        {
            if (!_collageRunning || _ownImageRunning) return;
            if (Interlocked.CompareExchange(ref _collageComposing, 1, 0) != 0) return;
            CollageSources.Selection selection;
            try { selection = CollageSources.Snapshot(); }   // copy the selection sets on this thread
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _collageComposing, 0);
                App.Logger?.Warning("[Collage] Could not read the asset selection: {Error}", ex.Message);
                return;
            }
            _ = Task.Run(() =>
            {
                try { ComposeAndApplyCollage(selection); }
                catch (Exception ex) { App.Logger?.Warning("[Collage] Compose tick failed: {Error}", ex.Message); }
                finally { Interlocked.Exchange(ref _collageComposing, 0); }
            });
        }

        /// <summary>Should the wallpaper be up? Switched on, and either the engine runs or "Run with CCP"
        /// keeps it up while CCP is open. The engine state is passed in because the start/stop paths call
        /// this before App.IsEngineRunning flips.</summary>
        public static bool ShouldBeUp(Models.AppSettings? s, bool engineRunning) =>
            s != null && s.CollageWallpaperEnabled && (engineRunning || s.CollageRunWithCcp);

        /// <summary>Bring it up or take it down to match <see cref="ShouldBeUp"/> right now. UI thread.</summary>
        public void ApplyRunPolicy()
        {
            if (ShouldBeUp(App.Settings?.Current, App.IsEngineRunning)) StartCollage();
            else StopCollage();
        }

        /// <summary>Re-apply in whatever mode is now selected: a mode switch, a performance-mode hand-over,
        /// Dynamic's Static fallback.</summary>
        public void RestartCollage()
        {
            StopCollage();
            if (ShouldBeUp(App.Settings?.Current, App.IsEngineRunning)) StartCollage();
        }

        /// <summary>A new own image was picked while the wallpaper is up: show it straight away.</summary>
        public void ApplyOwnImageNow()
        {
            var s = App.Settings?.Current;
            var path = s?.CollageOwnImagePath;
            if (s == null || !s.CollageWallpaperEnabled || s.CollageMode != Models.WallpaperMode.OwnImage ||
                string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            bool up;
            lock (_lock) up = _ownImageRunning;
            if (!up) { StartCollage(); return; }
            SetWallpaper(path, "own image");
            App.Logger?.Information("[Collage] Own image changed");
        }

        private static readonly CollageLayout[] AllCollageLayouts =
            { CollageLayout.SideHero, CollageLayout.FillerOnly, CollageLayout.CenterHero };

        /// <summary>Which layout to render this compose: a random roll across all three.</summary>
        internal CollageLayout PickCollageLayout() => AllCollageLayouts[_random.Next(AllCollageLayouts.Length)];

        /// <summary>Dynamic's desktop repaint on stop. Through the queue, so it can never land in the middle
        /// of another wallpaper write.</summary>
        internal void QueueDesktopRepaint(string path) => _setQueue.Enqueue(path);

        private void ComposeAndApplyCollage(CollageSources.Selection selection)
        {
            if (!_collageRunning) return;

            // Only the images the user has SELECTED under assets/images. Deliberately NOT
            // WallpaperSourceFolder: that belongs to the Takeover shuffle.
            var active = CollageSources.ActiveImages(selection);
            if (active.Count < 2)
            {
                App.Logger?.Warning("[Collage] Only {N} selected image(s) - need at least 2; desktop left alone", active.Count);
                return;
            }
            var pack = CollageSources.PickPack(active, selection.ImagesRoot, ref _lastCollagePack, _random);

            string outDir;
            try { outDir = App.GetMediaTempPath(); }
            catch { outDir = Path.Combine(App.EffectiveAssetsPath, ".temp"); }

            var accent = App.Mods?.GetAccentColorHex() ?? "#E84393";
            var background = App.Mods?.GetBackgroundColorHex() ?? "#08080C";
            var layout = PickCollageLayout();

            var newFile = CollageComposer.Compose(pack, outDir, accent, background, layout);
            // A theme folder with under 2 DECODABLE images yields null: fall back to everything once.
            if (string.IsNullOrEmpty(newFile) && pack.Count != active.Count)
                newFile = CollageComposer.Compose(active, outDir, accent, background, layout);
            if (string.IsNullOrEmpty(newFile) || !File.Exists(newFile))
                return;

            // Check + set + bookkeeping under _lock, atomic with Stop's flag flip + restore and Start's
            // capture: a compose can never land between a Stop and the next Start's capture and get saved
            // as the user's "original" (#692).
            string? previous;
            lock (_lock)
            {
                if (!_collageRunning || _ownImageRunning) { TryDeleteCollageFile(newFile); return; }

                // A rotation never stacks behind a write still in flight, same rule as Shuffle.
                if (_collageCurrentFile != null && _setQueue.IsBusy) { TryDeleteCollageFile(newFile); return; }

                SetWallpaper(newFile, "collage");

                previous = _collageCurrentFile;
                _collageCurrentFile = newFile;
                _currentImagePath = newFile;
            }

            if (!string.IsNullOrEmpty(previous) &&
                !string.Equals(previous, newFile, StringComparison.OrdinalIgnoreCase))
                TryDeleteCollageFile(previous);
        }

        private static void TryDeleteCollageFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { App.Logger?.Debug("[Collage] Could not delete temp {File}: {Error}", path, ex.Message); }
        }
    }
}

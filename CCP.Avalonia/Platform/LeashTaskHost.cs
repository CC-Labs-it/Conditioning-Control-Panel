// PORTED from WPF 7.1.5 Services/Leash/AppLeashTaskHost.cs + App.xaml.cs:3010-3013 and MainWindow.Leash.cs
// LeashOnPanicPress/EndLeashTask: lock cards (never strict), a leash session, bubbles and a caged video under the Core
// LeashTaskRunner. Head differences: completed cards are counted from LockCardWindow.Completed; the video clock is a
// 1 s script poll; the Deeper player raises no completion event, so a catalogue watch there never finishes by itself.
// The panic-off / Lockdown key route parks only leash-owned activity.
using System;
using System.Globalization;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using ILeashTaskRunner = ConditioningControlPanel.Controls.Leash.ILeashTaskRunner;

namespace ConditioningControlPanel.Avalonia.Platform;

internal sealed class LeashTaskHost : ILeashTaskHost, IDisposable
{
    /// <summary>Bubbles per minute while a bubble task runs and the player had them off.</summary>
    public const int BubblesPerMinute = 20;
    /// <summary>WPF App.LeashRunner. The shell binds whatever is here on every gate check (tests swap it).</summary>
    internal static ILeashTaskRunner? Runner { get; set; }
    private static LeashTaskHost? _host;
    /// <summary>WPF App.xaml.cs:3010-3013: the host and the runner. Idempotent.</summary>
    internal static void Start(LeashService? service)
    {
        if (Runner != null) return;
        LeashHead.Seed();   // the runner's UI timer factory
        _host = new LeashTaskHost();
        var r = new LeashTaskRunner(_host);
        if (service != null) r.AssignmentWatched += service.NoteAssignmentWatched;
        Runner = r;
        LeashHead.LeashedChanged += OnLeashedChanged;
    }
    /// <summary>WPF App.OnExit: the task and its window go with the app.</summary>
    internal static void Stop()
    {
        LeashHead.LeashedChanged -= OnLeashedChanged;
        try { Runner?.Cancel(); } catch { }
        _host?.Dispose();
        (_host, Runner) = (null, null);
    }
    /// <summary>WPF App.LeashedChanged(false) -> EndLeashTask("leash off"): a cut, the holder, a sign-out.</summary>
    private static void OnLeashedChanged(bool on) { if (!on) Dispatcher.UIThread.Post(() => EndTask("leash off")); }
    /// <summary>WPF EndLeashTask: cancel whatever the runner drives and close its window. Idempotent.</summary>
    internal static void EndTask(string why, MainShellWindow? shell = null)
    {
        try
        {
            if (Runner?.IsRunning == true || LeashPunishWindow.Current != null)
                Serilog.Log.Information("Leash: task ended ({Why})", why);
            Runner?.Cancel();
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash cancel failed: {E}", ex.Message); }
        LeashPunishWindow.CloseNow();
        try { (shell ?? MainShellWindow.Current)?.OnLeashTaskEnded(); } catch { }   // WPF: unplayable cleared, gate hidden
    }
    /// <summary>The "leash-task" panic surface (WPF LeashOnPanicPress, the first thing a panic press does): the
    /// runner parks with its progress, the video window closes, the gate stands back. The punishment stays pending.</summary>
    internal static void OnPanicPress(MainShellWindow? shell, bool panicRuns = true)
    {
        var r = Runner;
        shell ??= MainShellWindow.Current;
        if (r?.IsRunning != true && LeashPunishWindow.Current == null && (panicRuns || shell?.LeashGate?.IsUp != true)) return;
        if (SettingsPaletteWindow.IsOpen && !LockCardWindow.IsAnyOpen()   // WPF MainWindow.Leash.cs:382: an Escape
            && string.Equals(CoreSettings.Current?.PanicKey, "Escape", StringComparison.OrdinalIgnoreCase)) return;   // closing the palette is no panic
        Serilog.Log.Information("Leash: panic press stops the running task (kept pending)");
        var closeLines = !panicRuns && (r as LeashTaskRunner)?.RunningKind == PunishKind.Lines;
        try { if (panicRuns) r?.Park(); else r?.ParkAndStopItsSession(); } catch (Exception ex) { Serilog.Log.Debug("Leash park failed: {E}", ex.Message); }
        if (closeLines) LockCardWindow.ForceCloseAll();
        LeashPunishWindow.CloseNow();
        try { shell?.LeashStandBack(); }
        catch (Exception ex) { Serilog.Log.Debug("Leash gate stand-back failed: {E}", ex.Message); }
    }
    // ---- the host -----------------------------------------------------------------------------
    private bool _windowed;
    private LeashWatch? _watching;
    private int _lockCardsCompleted;
    private LeashWatchSample? _sample;
    private DispatcherTimer? _sampler;
    private bool _sampling;
    public event Action? BubblePopped;
    public event Action<LeashWatch>? WatchFinished;
    public event Action<LeashWatch>? WatchFailed;
    internal LeashTaskHost()
    {
        LeashPunishWindow.VideoEnded += OnWindowVideoEnded;
        LeashPunishWindow.PlaybackFailed += OnWindowPlaybackFailed;
        LockCardWindow.Completed += OnLockCardCompleted;
        Views.Overlays.BubbleOverlay.Popped += OnPopped;
    }
    private void OnWindowPlaybackFailed() { if (_watching is { } w && WatchCaged) WatchFailed?.Invoke(w); }
    private void OnWindowVideoEnded() { if (_watching is { } w) WatchFinished?.Invoke(w); }
    private void OnLockCardCompleted() => _lockCardsCompleted++;
    private void OnPopped() => BubblePopped?.Invoke();
    // ---- lock cards ----
    public int LockCardsCompleted => _lockCardsCompleted;
    public bool LockCardOpen { get { try { return LockCardWindow.IsAnyOpen(); } catch { return false; } } }
    /// <summary>Never strict, never a test card. A card that cannot open (no phrase enabled) says so here,
    /// or the task would wait on it forever.</summary>
    public bool ShowLockCard()
    {
        try
        {
            if (LockCardScheduler.EnabledPhrases().Count == 0)
            {
                Serilog.Log.Information("Leash lock card: no phrases enabled");
                return false;
            }
            LockCardWindow.ShowNext(isTest: false, strict: false);
            return true;
        }
        catch (Exception ex) { Serilog.Log.Warning("Leash lock card failed: {E}", ex.Message); return false; }
    }
    // ---- sessions ----
    public bool SessionRunning => App.Sessions?.IsRunning == true;
    /// <summary>WPF StartSessionFromRemote with BuildSession: false when the player is already in a session.</summary>
    public bool StartSession(PunishKind kind, int minutes)
    {
        if (App.Sessions is not { IsRunning: false } || MainShellWindow.Current is not { } shell) return false;
        try { shell.StartSession(BuildSession(kind, minutes)); return true; }
        catch (Exception ex) { Serilog.Log.Warning("Leash session failed: {E}", ex.Message); return false; }
    }
    /// <summary>WPF MainWindow.StopLeashSession: only the leash's own session (ids leash_*).</summary>
    public void StopSession()
    {
        try
        {
            if (App.Sessions is not { IsRunning: true } runner) return;
            var id = runner.CurrentSession?.Id;
            if (id == null || !id.StartsWith("leash_", StringComparison.Ordinal)) return;
            Serilog.Log.Information("Leash: stops the leash's own session ({Id})", id);
            runner.Stop(completed: false);
            if (CoreEngine.IsRunning) MainShellWindow.StopEngine();   // WPF StopEngineAndSession
        }
        catch (Exception ex) { Serilog.Log.Warning("Leash session stop failed: {E}", ex.Message); }
    }
    /// <summary>WPF AppLeashTaskHost.BuildSession: the player's own effects, and for a pink session the pink
    /// filter from start to end. No strict lock, ever.</summary>
    internal static Session BuildSession(PunishKind kind, int minutes)
    {
        var cur = CoreSettings.Current;
        var pink = kind == PunishKind.Pink;
        return new Session
        {
            Id = pink ? "leash_pink" : "leash_detention",
            Name = pink ? "Pink session" : "Detention",
            DurationMinutes = Math.Clamp(minutes, 1, 60),
            Difficulty = SessionDifficulty.Easy,
            BonusXP = 0,
            Settings = new SessionSettings
            {
                FlashEnabled = cur?.FlashEnabled ?? true, FlashPerHour = cur?.FlashFrequency ?? 10,
                FlashOpacity = cur?.FlashOpacity ?? 100, FlashImages = cur?.SimultaneousImages ?? 1,
                FlashClickable = cur?.FlashClickable ?? false, FlashAudioEnabled = cur?.FlashAudioEnabled ?? false,
                SubliminalEnabled = cur?.SubliminalEnabled ?? true, SubliminalPerMin = cur?.SubliminalFrequency ?? 5,
                SubliminalOpacity = cur?.SubliminalOpacity ?? 100, SubliminalFrames = cur?.SubliminalDuration ?? 5,
                MandatoryVideosEnabled = false, BubblesEnabled = cur?.BubblesEnabled ?? false,
                PinkFilterEnabled = pink, PinkFilterStartMinute = 0, PinkFilterEndMinute = -1,
                PinkFilterStartOpacity = 25, PinkFilterEndOpacity = 25,
            },
        };
    }
    // ---- bubbles ----
    public bool StartBubbles()
    {
        try
        {
            if (Views.Overlays.BubbleOverlay.IsRunning || MainShellWindow.Current is not { } host) return false;
            Views.Overlays.BubbleOverlay.Start(host, BubblesPerMinute);
            return Views.Overlays.BubbleOverlay.IsRunning;
        }
        catch (Exception ex) { Serilog.Log.Warning("Leash bubbles failed: {E}", ex.Message); return false; }
    }
    public bool BubblesRunning { get { try { return Views.Overlays.BubbleOverlay.IsRunning; } catch { return false; } } }
    public void StopBubbles()
    {
        try { Views.Overlays.BubbleOverlay.Stop(); }
        catch (Exception ex) { Serilog.Log.Debug("Leash bubbles stop failed: {E}", ex.Message); }
    }
    // ---- video ----
    public bool OpenWatch(LeashWatch watch)
    {
        if (!LeashGrammar.ValidWatch(watch)) return false;
        EndWatch();
        _watching = watch;
        try
        {
            var (holder, locked) = WhoAndWhy(watch);
            if (watch.Kind == "ht")
                return LandingRules.HtUrl(watch.Id) is { } url && LeashPunishWindow.OpenUrl(url, holder, locked) && StartSampler();
            var path = CataloguePath(watch.Id);
            if (path == null) return false;
            if (LocalMediaFor(path) is { } media)
            {
                _windowed = true;
                return LeashPunishWindow.OpenFile(media, holder, locked) && StartSampler();
            }
            MainShellWindow.Current?.PlayDeeperLibraryEntry(path);
            return true;
        }
        catch (Exception ex) { Serilog.Log.Warning("Leash watch failed: {E}", ex.Message); return false; }
    }
    /// <summary>The holder's name for the window's head, and whether this watch is a pending punishment
    /// (locked: the window will not close) or a task (it closes normally).</summary>
    internal static (string Holder, bool Locked) WhoAndWhy(LeashWatch watch)
    {
        try
        {
            var me = LeashHead.Service?.Snapshot.Me;
            if (me == null) return ("", false);
            foreach (var p in me.Pending)
                if (p.Kind == PunishKind.Video && p.Watch != null && p.Watch.Kind == watch.Kind && p.Watch.Id == watch.Id)
                    return (me.Holder.Name, true);
            return (me.Holder.Name, false);
        }
        catch { return ("", false); }
    }
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".m4v", ".mov", ".mkv" };
    /// <summary>The video a catalogue enhancement plays when it is a local file: its <c>mediaSource</c>, else a
    /// video beside it with the same base name. Null = let the Deeper player handle it.</summary>
    internal static string? LocalMediaFor(string enhancementPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(enhancementPath) ?? "";
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(enhancementPath));
            var src = (string?)(json["mediaSource"] ?? json["MediaSource"]);
            if (!string.IsNullOrWhiteSpace(src) && src != "*")
            {
                var full = Path.IsPathRooted(src) ? src : Path.Combine(dir, src);
                if (Array.IndexOf(VideoExtensions, Path.GetExtension(full).ToLowerInvariant()) >= 0 && File.Exists(full)) return full;
            }
            var stem = Path.GetFileName(enhancementPath);
            var cut = stem.IndexOf('.');
            if (cut > 0) stem = stem[..cut];
            foreach (var ext in VideoExtensions)
            {
                var sibling = Path.Combine(dir, stem + ext);
                if (File.Exists(sibling)) return sibling;
            }
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash local media lookup failed: {E}", ex.Message); }
        return null;
    }
    /// <summary>The local file for a catalogue id, the same lookup a friend's watch uses.</summary>
    private static string? CataloguePath(string catalogueId)
    {
        var subs = CoreSettings.Current?.DeeperSubmissions;
        if (subs == null) return null;
        foreach (var kv in subs)
            if (string.Equals(kv.Value?.CatalogueId, catalogueId, StringComparison.Ordinal) && File.Exists(kv.Key))
                return kv.Key;
        return null;
    }
    /// <summary>The page's main video as "current,duration" (the cage script's pick; WPF BrowserVideoTimeSource).</summary>
    internal const string SampleScript =
        "(function(){var b=null,s=-1;document.querySelectorAll('video').forEach(function(x){var r=x.getBoundingClientRect();"
        + "var k=(r.width*r.height)+(isFinite(x.duration)?x.duration*1000:0);if(k>s){b=x;s=k;}});"
        + "return b?(b.currentTime+','+b.duration):'';})()";
    private bool StartSampler()
    {
        _sample = null;
        _sampler?.Stop();
        if (LeashPunishWindow.Current?.View == null) return true;   // no engine: nothing to sample
        _sampler = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sampler.Tick += async (_, _) =>
        {
            if (_sampling) return;
            var view = LeashPunishWindow.Current?.View;
            if (view == null) { _sample = null; return; }
            _sampling = true;
            try { _sample = ParseSample(await view.InvokeScriptAsync(SampleScript), LeashPunishWindow.Watchable); }
            catch { _sample = null; }
            finally { _sampling = false; }
        };
        _sampler.Start();
        return true;
    }
    /// <summary>"12.5,300" (WebView2 hands it back JSON-quoted) to a sample; null when no video.</summary>
    internal static LeashWatchSample? ParseSample(string? raw, bool visible)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Trim().Trim('"').Split(',');
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var cur)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)) dur = double.NaN;
        return new LeashWatchSample(cur, dur, visible);
    }
    public LeashWatchSample? SampleWatch(LeashWatch watch)
    {
        if (watch.Kind != "ht" && !_windowed) return null;   // a Deeper-player watch (no completion event here)
        if (LeashPunishWindow.Current == null) return null;
        return _sample is { } s ? s with { Visible = LeashPunishWindow.Watchable } : null;
    }
    /// <summary>A caged watch is open while its window is; the Deeper player always reads open.</summary>
    public bool WatchOpen => _watching != null && (!WatchCaged || LeashPunishWindow.Current != null);
    public bool WatchCaged => _watching is { } w && (w.Kind == "ht" || _windowed);
    public void EndWatch()
    {
        (_watching, _windowed, _sample) = (null, false, null);
        _sampler?.Stop();
        _sampler = null;
        try { LeashPunishWindow.CloseNow(); } catch { }
    }
    public void Dispose()
    {
        EndWatch();
        LeashPunishWindow.VideoEnded -= OnWindowVideoEnded;
        LeashPunishWindow.PlaybackFailed -= OnWindowPlaybackFailed;
        LockCardWindow.Completed -= OnLockCardCompleted;
        Views.Overlays.BubbleOverlay.Popped -= OnPopped;
    }
}

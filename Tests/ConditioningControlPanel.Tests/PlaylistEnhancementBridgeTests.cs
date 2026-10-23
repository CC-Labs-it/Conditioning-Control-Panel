using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.AudioPlayer;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The playlist player's Deeper wiring (7.1.8): a time source over
/// <see cref="IAudioPlaylistService"/> and the bridge that runs a track's
/// enhancement with it and stops it when the track changes, stops (panic
/// included) or the Deeper player takes the stage.
/// </summary>
public class PlaylistEnhancementBridgeTests
{
    private const string A = @"C:\music\a.mp3";
    private const string B = @"C:\music\b.mp3";

    private sealed class FakePlaylist : IAudioPlaylistService
    {
        public List<string> List { get; } = new();
        public IReadOnlyList<string> Tracks => List;
        public int CurrentIndex { get; set; } = -1;
        public string? CurrentPath { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public int Volume { get; set; } = 80;
        public AudioPlaylistLoop Loop { get; set; }
        public bool Shuffle { get; set; }

        public double? SeekedTo;
        public int PauseCalls, PlayCalls;

        public int Add(IEnumerable<string> paths) { List.AddRange(paths); TracksChanged?.Invoke(); return 0; }
        public void RemoveAt(int index) { }
        public void Move(int fromIndex, int toIndex) { }
        public void Clear() { }
        public void PlayAt(int index) { }
        public void Play() { PlayCalls++; }
        public void Pause() { PauseCalls++; }
        public void Stop() { IsPlaying = false; IsPaused = false; PositionSeconds = 0; StateChanged?.Invoke(); }
        public void Next() { }
        public void Previous() { }
        public void Seek(double seconds) { SeekedTo = seconds; }

        public event Action? TracksChanged;
        public event Action<string?>? TrackChanged;
        public event Action? StateChanged;
        public event Action<double>? PositionChanged;

        /// <summary>Loads and plays a track the way a real service would report it.</summary>
        public void Start(string path)
        {
            CurrentPath = path;
            IsPlaying = true;
            IsPaused = false;
            PositionSeconds = 0;
            TrackChanged?.Invoke(path);
            StateChanged?.Invoke();
        }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
            IsPlaying = !paused;
            StateChanged?.Invoke();
        }

        public void Tick(double seconds)
        {
            PositionSeconds = seconds;
            PositionChanged?.Invoke(seconds);
        }

        public int PositionSubscribers => PositionChanged?.GetInvocationList().Length ?? 0;
    }

    private sealed class FakeBinding : IPlaylistEnhancementBinding
    {
        public bool IsBound { get; private set; }
        public bool Refuse;
        public int Binds, Unbinds;
        public PlaylistTimeSource? Source;
        public string? LoadedFile;

        public bool TryBind(ResolvedEnhancement resolved, PlaylistTimeSource source)
        {
            if (Refuse) return false;
            Binds++;
            IsBound = true;
            Source = source;
            LoadedFile = resolved.FilePath;
            source.Attach();
            return true;
        }

        public void Unbind()
        {
            if (!IsBound) return;
            Unbinds++;
            IsBound = false;
            Source?.Detach();
        }
    }

    private sealed class Rig
    {
        public FakePlaylist Playlist { get; } = new();
        public FakeBinding Binding { get; } = new();
        public HashSet<string> Enhanced { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool DeeperBusy;
        public bool Enabled = true;   // the card's "enhancements run with tracks" switch
        public int Lookups;
        public PlaylistEnhancementBridge Bridge { get; }

        public Rig(params string[] enhanced)
        {
            foreach (var p in enhanced) Enhanced.Add(p);
            Bridge = new PlaylistEnhancementBridge(Playlist, Binding, Resolve, () => DeeperBusy, () => Enabled);
        }

        private ResolvedEnhancement Resolve(string path)
        {
            Lookups++;
            return Enhanced.Contains(path)
                ? new ResolvedEnhancement(null, path + ".ccpenh.json", EnhancementDiscoverySource.Library)
                : ResolvedEnhancement.NotFound;
        }
    }

    // -- Time source -----------------------------------------------------------

    [Fact]
    public void TimeSource_ForwardsTicksOnlyWhileAttachedAndOnItsTrack()
    {
        var pl = new FakePlaylist { CurrentPath = A, IsPlaying = true, DurationSeconds = 90 };
        var src = new PlaylistTimeSource(pl, A);
        var seen = new List<double>();
        src.PlaybackTimeChanged += seen.Add;

        pl.Tick(1);                       // not attached yet
        src.Attach();
        src.Attach();                     // idempotent: one subscription
        Assert.Equal(1, pl.PositionSubscribers);
        pl.Tick(2);
        Assert.Equal(new[] { 2.0 }, seen);
        Assert.Equal(2, src.GetCurrentTimeSeconds());
        Assert.Equal(90, src.GetDurationSeconds());
        Assert.True(src.IsPlaying);

        pl.CurrentPath = B;               // playlist moved on, engine not unbound yet
        pl.Tick(0.1);
        Assert.Single(seen);
        Assert.Equal(0, src.GetCurrentTimeSeconds());
        Assert.False(src.IsPlaying);
        src.Seek(30);
        src.Pause();
        src.Play();
        Assert.Null(pl.SeekedTo);
        Assert.Equal(0, pl.PauseCalls + pl.PlayCalls);

        src.Dispose();
        Assert.Equal(0, pl.PositionSubscribers);
        Assert.True(src.GetVideoRect().IsEmpty);
    }

    [Fact]
    public void TimeSource_ControlsReachThePlaylistOnItsOwnTrack()
    {
        var pl = new FakePlaylist { CurrentPath = @"c:\MUSIC\A.MP3", IsPlaying = true };
        var src = new PlaylistTimeSource(pl, A);   // paths compare case-insensitively
        src.Attach();
        src.Seek(12.5);
        src.Pause();
        src.Play();
        Assert.Equal(12.5, pl.SeekedTo);
        Assert.Equal(1, pl.PauseCalls);
        Assert.Equal(1, pl.PlayCalls);
    }

    [Fact]
    public void TimeSource_IsDeadOnceThePlayerIsStoppedOrItIsDetached()
    {
        // A speak prompt releasing its hold calls Play() while the engine tears down. After a
        // panic or Stop that must never start the track again (review of ccp-bugs #1432 wave).
        var stopped = new FakePlaylist { CurrentPath = A, IsPlaying = false, IsPaused = false };
        var src = new PlaylistTimeSource(stopped, A);
        src.Attach();
        src.Play();
        src.Seek(3);
        Assert.Equal(0, stopped.PlayCalls);

        var paused = new FakePlaylist { CurrentPath = A, IsPaused = true };
        var held = new PlaylistTimeSource(paused, A);
        held.Attach();
        held.Play();
        Assert.Equal(1, paused.PlayCalls);   // a held (paused) track still resumes

        held.Detach();
        held.Play();
        Assert.Equal(1, paused.PlayCalls);   // detached: dead
    }

    // -- Bridge ----------------------------------------------------------------

    [Fact]
    public void Bridge_BindsTheTrackEnhancement_AndFeedsItTheClock()
    {
        var rig = new Rig(A);
        rig.Playlist.Start(A);

        Assert.Equal(1, rig.Binding.Binds);
        Assert.Equal(A, rig.Bridge.BoundPath);
        Assert.Equal(A + ".ccpenh.json", rig.Binding.LoadedFile);

        double last = -1;
        rig.Binding.Source!.PlaybackTimeChanged += t => last = t;
        rig.Playlist.Tick(3.5);
        Assert.Equal(3.5, last);
    }

    [Fact]
    public void Bridge_TrackWithoutEnhancement_BindsNothing_AndLooksUpOnce()
    {
        var rig = new Rig();
        rig.Playlist.Start(A);
        rig.Playlist.Tick(1);
        rig.Playlist.Tick(2);
        rig.Playlist.SetPaused(true);
        rig.Playlist.SetPaused(false);

        Assert.Equal(0, rig.Binding.Binds);
        Assert.Null(rig.Bridge.BoundPath);
        Assert.Equal(1, rig.Lookups);
    }

    [Fact]
    public void Bridge_TrackChange_StopsTheOldEnhancement_AndStartsTheNext()
    {
        var rig = new Rig(A, B);
        rig.Playlist.Start(A);
        var first = rig.Binding.Source;

        rig.Playlist.Start(B);
        Assert.Equal(1, rig.Binding.Unbinds);
        Assert.Equal(2, rig.Binding.Binds);
        Assert.Equal(B, rig.Bridge.BoundPath);
        Assert.NotSame(first, rig.Binding.Source);
        Assert.False(first!.IsAttached);

        rig.Playlist.Start(@"C:\music\plain.mp3");   // no enhancement for this one
        Assert.Equal(2, rig.Binding.Unbinds);
        Assert.False(rig.Binding.IsBound);
        Assert.Null(rig.Bridge.BoundPath);
    }

    [Fact]
    public void Bridge_Stop_Unbinds_AndPlayingAgainBindsAgain()
    {
        var rig = new Rig(A);
        rig.Playlist.Start(A);

        rig.Playlist.Stop();              // what panic and stop-all call on the service
        Assert.False(rig.Binding.IsBound);
        Assert.Null(rig.Bridge.BoundPath);
        Assert.Equal(0, rig.Playlist.PositionSubscribers - 1);   // only the bridge listens

        rig.Playlist.IsPlaying = true;
        rig.Playlist.SetPaused(false);
        Assert.Equal(2, rig.Binding.Binds);
    }

    [Fact]
    public void Bridge_Pause_KeepsTheEnhancementBound()
    {
        var rig = new Rig(A);
        rig.Playlist.Start(A);
        rig.Playlist.SetPaused(true);
        Assert.True(rig.Binding.IsBound);
        rig.Playlist.SetPaused(false);
        Assert.Equal(1, rig.Binding.Binds);
        Assert.Equal(0, rig.Binding.Unbinds);
    }

    [Fact]
    public void Bridge_NeverBindsWhileTheDeeperPlayerRuns()
    {
        var rig = new Rig(A) { DeeperBusy = true };
        rig.Playlist.Start(A);
        rig.Playlist.Tick(1);
        Assert.Equal(0, rig.Binding.Binds);

        rig.DeeperBusy = false;           // Deeper player let go: the next tick picks it up
        rig.Playlist.Tick(2);
        Assert.Equal(1, rig.Binding.Binds);
        Assert.Equal(A, rig.Bridge.BoundPath);
    }

    [Fact]
    public void Bridge_YieldsWhenTheDeeperPlayerStartsMidTrack()
    {
        var rig = new Rig(A);
        rig.Playlist.Start(A);
        Assert.True(rig.Binding.IsBound);

        rig.DeeperBusy = true;
        rig.Playlist.Tick(5);
        Assert.False(rig.Binding.IsBound);
        Assert.Null(rig.Bridge.BoundPath);
        rig.Playlist.Tick(6);
        Assert.Equal(1, rig.Binding.Binds);
    }

    [Fact]
    public void Bridge_LateBind_WhenTrackChangedArrivesBeforePlaying()
    {
        var rig = new Rig(A);
        rig.Playlist.CurrentPath = A;     // loaded, not playing, no events yet
        rig.Playlist.Tick(0);
        Assert.Equal(0, rig.Binding.Binds);

        rig.Playlist.IsPlaying = true;    // service starts without a StateChanged
        rig.Playlist.Tick(0.1);
        Assert.Equal(1, rig.Binding.Binds);
    }

    [Fact]
    public void Bridge_RefusedBind_LeavesNothingBehind_AndDisposeLetsGo()
    {
        var rig = new Rig(A);
        rig.Binding.Refuse = true;
        rig.Playlist.Start(A);
        Assert.Null(rig.Bridge.BoundPath);
        Assert.Equal(1, rig.Playlist.PositionSubscribers);   // the refused source never stayed attached

        rig.Binding.Refuse = false;
        rig.Playlist.Start(A);            // reloading the track asks again
        Assert.True(rig.Binding.IsBound);

        rig.Bridge.Dispose();
        Assert.False(rig.Binding.IsBound);
        Assert.Equal(0, rig.Playlist.PositionSubscribers);
        rig.Playlist.Start(B);            // a disposed bridge reacts to nothing
        Assert.False(rig.Binding.IsBound);
    }

    // -- The switch (owner, 7.1.8 round two) -------------------------------------

    [Fact]
    public void Bridge_SwitchOff_NeverBinds_AndNeverLooksUp()
    {
        var rig = new Rig(A, B) { Enabled = false };

        rig.Playlist.Start(A);
        rig.Playlist.Tick(1);
        rig.Playlist.SetPaused(true);
        rig.Playlist.SetPaused(false);
        rig.Playlist.Start(B);
        rig.Playlist.Tick(2);

        Assert.Equal(0, rig.Binding.Binds);
        Assert.Equal(0, rig.Lookups);
        Assert.Null(rig.Bridge.BoundPath);
        Assert.Equal(1, rig.Playlist.PositionSubscribers);   // only the bridge, no time source
    }

    [Fact]
    public void Bridge_SwitchOffMidTrack_UnbindsAtOnce_AndOnBindsAgain()
    {
        var rig = new Rig(A);
        rig.Playlist.Start(A);
        Assert.True(rig.Binding.IsBound);

        rig.Enabled = false;
        rig.Bridge.Refresh();             // what the card calls: no tick, no state change needed
        Assert.False(rig.Binding.IsBound);
        Assert.Equal(1, rig.Binding.Unbinds);
        Assert.Null(rig.Bridge.BoundPath);
        Assert.True(rig.Playlist.IsPlaying);                 // the track itself plays on
        Assert.Equal(1, rig.Playlist.PositionSubscribers);

        rig.Playlist.Tick(3);             // and it stays off
        Assert.Equal(1, rig.Binding.Binds);

        rig.Enabled = true;
        rig.Bridge.Refresh();
        Assert.True(rig.Binding.IsBound);
        Assert.Equal(2, rig.Binding.Binds);
        Assert.Equal(A, rig.Bridge.BoundPath);
    }

    [Fact]
    public void Bridge_SwitchOff_LetsGoOfAPausedTrack_AndOnTheNextTickWithoutRefresh()
    {
        var paused = new Rig(A);
        paused.Playlist.Start(A);
        paused.Playlist.SetPaused(true);
        Assert.True(paused.Binding.IsBound);
        paused.Enabled = false;
        paused.Bridge.Refresh();
        Assert.False(paused.Binding.IsBound);

        // A setting changed behind the card's back (no Refresh) is caught by the tick.
        var silent = new Rig(A);
        silent.Playlist.Start(A);
        silent.Enabled = false;
        silent.Playlist.Tick(1);
        Assert.False(silent.Binding.IsBound);
        Assert.Null(silent.Bridge.BoundPath);
    }

    [Fact]
    public void Bridge_AppWiring_ReadsTheSetting_AndTheCardTellsTheBridge()
    {
        string root = RepoRoot();
        string bridge = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "ConditioningControlPanel",
            "Services", "Deeper", "PlaylistEnhancementBridge.cs"));
        string model = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "ConditioningControlPanel",
            "Views", "Controls", "AudioPlaylistCardModel.cs"));

        Assert.Contains("App.Settings?.Current?.AudioPlaylistEnhancements != false", bridge);
        Assert.Contains("PlaylistEnhancementBridge.Installed?.Refresh();", model);
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(
                   System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "MainWindow")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new Xunit.Sdk.XunitException("repo root not found");
    }
}

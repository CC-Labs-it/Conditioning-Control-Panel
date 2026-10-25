using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Builds before the clear stamps let Chromium cache passed-through clips in every profile, with
/// no stamp anywhere. A profile the user never reopens after the upgrade (Goon, Race, DtRH, Intake,
/// Loom...) must still lose those bytes: every pass walks the user data folders on disk, and one
/// with no stamp and no live core loses its disk cache at once, on the hourly sweep and on the off
/// switch. Runs with the switch tests: the switch is process-wide.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class ScrolllerWebGateProfileSeedTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ccp-udf-seed-" + Guid.NewGuid().ToString("N"));
    private readonly string _userData;
    private readonly string _clears;
    private readonly Func<string?> _oldClearsPath;
    private DateTime _now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    public ScrolllerWebGateProfileSeedTests()
    {
        _userData = Path.Combine(_base, "userdata");
        _clears = Path.Combine(_base, "cache", ScrolllerWebGate.ClearsFileName);
        Directory.CreateDirectory(_userData);
        _oldClearsPath = ScrolllerWebGate.ClearsPathProvider;
        ScrolllerWebGate.ClearsPathProvider = () => _clears;
        ScrolllerWebGate.ClearsUtcNow = () => _now;
        ScrolllerWebGate.ProfileRootsProvider = () => new[] { _userData };
        ScrolllerWebGate.ExtraProfileFoldersProvider = () => Array.Empty<string>();
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
    }

    public void Dispose()
    {
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
        ScrolllerWebGate.ClearsPathProvider = _oldClearsPath;
        ScrolllerWebGate.ClearsUtcNow = () => DateTime.UtcNow;
        ScrolllerWebGate.ProfileRootsProvider = () => Array.Empty<string>();
        ScrolllerWebGate.ExtraProfileFoldersProvider = () => Array.Empty<string>();
        try { Directory.Delete(_base, true); } catch { }
    }

    /// <summary>A profile a shipped build filled: a cached clip, plus the page's own storage.</summary>
    private (string Cache, string Storage) Profile(string root, string name)
    {
        var cache = Path.Combine(root, name, "EBWebView", "Default", "Cache", "Cache_Data");
        var storage = Path.Combine(root, name, "EBWebView", "Default", "Local Storage");
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(storage);
        File.WriteAllText(Path.Combine(cache, "f_000001"), "clip bytes");
        return (Path.GetDirectoryName(cache)!, storage);
    }

    [Fact]
    public void Every_user_data_folder_on_disk_is_known_and_nothing_else()
    {
        Profile(_userData, "browser_data_goon");
        Profile(_userData, "arcademy");
        Directory.CreateDirectory(Path.Combine(_userData, "logs"));   // not a profile
        var video = Path.Combine(_base, "local", "browser_data_video");
        Profile(Path.GetDirectoryName(video)!, "browser_data_video");
        ScrolllerWebGate.ExtraProfileFoldersProvider = () => new[] { video, video + "\\" };

        var known = ScrolllerWebGate.KnownProfileFolders();

        Assert.Equal(3, known.Count);
        Assert.Contains(known, k => k.EndsWith("browser_data_goon", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(known, k => k.EndsWith("arcademy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(known, k => k.EndsWith("browser_data_video", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_never_stamped_profile_loses_its_cache_on_the_hourly_sweep()
    {
        var (cache, storage) = Profile(_userData, "browser_data_race");

        ScrolllerCopySweeper.SweepAll();

        Assert.False(Directory.Exists(cache));
        Assert.True(Directory.Exists(storage));   // the page's own storage survives
        var stamps = ScrolllerWebGate.ParseClears(File.ReadAllText(_clears));
        Assert.Equal(_now, stamps["browser_data_race"].At);
    }

    [Fact]
    public void A_never_stamped_profile_loses_its_cache_on_the_off_switch()
    {
        var (cache, storage) = Profile(_userData, "browser_data_dtrh");

        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));

        Assert.False(Directory.Exists(cache));
        Assert.True(Directory.Exists(storage));
    }

    [Fact]
    public void Once_stamped_the_profile_follows_the_day_rule()
    {
        var (cache, _) = Profile(_userData, "browser_data_loom");
        Assert.Equal(1, ScrolllerWebGate.ClearDueChromiumCachesAsync().Result);
        Assert.False(Directory.Exists(cache));

        Profile(_userData, "browser_data_loom");   // refilled an hour later: not due yet
        _now = _now.AddHours(1);
        Assert.Equal(0, ScrolllerWebGate.ClearDueChromiumCachesAsync().Result);
        Assert.True(Directory.Exists(cache));

        _now = _now.AddHours(22);                  // 23 h: a day old before the next sweep
        Assert.Equal(1, ScrolllerWebGate.ClearDueChromiumCachesAsync().Result);
        Assert.False(Directory.Exists(cache));
    }

    [Fact]
    public void The_off_switch_clears_a_stamped_profile_that_is_not_due()
    {
        var (cache, _) = Profile(_userData, "browser_data_intake");
        Assert.Equal(1, ScrolllerWebGate.ClearDueChromiumCachesAsync().Result);
        Profile(_userData, "browser_data_intake");
        Assert.Equal(1, ScrolllerWebGate.ClearChromiumCachesAsync("off").Result);
        Assert.False(Directory.Exists(cache));
    }
}

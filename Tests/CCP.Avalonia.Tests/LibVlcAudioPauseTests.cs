using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LibVlcAudioPauseTests
{
    // WPF DtrhHostService.cs:819-835,1073: a release resumes even if freeze work arrives late.
    [Fact]
    public void Rapid_freeze_and_release_apply_latest_state_even_in_reverse_order()
    {
        var queued = new List<Action>();
        var applied = new List<bool>();
        var control = LibVlcAudio.LatestPauseControl(new object(), () => false, applied.Add, queued.Add);
        Action stop = () => { };
        LibVlcAudio.Pausers.AddOrUpdate(stop, control.Set);
        try
        {
            LibVlcAudio.SetPaused(stop, true);
            LibVlcAudio.SetPaused(stop, false);
            Assert.Equal(2, queued.Count);
            queued[1]();
            queued[0]();
            Assert.Equal(new[] { false, false }, applied);
        }
        finally { LibVlcAudio.Pausers.Remove(stop); }
    }

    // An opening clip or an asynchronous pause event must reapply the current request, never a stale one.
    [Fact]
    public void Refresh_reapplies_latest_state_and_ignores_a_finished_clip()
    {
        var queued = new List<Action>();
        var applied = new List<bool>();
        var finished = false;
        var control = LibVlcAudio.LatestPauseControl(new object(), () => finished, applied.Add, queued.Add);
        control.Set(true);
        queued[0]();
        control.Refresh(); // playback becomes ready after the freeze
        queued[1]();
        control.Set(false);
        control.Refresh(); // the late Paused event follows the release
        queued[3]();
        queued[2]();
        Assert.Equal(new[] { true, true, false, false }, applied);
        control.Set(true);
        finished = true;
        queued[4]();
        Assert.Equal(4, applied.Count);
    }
}

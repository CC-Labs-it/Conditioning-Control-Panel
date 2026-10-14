using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Remix;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The director's head seams (ownership, motion level, builder, free memory) now that it
/// lives in Core: each one gates the roll or the build exactly as the WPF statics did.</summary>
public sealed class JackpotRemixDirectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-remix-" + Guid.NewGuid().ToString("N"));
    private readonly string _out;
    private readonly List<string> _pool = new();
    private int _builds;

    public JackpotRemixDirectorTests()
    {
        _out = Path.Combine(_root, "out");
        Directory.CreateDirectory(_out);
        foreach (var n in new[] { "a.gif", "b.gif" })
        {
            var p = Path.Combine(_root, n);
            File.WriteAllBytes(p, new byte[] { 1 });
            _pool.Add(p);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } // swallow: temp cleanup
    }

    private JackpotRemixDirector Make(bool owned = true, MotionLevel motion = MotionLevel.Full, bool builderDisabled = false,
        ulong freeBytes = ulong.MaxValue, bool flashShowing = false)
        => new(() => _pool, () => flashShowing, () => false, () => true, _out,
            owned: () => owned,
            motion: () => motion,
            builderDisabled: () => builderDisabled,
            build: (sources, seed, ct) =>
            {
                Interlocked.Increment(ref _builds);
                Assert.Equal(JackpotRemixPlan.Tiles, sources.Count);
                var path = Path.Combine(_out, "remix-built.gif");
                File.WriteAllBytes(path, new byte[] { 1 });
                return Task.FromResult<string?>(path);
            },
            availablePhysicalBytes: () => freeBytes,
            odds: 1);

    private string Leftover()
    {
        var p = Path.Combine(_out, "remix-left.gif");
        File.WriteAllBytes(p, new byte[] { 1 });
        return p;
    }

    [Fact]
    public void A_hit_with_a_leftover_file_plays_it()
    {
        var left = Leftover();
        using var d = Make();
        Assert.Equal(left, d.TakeForFlash());
    }

    [Fact]
    public void Unowned_motion_off_or_a_dead_builder_never_plays()
    {
        Leftover();
        using (var d = Make(owned: false)) Assert.Null(d.TakeForFlash());
        using (var d = Make(motion: MotionLevel.Off)) Assert.Null(d.TakeForFlash());
        using (var d = Make(builderDisabled: true)) Assert.Null(d.TakeForFlash());
    }

    [Fact]
    public void A_poll_builds_once_through_the_seam_then_a_hit_plays_the_file()
    {
        // Poll is driven directly (no TakeForFlash first, so no Task.Run nudge races it); the build seam
        // returns a completed task, so the file is cached by the time Poll returns.
        using var d = Make();
        d.Poll();
        Assert.Equal(1, _builds);
        Assert.Equal(Path.Combine(_out, "remix-built.gif"), d.TakeForFlash());
        Assert.Equal(1, _builds);
    }

    [Fact]
    public void Memory_pressure_or_a_flash_on_screen_holds_the_build()
    {
        using var tight = Make(freeBytes: 0);
        using var busy = Make(flashShowing: true);
        tight.Poll();
        busy.Poll();
        Assert.Equal(0, _builds);
        using var free = Make();
        free.Poll();
        Assert.Equal(1, _builds); // the same tick does build when nothing holds it
    }
}

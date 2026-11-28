using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Afterglow wiring: the pops ride the ambient scheduler's own start and stop, and the
/// subliminal card itself is untouched.</summary>
public class AfterglowWiringTests
{
    private static string Src(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    /// <summary>Panic and the emergency exit stop subliminals through Stop / Dispose; both must take
    /// every pop down. Start arms the driver, the OCR exclusion knows the words.</summary>
    [Fact]
    public void Service_StartsAndStopsTheDriverWithTheAmbientScheduler()
    {
        var src = Src("ConditioningControlPanel", "Services", "Subliminal", "SubliminalService.cs");
        // Take two: the ghost line is fed where a subliminal is shown and cleared where the service stops.
        // On the port the scheduler is Core's: the head draws one chosen phrase (FlashPhrase) and
        // clears its surfaces in TearDownSurfaces, which Stop and the run-state hook both reach.
        Assert.DoesNotContain("new Super.AfterglowDriver()", src);
        Assert.Contains("Compositor.AfterglowLineLayer.WordShown(text);", Body(src, "internal void FlashPhrase(string text, bool restCard = false)"));
        Assert.Contains("TearDownSurfaces();", Body(src, "public void Stop()"));
        Assert.Contains("AfterglowLineLayer.StopAll", Body(src, "private void TearDownSurfaces()"));
        Assert.Contains("_afterglow?.Stop();", Body(src, "private void TearDownSurfaces()"));
        Assert.Contains("_afterglow?.Dispose();", Body(src, "public void Dispose()"));
        Assert.Contains("glow.GetActiveTextRectsPx()", Body(src, "public System.Drawing.Rectangle[] GetActiveTextScreenRects()"));
    }

    /// <summary>The owner wants the old subliminals back exactly: no ghost of the card, no weighted pick.</summary>
    [Fact]
    public void Service_LeavesTheCardAndThePickAlone()
    {
        var src = Src("ConditioningControlPanel", "Services", "Subliminal", "SubliminalService.cs");
        Assert.DoesNotContain("SpawnAfterglow", src);
        Assert.DoesNotContain("_afterglowBonus", src);
        Assert.DoesNotContain("PickWeighted", src);
        // The pick is Core's plain one on the port.
        Assert.Contains("CoreSubliminal.PickPhrase()", Body(src, "public void FlashSubliminal(bool restCard = false)"));
    }

    /// <summary>The driver tears down on the switch, rechecks every second and needs the compositor.</summary>
    [Fact]
    public void Driver_TearsDownOnLapse_AndOnlyRunsWithTheCompositor()
    {
        var src = Src("ConditioningControlPanel", "Services", "Super", "AfterglowDriver.cs");
        Assert.Contains("SuperAccess.Changed += OnChanged", src);
        Assert.Contains("SuperAccess.Changed -= OnChanged", Body(src, "public void Dispose()"));
        var allowed = src.Substring(src.IndexOf("private static bool Allowed()", StringComparison.Ordinal), 300);
        Assert.Contains("SuperAccess.IsOn(SuperEffect.Afterglow)", allowed);
        Assert.Contains("App.CompositorEnabled", allowed);
        var stop = Body(src, "public void Stop()");
        Assert.Contains("_tick.Stop();", stop);
        Assert.Contains("_layer?.Clear()", stop);
        var arm = Body(src, "private void Arm()");
        Assert.Contains("_layer?.Clear()", arm);
        Assert.Contains("RecheckS", Body(src, "private void OnTick("));
    }

    /// <summary>The compositor can run Update off the UI thread. Turning the layer off for an empty field
    /// must happen under the same lock Spawn turns it on under, or a pop spawned between the two is
    /// stranded on an inactive layer.</summary>
    [Fact]
    public void Layer_FlipsItsActivityUnderTheFieldLock()
    {
        var src = Src("ConditioningControlPanel", "Services", "Compositor", "AfterglowLayer.cs");
        foreach (var method in new[] { "public void Spawn(", "public void Clear()", "public override void Update(" })
        {
            var body = Body(src, method);
            var lockAt = body.IndexOf("lock (_sync)", StringComparison.Ordinal);
            Assert.True(lockAt >= 0, method);
            var lockBody = Body(body.Substring(lockAt), "lock (_sync)");
            Assert.Contains("SetActive(", lockBody);
            Assert.DoesNotContain("SetActive(", body.Replace(lockBody, ""));
        }
    }

    private static string Body(string src, string signature)
    {
        int i = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i >= 0, signature);
        int open = src.IndexOf('{', i), depth = 0;
        for (int j = open; j < src.Length; j++)
        {
            if (src[j] == '{') depth++;
            else if (src[j] == '}' && --depth == 0) return src.Substring(open, j - open + 1);
        }
        return src.Substring(open);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }
}

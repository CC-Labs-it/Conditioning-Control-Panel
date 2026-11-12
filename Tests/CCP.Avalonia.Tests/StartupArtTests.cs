using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using Xunit;
using R = ConditioningControlPanel.Avalonia.Helpers.StartupArt.Request;

namespace CCP.Avalonia.Tests;

/// <summary>Owner WSLg run: "super laggy on load". The shell build decoded ~4 s of art on the UI thread;
/// a launch now decodes the art the last launch's first seconds asked for on worker threads.</summary>
[Collection(RunsAloneCollection.Name)]   // StartupArt is process-wide state, like the startup it serves
public sealed class StartupArtTests
{
    private static string Manifest(out string dir)
    {
        dir = Directory.CreateTempSubdirectory("ccp-startup-art-").FullName;
        return Path.Combine(dir, StartupArt.ManifestName);
    }

    [Fact]
    public void ALaunchRecordsItsLoadsInOrderAndTheNextOneDecodesThemOffTheUiThread()
    {
        var path = Manifest(out var dir);
        try
        {
            // First launch: nothing listed yet, so nothing is prewarmed; the loads are recorded, first-asked first.
            Assert.Null(StartupArt.Start(path, _ => null, (_, _) => throw new InvalidOperationException("no list, no decode")));
            Assert.False(StartupArt.Take(new R("o", "features/flash.png", 800), null, out _));
            Assert.False(StartupArt.Take(new R("b", "Patreon tier1.png", 160), null, out _));
            Assert.False(StartupArt.Take(new R("o", "features/flash.png", 800), null, out _));   // asked twice, listed once
            StartupArt.Finish(path);
            Assert.False(StartupArt.Recording);
            Assert.Equal(new[] { new R("o", "features/flash.png", 800), new R("b", "Patreon tier1.png", 160) }, StartupArt.Read(path));

            // Next launch: both decode on workers, with the override each would have resolved.
            var ui = Environment.CurrentManagedThreadId;
            var decodedOn = new ConcurrentDictionary<R, int>();
            var overrides = new ConcurrentDictionary<R, string?>();
            var task = StartupArt.Start(path, r => r.Kind == "o" ? "/mods/x/flash.png" : null, (r, ov) =>
            {
                decodedOn[r] = Environment.CurrentManagedThreadId;
                overrides[r] = ov;
                return null;
            });
            Assert.NotNull(task);
            Assert.True(task!.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(2, decodedOn.Count);
            Assert.DoesNotContain(ui, decodedOn.Values);
            Assert.Equal("/mods/x/flash.png", overrides[new R("o", "features/flash.png", 800)]);

            // Handed out once, and only for the same override: a mod switched since then decodes as usual.
            Assert.False(StartupArt.Take(new R("o", "features/flash.png", 800), "/mods/other/flash.png", out _));
            Assert.True(StartupArt.Take(new R("o", "features/flash.png", 800), "/mods/x/flash.png", out _));
            Assert.False(StartupArt.Take(new R("o", "features/flash.png", 800), "/mods/x/flash.png", out _));
            Assert.True(StartupArt.Take(new R("b", "Patreon tier1.png", 160), null, out _));
        }
        finally { StartupArt.Finish(path); Directory.Delete(dir, true); }
    }

    [Fact]
    public void AnEntryNotYetStartedIsDecodedByItsFirstAskerOnlyOnce()
    {
        var path = Manifest(out var dir);
        try
        {
            File.WriteAllText(path, "[{\"K\":\"o\",\"N\":\"a.png\",\"W\":700}]");
            using var gate = new ManualResetEventSlim();
            int decodes = 0;
            var task = StartupArt.Start(path, _ => null, (_, _) => { Interlocked.Increment(ref decodes); gate.Wait(TimeSpan.FromSeconds(10)); return null; });
            gate.Set();
            Assert.True(StartupArt.Take(new R("o", "a.png", 700), null, out _));   // waits for the worker's decode, or runs it
            Assert.True(task!.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, decodes);
        }
        finally { StartupArt.Finish(path); Directory.Delete(dir, true); }
    }

    [Fact]
    public void AnUnreadableOrHostileListPrewarmsNothing()
    {
        var path = Manifest(out var dir);
        try
        {
            File.WriteAllText(path, "not json");
            Assert.Empty(StartupArt.Read(path));
            File.WriteAllText(path, "[{\"K\":\"x\",\"N\":\"a.png\"},{\"K\":\"o\",\"N\":\" \"},{\"K\":\"o\",\"N\":\"b.png\",\"W\":-1},{\"K\":\"b\",\"N\":\"c.png\",\"W\":99999},{\"K\":\"b\",\"N\":\"ok.png\"}]");
            Assert.Equal(new[] { new R("b", "ok.png", null) }, StartupArt.Read(path));
            File.WriteAllText(path, "[" + string.Join(",", Enumerable.Range(0, 1000).Select(i => $"{{\"K\":\"o\",\"N\":\"{i}.png\",\"W\":64}}")) + "]");
            Assert.Equal(StartupArt.MaxEntries, StartupArt.Read(path).Count);
        }
        finally { Directory.Delete(dir, true); }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using LibVLCSharp.Shared;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// LibVLCSharp completes <c>Media.Parse</c> inside libvlc's ParsedChanged callback, on libvlc's
/// thread. Disposing the media in that continuation frees it under libvlc and corrupts the
/// native heap (the host dies later at ntdll+0x1A12D). The Deeper duration probe
/// (<see cref="MediaDurationCache"/>) did exactly that with a <c>using var media</c>; the same
/// bug was proven on the Avalonia head (restack fixw4c #5071: 3 of 3 host crashes before, 0 after).
/// </summary>
public sealed class LibVlcParseNativeTests
{
    private static readonly object VlcLock = new();
    private static LibVLC? _vlc;
    private static string? _vlcError;

    private static LibVLC Vlc()
    {
        lock (VlcLock)
        {
            if (_vlc == null && _vlcError == null)
            {
                try
                {
                    Core.Initialize();
                    _vlc = new LibVLC("--aout=dummy", "--vout=dummy", "--no-video-title-show", "--verbose=-1");
                }
                catch (Exception ex) { _vlcError = ex.Message; }
            }
        }
        if (_vlc == null) Assert.Skip("libvlc unavailable: " + _vlcError);
        return _vlc!;
    }

    // Eight bytes of an mp4 header: libvlc opens it, then the parse fails.
    private static string Bogus(string dir)
    {
        var path = Path.Combine(dir, "bogus.mp4");
        File.WriteAllBytes(path, new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });
        return path;
    }

    [Fact]
    public async Task ParseResumesOffLibvlcsCallbackThread()
    {
        var vlc = Vlc();
        var dir = Directory.CreateTempSubdirectory("ccp-parse-thread-").FullName;
        try
        {
            var path = Bogus(dir);
            await Task.Run(async () =>
            {
                using var media = new Media(vlc, path, FromType.FromPath);
                int callback = 0;
                media.ParsedChanged += (_, _) => Volatile.Write(ref callback, Environment.CurrentManagedThreadId);
                await LibVlcParse.ParseOffCallbackAsync(media, MediaParseOptions.ParseLocal, 5000);
                var raised = Volatile.Read(ref callback);
                if (raised != 0) Assert.NotEqual(raised, Environment.CurrentManagedThreadId);
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    // Before the fix the second or third probe of a file libvlc cannot parse killed the host.
    [Fact]
    public async Task RepeatedDurationProbesOfAnUnparseableFileSurvive()
    {
        var vlc = Vlc();
        var dir = Directory.CreateTempSubdirectory("ccp-parse-repeat-").FullName;
        try
        {
            var path = Bogus(dir);
            for (int i = 0; i < 20; i++)
                Assert.Null(await Task.Run(() => MediaDurationCache.ProbeLibVlcAsync(vlc, path)));
        }
        finally { Directory.Delete(dir, true); }
    }

    // One way to await a parse: a bare `await media.Parse(` anywhere else in the app reopens the bug.
    [Fact]
    public void EveryAwaitedParseGoesThroughTheHelper()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        var app = Path.Combine(dir!.FullName, "ConditioningControlPanel");
        var bareParse = new Regex(@"\.Parse\(\s*(MediaParseOptions|options\b)");
        var offenders = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                     && !f.EndsWith("LibVlcParse.cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => bareParse.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(app, f))
            .ToList();
        Assert.True(offenders.Count == 0,
            "await LibVlcParse.ParseOffCallbackAsync instead of Media.Parse in: " + string.Join(", ", offenders));
    }
}

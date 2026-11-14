using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using LibVLCSharp.Shared;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>restack fixw4c: LibVLCSharp completes Media.Parse inside libvlc's ParsedChanged callback.
/// Disposing the media in that continuation freed it under libvlc and corrupted the native heap; the
/// Deeper duration probe did that for every local file, and the test host died later at ntdll+0x1A12D.</summary>
public sealed class DeeperParseNativeTests
{
    private static string Bogus(string dir) =>
        WriteAll(Path.Combine(dir, "bogus.mp4"), new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });

    private static string WriteAll(string path, byte[] bytes) { File.WriteAllBytes(path, bytes); return path; }

    private static LibVLC Vlc()
    {
        try { _ = new LibVlcAudio("--aout=dummy"); }
        catch (Exception ex) { Assert.Skip("libvlc unavailable: " + ex.Message); }
        return LibVlcAudio.Shared ?? throw new InvalidOperationException("no shared libvlc");
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
                await LibVlcAudio.ParseAsync(media, MediaParseOptions.ParseLocal, 5000);
                var raised = Volatile.Read(ref callback);
                if (raised != 0) Assert.NotEqual(raised, Environment.CurrentManagedThreadId);
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    // Before the fix the second or third probe of a file libvlc cannot parse killed the process.
    [Fact]
    public async Task RepeatedDurationProbesAndOpensSurvive()
    {
        Vlc();
        var dir = Directory.CreateTempSubdirectory("ccp-parse-repeat-").FullName;
        try
        {
            var path = Bogus(dir);
            var probe = typeof(DeeperTabViewModel).GetMethod("ProbeLocalDurationAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            // The real opener: tests swap DeeperLocalAudio.Open.
            var open = typeof(DeeperLocalAudio).GetMethod("OpenVlcAsync", BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(string) })!;
            for (int i = 0; i < 20; i++)
            {
                Assert.Null(await (Task<double?>)probe.Invoke(null, new object[] { path })!);
                Assert.Null(await Task.Run(() => (Task<IDeeperLocalAudio?>)open.Invoke(null, new object[] { path })!));
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}

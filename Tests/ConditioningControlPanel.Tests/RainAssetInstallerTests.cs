using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Lane R12: Rain's big files come from one pinned zip. A placeholder pin never downloads, a size or
/// sha256 mismatch keeps nothing, a zip-slip entry is refused, a good zip lands whole by rename, an
/// interrupted run's .tmp is swept, and CCP_RAIN_ASSETS_DIR (dev override) is never written to.
/// </summary>
public class RainAssetInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-rain-assets-" + Guid.NewGuid().ToString("N"));
    private string Dir => Path.Combine(_root, "rain-assets");

    public RainAssetInstallerTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch (IOException) { } }

    private static byte[] Zip(params (string name, string body)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, body) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(body);
            }
        return ms.ToArray();
    }

    private static byte[] GoodZip() => Zip(("avatar.glb", "glTF"), ("pelvis-data.js", "export default 1;"), ("runtime-info.json", "{}"));

    private static RainAssetPin PinFor(byte[] zip) =>
        new("https://example.invalid/rain-assets.zip", Convert.ToHexString(SHA256.HashData(zip)), zip.Length, "rain-assets-test");

    private RainAssetInstaller Make(RainAssetPin pin, byte[] zip, Action? onOpen = null, bool dev = false) =>
        new(Dir, pin, dev, _ => { onOpen?.Invoke(); return Task.FromResult<Stream>(new MemoryStream(zip)); });

    private void AssertNothingLeft()
    {
        Assert.False(Directory.Exists(Dir));
        Assert.False(Directory.Exists(Dir + ".tmp"));
        Assert.False(File.Exists(Dir + ".download"));
    }

    [Fact]
    public async Task PlaceholderPin_NeverDownloads()
    {
        Assert.True(RainAssetPins.Current.IsReady); // the shipped pin is real; the rule is tested on blank pins below
        Assert.EndsWith("/releases/download/rain-assets-v1/rain-assets.zip", RainAssetPins.Current.Url);
        var opened = false;
        foreach (var pin in new[] { RainAssetPins.Current with { Sha256 = RainAssetPins.Placeholder, Bytes = 1 },
                     new RainAssetPin("", "", 0, ""), new RainAssetPin("http://insecure/x.zip", new string('a', 64), 10, "v") })
        {
            var installer = Make(pin, GoodZip(), () => opened = true);
            Assert.Equal(RainAssetState.NotAvailable, installer.State);
            Assert.Equal(RainInstallResult.NotAvailable, await installer.InstallAsync(null, CancellationToken.None));
        }
        Assert.False(opened);
        AssertNothingLeft();
    }

    [Fact]
    public async Task ShaMismatch_Refuses_AndLeavesNothing()
    {
        var zip = GoodZip();
        var pin = PinFor(zip) with { Sha256 = new string('0', 64) };
        Assert.Equal(RainInstallResult.HashMismatch, await Make(pin, zip).InstallAsync(null, CancellationToken.None));
        AssertNothingLeft();
    }

    [Fact]
    public async Task SizeMismatch_Refuses_BothWays()
    {
        var zip = GoodZip();
        Assert.Equal(RainInstallResult.SizeMismatch, await Make(PinFor(zip) with { Bytes = zip.Length - 1 }, zip).InstallAsync(null, CancellationToken.None));
        Assert.Equal(RainInstallResult.SizeMismatch, await Make(PinFor(zip) with { Bytes = zip.Length + 1 }, zip).InstallAsync(null, CancellationToken.None));
        AssertNothingLeft();
    }

    [Fact]
    public async Task ZipSlipEntry_Refuses_AndWritesNothingOutside()
    {
        var zip = Zip(("avatar.glb", "glTF"), ("pelvis-data.js", "x"), ("../escaped.txt", "nope"));
        Assert.Equal(RainInstallResult.BadArchive, await Make(PinFor(zip), zip).InstallAsync(null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
        AssertNothingLeft();
        Assert.False(RainAssetInstaller.IsInside(Dir, @"C:\Windows\x.dll"));
        Assert.False(RainAssetInstaller.IsInside(Dir, "a/../../b"));
        Assert.True(RainAssetInstaller.IsInside(Dir, "sub/avatar.glb"));
        var half = Zip(("avatar.glb", "glTF"));
        Assert.Equal(RainInstallResult.BadArchive, await Make(PinFor(half), half).InstallAsync(null, CancellationToken.None));
        AssertNothingLeft();
    }

    [Fact]
    public async Task GoodZip_InstallsWhole_ReplacingAPartialFolder()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "avatar.glb"), "half");
        var zip = GoodZip();
        var pin = PinFor(zip);
        var installer = Make(pin, zip);
        Assert.Equal(RainAssetState.Missing, installer.State);
        double last = -1;
        var result = await installer.InstallAsync(new SyncProgress(f => last = f), CancellationToken.None);
        Assert.Equal(RainInstallResult.Installed, result);
        Assert.Equal(1.0, last);
        Assert.True(installer.IsInstalled());
        Assert.Equal("glTF", File.ReadAllText(Path.Combine(Dir, "avatar.glb")));
        Assert.True(File.Exists(Path.Combine(Dir, "runtime-info.json")));
        var marker = JObject.Parse(File.ReadAllText(Path.Combine(Dir, RainAssetInstaller.Marker)));
        Assert.Equal("rain-assets-test", (string?)marker["version"]);
        Assert.Equal(pin.Sha256.ToLowerInvariant(), (string?)marker["sha256"]);
        Assert.False(Directory.Exists(Dir + ".tmp") || Directory.Exists(Dir + ".old") || File.Exists(Dir + ".download"));
    }

    [Fact]
    public async Task Cancel_LeavesNothing()
    {
        var zip = GoodZip();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Equal(RainInstallResult.Cancelled, await Make(PinFor(zip), zip).InstallAsync(null, cts.Token));
        AssertNothingLeft();
    }

    [Fact]
    public async Task LeftoverTmp_IsSwept()
    {
        Directory.CreateDirectory(Dir + ".tmp");
        File.WriteAllText(Path.Combine(Dir + ".tmp", "avatar.glb"), "stale");
        File.WriteAllText(Dir + ".download", "stale");
        var installer = Make(RainAssetPins.Current with { Sha256 = RainAssetPins.Placeholder }, GoodZip());
        Assert.Equal(RainInstallResult.NotAvailable, await installer.InstallAsync(null, CancellationToken.None));
        AssertNothingLeft();
    }

    [Fact]
    public async Task EnvOverride_ReportsInstalled_WithoutWriting()
    {
        var zip = GoodZip();
        Directory.CreateDirectory(Dir + ".tmp");
        var opened = false;
        var installer = Make(PinFor(zip), zip, () => opened = true, dev: true);
        Assert.True(installer.IsInstalled());
        Assert.Equal(RainAssetState.Installed, installer.State);
        Assert.Equal(RainInstallResult.Installed, await installer.InstallAsync(null, CancellationToken.None));
        Assert.False(opened);
        Assert.False(Directory.Exists(Dir));
        Assert.True(Directory.Exists(Dir + ".tmp"));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

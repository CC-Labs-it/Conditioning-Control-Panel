using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Race;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RaceEngineFetcherTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "ccp-engine-test-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(data, "engines");
    private static byte[] Zip(string name = "electron", int mode = 0)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var licence in new[] { "LICENSE", "LICENSES.chromium.html" })
            {
                if (licence == name) continue;
                using var licenceWriter = new StreamWriter(zip.CreateEntry(licence).Open());
                licenceWriter.Write("licence");
            }
            if (name == "LICENSE" && mode != 0)
            {
                using var engine = new StreamWriter(zip.CreateEntry("electron").Open());
                engine.Write("test engine");
            }
            var entry = zip.CreateEntry(name);
            entry.ExternalAttributes = mode << 16;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("test engine");
            writer.Dispose();
            foreach (var licence in new[] { "LICENSE", "LICENSES.chromium.html" }.Where(x => x != name))
                using (var w = new StreamWriter(zip.CreateEntry(licence).Open())) w.Write("licence");
        }
        return output.ToArray();
    }
    private static RaceEnginePackage Pin(byte[] zip) => new("45.1.0", "linux-x64", new Uri("https://example.invalid/engine.zip"), Convert.ToHexString(SHA256.HashData(zip)));
    private sealed class Handler(byte[] bytes, long? length = null) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentLength = length ?? bytes.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    private sealed class Progress(Action<RaceEngineProgress> report) : IProgress<RaceEngineProgress>
    {
        public void Report(RaceEngineProgress value) => report(value);
    }
    [Fact]
    public void ManifestPinsBothArchitecturesAndRejectsOthers()
    {
        Assert.Equal("45.1.0", RaceEngineManifest.Version);
        Assert.Equal("9dafab2c57ce209f8f716f8f3e4fddd8ffe0820e2455bfbbfc00b0fc13e03a3b", RaceEngineManifest.For(Architecture.X64).Sha256);
        Assert.Equal("64b78d93d1129ab805cdc0f2710f446716afbe814a1d08e639dab3175d2285a1", RaceEngineManifest.For(Architecture.Arm64).Sha256);
        foreach (var arch in new[] { Architecture.X64, Architecture.Arm64 })
        {
            var pin = RaceEngineManifest.For(arch);
            Assert.Equal($"https://github.com/electron/electron/releases/download/v45.1.0/electron-v45.1.0-{pin.Architecture}.zip", pin.Url.AbsoluteUri);
        }
        Assert.Throws<PlatformNotSupportedException>(() => RaceEngineManifest.For(Architecture.X86));
    }
    [Fact]
    public async Task VerifiedInstallReportsProgressChmodsRetiresOldAndReusesCache()
    {
        var bytes = Zip();
        using var handler = new Handler(bytes);
        using var http = new HttpClient(handler);
        var fetcher = new RaceEngineFetcher(http, data);
        var old = Path.Combine(Root, "electron-44.0.0-linux-x64");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(Path.Combine(Root, "unrelated"));
        RaceEngineProgress last = default;
        var path = await fetcher.FetchAsync(Pin(bytes), new Progress(p => last = p));
        Assert.Equal(Path.Combine(Root, Pin(bytes).DirectoryName, "electron"), path);
        Assert.Equal("test engine", File.ReadAllText(path));
        Assert.Equal(bytes.Length, last.Received);
        Assert.Equal(bytes.Length, last.Total);
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(Path.Combine(Root, "unrelated")));
        if (!OperatingSystem.IsWindows()) Assert.NotEqual(0, (int)(File.GetUnixFileMode(path) & UnixFileMode.UserExecute));
        Assert.Equal(path, await fetcher.FetchAsync(Pin(bytes)));
        Assert.Equal(1, handler.Calls);
        Assert.Empty(Directory.GetDirectories(Root, ".fetch-*"));
    }
    [Fact]
    public async Task InstalledRequiresExecutableAndMatchingReceipt()
    {
        var bytes = Zip();
        using var http = new HttpClient(new Handler(bytes));
        var fetcher = new RaceEngineFetcher(http, data);
        var pin = Pin(bytes);
        Assert.False(fetcher.IsInstalled(pin));
        var exe = await fetcher.FetchAsync(pin);
        Assert.True(fetcher.IsInstalled(pin));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe)!, ".verified-sha256"), "wrong");
        Assert.False(fetcher.IsInstalled(pin));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe)!, ".verified-sha256"), pin.Sha256);
        File.Delete(exe);
        Assert.False(fetcher.IsInstalled(pin));
    }
    [Fact]
    public async Task MissingLicencesRejectsArchive()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("electron").Open())) writer.Write("engine");
        var bytes = output.ToArray();
        using var http = new HttpClient(new Handler(bytes));
        await Assert.ThrowsAsync<InvalidDataException>(() => new RaceEngineFetcher(http, data).FetchAsync(Pin(bytes)));
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }
    [Fact]
    public async Task BadChecksumNeverUnpacksAndKeepsOldVersion()
    {
        var bytes = Zip("../escaped"); // a checksum failure must happen BEFORE archive inspection
        using var http = new HttpClient(new Handler(bytes));
        var old = Path.Combine(Root, "electron-old");
        Directory.CreateDirectory(old);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new RaceEngineFetcher(http, data)
            .FetchAsync(Pin(bytes) with { Sha256 = new string('0', 64) }));
        Assert.Contains("checksum", error.Message);
        Assert.True(Directory.Exists(old));
        Assert.Equal(new[] { old }, Directory.GetDirectories(Root));
        Assert.Empty(Directory.GetFiles(Root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task CancelDuringDownloadRemovesPartialAndAllowsRetry()
    {
        var bytes = Zip();
        using var http = new HttpClient(new Handler(bytes));
        using var cancel = new CancellationTokenSource();
        var fetcher = new RaceEngineFetcher(http, data);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchAsync(Pin(bytes),
            new Progress(_ => cancel.Cancel()), cancel.Token));
        Assert.Empty(Directory.GetFileSystemEntries(Root));
        Assert.True(File.Exists(await fetcher.FetchAsync(Pin(bytes))));
    }
    [Theory]
    [InlineData("../escaped")]
    [InlineData("/absolute")]
    [InlineData("a/../../escaped")]
    [InlineData("..\\escaped")]
    [InlineData("C:/escaped")]
    public async Task ArchiveCannotEscapeCache(string name)
    {
        var bytes = Zip(name);
        using var http = new HttpClient(new Handler(bytes));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new RaceEngineFetcher(http, data).FetchAsync(Pin(bytes)));
        Assert.True(error.Message.StartsWith("Unsafe race engine archive") || error.Message.StartsWith("Race engine archive escapes cache"), error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(Root));
        Assert.False(File.Exists(Path.Combine(data, "escaped")));
    }
    [Fact]
    public async Task ArchiveLinksAndCacheLinksAreRefused()
    {
        var bytes = Zip("electron", 0xa1ff);
        using var http = new HttpClient(new Handler(bytes));
        await Assert.ThrowsAsync<InvalidDataException>(() => new RaceEngineFetcher(http, data).FetchAsync(Pin(bytes)));
        if (OperatingSystem.IsWindows()) return;
        var outside = Path.Combine(data, "outside");
        Directory.CreateDirectory(outside);
        Directory.Delete(Root);
        Directory.CreateSymbolicLink(Root, outside);
        await Assert.ThrowsAsync<IOException>(() => new RaceEngineFetcher(http, data).FetchAsync(Pin(bytes)));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SymlinkedUserDataOrParentAllowsVerifiedInstall(bool userDataIsLink)
    {
        if (OperatingSystem.IsWindows()) return;
        var actual = Path.Combine(data, "actual");
        var alias = Path.Combine(data, "alias");
        Directory.CreateDirectory(actual);
        Directory.CreateSymbolicLink(alias, actual);
        var userData = userDataIsLink ? alias : Path.Combine(alias, "profile");
        var cache = Path.Combine(userData, "engines");
        var old = Path.Combine(cache, "electron-old");
        Directory.CreateDirectory(old);
        var bytes = Zip();
        using var handler = new Handler(bytes);
        using var http = new HttpClient(handler);
        var fetcher = new RaceEngineFetcher(http, userData);
        var path = await fetcher.FetchAsync(Pin(bytes));
        Assert.Equal(Path.Combine(cache, Pin(bytes).DirectoryName, "electron"), path);
        Assert.Equal("test engine", File.ReadAllText(path));
        Assert.False(Directory.Exists(old));
        Assert.Equal(path, await fetcher.FetchAsync(Pin(bytes)));
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task TruncatedDownloadRemovesPartialFile()
    {
        var bytes = Zip();
        using var http = new HttpClient(new Handler(bytes, bytes.Length + 1));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new RaceEngineFetcher(http, data).FetchAsync(Pin(bytes)));
        Assert.Contains("Incomplete", error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }
    [Fact]
    public async Task MissingExecutableDoesNotRetireOldEngine()
    {
        var bytes = Zip("LICENSE");
        using var http = new HttpClient(new Handler(bytes));
        var old = Path.Combine(Root, "electron-old");
        Directory.CreateDirectory(old);
        await Assert.ThrowsAsync<InvalidDataException>(() => new RaceEngineFetcher(http, data).FetchAsync(Pin(bytes)));
        Assert.Equal(new[] { old }, Directory.GetDirectories(Root));
    }
    [Theory]
    [InlineData("LICENSE", false)]
    [InlineData("LICENSES.chromium.html", true)]
    public async Task InvalidLicenceDoesNotPromoteOrPrune(string name, bool empty)
    {
        var bytes = ZipWithInvalidLicence(name, empty);
        using var http = new HttpClient(new Handler(bytes));
        var old = Path.Combine(Root, "electron-old");
        Directory.CreateDirectory(old);
        await Assert.ThrowsAsync<InvalidDataException>(() => new RaceEngineFetcher(http, data).FetchAsync(Pin(bytes)));
        Assert.False(Directory.Exists(Path.Combine(Root, Pin(bytes).DirectoryName)));
        Assert.True(Directory.Exists(old));
    }
    [Fact]
    public async Task SymlinkLicenceDoesNotPromoteOrPrune()
    {
        var bytes = Zip("LICENSE", 0xa1ff);
        using var http = new HttpClient(new Handler(bytes));
        var old = Path.Combine(Root, "electron-old");
        Directory.CreateDirectory(old);
        await Assert.ThrowsAsync<InvalidDataException>(() => new RaceEngineFetcher(http, data).FetchAsync(Pin(bytes)));
        Assert.False(Directory.Exists(Path.Combine(Root, Pin(bytes).DirectoryName)));
        Assert.True(Directory.Exists(old));
    }
    [Fact]
    public async Task CachedReceiptWithoutLicenceIsNotReused()
    {
        var bytes = Zip();
        using var handler = new Handler(bytes);
        using var http = new HttpClient(handler);
        var fetcher = new RaceEngineFetcher(http, data);
        var path = await fetcher.FetchAsync(Pin(bytes));
        File.Delete(Path.Combine(Path.GetDirectoryName(path)!, "LICENSE"));
        Assert.Equal(path, await fetcher.FetchAsync(Pin(bytes)));
        Assert.Equal(2, handler.Calls);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "LICENSE")));
    }
    [Fact]
    public async Task CachedSymlinkLicenceIsNotReused()
    {
        if (OperatingSystem.IsWindows()) return;
        var bytes = Zip();
        using var handler = new Handler(bytes);
        using var http = new HttpClient(handler);
        var fetcher = new RaceEngineFetcher(http, data);
        var path = await fetcher.FetchAsync(Pin(bytes));
        var licence = Path.Combine(Path.GetDirectoryName(path)!, "LICENSE");
        File.Delete(licence);
        File.CreateSymbolicLink(licence, Path.Combine(Path.GetDirectoryName(path)!, "LICENSES.chromium.html"));
        Assert.Equal(path, await fetcher.FetchAsync(Pin(bytes)));
        Assert.Equal(2, handler.Calls);
        Assert.Null(new FileInfo(licence).LinkTarget);
    }
    private static byte[] ZipWithInvalidLicence(string name, bool empty)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var item in new[] { "electron", "LICENSE", "LICENSES.chromium.html" })
            {
                if (item == name && !empty) continue;
                using var writer = new StreamWriter(zip.CreateEntry(item).Open());
                if (item != name) writer.Write("content");
            }
        }
        return output.ToArray();
    }
    [Theory]
    [InlineData("../outside")]
    [InlineData("..")]
    [InlineData("/tmp/outside")]
    public async Task PinCannotChooseAnOutsideDirectory(string version)
    {
        var bytes = Zip();
        using var handler = new Handler(bytes);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new RaceEngineFetcher(http, data).FetchAsync(Pin(bytes) with { Version = version }));
        Assert.Equal(0, handler.Calls);
        Assert.False(Directory.Exists(data));
    }
    public void Dispose()
    {
        if (Directory.Exists(data)) Directory.Delete(data, true);
    }
}

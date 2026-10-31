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
            var entry = zip.CreateEntry(name);
            entry.ExternalAttributes = mode << 16;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("test engine");
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

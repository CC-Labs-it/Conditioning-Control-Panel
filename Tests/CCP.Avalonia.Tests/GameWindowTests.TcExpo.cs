using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Llama Expo download (WPF TcHostService.Expo.cs, main 6dca5d6d5b, 1dbdeaebae) through a fake HTTP
/// handler into the test sandbox: fetch, sha, unpack, stamp, the served base, and cancel / panic / close.</summary>
public sealed partial class GameWindowTests
{
    private static TcExpoFake TcFake = new();
    private static string TcRoot = "";

    internal sealed class TcExpoFake : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Answer(r, ct);
        }
    }

    private static string TcManifest(long bytes, string sha) =>
        $"{{\"v\":1,\"file\":\"expo-1.zip\",\"bytes\":{bytes},\"sha256\":\"{sha}\",\"version\":\"1.0.0\",\"minApp\":\"1.0\"}}";

    private static byte[] TcExpoZip(string zones)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            using (var w = new StreamWriter(zip.CreateEntry(TcExpoPackRules.RootProbe).Open())) w.Write(zones);
        return ms.ToArray();
    }

    /// <summary>The release tag through the fake: the manifest for <paramref name="zip"/> (sha as given) and the zip.</summary>
    private static void ServeTcExpo(byte[] zip, string? sha = null, Func<CancellationToken, Task>? beforeZip = null)
    {
        var manifest = TcManifest(zip.Length, sha ?? Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant());
        TcFake.Answer = async (r, ct) =>
        {
            var url = r.RequestUri!.ToString();
            if (url == TcExpoPackRules.ManifestUrl) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) };
            if (url != TcExpoPackRules.ReleaseBase + "expo-1.zip") return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (beforeZip != null) await beforeZip(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        };
    }

    private static bool PumpUntil(Func<bool> done)
    {
        var sw = Stopwatch.StartNew();
        while (!done())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(15)) return false;
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        return true;
    }

    [Fact]
    public void Tc_ExpoDownloadsIntoTheSandbox_AndTheBaseItSendsIsServed() => RunTc("full", (_, w, sent) =>
    {
        ServeTcExpo(TcExpoZip("{\"zones\":1}"));
        w.HandleMessage("{\"type\":\"ready\"}");
        w.HandleMessage("{\"type\":\"expo-fetch\"}");
        Assert.True(PumpUntil(() => sent.Any(f => (string?)f["type"] is "expo-ready" or "expo-failed")));
        var ready = sent.Last(f => (string?)f["type"] is "expo-ready" or "expo-failed");
        Assert.Equal("expo-ready", (string?)ready["type"]);
        Assert.Contains(sent, f => (string?)f["type"] == "expo-progress");
        Assert.True(File.Exists(TcExpoPackRules.StampPath(TcRoot, "1.0.0")));
        Assert.False(Directory.Exists(Path.Combine(TcRoot, ".downloads")) && Directory.EnumerateFiles(Path.Combine(TcRoot, ".downloads")).Any());

        // A real fetch through the server the window uses: the base the page got serves the pack, nothing above it.
        var server = GameWindow.Server();
        Assert.Equal(server.ExpoBase, (string?)ready["base"]);
        using var http = new HttpClient();
        var zones = http.GetAsync((string)ready["base"]! + "zones/zones.json?ccp_t=" + server.Token).GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.OK, zones.StatusCode);
        Assert.Equal("{\"zones\":1}", zones.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        var above = http.GetAsync((string)ready["base"]! + "..%2F1.0.0.ready?ccp_t=" + server.Token).GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.NotFound, above.StatusCode);

        // The next ready reads it as ready; closing the window stops serving it.
        w.HandleMessage("{\"type\":\"ready\"}");
        var expo = (JObject)sent.Last(f => (string?)f["type"] == "init")["caps"]!["expo"]!;
        Assert.Equal("ready", (string?)expo["state"]);
        w.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(HttpStatusCode.NotFound, http.GetAsync(server.ExpoBase + "zones/zones.json?ccp_t=" + server.Token).GetAwaiter().GetResult().StatusCode);
    });

    [Fact]
    public void Tc_ExpoShaMismatchIsRefused() => RunTc("full", (_, w, sent) =>
    {
        ServeTcExpo(TcExpoZip("{}"), sha: new string('0', 64));
        w.HandleMessage("{\"type\":\"expo-fetch\"}");
        Assert.True(PumpUntil(() => sent.Any(f => (string?)f["type"] == "expo-failed")));
        Assert.Equal(TcExpoRules.ReasonRefused, (string?)sent.Last()["reason"]);
        Assert.False(File.Exists(TcExpoPackRules.StampPath(TcRoot, "1.0.0")));
    });

    [Fact]
    public void Tc_ExpoSmallEditionIsRefusedWithoutARequest() => RunTc("small", (_, w, sent) =>
    {
        w.HandleMessage("{\"type\":\"expo-fetch\"}");
        Assert.Equal(TcExpoRules.ReasonRefused, (string?)Assert.Single(sent, f => (string?)f["type"] == "expo-failed")["reason"]);
        Assert.Equal(0, TcFake.Calls);
    });

    [Theory]
    [InlineData("cancel")]
    [InlineData("panic")]
    [InlineData("close")]
    public void Tc_ExpoCancelPanicAndCloseStopARunningDownload(string route) => RunTc("full", (shell, w, sent) =>
    {
        var entered = new ManualResetEventSlim();
        var stopped = new ManualResetEventSlim();
        ServeTcExpo(TcExpoZip("{}"), beforeZip: async ct =>
        {
            entered.Set();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { if (ct.IsCancellationRequested) stopped.Set(); }
        });
        w.HandleMessage("{\"type\":\"expo-fetch\"}");
        Assert.True(PumpUntil(() => entered.IsSet));
        Assert.Equal(TcExpoPhase.Fetching, w.TcExpo.Phase);
        if (route == "cancel") w.HandleMessage("{\"type\":\"expo-cancel\"}");
        else if (route == "panic") PanicSurfaces.StopAll("tc expo panic test", shell);
        else w.Close();
        Assert.True(PumpUntil(() => stopped.IsSet));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(TcExpoPhase.Absent, w.TcExpo.Phase);
        Assert.DoesNotContain(sent, f => (string?)f["type"] is "expo-ready" or "expo-failed");
        Assert.Null(WebAssetServer.ExpoFolder);
    });
}

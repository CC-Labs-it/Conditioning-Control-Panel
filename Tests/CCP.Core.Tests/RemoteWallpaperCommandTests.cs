using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class RemoteWallpaperCommandTests
{
    private sealed class Desktop : IWallpaperPlatform
    {
        public readonly List<string> Writes = new();
        public string? Capture() => "original";
        public bool Apply(string path) { Writes.Add(path); return true; }
        public bool Restore(string snapshot) { Writes.Add(snapshot); return true; }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        public readonly Desktop Desktop = new();
        public readonly WallpaperHost Host;
        private readonly WallpaperHost? _previous = RemoteCommands.Wallpaper;
        private readonly string _folder = CoreSettings.Current.WallpaperSourceFolder;
        private readonly string _original = CoreSettings.Current.WallpaperOriginalPath;
        public Fixture()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "one.png"), "image");
            CoreSettings.Current.WallpaperSourceFolder = Folder;
            CoreSettings.Current.WallpaperOriginalPath = "";
            Host = new WallpaperHost(Desktop);
            RemoteCommands.Wallpaper = Host;
        }
        public async Task Trigger()
        {
            Assert.Null(RemoteCommands.Execute("trigger_wallpaper", null));
            await Host.WhenIdle;
            Assert.True(Host.IsActive);
            Assert.Equal(Path.Combine(Folder, "one.png"), Desktop.Writes.Last());
            Assert.Equal("original", CoreSettings.Current.WallpaperOriginalPath);
        }
        public void Dispose()
        {
            Host.DeactivateAsync().GetAwaiter().GetResult();
            RemoteCommands.Wallpaper = _previous;
            CoreSettings.Current.WallpaperSourceFolder = _folder;
            CoreSettings.Current.WallpaperOriginalPath = _original;
            Directory.Delete(Folder, true);
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop(); // a relay StartAsync takes the static session
        }
    }

    [Fact]
    public async Task TriggerUsesHostAndUnseededVerbsRefuse()
    {
        using var f = new Fixture();
        RemoteCommands.Wallpaper = null;
        Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("trigger_wallpaper", null));
        Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("stop_wallpaper", null));
        RemoteCommands.Wallpaper = f.Host;
        await f.Trigger();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAndPanicRestoreOriginal(bool panic)
    {
        using var f = new Fixture();
        await f.Trigger();
        if (panic) RemoteCommands.StopEffects(force: true);
        else Assert.Null(RemoteCommands.Execute("stop_wallpaper", null));
        await f.Host.WhenIdle;
        Assert.Equal("original", f.Desktop.Writes.Last());
        Assert.Equal("", CoreSettings.Current.WallpaperOriginalPath);
        Assert.False(f.Host.IsActive);
    }

    [Fact]
    public async Task LocalRunLeaveKeepsWallpaper()
    {
        using var f = new Fixture();
        var s = CoreSettings.Current;
        var oldAudio = s.AudioOnlySession;
        try
        {
            await f.Trigger();
            s.AudioOnlySession = true;
            CoreEngine.Start(systemInitiated: true);
            RemoteCommands.StopEffects(force: false);
            await f.Host.WhenIdle;
            Assert.True(f.Host.IsActive);
            Assert.Single(f.Desktop.Writes);
            Assert.Equal("original", s.WallpaperOriginalPath);
        }
        finally { CoreEngine.Stop(); s.AudioOnlySession = oldAudio; }
    }

    private sealed class RelayHandler : HttpMessageHandler
    {
        public JObject? Status;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (request.RequestUri!.AbsolutePath == "/v2/remote/status") Status = body;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath == "/v2/remote/start" ? "{\"code\":\"ABC123\"}" : "{}") };
        }
    }

    [Fact]
    public async Task StatusReportsWallpaperOnlyWhileActive()
    {
        using (var f = new Fixture())
        {
            var handler = new RelayHandler();
            using var relay = new RemoteRelay(() => "token", () => "user", "1", RemoteCommands.Execute, _ => { }, handler) { AutoPoll = false };
            await relay.StartAsync("full");
            async Task<bool> Reported()
            {
                await relay.PushStatusNowAsync();
                return handler.Status!["active_services"]!.Values<string>().Contains("wallpaper");
            }
            Assert.False(await Reported());
            await f.Trigger();
            Assert.True(await Reported());
            Assert.Null(RemoteCommands.Execute("stop_wallpaper", null));
            await f.Host.WhenIdle;
            Assert.False(await Reported());
        }
        // StartAsync took the static remote session; a later SessionStatics test must not inherit it.
        Assert.False(ConditioningControlPanel.Services.Remote.RemoteWearer.Holding);
    }
}

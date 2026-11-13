using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The automatic cloud settings backup (WPF ProfileSyncService.BackupSettingsAsync with force: false,
/// ProfileSyncService.cs:4317-4320, 4464-4510, 4582-4588): one upload per five minutes, never over settings
/// restored from a daily backup, and a 413 ends automatic backups for the session.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class AutoSettingsBackupTests : IDisposable
{
    private const string Upload = "https://codebambi-proxy.vercel.app/v2/user/backup-settings";
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly Fake _fake = new();
    private DateTime _now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    public AutoSettingsBackupTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
    }

    public void Dispose()
    {
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
    }

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<string> Seen = new();
        public HttpStatusCode UploadStatus = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var url = r.RequestUri!.AbsoluteUri;
            Seen.Add(url);
            var status = url == Upload ? UploadStatus : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"backup\":null}") });
        }
    }

    private int Uploads => _fake.Seen.Count(u => u == Upload);

    private AutoSettingsBackup NewGate() => new(() => new CloudSettingsBackup(_fake)) { UtcNow = () => _now };

    private static AppSettings Signed() => new() { UnifiedId = "u_auto", AuthToken = "TOK" };

    [Fact]
    public async Task OneUploadPerFiveMinutes()
    {
        var gate = NewGate();
        var s = Signed();
        Assert.True(await gate.RunAsync(s, restoredFromBackup: false));
        Assert.Equal(1, Uploads);
        _now = _now.AddMinutes(4).AddSeconds(59);
        Assert.False(await gate.RunAsync(s, restoredFromBackup: false));
        Assert.Equal(1, Uploads);
        _now = _now.AddSeconds(1);
        Assert.True(await gate.RunAsync(s, restoredFromBackup: false));
        Assert.Equal(2, Uploads);
    }

    [Fact]
    public async Task SettingsFromADailyBackupAreNeverUploadedAutomatically()
    {
        var gate = NewGate();
        Assert.False(await gate.RunAsync(Signed(), restoredFromBackup: true));
        Assert.Empty(_fake.Seen);
        // The refusal did not spend the window: the first reconciled save backs up at once.
        Assert.True(await gate.RunAsync(Signed(), restoredFromBackup: false));
        Assert.Equal(1, Uploads);
    }

    [Fact]
    public async Task TooLargeStopsAutomaticBackupsForTheSession()
    {
        var gate = NewGate();
        _fake.UploadStatus = HttpStatusCode.RequestEntityTooLarge;
        Assert.False(await gate.RunAsync(Signed(), restoredFromBackup: false));
        Assert.Equal(1, Uploads);
        _fake.UploadStatus = HttpStatusCode.OK;
        _now = _now.AddHours(1);
        Assert.False(await gate.RunAsync(Signed(), restoredFromBackup: false));
        Assert.Equal(1, Uploads);
    }

    [Fact]
    public async Task OtherRefusalsOnlyWaitForTheNextWindow()
    {
        var gate = NewGate();
        _fake.UploadStatus = HttpStatusCode.InternalServerError;
        Assert.False(await gate.RunAsync(Signed(), restoredFromBackup: false));
        _fake.UploadStatus = HttpStatusCode.OK;
        _now = _now.AddMinutes(5);
        Assert.True(await gate.RunAsync(Signed(), restoredFromBackup: false));
        Assert.Equal(2, Uploads);
    }

    [Fact]
    public async Task BackupNowRestartsTheWindow()
    {
        var gate = NewGate();
        gate.NoteForced();
        _now = _now.AddMinutes(1);
        Assert.False(await gate.RunAsync(Signed(), restoredFromBackup: false));
        Assert.Empty(_fake.Seen);
    }

    [Fact]
    public async Task SignedOutOfflineOrTokenlessSendsNothing()
    {
        var gate = NewGate();
        Assert.False(await gate.RunAsync(null, restoredFromBackup: false));
        Assert.False(await gate.RunAsync(new AppSettings { AuthToken = "TOK" }, restoredFromBackup: false));
        Assert.False(await gate.RunAsync(new AppSettings { UnifiedId = "u_auto", AuthToken = "TOK", OfflineMode = true }, restoredFromBackup: false));
        Assert.Empty(_fake.Seen);
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public sealed class SyncWalletSentTests
{
    [Theory]
    [InlineData(0, false, 60)]
    [InlineData(20, false, 120)]
    [InlineData(0, true, 100)]
    public async Task ReplyUsesSentBalanceUnlessCreditOrReset(int credit, bool reset, int expected)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var directory = Path.Combine(Path.GetTempPath(), "ccp-wallet-sync-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(Path.Combine(directory, "settings.json"))
        {
            SaveDebounceDueTimeMilliseconds = 60000
        };
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var wire = new Wire(reset);
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, wire);
        Task<bool>? push = null;
        try
        {
            var s = settings.Current;
            s.UnifiedId = "wallet-test";
            s.AuthToken = "fake";
            s.OfflineMode = false;
            s.LastConfirmedServerXp = 0;
            s.SkillPoints = 100;
            sync.MarkLoaded(null);
            var now = DateTime.UtcNow;
            sync.UtcNow = () => now;
            Assert.True(await sync.PushAsync("seed"));
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            push = sync.PushAsync("wallet-test");
            await wire.Received.Task;
            Assert.Equal(100, wire.Sent);
            s.SkillPoints += credit;
            wire.Release.SetResult(true);
            Assert.True(await push);
            Assert.Equal(expected, s.SkillPoints);
            Assert.True(settings.HasPendingSave);
            settings.SealForReset();
            Assert.False(settings.HasPendingSave);
        }
        finally
        {
            wire.Release.TrySetResult(true);
            try
            {
                if (push != null) await push;
            }
            finally
            {
                sync.Reset();
                settings.SealForReset();
                CoreSettings.ServiceProvider = oldSettings;
                CoreAccount.IsLoggedInProvider = oldLogin;
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class Wire(bool reset) : HttpMessageHandler
    {
        internal int Sent;
        private int calls;
        internal readonly TaskCompletionSource<bool> Received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (++calls == 1)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"skill_points\":100,\"unlocked_skills\":[]}") };
            Sent = JObject.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).Value<int>("skill_points");
            Received.TrySetResult(true);
            await Release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reset ? "{\"skill_points\":60,\"level_reset\":true}" : "{\"skill_points\":60}") };
        }
    }
}

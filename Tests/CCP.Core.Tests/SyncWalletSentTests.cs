using System;
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
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var wire = new Wire(reset);
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, wire);
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
            var push = sync.PushAsync("wallet-test");
            await wire.Received.Task;
            Assert.Equal(100, wire.Sent);
            s.SkillPoints += credit;
            wire.Release.SetResult(true);
            Assert.True(await push);
            Assert.Equal(expected, s.SkillPoints);
        }
        finally
        {
            wire.Release.TrySetResult(true);
            sync.Reset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
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

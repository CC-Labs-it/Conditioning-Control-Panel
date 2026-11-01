using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF seeds its stores before constructing the providers (App.xaml.cs:692-707,2763)
// and initializes Patreon at :4622. Exercise the same port path with synthetic data only.
public class InstallerAccountMigrationTests
{
    private sealed class Unavailable : HttpMessageHandler
    {
        public string? Bearer;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bearer = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private static string Token() => JsonConvert.SerializeObject(new PatreonTokenData
    {
        AccessToken = "synthetic-access", RefreshToken = "synthetic-refresh", ExpiresAt = DateTime.UtcNow.AddDays(1)
    });

    private static string Cache() => JsonConvert.SerializeObject(new PatreonCachedState
    {
        Tier = PatreonTier.Level2, IsActive = true, CacheExpiresAt = DateTime.UtcNow.AddHours(1)
    });

    private static async Task CheckStartup(Func<string, string?> read, Action<string, string?> write)
    {
        CoreSecrets.RetrieveProvider = read;
        CoreSecrets.StoreProvider = write;
        File.WriteAllText(Path.Combine(TestUserDataProfile.Root, "settings.json"),
            "{\"PatreonTier\":2,\"UnifiedId\":\"synthetic-user\",\"PlayerLevel\":12}");
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var settings = service.Current;
        var unavailable = new Unavailable();
        Assert.True(AccountSeed.Seed(prefix => new ProviderSubscription(prefix, () => settings, unavailable),
            peer => new DiscordAccount(() => peer, () => settings)));
        AccountSeed.RestoreSession();
        Assert.Equal("synthetic-user", CoreAccount.UnifiedUserId);
        Assert.True(AccountSeed.Patreon!.IsAuthenticated);
        Assert.Equal(PatreonTier.Level2, AccountSeed.Patreon.CurrentTier);
        Assert.True(CoreAccount.HasLabAccess);
        await AccountSeed.InitializeAsync();
        Assert.Equal(PatreonTier.Level2, AccountSeed.Patreon.CurrentTier);
        Assert.Equal("synthetic-access", unavailable.Bearer);
        Assert.Equal(2, settings.PatreonTier);
        Assert.Equal("synthetic-access", AccountSeed.Patreon.GetAccessToken());
        Assert.Equal("synthetic-session", settings.AuthToken);
        service.SaveImmediate();
        var reloaded = new SettingsService().Current;
        Assert.Equal(2, reloaded.PatreonTier);
        Assert.Equal(12, reloaded.PlayerLevel);
        Assert.Equal("synthetic-user", reloaded.UnifiedId);
    }

    [Fact]
    public async Task SyntheticWpfTokensSurviveAnUnavailableValidation()
    {
        var data = new Dictionary<string, string?>
        { ["patreon_auth"] = Token(), ["patreon_cache"] = Cache(), ["authtoken"] = "synthetic-session" };
        await CheckStartup(n => data.GetValueOrDefault(n), (n, v) => data[n] = v);
    }

    [Fact]
    public async Task WpfDpapiTokensSurviveAccountStartupOnWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("DPAPI is Windows-only; synthetic store case runs on all platforms.");
        else await WindowsStartup();
    }

    [SupportedOSPlatform("windows")]
    private static async Task WindowsStartup()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-installer-migration-").FullName;
        try
        {
            // WPF SecureTokenStorage.cs:27-42,81-102 and SecureAuthTokenStore.cs use these files and entropy.
            void WpfWrite(string file, string entropy, string value) => File.WriteAllBytes(Path.Combine(dir, file),
                ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(entropy), DataProtectionScope.CurrentUser));
            WpfWrite("patreon_auth.dat", "ConditioningControlPanel_Patreon_v1", Token());
            WpfWrite("patreon_cache.dat", "ConditioningControlPanel_Patreon_v1", Cache());
            WpfWrite("auth_token.dat", "ConditioningControlPanel_AuthToken_v1", "synthetic-session");
            await CheckStartup(n => Dpapi.Read(dir, n), (n, v) => Assert.True(Dpapi.Write(dir, n, v)));
        }
        finally { Directory.Delete(dir, true); }
    }
}

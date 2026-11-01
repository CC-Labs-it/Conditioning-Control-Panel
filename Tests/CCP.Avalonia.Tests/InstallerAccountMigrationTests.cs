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

    private static async Task CheckStartup(Func<string, string?> read, Action<string, string?> write, Action? afterSeed = null)
    {
        var snapshot = ProcessStateSnapshot.Take(repairLateWrites: false);
        var intakePass = new IntakePassService();
        try
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
                peer => new DiscordAccount(() => peer, () => settings), intakePass));
            afterSeed?.Invoke();
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
        finally
        {
            intakePass.Dispose();
            snapshot.Restore();
        }
    }

    [Fact]
    public async Task SyntheticWpfTokensSurviveAnUnavailableValidation()
    {
        var data = new Dictionary<string, string?>
        { ["patreon_auth"] = Token(), ["patreon_cache"] = Cache(), ["authtoken"] = "synthetic-session" };
        await CheckStartup(n => data.GetValueOrDefault(n), (n, v) => data[n] = v);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StartupRestoresProcessStateEvenWhenAnAssertionFails(bool fail, bool fileExists)
    {
        CoreSecrets.RetrieveProvider = _ => null;
        CoreSecrets.StoreProvider = (_, _) => { };
        CoreAccount.HasLabAccessProvider = () => false;
        CoreAccount.UnifiedUserId = "before-migration";
        object?[] State() => new object?[]
        {
            CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, CoreSettings.ServiceProvider,
            CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider, CoreAccount.IsWhitelistedProvider,
            CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider,
            CoreAccount.ChangeDisplayNameProvider, CoreAccount.DeleteAccountProvider,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreProgram.HasPremiumProvider,
            AuthRecovery.Recovered, AuthRecovery.ProviderRevalidate,
            AccountSeed.Patreon, AccountSeed.SubscribeStar, AccountSeed.Discord,
        };
        var before = State();
        var whitelist = AccountSeed.SandboxWhitelisted;
        var path = Path.Combine(TestUserDataProfile.Root, "settings.json");
        if (fileExists) File.WriteAllText(path, "{\"PlayerLevel\":9}");
        else File.Delete(path);
        var existed = File.Exists(path);
        var original = existed ? File.ReadAllBytes(path) : null;
        var pass = ConditioningControlPanel.Avalonia.App.IntakePass;
        var detach = typeof(IntakePassService).GetField("_detachEntitlement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var handler = typeof(IntakePassService).GetField("_entitlementHandler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var originalDetach = detach.GetValue(pass);
        var originalHandler = handler.GetValue(pass);
        var data = new Dictionary<string, string?>
        { ["patreon_auth"] = Token(), ["patreon_cache"] = Cache(), ["authtoken"] = "synthetic-session" };
        if (fail)
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => CheckStartup(n => data.GetValueOrDefault(n),
                (n, v) => data[n] = v, () => Assert.Fail("injected assertion")));
            Assert.Contains("injected assertion", ex.Message);
        }
        else await CheckStartup(n => data.GetValueOrDefault(n), (n, v) => data[n] = v);

        var after = State();
        for (var i = 0; i < before.Length; i++) Assert.Same(before[i], after[i]);
        Assert.Equal("before-migration", CoreAccount.UnifiedUserId);
        Assert.Equal(whitelist, AccountSeed.SandboxWhitelisted);
        Assert.Equal(existed, File.Exists(path));
        if (existed) Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Same(originalDetach, detach.GetValue(pass));
        Assert.Same(originalHandler, handler.GetValue(pass));
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

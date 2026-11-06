using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The head's CoreSecrets provider. The two Linux tests talk to a real Secret Service, so they
/// run ONLY inside scripts/secrets-roundtrip.sh's container (throwaway gnome-keyring on its own
/// session bus) and skip everywhere else - never the user's keyring. The DPAPI test runs on
/// Windows CI.
/// </summary>
public sealed class SecretStoreTests
{
    private const string Json = "{\"AccessToken\":\"acc\",\"RefreshToken\":\"ref\",\"ExpiresAt\":\"2030-01-01T00:00:00Z\"}";

    [Theory]
    [InlineData("authtoken", "auth_token.dat", "ConditioningControlPanel_AuthToken_v1")]
    [InlineData("apikey", "api_key.dat", "ConditioningControlPanel_ApiKey_v1")]
    [InlineData("patreon_auth", "patreon_auth.dat", "ConditioningControlPanel_Patreon_v1")]
    [InlineData("patreon_cache", "patreon_cache.dat", "ConditioningControlPanel_Patreon_v1")]
    [InlineData("discord_auth", "discord_auth.dat", "ConditioningControlPanel_Discord_v1")]
    [InlineData("substar_cache", "substar_cache.dat", "ConditioningControlPanel_Substar_v1")]
    public void DpapiSlotsAreWpfs(string name, string file, string entropy)
    {
        var (path, e) = Dpapi.Slot("d", name);
        Assert.Equal(Path.Combine("d", file), path);
        Assert.Equal(entropy, Encoding.UTF8.GetString(e));
    }

    [Fact]
    public void DpapiReadsWpfWrittenFiles()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("DPAPI is Windows-only; runs on Windows CI.");
        else DpapiRoundTrip();
    }

    [SupportedOSPlatform("windows")]
    private static void DpapiRoundTrip()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-dpapi-").FullName;
        try
        {
            // Written the way WPF SecureTokenStorage.StoreTokens does (SecureTokenStorage.cs:59-67).
            var wpf = Encoding.UTF8.GetBytes("ConditioningControlPanel_Patreon_v1");
            File.WriteAllBytes(Path.Combine(dir, "patreon_auth.dat"),
                ProtectedData.Protect(Encoding.UTF8.GetBytes(Json), wpf, DataProtectionScope.CurrentUser));
            Assert.Equal(Json, Dpapi.Read(dir, "patreon_auth"));

            Assert.True(Dpapi.Write(dir, "authtoken", "tok"));
            var bytes = File.ReadAllBytes(Path.Combine(dir, "auth_token.dat"));
            Assert.Equal("tok", Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes,
                Encoding.UTF8.GetBytes("ConditioningControlPanel_AuthToken_v1"), DataProtectionScope.CurrentUser)));

            Assert.True(Dpapi.Write(dir, "authtoken", null));
            Assert.False(File.Exists(Path.Combine(dir, "auth_token.dat")));
            Assert.Null(Dpapi.Read(dir, "authtoken"));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>TestUserDataProfile sets CCP_USERDATA_DIR before this loads, as kc and CI sandboxes do.</summary>
    [Fact]
    public void ASandboxedProfileIsMemoryOnly()
    {
        Assert.True(SecretStore.Sandboxed);   // first: a broken flag must fail before anything reaches a keyring
        SecretStore.NotRemembered = false;
        SecretStore.Store("sandbox_probe", "v");
        Assert.Equal("v", SecretStore.Retrieve("sandbox_probe"));
        // Memory-only by design, not a missing Secret Service: no "no secret store" notice.
        Assert.False(SecretStore.NotRemembered);
    }

    [Fact]
    public void LibsecretRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("CCP_SECRETS_ROUNDTRIP") != "1")
            Assert.Skip("Container-only (scripts/secrets-roundtrip.sh): never the user's keyring.");
        SecretStore.Sandboxed = false;   // the test profile sets CCP_USERDATA_DIR; this run owns its keyring

        SecretStore.Store("patreon_auth", Json);
        Assert.Equal(Json, Libsecret.Lookup("patreon_auth"));   // in the keyring, not just the cache
        Assert.True(Libsecret.Write("authtoken", "tok"));
        Assert.Equal("tok", Libsecret.Lookup("authtoken"));
        Assert.Null(Libsecret.Lookup("discord_auth"));

        SecretStore.Store("patreon_auth", null);
        Assert.Null(Libsecret.Lookup("patreon_auth"));
        Assert.Null(SecretStore.Retrieve("patreon_auth"));
        Assert.True(Libsecret.Write("authtoken", null));
        Assert.True(Libsecret.Write("authtoken", null));        // clearing nothing is not a failure
        Assert.Null(Libsecret.Lookup("authtoken"));
    }

    /// <summary>Owner WSLg run: no org.freedesktop.secrets, ~200 "Secret lookup failed" warnings in 10 s
    /// (patreon_auth / discord_auth / substar_auth asked again and again). One warning, then the gate answers.</summary>
    [Fact]
    public void AnUnreachableServiceWarnsOnceAndStopsAsking()
    {
        // Windows has no libsecret at all; Linux runs this only with no bus (scripts/secrets-roundtrip.sh), never a keyring.
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("CCP_SECRETS_NOBUS") != "1")
            Assert.Skip("Run by scripts/secrets-roundtrip.sh with DBUS_SESSION_BUS_ADDRESS pointing at nothing.");
        var saved = Libsecret.Gate;
        long now = 0;
        Libsecret.Gate = new SecretServiceGate(() => now);
        try
        {
            for (int i = 0; i < 200; i++) Assert.Equal((false, (string?)null), Libsecret.Read(i % 2 == 0 ? "patreon_auth" : "discord_auth"));
            Assert.False(Libsecret.Write("substar_auth", Json));
            Assert.Equal(1, Libsecret.Gate.Failures);   // one real attempt; the gate answered the rest
            Assert.Equal(1, Libsecret.Gate.Warnings);

            now += (long)SecretServiceGate.RetryAfter.TotalMilliseconds + 1;
            Assert.Null(Libsecret.Lookup("patreon_auth"));
            // A minute on, one quiet retry over D-Bus; without libsecret there is nothing to retry.
            Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, Libsecret.Gate.Failures);
            Assert.Equal(1, Libsecret.Gate.Warnings);
        }
        finally { Libsecret.Gate = saved; }
    }

    /// <summary>Owner WSLg log: a startup logout over a keyring that was never there said "logout not complete".
    /// Nothing reached the store this run, so the memory clear is the clear; a service that answered keeps the notice.</summary>
    [Fact]
    public void AClearOverAServiceNeverReachedThisRunIsNotAFailedLogout()
    {
        var (gate, sandboxed, write) = (Libsecret.Gate, SecretStore.Sandboxed, SecretStore.OsWriteOverride);
        try
        {
            SecretStore.Sandboxed = false;
            SecretStore.NotRemembered = SecretStore.ClearFailed = false;
            SecretStore.OsWriteOverride = (_, _) => false;   // every OS write fails, as with no Secret Service
            Libsecret.Gate = new SecretServiceGate(() => 0);
            Libsecret.Gate.Down("lookup", "patreon_auth", "The name org.freedesktop.secrets was not provided", permanent: false);

            SecretStore.Store("patreon_cache", null);
            Assert.False(SecretStore.ClearFailed);

            Libsecret.Gate.Answered();   // it is there (a locked keyring): a failed clear may have left a token behind
            SecretStore.Store("patreon_cache", null);
            Assert.True(SecretStore.ClearFailed);
        }
        finally
        {
            (Libsecret.Gate, SecretStore.Sandboxed, SecretStore.OsWriteOverride) = (gate, sandboxed, write);
            SecretStore.NotRemembered = SecretStore.ClearFailed = false;
        }
    }

    [Fact]
    public void TheGateRetriesAMinuteOnAndReopensWhenTheServiceAnswers()
    {
        long now = 1000;
        var gate = new SecretServiceGate(() => now);
        Assert.False(gate.Closed);
        gate.Down("lookup", "patreon_auth", "The name org.freedesktop.secrets was not provided by any .service files", permanent: false);
        Assert.True(gate.Closed);
        now += (long)SecretServiceGate.RetryAfter.TotalMilliseconds - 1;
        Assert.True(gate.Closed);
        now += 2;
        Assert.False(gate.Closed);   // a keyring started since gets its chance
        gate.Down("lookup", "patreon_auth", "still nothing", permanent: false);
        Assert.True(gate.Closed);
        Assert.Equal(1, gate.Warnings);   // the retry failed quietly

        now += (long)SecretServiceGate.RetryAfter.TotalMilliseconds + 1;
        gate.Up();                        // it answered
        Assert.False(gate.Closed);
        gate.Down("lookup", "patreon_auth", "gone again", permanent: false);
        Assert.Equal(2, gate.Warnings);   // lost after coming back: worth one more line

        var missing = new SecretServiceGate(() => now);
        missing.Down("lookup", "authtoken", "libsecret-1.so.0 not found", permanent: true);
        now += 10 * (long)SecretServiceGate.RetryAfter.TotalMilliseconds;
        Assert.True(missing.Closed);      // no library: never retried this run
    }

    [Theory]
    [InlineData("g-dbus-error-quark", 2, true)]    // ServiceUnknown: "not provided by any .service files"
    [InlineData("g-dbus-error-quark", 9, true)]    // AccessDenied (a sandbox without the permission)
    [InlineData("g-io-error-quark", 1, true)]      // no bus socket: "Could not connect: No such file or directory"
    [InlineData("g-io-error-quark", 0, true)]      // "Cannot autolaunch D-Bus without X11 $DISPLAY"
    [InlineData("g-io-error-quark", 19, false)]    // cancelled prompt: the user's answer
    [InlineData("g-io-error-quark", 36, false)]    // the service's own error
    [InlineData("secret-error-quark", 2, false)]   // IsLocked: unlock and it works
    [InlineData(null, 0, false)]
    public void OnlyAnUnreachableServiceClosesTheGate(string? domain, int code, bool unreachable) =>
        Assert.Equal(unreachable, Libsecret.IsUnreachable(domain, code));

    [Fact]
    public void NoBusReadsNullAndWritesNoFile()
    {
        if (Environment.GetEnvironmentVariable("CCP_SECRETS_NOBUS") != "1")
            Assert.Skip("Run by scripts/secrets-roundtrip.sh with DBUS_SESSION_BUS_ADDRESS pointing at nothing.");
        SecretStore.Sandboxed = false;

        var home = Environment.GetEnvironmentVariable("HOME")!;
        string[] Files() => new[] { home, CorePaths.UserData }.Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)).Order().ToArray();
        var before = Files();

        Assert.Null(Libsecret.Lookup("authtoken"));
        Assert.False(Libsecret.Write("authtoken", "tok"));
        Assert.Null(SecretStore.Retrieve("apikey"));
        SecretStore.Store("authtoken", "tok");
        SecretStore.Store("discord_auth", Json);
        Assert.Equal("tok", SecretStore.Retrieve("authtoken"));  // this run only, from memory
        Assert.True(SecretStore.NotRemembered);
        Assert.Equal(before, Files());
    }
}

using System;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionApiKeyTests
{
    [Fact]
    public void WritesAndClearsTheDedicatedOsSlot()
    {
        Assert.True(SecretStore.Sandboxed);
        var protector = CompanionApiKey.WindowsProtect;
        CompanionApiKey.WindowsProtect = null;
        var previous = SecretStore.OsWriteOverride;
        string? writtenName = null, writtenValue = null;
        SecretStore.OsWriteOverride = (name, value) => { writtenName = name; writtenValue = value; return true; };
        try
        {
            var reference = Assert.IsType<string>(CompanionApiKey.Store("synthetic-key"));
            Assert.Equal(CompanionApiKey.Slot, writtenName);
            Assert.Equal("synthetic-key", writtenValue);
            Assert.Equal(CompanionApiKey.Reference, reference);
            Assert.Equal("synthetic-key", CompanionApiKey.Read(reference));
            Assert.Equal("", CompanionApiKey.Store(null));
            Assert.Null(writtenValue);
            Assert.Null(CompanionApiKey.Read(reference));
            Assert.Null(CompanionApiKey.Read(Convert.ToBase64String(new byte[] { 1, 2, 3 })));
        }
        finally { SecretStore.Store(CompanionApiKey.Slot, null); SecretStore.OsWriteOverride = previous; CompanionApiKey.WindowsProtect = protector; }
    }

    [Fact]
    public void WindowsWritesUseTheProtectorAndReportFailure()
    {
        var previous = CompanionApiKey.WindowsProtect;
        try
        {
            CompanionApiKey.WindowsProtect = key => Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
            Assert.Equal("c3ludGhldGlj", CompanionApiKey.Store("synthetic"));
            CompanionApiKey.WindowsProtect = _ => throw new CryptographicException();
            Assert.Null(CompanionApiKey.Store("synthetic"));
        }
        finally { CompanionApiKey.WindowsProtect = previous; }
    }

    [Fact]
    public void ReadsExistingWpfProtectedKeyOnWindows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("DPAPI requires Windows."); return; }
        // WPF SecureStringHelper.cs:25-27,45-46: same user, null entropy.
        var blob = Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes("synthetic-wpf-key"), null, DataProtectionScope.CurrentUser));
        Assert.Equal("synthetic-wpf-key", CompanionApiKey.Read(blob));
        var written = CompanionApiKey.Store("synthetic-wpf-key")!;
        Assert.Equal("synthetic-wpf-key", Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(written), null, DataProtectionScope.CurrentUser)));
    }
}

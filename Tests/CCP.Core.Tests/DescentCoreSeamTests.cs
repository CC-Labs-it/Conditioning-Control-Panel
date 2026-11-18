using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The two Descent rules that read live settings now reach them through <see cref="CoreSettings"/>
/// instead of the WPF <c>App.Settings</c>: the vat's pour watermark and the cycle-XP bonus. Runs in
/// a private settings file so other tests' delayed saves cannot race its cleanup.
/// </summary>
public sealed class DescentCoreSeamTests
{
    private static void WithSeededSettings(Action<SettingsService> body)
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var directory = Path.Combine(Path.GetTempPath(), "ccp-descent-seam-" + Guid.NewGuid().ToString("N"));
        SettingsService? svc = null;
        try
        {
            Directory.CreateDirectory(directory);
            svc = new SettingsService(Path.Combine(directory, "settings.json"));
            CoreSettings.ServiceProvider = () => svc;
            body(svc);
        }
        finally
        {
            if (svc != null) { svc.SaveImmediate(); svc.SealForReset(); }
            CoreSettings.ServiceProvider = oldProvider;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SeededSettingsLeaveSharedProfileFilesAlone()
    {
        Directory.CreateDirectory(CorePaths.UserData);
        var shared = Path.Combine(CorePaths.UserData, "settings." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (File.Open(shared, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                WithSeededSettings(svc => Assert.NotNull(svc.Current));
                Assert.True(File.Exists(shared));
            }
        }
        finally { File.Delete(shared); }
    }

    [Fact]
    public void PourLedgerWritesTheSeededSettingsScopedToAccountAndUtcDay()
    {
        WithSeededSettings(svc =>
        {
            var now = new DateTime(2026, 10, 12, 23, 0, 0, DateTimeKind.Utc);
            svc.Current.UnifiedId = "acct-a";
            var ledger = new AppSettingsVatPourLedger { UtcNow = () => now };

            ledger.Record(420);
            Assert.Equal(420, svc.Current.VatPouredTodayXp);
            Assert.Equal("2026-10-12", svc.Current.VatPouredDayUtc);
            Assert.Equal("acct-a", svc.Current.VatPouredAccount);
            Assert.Equal(420, ledger.PouredTodayXp);

            // Another account on the same machine sees nothing poured.
            svc.Current.UnifiedId = "acct-b";
            Assert.Equal(0, ledger.PouredTodayXp);

            // A finished UTC day describes nothing either.
            svc.Current.UnifiedId = "acct-a";
            now = now.AddHours(2);
            Assert.Equal(0, ledger.PouredTodayXp);
        });
    }

    [Fact]
    public void PourLedgerWithoutSettingsServiceWritesNothing()
    {
        // WPF had `if (s == null) return;`: no head settings means no write, not a write into
        // Core's shared default AppSettings.
        var oldProvider = CoreSettings.ServiceProvider;
        try
        {
            CoreSettings.ServiceProvider = null;
            var before = CoreSettings.Current.VatPouredTodayXp;
            new AppSettingsVatPourLedger().Record(before + 777);
            Assert.Equal(before, CoreSettings.Current.VatPouredTodayXp);
            Assert.Equal(0, new AppSettingsVatPourLedger().PouredTodayXp);
        }
        finally { CoreSettings.ServiceProvider = oldProvider; }
    }

    [Fact]
    public void ActiveCycleXpBonusFollowsTheSeededSettings()
    {
        WithSeededSettings(svc =>
        {
            svc.Current.DescentMigrationCompleted = false;
            svc.Current.PendingDescentMigrationChoice = null;
            Assert.Equal(1.0, DescentMigration.ActiveCycleXpBonus);

            svc.Current.DescentMigrationCompleted = true;
            Assert.Equal(DescentMigration.CycleXpBonus, DescentMigration.ActiveCycleXpBonus);
        });
    }
}

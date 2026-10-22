using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// <see cref="NativeBundleRepair"/>, the rules half of the WPF NativeBundleGuard moved to Core.
/// The helper golden was printed by the PRE-MOVE NativeBundleGuard line array, copied verbatim
/// into a throwaway console app.
/// </summary>
public sealed class NativeBundleRepairTests
{
    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "Update", name);

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ccp-nativerepair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void HelperScriptMatchesPreMoveBytes()
    {
        var golden = File.ReadAllBytes(Fixture("native_repair_spaces_premove.cmd.txt"));
        Assert.Contains("\r\n", Encoding.UTF8.GetString(golden));   // a checkout that ate the CRLFs fails loudly
        var script = NativeBundleRepair.BuildRepairHelperScript(
            @"C:\Users\a b\AppData\Local\Temp\.net\ConditioningControlPanel",
            @"C:\Users\a b\AppData\Local\Programs\Conditioning Control Panel\ConditioningControlPanel.exe",
            @"C:\Users\a b\AppData\Local\ConditioningControlPanel\logs\native-repair.log", 4242);
        Assert.Equal(golden, new UTF8Encoding(false).GetBytes(script));
    }

    [Fact]
    public void StampBlocksOnlyTheSameVersionInsideTheCooldown()
    {
        var d = TempDir();
        try
        {
            var t0 = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
            Assert.False(NativeBundleRepair.RepairedRecently(d, "7.1.5", t0));

            NativeBundleRepair.WriteStamp(d, "7.1.5", t0);
            Assert.True(NativeBundleRepair.RepairedRecently(d, "7.1.5", t0.AddMinutes(9)));
            Assert.False(NativeBundleRepair.RepairedRecently(d, "7.1.5", t0 + NativeBundleRepair.RepairCooldown));
            Assert.False(NativeBundleRepair.RepairedRecently(d, "7.1.6", t0.AddMinutes(1)));   // a version bump gets a fresh attempt

            NativeBundleRepair.ClearStamp(d);
            Assert.False(File.Exists(Path.Combine(d, "native-repair.stamp")));
            Assert.False(NativeBundleRepair.RepairedRecently(d, "7.1.5", t0));
        }
        finally { Directory.Delete(d, true); }
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("7.1.5|notticks")]
    [InlineData("7.1.5|1|2")]
    public void UnreadableStampNeverBlocksTheRepair(string text)
    {
        var d = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(d, "native-repair.stamp"), text);
            Assert.False(NativeBundleRepair.RepairedRecently(d, "7.1.5", DateTime.UtcNow));
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void ExtractionRootOnlyForASingleFileBundleWithAnExistingCache()
    {
        var d = TempDir();
        try
        {
            var exe = Path.Combine("install", "ConditioningControlPanel.exe");
            var root = Path.Combine(d, ".net", "ConditioningControlPanel");

            Assert.Null(NativeBundleRepair.ExtractionRoot("", exe, null, d));                      // cache not there
            Directory.CreateDirectory(root);
            Assert.Equal(root, NativeBundleRepair.ExtractionRoot("", exe, null, d));
            Assert.Equal(root, NativeBundleRepair.ExtractionRoot(null, exe, " ", d));               // blank env falls back to temp
            Assert.Null(NativeBundleRepair.ExtractionRoot("/app/CCP.dll", exe, null, d));           // not single-file
            Assert.Null(NativeBundleRepair.ExtractionRoot("", null, null, d));

            var custom = Path.Combine(d, "custom");
            Directory.CreateDirectory(Path.Combine(custom, "ConditioningControlPanel"));
            Assert.Equal(Path.Combine(custom, "ConditioningControlPanel"),
                NativeBundleRepair.ExtractionRoot("", exe, custom, Path.Combine(d, "elsewhere")));  // env var wins
        }
        finally { Directory.Delete(d, true); }
    }
}

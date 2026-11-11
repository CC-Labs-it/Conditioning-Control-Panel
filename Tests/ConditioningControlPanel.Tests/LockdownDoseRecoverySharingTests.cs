using System;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class LockdownDoseRecoverySharingTests
{
    [Fact]
    public void Wpf_recovery_uses_the_shared_mapping_and_keeps_its_save_and_cleanup_policy()
    {
        var source = SourceRoots.ReadProductFile("Services", "Haptics", "LockdownDoseKeeper.cs");
        int start = source.IndexOf("public static void RecoverIfNeeded()", StringComparison.Ordinal);
        Assert.True(start >= 0, "WPF recovery method not found");
        int end = source.IndexOf("public void Dispose()", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var recovery = source[start..end];
        const string apply = "LockdownDoseRecovery.ApplyFlipped(state.Flipped, s);";
        Assert.Contains(apply, recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (key)", recovery, StringComparison.Ordinal);
        Assert.True(recovery.IndexOf(apply, StringComparison.Ordinal)
            < recovery.IndexOf("App.Settings?.SaveImmediate();", StringComparison.Ordinal));
        Assert.Contains("finally { DeleteRecoveryFile(); }", recovery, StringComparison.Ordinal);
    }
}

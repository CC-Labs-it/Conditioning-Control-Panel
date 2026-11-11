using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;
public class InstallDateEvidenceTests
{
    [Fact]
    public void RecordsEarliestPlausibleEvidenceOnce()
    {
        var s = new AppSettings();
        Func<DateTime?>[] probes = { () => new DateTime(2024, 2, 3), () => throw new Exception(),
            () => new DateTime(2019, 1, 1), () => new DateTime(2022, 1, 2) };
        Assert.True(InstallDateEvidence.Record(s, new DateTime(2025, 1, 1), probes));
        Assert.Equal("2022-01-02", s.InstallDate);
        Assert.False(InstallDateEvidence.Record(s, new DateTime(2025, 1, 1), probes));
        Assert.Equal("2022-01-02", s.InstallDate);
    }
    [Fact]
    public void FallsBackToTodayAndClampsFuture()
    {
        var s = new AppSettings();
        Assert.True(InstallDateEvidence.Record(s, new DateTime(2025, 1, 1), new Func<DateTime?>[] { () => throw new Exception(), () => new DateTime(2010, 1, 1) }));
        Assert.Equal("2025-01-01", s.InstallDate);
        s.InstallDate = null;
        Assert.True(InstallDateEvidence.Record(s, new DateTime(2025, 1, 1), new Func<DateTime?>[] { () => new DateTime(2030, 1, 1) }));
        Assert.Equal("2025-01-01", s.InstallDate);
    }
}

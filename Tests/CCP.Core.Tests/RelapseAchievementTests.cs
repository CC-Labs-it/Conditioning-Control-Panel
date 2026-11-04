using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RelapseAchievementTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("ccp-relapse-").FullName;
    private string PathName => Path.Combine(dir, "achievements.json");
    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void PanicIsDirtyAndPersists()
    {
        var engine = new AchievementEngine(new AchievementStore(PathName));
        engine.TrackPanicPressed();
        Assert.NotNull(engine.Progress.LastPanicPressTime);
        Assert.True(engine.IsDirty);
        engine.Save();
        Assert.Equal(engine.Progress.LastPanicPressTime, new AchievementStore(PathName).Load().LastPanicPressTime);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(10.001, false)]
    public void RestartBoundaryAndUnlockPersistence(double seconds, bool expected)
    {
        var engine = new AchievementEngine(new AchievementStore(PathName));
        var panic = new DateTime(2026, 1, 1, 12, 0, 0);
        engine.Progress.LastPanicPressTime = panic;
        engine.CheckRelapse(panic.AddSeconds(seconds));
        Assert.Equal(expected, engine.Progress.IsUnlocked("relapse"));
        if (expected) Assert.True(new AchievementStore(PathName).Load().IsUnlocked("relapse"));
    }
}

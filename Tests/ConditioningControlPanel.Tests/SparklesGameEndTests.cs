using ConditioningControlPanel.Services.Sparkles;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>B1 W2: each game's own grading maps to a medal the same way every time (the server
/// prices the medal; these maps are the only thing a host decides).</summary>
public class SparklesGameEndTests
{
    [Theory]
    [InlineData("S+", "gold")]
    [InlineData("s", "gold")]
    [InlineData("A", "silver")]
    [InlineData("B", "bronze")]
    [InlineData("C", "none")]
    [InlineData("pass", "none")]
    [InlineData(null, "none")]
    public void ArcademyGradesMapToMedals(string? grade, string medal) =>
        Assert.Equal(medal, SparklesGameEnd.ArcademyMedal(grade));

    [Theory]
    [InlineData(250, 250, "gold")]
    [InlineData(400, 250, "gold")]
    [InlineData(150, 250, "silver")]
    [InlineData(75, 250, "bronze")]
    [InlineData(74, 250, "none")]
    [InlineData(100, 0, "none")]
    public void AScoreAgainstItsCeilingMapsToAMedal(double score, double cap, string medal) =>
        Assert.Equal(medal, SparklesGameEnd.RatioMedal(score, cap));

    [Theory]
    [InlineData(90, 100, "gold")]
    [InlineData(70, 100, "silver")]
    [InlineData(50, 100, "bronze")]
    [InlineData(49, 100, "none")]
    [InlineData(10, 0, "none")]
    public void IntakeMarksMapToMedals(double total, double max, string medal) =>
        Assert.Equal(medal, SparklesGameEnd.IntakeMedal(total, max, 90));
}

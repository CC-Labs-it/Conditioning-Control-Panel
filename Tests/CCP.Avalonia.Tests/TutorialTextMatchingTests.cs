using System.Reflection;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TutorialTextMatchingTests
{
    [Theory]
    [InlineData("  PuLsE curve ", "pulse", true)]
    [InlineData("12.49", "12", true)]
    [InlineData("12.5", "12", false)]
    [InlineData("112", "12", false)]
    [InlineData("anything", "", false)]
    [InlineData("other", "pulse", false)]
    public void UsesWpfTrimCaseNumericToleranceAndSubstringRules(string actual, string expected, bool matches)
    {
        var method = typeof(TutorialOverlay).GetMethod("TextMatches", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(matches, (bool)method.Invoke(null, new object[] { actual, expected })!);
    }
}

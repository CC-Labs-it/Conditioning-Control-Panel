using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Serializes every suite that sets the static <c>NavStripRules.BadgeProvider</c> or lays out a
/// <c>SectionTabStrip</c>. The provider is process-wide: with xUnit running classes in parallel, a
/// strip laid out by another class while NavPillHelpTests holds a test provider would read "5 on"
/// on a pill that has no badge. One non-parallel collection removes the race; a test that sets the
/// provider still puts the old one back in a finally.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SectionTabStripCollection
{
    public const string Name = "SectionTabStrip";
}

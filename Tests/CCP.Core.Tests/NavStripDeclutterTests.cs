using System;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Declutter wave 1 (2026-11-15, twin of WPF NavPillHelpTests f04bd6a52e): the pure strip
/// rules both heads read. A resting pill has no face, the count badge seam is short and never
/// throws, the crumb's section word is the room title.</summary>
public sealed class NavStripDeclutterTests
{
    [Fact]
    public void RestingPillsShowNoFaceUntilThePointerAsks()
    {
        Assert.True(NavStripTable.ShowsFace(active: true, hovered: false, pressed: false));
        Assert.True(NavStripTable.ShowsFace(active: false, hovered: true, pressed: false));
        Assert.True(NavStripTable.ShowsFace(active: false, hovered: false, pressed: true));
        Assert.False(NavStripTable.ShowsFace(active: false, hovered: false, pressed: false));
    }

    [Fact]
    public void TheCountBadgeSeamTrimsCapsAndStaysQuiet()
    {
        var before = NavStripTable.BadgeProvider;
        int raised = 0;
        Action onChanged = () => raised++;
        try
        {
            NavStripTable.BadgeProvider = null;
            Assert.Null(NavStripTable.BadgeText("studio"));
            NavStripTable.BadgeProvider = k => k == "studio" ? "  5 on " : k == "presets" ? "   " : k == "library" ? "123456789012" : null;
            Assert.Equal("5 on", NavStripTable.BadgeText("studio"));
            Assert.Null(NavStripTable.BadgeText("presets"));
            Assert.Null(NavStripTable.BadgeText("justdrop"));
            Assert.Null(NavStripTable.BadgeText(null));
            Assert.Null(NavStripTable.BadgeText(""));
            Assert.Equal("12345678", NavStripTable.BadgeText("library"));
            Assert.Equal(8, NavStripTable.BadgeTextMax);
            NavStripTable.BadgeProvider = _ => throw new InvalidOperationException("page gone");
            Assert.Null(NavStripTable.BadgeText("studio"));

            NavStripTable.BadgesChanged += onChanged;
            NavStripTable.NotifyBadgesChanged();
            Assert.Equal(1, raised);
            NavStripTable.BadgesChanged -= onChanged;
            NavStripTable.NotifyBadgesChanged();
            Assert.Equal(1, raised);
        }
        finally
        {
            NavStripTable.BadgesChanged -= onChanged;
            NavStripTable.BadgeProvider = before;
        }
    }

    [Fact]
    public void TheSectionWordIsTheRoomTitle()
    {
        Assert.Equal(17, NavStripTable.CrumbTitleSize);
        Assert.Equal(12.5, NavStripTable.CrumbPageSize);
        Assert.True(NavStripTable.CrumbTitleSize >= NavStripTable.CrumbPageSize + 4);
    }
}

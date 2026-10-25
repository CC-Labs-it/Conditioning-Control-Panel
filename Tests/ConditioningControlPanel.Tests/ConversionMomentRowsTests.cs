using System;
using System.IO;
using ConditioningControlPanel.Services.Startup.Conversion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The rows that follow a moment instead of interrupting it (proposal 7): the companion cap, the
/// Daily Gift relock, the session recap's taste line and the patron gallery row.
/// </summary>
public class ConversionMomentRowsTests
{
    private static readonly DateTime Today = new(2026, 10, 13);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void CapRow_OffersTheNextTier(int tierNow, int offered)
        => Assert.Equal(offered, ConversionRules.CapRowOffers(tierNow));

    [Fact]
    public void CapRow_PrimeGetsNone() => Assert.Null(ConversionRules.CapRowOffers(2));

    [Fact]
    public void CompanionCaps_MatchTheOwnerDecision()
    {
        Assert.Equal(100, ConversionRules.CompanionCapFree);
        Assert.Equal(1000, ConversionRules.CompanionCapBasic);
        Assert.Equal(2000, ConversionRules.CompanionCapPrime);
    }

    [Fact]
    public void GiftUse_RoundTripsYesterdayOnly()
    {
        Assert.Equal("2026-10-12|takeover", ConversionRules.GiftUse(Today.AddDays(-1), "takeover"));
        Assert.Equal("takeover", ConversionRules.UsedYesterday("2026-10-12|takeover", Today));
        Assert.Null(ConversionRules.UsedYesterday("2026-10-13|takeover", Today));
        Assert.Null(ConversionRules.UsedYesterday("2026-10-10|takeover", Today));
        Assert.Null(ConversionRules.UsedYesterday("garbage", Today));
        Assert.Null(ConversionRules.UsedYesterday("2026-10-12|", Today));
        Assert.Null(ConversionRules.UsedYesterday(null, Today));
    }

    [Fact]
    public void Relock_OwedWhenYesterdaysGiftIsShutToday()
    {
        Assert.True(ConversionRules.RelockOwed("2026-10-12|awareness", Today, premium: false, _ => false, out var key));
        Assert.Equal("awareness", key);
    }

    [Fact]
    public void Relock_NotOwed_WhenTheDoorIsStillOpenOrATierHoldsIt()
    {
        Assert.False(ConversionRules.RelockOwed("2026-10-12|awareness", Today, premium: false, k => k == "awareness", out _));
        Assert.False(ConversionRules.RelockOwed("2026-10-12|awareness", Today, premium: true, _ => false, out _));
        Assert.False(ConversionRules.RelockOwed("2026-10-11|awareness", Today, premium: false, _ => false, out _));
        Assert.False(ConversionRules.RelockOwed("2026-10-12|awareness", Today, premium: false, _ => throw new InvalidOperationException(), out _));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]  // ended early
    [InlineData(true, false, false, false)]  // reopened from history
    [InlineData(true, true, true, false)]    // has a tier
    public void SessionTasteLine_OnlyAtTheLiveEndForNoTier(bool completed, bool live, bool premium, bool expected)
        => Assert.Equal(expected, ConversionRules.SessionTasteLine(completed, live, premium));

    [Fact]
    public void SessionRecap_TasteLineIsTextOnly_NeverASecondButton()
    {
        var xaml = File.ReadAllText(RepoFile("ConditioningControlPanel", "Windows", "SessionCompleteWindow.xaml"));
        Assert.Contains("x:Name=\"TxtTasteLine\"", xaml);
        Assert.Equal(1, CountOf(xaml, "<Button Grid.Row=\"4\""));
        Assert.DoesNotContain("<Button x:Name=\"BtnTaste", xaml);
    }

    [Fact]
    public void ConversationPage_NeverSaysTheOfferItself()
    {
        var vm = File.ReadAllText(RepoFile("ConditioningControlPanel", "Views", "Controls", "Companion", "V2", "ConversationPageVm.cs"));
        Assert.Contains("ConversionSignals.NoteCompanionCap()", vm);
        Assert.DoesNotContain("conv_cap_", vm);
    }

    private static int CountOf(string s, string part)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(part, i, StringComparison.Ordinal)) >= 0) { n++; i += part.Length; }
        return n;
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Windows")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, Path.Combine(parts));
    }
}

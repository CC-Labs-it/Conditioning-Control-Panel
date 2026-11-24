using System;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>WPF AvatarTubeWindow.Speech.cs:2617 GreetingAwayBucket, :2669 BuildAbsenceGreeting, :2787
/// FormatGreetingName and :2638 the streak-milestone latch.</summary>
public class AbsenceGreetingTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, "first")]
    [InlineData(-1.0, "soon")]      // a future stamp (clock change) reads as a quick return
    [InlineData(5.9, "soon")]
    [InlineData(6.0, "back")]
    [InlineData(17.9, "back")]
    [InlineData(18.0, "while")]
    [InlineData(71.9, "while")]
    [InlineData(72.0, "long")]
    public void AwayBucketFollowsWpfThresholds(double? hoursAway, string bucket) =>
        Assert.Equal(bucket, AbsenceGreeting.AwayBucket(hoursAway is { } h ? Now.AddHours(-h) : null, Now));

    [Fact]
    public void BuildPicksTheTierAndNamesTheUser()
    {
        ConditioningControlPanel.Localization.LocalizationManager.Instance.SetLanguage("en");
        Assert.Null(AbsenceGreeting.Build(null, Now, "Mia", new Random(1)));
        var tiers = new (double Hours, string[] Lines)[]
        {
            (1, new[] { "Back already? Hehe~ 💕", "Ooh, you're back so soon! ✨", "Couldn't stay away, Mia? 😘" }),
            (10, new[] { "Welcome back, Mia! 💖", "Yay, you're here again! ✨", "Hi again, cutie~ 💕" }),
            (24 * 5, new[] { "Yay, you came back! 💕", "So happy you're here, Mia! ✨", "Hi hi! Let's have some fun~ 💖" }),
            (24 * 40, new[] { "It's been ages — welcome back! 💕✨", "Yaaay, you're back, Mia! So happy to see you! 💖", "Welcome back! Let's pick up right where we left off~ ✨" }),
        };
        foreach (var (hours, lines) in tiers)
            for (int seed = 0; seed < 12; seed++)
                Assert.Contains(AbsenceGreeting.Build(Now.AddHours(-hours), Now, "Mia", new Random(seed)), lines);
    }

    [Fact]
    public void FormatNameDropsTheTokenCleanlyWithoutAName()
    {
        Assert.Equal("Welcome back! 💖", AbsenceGreeting.FormatName("Welcome back, {name}! 💖", null));
        Assert.Equal("Couldn't stay away? 😘", AbsenceGreeting.FormatName("Couldn't stay away, {name}? 😘", " "));
        Assert.Equal("Welcome back, Mia! 💖", AbsenceGreeting.FormatName("Welcome back, {name}! 💖", " Mia "));
    }

    [Theory]
    [InlineData(0, 7, null, false)]    // a transient zero never resets the latch
    [InlineData(6, 0, null, false)]
    [InlineData(7, 0, 7, true)]
    [InlineData(29, 7, 14, true)]
    [InlineData(29, 14, null, false)]  // already announced
    [InlineData(10, 30, 7, false)]     // streak dropped: latch falls silently, so 30 can announce again
    [InlineData(400, 100, 365, true)]
    public void StreakMilestoneAnnouncesOnlyANewHigherMilestone(int streak, int announced, int? latch, bool announce) =>
        Assert.Equal((latch, announce), AbsenceGreeting.StreakMilestone(streak, announced));
    [Fact]
    public void LapseNeedsConfirmationAndIsOncePerClosure()
    {
        var s = new ConditioningControlPanel.Models.AppSettings { GraceClosedTier = 2, GraceClosedAtUtc = Now.AddHours(-1) };
        var reading = AbsenceGreeting.Lapse(s, Now, true, false, _ => false);
        Assert.NotNull(reading);
        Assert.Equal(2, reading.Value.Tier);
        Assert.Null(AbsenceGreeting.Lapse(s, Now, false, false, _ => false));
        Assert.Null(AbsenceGreeting.Lapse(s, Now, true, false, _ => true));
        ConditioningControlPanel.Services.Startup.Conversion.ConversionRules.Stamp(s.InboxStamps,
            ConditioningControlPanel.Services.Startup.Conversion.ConversionRules.KeyLapseGreeting, reading.Value.Stamp);
        Assert.Null(AbsenceGreeting.Lapse(s, Now, true, false, _ => false));
    }

    [Fact]
    public void AllLanguagesContainLocalizedGreetingTemplates()
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            foreach (var (code, _, _) in ConditioningControlPanel.Localization.LocalizationManager.AvailableLanguages)
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
                var entries = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(System.IO.File.ReadAllText(path))!;
                loc.SetLanguage(code);
                for (int bucket = 1; bucket <= 6; bucket++)
                    for (int variant = 1; variant <= 3; variant++)
                    {
                        var key = $"avatar_greeting_{bucket}_{variant}";
                        Assert.True(entries.TryGetValue(key, out var value), $"{code}: {key}");
                        Assert.Equal(value, loc.Get(key));
                    }
                for (int i = 1; i <= 3; i++) Assert.Contains("{tier}", entries[$"avatar_lapse_{i}"]);
            }
        }
        finally { loc.SetLanguage(previous); }
    }

}

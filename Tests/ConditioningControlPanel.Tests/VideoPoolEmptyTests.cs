using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Brain;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Companion → Links pool (ccp-bugs #1754, #1757, 7.4.1).
/// #1754: binning every video saved "no override", so the shipped list came back on the next
/// page mount (and after a restart). An emptied pool must stay empty.
/// #1757: with one video left, the anti-repeat line banned it and the model named an off-pool
/// built-in title instead. With every listed title banned, the oldest pick may repeat.
/// </summary>
public class VideoPoolEmptyTests
{
    private const string Mod = "builtin-bambisleep";

    private static readonly Dictionary<string, string> Shipped = new()
    {
        ["Bambi - I Want Your Cum"] = "https://hypnotube.com/video/shipped-1.html",
        ["Naughty Bambi"] = "https://hypnotube.com/video/shipped-2.html",
    };

    private static readonly Dictionary<string, string> BaseShipped = new()
    {
        ["Base video"] = "https://hypnotube.com/video/base-1.html",
    };

    private static Dictionary<string, string>? Resolve(Dictionary<string, Dictionary<string, string>>? byMod) =>
        ModService.ResolveVideoLinks(byMod, Mod, Shipped, BaseShipped);

    [Fact]
    public void An_emptied_pool_stays_empty_after_a_restart()
    {
        var settings = new AppSettings { VideoLinksByMod = new() };
        settings.VideoLinksByMod[Mod] = new() { ["BambiOS"] = "https://hypnotube.com/video/bambios.html" };

        // The bin on the last row saves an empty pool.
        ModService.StoreUserVideoLinks(settings.VideoLinksByMod, Mod, new Dictionary<string, string>());

        // Restart: settings.json written and read back the way SettingsService loads it.
        var json = JsonConvert.SerializeObject(settings);
        var back = JsonConvert.DeserializeObject<AppSettings>(json,
            new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace })!;

        Assert.NotNull(back.VideoLinksByMod);
        Assert.True(back.VideoLinksByMod!.ContainsKey(Mod));
        var pool = Resolve(back.VideoLinksByMod);
        Assert.NotNull(pool);
        Assert.Empty(pool!);
    }

    [Fact]
    public void A_pool_never_set_falls_back_to_the_shipped_list()
    {
        Assert.Same(Shipped, Resolve(null));
        Assert.Same(Shipped, Resolve(new()));
        Assert.Same(BaseShipped, ModService.ResolveVideoLinks(
            new Dictionary<string, Dictionary<string, string>>(), Mod, new Dictionary<string, string>(), BaseShipped));
    }

    [Fact]
    public void Null_clears_the_override_and_brings_the_shipped_list_back()
    {
        var byMod = new Dictionary<string, Dictionary<string, string>> { [Mod] = new() };
        ModService.StoreUserVideoLinks(byMod, Mod, null);

        Assert.False(byMod.ContainsKey(Mod));
        Assert.Same(Shipped, Resolve(byMod));
    }

    [Fact]
    public void A_user_pool_wins_over_the_shipped_list()
    {
        var byMod = new Dictionary<string, Dictionary<string, string>>();
        ModService.StoreUserVideoLinks(byMod, Mod, new() { ["BambiOS"] = "https://hypnotube.com/video/bambios.html" });

        var pool = Resolve(byMod)!;
        Assert.Single(pool);
        Assert.True(pool.ContainsKey("BambiOS"));
    }

    // ---------- #1757: pool of one ----------

    [Fact]
    public void A_pool_of_one_lets_its_video_repeat_and_never_points_off_pool()
    {
        var recent = new RecentRecommendations();
        recent.Note("BambiOS");

        var line = recent.BuildExclusionLine(new[] { "BambiOS" })!;

        Assert.DoesNotContain("pick something else", line);
        Assert.Contains("\"BambiOS\" may be suggested again", line);
        Assert.Contains("never name a video that is not in your list", line);
    }

    [Fact]
    public void A_pool_of_one_keeps_an_off_pool_title_banned()
    {
        var recent = new RecentRecommendations();
        recent.Note("BambiOS");
        recent.Note("Bambi - I Want Your Cum"); // the off-pool title it fell back to

        var line = recent.BuildExclusionLine(new[] { "BambiOS" })!;

        Assert.StartsWith("Already suggested recently (pick something else): Bambi - I Want Your Cum.", line);
        Assert.DoesNotContain("else): BambiOS", line);
        Assert.Contains("\"BambiOS\" may be suggested again", line);
    }

    [Fact]
    public void When_every_listed_title_is_banned_only_the_oldest_comes_back()
    {
        var recent = new RecentRecommendations();
        recent.Note("A");
        recent.Note("B"); // newest

        var line = recent.BuildExclusionLine(new[] { "a", "b" })!;

        Assert.StartsWith("Already suggested recently (pick something else): B.", line);
        Assert.Contains("\"A\" may be suggested again", line);
    }

    [Fact]
    public void A_pool_with_titles_left_keeps_the_old_line()
    {
        var recent = new RecentRecommendations();
        recent.Note("A");

        Assert.Equal("Already suggested recently (pick something else): A",
            recent.BuildExclusionLine(new[] { "A", "B" }));
        Assert.Equal("Already suggested recently (pick something else): A",
            recent.BuildExclusionLine());
        Assert.Null(new RecentRecommendations().BuildExclusionLine(new[] { "A" }));
    }

    [Fact]
    public void The_chat_tail_with_a_pool_of_one_allows_the_repeat()
    {
        var recent = new RecentRecommendations();
        recent.Note("BambiOS");
        var assembler = new PromptAssembler(new InertMemoryStore(), recent,
            systemPromptProvider: () => "PREFIX",
            localClock: () => new DateTime(2026, 11, 26, 18, 0, 0),
            linkPool: () => Array.Empty<(string, string)>(),
            personaFence: () => null,
            identityFence: () => null,
            poolTitles: () => new[] { "BambiOS" });

        var tail = assembler.BuildTail(ConditioningControlPanel.Services.AIService.AiPurpose.Chat,
            Array.Empty<CompanionTurn>());

        Assert.Contains("\"BambiOS\" may be suggested again", tail);
        Assert.DoesNotContain("pick something else): BambiOS", tail);
    }
}

using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Lane R5 (RAIN-V2-PLAN 2.4): the wardrobe window's page frames, its counter door and its panic seat.</summary>
[Collection(GameSurfacesCollection.Name)]
public sealed class RainWardrobeRouteTests
{
    private static JObject Msg(object o) => JObject.FromObject(o);

    [Fact]
    public void Open_counter_routes_to_the_counter_or_says_it_is_coming()
    {
        var saved = RainWardrobeHostService.CounterLauncher;
        try
        {
            var calls = new List<(string from, string? tab)>();
            int soon = 0;
            RainWardrobeHostService.CounterLauncher = null;
            RainWardrobeHostService.Route(Msg(new { type = "open-counter", tab = "rain-hair" }), null, _ => { }, () => new JObject(), () => soon++);
            Assert.Equal(1, soon);

            RainWardrobeHostService.CounterLauncher = (from, tab) => calls.Add((from, tab));
            RainWardrobeHostService.Route(Msg(new { type = "open-counter", tab = "rain-hair" }), null, _ => { }, () => new JObject(), () => soon++);
            RainWardrobeHostService.Route(JObject.Parse("{\"type\":\"open-counter\",\"tab\":null}"), null, _ => { }, () => new JObject(), () => soon++);
            Assert.Equal(new[] { ("rain-wardrobe", (string?)"rain-hair"), ("rain-wardrobe", (string?)null) }, calls);
            Assert.Equal(1, soon);
        }
        finally { RainWardrobeHostService.CounterLauncher = saved; }
    }

    [Fact]
    public void Ready_posts_init_and_apply_goes_to_the_controller_never_the_old_autosave()
    {
        var posted = new List<object>();
        var init = new JObject { ["type"] = "init", ["applyMode"] = true };
        RainWardrobeHostService.Route(Msg(new { type = "rain-ready" }), null, posted.Add, () => init, () => { });
        Assert.Same(init, Assert.Single(posted));

        int commits = 0;
        var apply = new RainApplyController(new RainApplyController.Hooks
        {
            Post = posted.Add,
            Store = () => new RainPackStore(System.IO.Path.GetTempPath(), () => null, _ => null),
            Access = () => new RainItemAccess(RainItemManifest.Empty, new RainGrantSet(null), new NoProgress()),
            Clips = () => new[] { "idle" },
            Commit = (_, _) => commits++,
        });
        var look = JObject.Parse("{\"rev\":1,\"items\":{},\"colors\":{}}");
        RainWardrobeHostService.Route(Msg(new { type = "rain-look", look }), apply, posted.Add, () => init, () => { });
        Assert.Equal(0, commits);
        RainWardrobeHostService.Route(Msg(new { type = "rain-apply", look }), apply, posted.Add, () => init, () => { });
        Assert.Equal(1, commits);   // the default look: no bake, committed at once
    }

    private sealed class NoProgress : IRainProgressSource
    {
        public int Level => 0;
        public IReadOnlyCollection<string> UnlockedAchievements => Array.Empty<string>();
    }

    [Fact]
    public void The_wardrobe_is_a_panic_surface_and_its_close_is_safe_when_shut()
    {
        Assert.Contains("rain-wardrobe", GameSurfaces.Ids());
        RainWardrobeHostService.Close();   // nothing open, nothing baking: a no-op, never a throw
        Assert.False(RainWardrobeHostService.IsActive);
    }
}

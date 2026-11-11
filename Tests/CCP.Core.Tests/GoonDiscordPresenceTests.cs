using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace CCP.Core.Tests;

public class GoonDiscordPresenceTests
{
    [Fact]
    public void Unknown_state_is_dropped()
    {
        var calls = new List<string>();
        GoonDiscordPresence.State("unknown", true, calls.Add);
        Assert.Empty(calls);
    }

    [Fact]
    public void State_requires_opt_in()
    {
        var calls = new List<string>();
        GoonDiscordPresence.State("live", false, calls.Add);
        Assert.Empty(calls);
        GoonDiscordPresence.State("live", true, calls.Add);
        Assert.Equal(new[] { "live" }, calls);
    }

    [Fact]
    public void Turning_presence_off_retracts_immediately()
    {
        var settings = new AppSettings { GoonRichPresence = true };
        var calls = new List<string>();
        GoonDiscordPresence.Prefs(settings, null, null, false, null, () => { }, calls.Add, () => { });
        Assert.False(settings.GoonRichPresence);
        Assert.Equal(new[] { "off" }, calls);
    }

    [Fact]
    public void Only_shared_flag_changes_request_sync()
    {
        var settings = new AppSettings { GoonShareAvatar = false, GoonShareDiscordDm = false, GoonRichPresence = false, GoonSeenSharePrompt = false };
        var syncs = 0;
        void Apply(bool? avatar = null, bool? dm = null, bool? presence = null, bool? seen = null) =>
            GoonDiscordPresence.Prefs(settings, avatar, dm, presence, seen, () => { }, _ => { }, () => syncs++);
        Apply(presence: true, seen: true);
        Assert.Equal(0, syncs);
        Apply(avatar: true);
        Assert.Equal(1, syncs);
        Apply(dm: true);
        Assert.Equal(2, syncs);
        Apply(avatar: true, dm: true);
        Assert.Equal(2, syncs);
        Assert.True(settings.GoonSeenSharePrompt);
        Assert.True(settings.GoonShareAvatar);
        Assert.True(settings.GoonShareDiscordDm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unchanged_local_preferences_do_not_notify(bool value)
    {
        var settings = new AppSettings { GoonRichPresence = value, GoonSeenSharePrompt = value };
        var notifications = new List<string?>();
        settings.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        GoonDiscordPresence.Prefs(settings, null, null, value, value, () => { }, _ => { }, () => { });
        Assert.Empty(notifications);

        GoonDiscordPresence.Prefs(settings, null, null, !value, !value, () => { }, _ => { }, () => { });
        Assert.Equal(new[] { nameof(AppSettings.GoonRichPresence), nameof(AppSettings.GoonSeenSharePrompt) }, notifications);
    }

    [Fact]
    public void Teardown_retracts_even_when_disabled()
    {
        var settings = new AppSettings { GoonRichPresence = false };
        var calls = new List<string>();
        GoonDiscordPresence.Teardown(calls.Add);
        Assert.False(settings.GoonRichPresence);
        Assert.Equal(new[] { "off" }, calls);
    }
}

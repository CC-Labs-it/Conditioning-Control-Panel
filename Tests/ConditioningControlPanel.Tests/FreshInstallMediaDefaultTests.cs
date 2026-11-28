using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A fresh install's Library source. "My own assets" on an empty folder was an app with no
/// pictures at all for a new user (2026-11-25), so the first-run claim moves the source to Both.
/// It must never consent for the user, and it must never touch a source someone already chose.
/// </summary>
public class FreshInstallMediaDefaultTests
{
    [Fact]
    public void A_new_settings_file_still_defaults_to_local()
        // The property default stays "local": an old settings file without the key, read by
        // an existing install, must not move. Only the first-run claim changes it.
        => Assert.Equal("local", new AppSettings().MediaSource);

    [Fact]
    public void The_first_run_moves_local_to_both()
    {
        var s = new AppSettings();
        Assert.True(FirstRunGate.ApplyFreshInstallMediaDefault(s));
        Assert.Equal("mixed", s.MediaSource);
    }

    [Theory]
    [InlineData("online")]
    [InlineData("mixed")]
    public void A_source_already_chosen_is_kept(string chosen)
    {
        var s = new AppSettings { MediaSource = chosen };
        Assert.False(FirstRunGate.ApplyFreshInstallMediaDefault(s));
        Assert.Equal(chosen, s.MediaSource);
    }

    [Fact]
    public void Null_settings_are_ignored()
        => Assert.False(FirstRunGate.ApplyFreshInstallMediaDefault(null));

    [Fact]
    public void The_default_does_not_consent_for_the_user()
    {
        var s = new AppSettings();
        FirstRunGate.ApplyFreshInstallMediaDefault(s);
        Assert.False(s.RemoteMediaConsented);
        Assert.False(s.HasRemoteMediaConsent);
        // Both, but nothing remote is drawn until the yes: the empty-library rescues still fire.
        Assert.False(s.UsesRemoteMedia);
    }

    [Theory]
    [InlineData("local", false, false)]
    [InlineData("local", true, false)]
    [InlineData("mixed", false, false)]
    [InlineData("mixed", true, true)]
    [InlineData("online", true, true)]
    public void UsesRemoteMedia_needs_a_remote_source_and_consent(string source, bool consent, bool expected)
    {
        var s = new AppSettings { MediaSource = source, RemoteMediaConsented = consent };
        Assert.Equal(expected, s.UsesRemoteMedia);
    }

    [Fact]
    public void The_feed_card_yes_counts_as_consent()
    {
        // HasRemoteMediaConsent folds in the For You card's yes; UsesRemoteMedia must read it,
        // not the raw app-wide flag, or a feed user on Both would be told their library is empty.
        var s = new AppSettings { MediaSource = "mixed", FypOnlineConsented = true };
        Assert.True(s.UsesRemoteMedia);
    }
}

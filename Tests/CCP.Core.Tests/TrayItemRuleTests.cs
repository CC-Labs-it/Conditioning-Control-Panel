using System;
using ConditioningControlPanel.Services.Notifications;
using Xunit;

namespace CCP.Core.Tests;

public sealed class TrayItemRuleTests
{
    private static readonly string[] Own = { "org.kde.StatusNotifierItem-7-0", ":1.42" };

    [Theory]
    [InlineData("org.kde.StatusNotifierItem-7-0")]                     // bare well-known name
    [InlineData("org.kde.StatusNotifierItem-7-0/StatusNotifierItem")]  // well-known name and path
    [InlineData(":1.42/org/ayatana/NotificationItem/app")]             // unique name and path
    public void Either_name_form_counts_as_ours(string item) =>
        Assert.True(TrayItemRule.Holds(new[] { "org.kde.StatusNotifierItem-9-0", item }, Own));

    [Theory]
    [InlineData("org.kde.StatusNotifierItem-70-0")]
    [InlineData(":1.420/StatusNotifierItem")]
    public void Another_apps_item_is_not_ours(string item) =>
        Assert.False(TrayItemRule.Holds(new[] { item }, Own));

    [Theory]
    [InlineData(nameof(TrayItemState.Held), true)]
    [InlineData(nameof(TrayItemState.Unknown), true)]    // a busy panel must not end a session
    [InlineData(nameof(TrayItemState.NotHeld), false)]
    public void X_hides_unless_the_tray_said_no(string state, bool hides) =>
        Assert.Equal(hides, TrayItemRule.XMayHide(Enum.Parse<TrayItemState>(state)));

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]      // --tray-check opts in
    [InlineData(false, true, true)]
    public void Only_the_real_app_or_the_live_probe_publishes(bool sandboxed, bool liveProbe, bool published) =>
        Assert.Equal(published, TrayItemRule.ShouldPublish(sandboxed, liveProbe));
}

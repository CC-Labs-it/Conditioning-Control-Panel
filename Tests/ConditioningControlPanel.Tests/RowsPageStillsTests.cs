using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Views.Controls.AppSettingsSections;
using ConditioningControlPanel.Views.Controls.Studio;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows sweep stills (overnight sweep 2026-11-24): every page of the lane tables rendered
/// through <see cref="RowsRender"/>'s render half (900 px, folds open, no row asserts) into
/// <c>CCP_ROWS_DUMP/&lt;page&gt;.png</c>. Does nothing without that env var. The foundation lane ran
/// it into C:/wt-rows-dump/before; page lanes re-run it into .../after once their rows are done
/// (their own Check tests carry the asserts), with env CCP_ROWS_PAGES=&lt;key,key&gt; to render only
/// their own pages. Windows are rendered by lifting their Content into
/// the host, so chrome and title bar are not in the picture.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsPageStillsTests
{
    /// <summary>Page key (file name of the still) and the lane that owns it.</summary>
    public static IEnumerable<object[]> Pages() => new[]
    {
        // R1
        new object[] { "video" }, new object[] { "visuals" }, new object[] { "lockcard" }, new object[] { "wallpaper" },
        // R2
        new object[] { "bubbles" }, new object[] { "bubblecount" }, new object[] { "bouncingtext" }, new object[] { "subliminal" },
        // R3
        new object[] { "spiral" }, new object[] { "pinkfilter" }, new object[] { "braindrain" }, new object[] { "mindwipe" },
        // R4
        new object[] { "scheduler" }, new object[] { "ramp" }, new object[] { "session-editor" },
        new object[] { "home" },
        // R5
        new object[] { "settings-account" }, new object[] { "settings-audio" }, new object[] { "settings-data" },
        new object[] { "settings-devices" }, new object[] { "settings-emidesk" }, new object[] { "settings-general" },
        new object[] { "settings-monitors" }, new object[] { "settings-notifications" }, new object[] { "settings-performance" },
        new object[] { "settings-updates" },
        // R6
        new object[] { "companion" }, new object[] { "haptics" }, new object[] { "takeover" }, new object[] { "lockdown" },
        new object[] { "chaster" }, new object[] { "remote" }, new object[] { "awareness" },
    };

    /// <summary>The page behind a key. Windows hand over their Content.</summary>
    internal static FrameworkElement Make(string page) => page switch
    {
        "video" => new VideoFeatureControl(),
        "visuals" => new VisualsFeatureControl(),
        "lockcard" => new LockCardFeatureControl(),
        "wallpaper" => new WallpaperFeatureControl(),
        "bubbles" => new BubblePopFeatureControl(),
        "bubblecount" => new BubbleCountFeatureControl(),
        "bouncingtext" => new BouncingTextFeatureControl(),
        "subliminal" => new SubliminalFeatureControl(),
        "spiral" => new SpiralFeatureControl(),
        "pinkfilter" => new PinkFilterFeatureControl(),
        "braindrain" => new BrainDrainFeatureControl(),
        "mindwipe" => new MindWipeFeatureControl(),
        "scheduler" => new SchedulerFeatureControl(),
        "ramp" => new IntensityRampFeatureControl(),
        "session-editor" => Lift(new SessionEditorWindow()),
        // NEVER "dollhouse": ChaosHubWindow starts its menu music (a WPF MediaPlayer) and fog on
        // build; a stills run played a song on the owner's speakers (2026-11-24). Left out.
        "home" => new SettingsTabView(),
        "settings-account" => new AccountSettingsSection(),
        "settings-audio" => new AudioSettingsSection(),
        "settings-data" => new DataSettingsSection(),
        "settings-devices" => new DevicesSettingsSection(),
        "settings-emidesk" => new EmiDeskSettingsSection(),
        "settings-general" => new GeneralSettingsSection(),
        "settings-monitors" => new MonitorsSettingsSection(),
        "settings-notifications" => new NotificationsSettingsSection(),
        "settings-performance" => new PerformanceSettingsSection(),
        "settings-updates" => new UpdatesSettingsSection(),
        "companion" => new CompanionTabView(),
        "haptics" => new HapticsTabView(),
        "takeover" => new BambiTakeoverTabView(),
        "lockdown" => new LockdownTabView(),
        "chaster" => new ChasterTabView(),
        "remote" => new RemoteControlTabView(),
        "awareness" => new AwarenessTabView(),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };

    private static FrameworkElement Lift(Window w)
    {
        var content = (FrameworkElement)w.Content;
        w.Content = null;
        return content;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Page_still_when_asked(string page)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CCP_ROWS_DUMP"))) return;
        // CCP_ROWS_PAGES=video,visuals limits the run to a lane's own pages.
        if (Environment.GetEnvironmentVariable("CCP_ROWS_PAGES") is { Length: > 0 } only
            && Array.IndexOf(only.Split(',', StringSplitOptions.TrimEntries), page) < 0) return;
        WpfRenderHarness.OnStaThread(() =>
        {
            var host = RowsRender.Host(Make(page));
            Assert.NotNull(RowsRender.Dump(host, page));
        });
        RowsRender.AssertSilent(page);
    }
}

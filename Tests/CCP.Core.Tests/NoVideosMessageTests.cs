using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class NoVideosMessageTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(3, 2)]
    public void Empty_library_explains_pack_state_before_add_files(int installed, int active)
    {
        var prefix = Loc.GetF("video_no_videos_found", "/videos") + "\n\n";
        var expected = installed > 0 && active == 0
            ? Loc.GetF("video_packs_installed_none_active", installed) + "\n" + Loc.Get("video_enable_packs_hint") + "\n\n"
            : active > 0 ? Loc.GetF("video_active_packs_no_videos", active) + "\n\n" : "";
        Assert.Equal(prefix + expected + Loc.Get("video_add_files_hint"),
            NoVideosReason.Message("/videos", 0, 0, 0, 0, installed, active));
    }

    [Fact]
    public void Length_filter_takes_precedence_over_pack_and_add_files_guidance() =>
        Assert.Equal(Loc.GetF("video_length_filter_emptied", 4, "52s - 2m 50s"),
            NoVideosReason.Message("/videos", 4, 0, 52, 170, 3, 2));
}

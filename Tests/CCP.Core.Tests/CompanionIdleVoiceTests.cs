using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CompanionIdleVoiceTests
{
    [Fact]
    public void AwarenessAndSpeechDeliveryCarryTheQuietFlag()
    {
        var root = System.IO.Path.GetDirectoryName(Here())!;
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(root, "CCP.Avalonia")))
            root = System.IO.Directory.GetParent(root)!.FullName;
        string Read(string path) => System.IO.File.ReadAllText(System.IO.Path.Combine(root, "CCP.Avalonia", path));
        var reactions = Read("Views/AvatarTube/AvatarTubeWindow.Reactions.cs");
        Assert.Equal(4, reactions.Split("unprompted: true").Length - 1);
        Assert.Contains("!IsMuted && MayVoice(unprompted)", Read("Views/AvatarTube/AvatarTubeWindow.axaml.cs"));
        Assert.Contains("unprompted: line.Unprompted", Read("Views/AvatarTube/AvatarTubeWindow.SpeechQueue.cs"));
        Assert.Equal(2, Read("Platform/BarkHead.cs").Split("unprompted: s.Unprompted").Length - 1);
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Theory]
    [InlineData("Idle")]
    [InlineData("IdleStateChanged")]
    [InlineData("FaceLost")]
    [InlineData("facefound")]
    [InlineData("Blink")]
    [InlineData("TrackingStateChanged")]
    [InlineData("ActivityChanged")]
    [InlineData("StillOnActivity")]
    public void SensingNeedsAnActiveRunOrOptIn(string trigger)
    {
        Assert.True(CompanionIdleVoice.IsUnprompted(trigger));
        Assert.False(CompanionIdleVoice.ShouldVoice(true, false, false, false, false, false));
        Assert.True(CompanionIdleVoice.ShouldVoice(true, true, false, false, false, false));
        Assert.True(CompanionIdleVoice.ShouldVoice(true, false, true, false, false, false));
        Assert.True(CompanionIdleVoice.ShouldVoice(true, false, false, false, true, false));
        Assert.False(CompanionIdleVoice.ShouldVoice(true, false, true, true, false, false));
        Assert.False(CompanionIdleVoice.ShouldVoice(true, true, true, false, true, true));
    }

    [Theory]
    [InlineData("TongueOut")]
    [InlineData("LongStare")]
    [InlineData("LevelUp")]
    [InlineData("SessionCompleted")]
    [InlineData("AvatarClicked")]
    [InlineData("UserMessageSent")]
    [InlineData("")]
    [InlineData(null)]
    public void AnswersKeepTheirVoice(string? trigger)
    {
        Assert.False(CompanionIdleVoice.IsUnprompted(trigger));
        Assert.True(CompanionIdleVoice.ShouldVoice(false, false, false, false, false, true));
    }
}

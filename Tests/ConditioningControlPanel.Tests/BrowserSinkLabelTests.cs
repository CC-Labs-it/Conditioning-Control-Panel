using System;
using System.IO;
using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #938 plumbing — audio-output routing on the first-party player page (https://ccp.game).
///
/// <para>Two halves must stay true together. The C# half (<see cref="BrowserSinkLabel"/>) decides
/// WHETHER a device label rides the <c>load</c> message at all — null is the "leave audio on the
/// Windows default" signal and must come out of every configuration that cannot be routed. The
/// page half (player.js <c>applySink</c>) matches that label against <c>enumerateDevices()</c> and
/// applies <c>setSinkId</c>, with the fail-safe contract that every failure path posts
/// <c>{type:'sink', ok:false}</c> and changes nothing.</para>
///
/// <para>The pure resolver moved to CCP.Core with BrowserSinkLabel; its rules are tested in
/// Tests/CCP.Core.Tests/BrowserSinkLabelTests.cs. The page half cannot run under xunit, so it is pinned the way this suite pins every
/// player-page contract: static reads of the product sources, asserting the wiring strings that
/// the C# side depends on byte-for-byte.</para>
/// </summary>
public class BrowserSinkLabelTests
{
    // =====================================================================================
    //  wiring — static reads of the product sources
    // =====================================================================================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProduct(params string[] parts)
    {
        var path = Path.Combine(RepoRoot(), Path.Combine(parts));
        Assert.True(File.Exists(path), $"product source missing: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void PlayerPage_CarriesTheSinkContract()
    {
        var js = ReadProduct("Assets", "web", "player", "player.js");

        // The load message's field, read exactly as C# writes it.
        Assert.Contains("applySink(d.sinkLabel)", js);

        // The fail-safe report both hosts switch on.
        Assert.Contains("type: 'sink'", js);

        // Label matching happens against enumerateDevices via setSinkId — the two APIs this whole
        // feature stands on. Their absence would mean the sink section was gutted.
        Assert.Contains("enumerateDevices", js);
        Assert.Contains("setSinkId", js);

        // The permission probe must stop its tracks (nothing may keep a live mic stream).
        Assert.Contains("getTracks().forEach", js);
    }

    [Fact]
    public void Engine_PostsTheLabelAndLogsTheVerdict()
    {
        var cs = ReadProduct("ConditioningControlPanel", "Services", "Video", "Browser", "BrowserVideoEngine.cs");

        // load post carries the field player.js reads, primary-gated through ForWindow.
        Assert.Contains("sinkLabel = BrowserSinkLabel.ForWindow(primary, sinkLabel)", cs);

        // Exactly one log line each way.
        Assert.Contains("case \"sink\"", cs);
        Assert.Contains("staying on the Windows default output", cs);
    }

    [Fact]
    public void BubbleCount_PostsTheLabelAndLogsTheVerdict()
    {
        var cs = ReadProduct("ConditioningControlPanel", "Windows", "BubbleCountWindow.xaml.cs");
        Assert.Contains("sinkLabel = BrowserSinkLabel.ForWindow(_isPrimary, BrowserSinkLabel.Resolve())", cs);
        Assert.Contains("case \"sink\"", cs);
        Assert.Contains("staying on the Windows default output", cs);
    }

    [Fact]
    public void Surface_GrantsMicOnlyToOurOwnHttpsHost()
    {
        var cs = ReadProduct("ConditioningControlPanel", "Services", "Video", "Browser", "BrowserVideoSurface.cs");

        // The grant predicate: https scheme AND the navigated host, mic only; all else denied.
        Assert.Contains("PermissionRequested += OnPermissionRequested", cs);
        Assert.Contains("uri.Scheme == Uri.UriSchemeHttps", cs);
        Assert.Contains("uri.Host, _navHost, StringComparison.OrdinalIgnoreCase", cs);
        Assert.Contains("CoreWebView2PermissionKind.Microphone", cs);
        Assert.Contains("CoreWebView2PermissionState.Deny", cs);
        Assert.Contains("e.Handled = true", cs);

        // And the unhook alongside the other core events.
        Assert.Contains("PermissionRequested -= OnPermissionRequested", cs);
    }

    [Fact]
    public void Gate_StillDemotesDevicePinnedUsersToLibVlc()
    {
        // Scoping decision pinned in code: BrowserVideoGate's #938 force-demotion is deliberately
        // untouched by this PR, so a user with a chosen output device still gets LibVLC (which can
        // target the device natively). Relaxing the gate is a separate, later change.
        var cs = SourceRoots.ReadProductFile("Services", "Video", "Browser", "BrowserVideoGate.cs");
        Assert.Contains("AudioOutputDeviceId", cs);
        Assert.Contains("routing to LibVLC, which can target it (#938)", cs);
    }
}

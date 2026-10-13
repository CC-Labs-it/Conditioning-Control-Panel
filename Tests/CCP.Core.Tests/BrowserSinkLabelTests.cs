using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace CCP.Core.Tests;

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
/// <para>The page half cannot run under xunit; its wiring is pinned by static reads of the product
/// sources in the WPF suite's BrowserSinkLabelWiringTests.</para>
/// </summary>
public class BrowserSinkLabelTests
{
    // =====================================================================================
    //  pure resolver
    // =====================================================================================

    [Fact]
    public void Resolve_SystemDefault_ReturnsNull()
    {
        // Empty/null device id is the picker's "System default" — no routing wanted.
        Assert.Null(BrowserSinkLabel.Resolve(null, "Speakers (Realtek)", null));
        Assert.Null(BrowserSinkLabel.Resolve("", "Speakers (Realtek)", null));
    }

    [Fact]
    public void Resolve_DeviceChosenWithName_ReturnsTrimmedName()
    {
        Assert.Equal("Speakers (Realtek High Definition Audio)",
            BrowserSinkLabel.Resolve("{guid}", "  Speakers (Realtek High Definition Audio)  ", null));
    }

    [Fact]
    public void Resolve_DeviceChosenButNoName_ReturnsNull()
    {
        // The page can only match by label; an id with no persisted friendly name is unroutable.
        Assert.Null(BrowserSinkLabel.Resolve("{guid}", null, null));
        Assert.Null(BrowserSinkLabel.Resolve("{guid}", "", null));
        Assert.Null(BrowserSinkLabel.Resolve("{guid}", "   ", null));
    }

    [Fact]
    public void Resolve_TestOverride_WinsOverEverything()
    {
        // The manual-verification path: CCP_BROWSER_SINK_TEST_LABEL routes the page even with the
        // picker on "System default" (the one configuration BrowserVideoGate's #938 demotion lets
        // reach the browser engine at all).
        Assert.Equal("USB Headset", BrowserSinkLabel.Resolve(null, null, "USB Headset"));
        Assert.Equal("USB Headset", BrowserSinkLabel.Resolve("", "", " USB Headset "));
        Assert.Equal("USB Headset", BrowserSinkLabel.Resolve("{guid}", "Speakers", "USB Headset"));
    }

    [Fact]
    public void Resolve_BlankTestOverride_DoesNotShadowSettings()
    {
        Assert.Equal("Speakers", BrowserSinkLabel.Resolve("{guid}", "Speakers", "   "));
        Assert.Null(BrowserSinkLabel.Resolve(null, null, ""));
    }

    [Fact]
    public void ForWindow_OnlyThePrimaryCarriesTheLabel()
    {
        // Secondaries are permanently muted (one WASAPI session per clip); a label there would
        // spend a mic-probe stream per monitor for audio nobody hears.
        Assert.Equal("Speakers", BrowserSinkLabel.ForWindow(primary: true, "Speakers"));
        Assert.Null(BrowserSinkLabel.ForWindow(primary: false, "Speakers"));
        Assert.Null(BrowserSinkLabel.ForWindow(primary: true, null));
    }

    [Fact]
    public void TestOverrideVariable_IsTheDocumentedName()
    {
        // The PR body / manual verification script names this variable; a rename must be loud.
        Assert.Equal("CCP_BROWSER_SINK_TEST_LABEL", BrowserSinkLabel.TestOverrideVariable);
    }
}

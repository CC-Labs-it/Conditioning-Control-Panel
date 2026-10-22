using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// #938 plumbing, the C# half: <see cref="BrowserSinkLabel"/> decides whether a device label rides the
/// player page's <c>load</c> message. Null is the "leave audio on the Windows default" signal. The page
/// half and the engine wiring are pinned by the WPF suite's source scans.
/// </summary>
[Collection(SessionStatics.Name)]
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

    [Fact]
    public void Resolve_Live_ReadsTheCoreSettingsSeam()
    {
        // The parameterless overload used to read App.Settings; in Core it reads CoreSettings.Current.
        var s = CoreSettings.Current;
        var (id, name) = (s.AudioOutputDeviceId, s.AudioOutputDeviceName);
        var env = Environment.GetEnvironmentVariable(BrowserSinkLabel.TestOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(BrowserSinkLabel.TestOverrideVariable, null);
            s.AudioOutputDeviceId = "{guid}";
            s.AudioOutputDeviceName = " Headphones ";
            Assert.Equal("Headphones", BrowserSinkLabel.Resolve());

            s.AudioOutputDeviceId = "";
            Assert.Null(BrowserSinkLabel.Resolve());
        }
        finally
        {
            s.AudioOutputDeviceId = id;
            s.AudioOutputDeviceName = name;
            Environment.SetEnvironmentVariable(BrowserSinkLabel.TestOverrideVariable, env);
        }
    }
}

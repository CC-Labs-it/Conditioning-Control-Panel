using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Super stage's cursor and click seams on this head (WPF SuperStageWpf.cs).</summary>
public class SuperPointerTests
{
    [Fact]
    public void SteamSeedWiresTheCursorAndTheClick() => AvaloniaTestDispatcher.Run(() =>
    {
        SteamOverlay.Seed();
        Assert.Equal(nameof(SuperPointer.Cursor), SuperStageHost.CursorProvider.Method.Name);
        Assert.Equal(nameof(SuperPointer.Click), SuperStageHost.ClickProvider.Method.Name);
    });

    [Fact]
    public void Win32KeysMapLikeWpf()
    {
        static short Key(int vk, short left, short right) => vk == 0x01 ? left : vk == 0x02 ? right : (short)0;
        Assert.Equal((false, false), SuperPointer.FromKeys(vk => Key(vk, 0, 0)));
        Assert.Equal((true, false), SuperPointer.FromKeys(vk => Key(vk, unchecked((short)0x8000), 0)));
        Assert.Equal((true, false), SuperPointer.FromKeys(vk => Key(vk, 0, unchecked((short)0x8000))));
        // A tap shorter than one step: only the "pressed since the last read" bit is left.
        Assert.Equal((false, true), SuperPointer.FromKeys(vk => Key(vk, 0x0001, 0)));
        Assert.Equal((true, true), SuperPointer.FromKeys(vk => Key(vk, 0, unchecked((short)0x8001))));
        // Any other key never counts.
        Assert.Equal((false, false), SuperPointer.FromKeys(vk => vk == 0x04 ? unchecked((short)0x8001) : (short)0));
    }

    [Fact]
    public void X11KnowsOnlyTheHeldButton()
    {
        Assert.Null(SuperPointer.FromX11(null));
        Assert.Equal((true, false), SuperPointer.FromX11(true)!.Value);
        Assert.Equal((false, false), SuperPointer.FromX11(false)!.Value);
    }
}

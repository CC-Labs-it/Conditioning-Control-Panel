using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ModCreatorArtFramingEditorTests
{
    private const string Slot = "features/fyp.png";
    private const string Rail = ModArtFramingRegistry.SurfaceRailChip;
    private const string Play = ModArtFramingRegistry.SurfacePlayCard;

    [Fact]
    public void WorkingCopiesArePerSurfaceAndCancelLeavesStoredValuesAlone()
    {
        var stored = new Dictionary<string, ModArtFraming> { [Rail] = new() { Zoom = 2 } };
        var working = ModCreatorWindow.BeginFramingEdit(Slot, stored);
        Assert.Equal(2, working.Count);
        Assert.Contains(Play, working.Keys);
        ModCreatorWindow.ZoomFraming(working[Rail], 1, 2, true);
        ModCreatorWindow.PanFraming(working[Rail], 1, 2, .5, .5, 100, 100);
        Assert.Equal(2, stored[Rail].Zoom);
        Assert.Equal(.5, stored[Rail].CenterX);
        Assert.True(working[Play].IsDefault);
        Assert.Empty(ModCreatorWindow.BeginFramingEdit("features/goon_game.png", null));
    }

    [Fact]
    public void PanAndZoomStayWithinTheVisibleWindow()
    {
        var f = new ModArtFraming { Zoom = 2 };
        ModCreatorWindow.PanFraming(f, 1, 2, .5, .5, 100, -100);
        var box = ModArtFramingRegistry.ToViewbox(f, 1, 2);
        Assert.Equal(box.Width / 2, f.CenterX, 8);
        Assert.Equal(1 - box.Height / 2, f.CenterY, 8);
        for (int i = 0; i < 100; i++) ModCreatorWindow.ZoomFraming(f, 1, 2, true);
        Assert.Equal(ModArtFramingRegistry.MaxZoom, f.Zoom);
        for (int i = 0; i < 100; i++) ModCreatorWindow.ZoomFraming(f, 1, 2, false);
        Assert.Equal(1, f.Zoom);
        Assert.Equal(.5, f.CenterX, 8);
    }

    [Fact]
    public void CommitCopiesNonDefaultFramingAndRemovesDefaults()
    {
        var destination = new Dictionary<string, Dictionary<string, ModArtFraming>>();
        var working = ModCreatorWindow.BeginFramingEdit(Slot, null);
        working[Rail].Zoom = 2;
        ModCreatorWindow.CommitFramingEdit(destination, Slot, working);
        Assert.Single(destination[Slot]);
        working[Rail].Zoom = 3;
        Assert.Equal(2, destination[Slot][Rail].Zoom);
        working[Rail].Zoom = 1;
        ModCreatorWindow.CommitFramingEdit(destination, Slot, working);
        Assert.Empty(destination);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
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
    [Fact]
    public Task FrameButtonOpensIndependentPreviewsAndCancelOrOkControlsExport() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var w = new ModCreatorWindow();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        using (var bmp = new WriteableBitmap(new PixelSize(120, 120), new Vector(96, 96))) bmp.Save(path);
        object? Call(string method, params object?[] args) => typeof(ModCreatorWindow)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, args);
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            w.Show();
            var buttons = (Dictionary<string, Button>)typeof(ModCreatorWindow).GetField("_frameButtons", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            Assert.True(buttons.ContainsKey(Slot));
            Assert.False(buttons.ContainsKey("features/goon_game.png"));
            Assert.False(buttons[Slot].IsVisible);
            await (Task)Call("SetImageSlot", Slot, path, false)!;
            Assert.True(buttons[Slot].IsVisible);
            Click(buttons[Slot]);
            var dialog = Assert.Single(w.OwnedWindows);
            Dispatcher.UIThread.RunJobs();
            var cards = dialog.GetLogicalDescendants().OfType<StackPanel>().Where(c => c.Tag is string id && (id == Rail || id == Play)).ToList();
            Assert.Equal(2, cards.Count);
            var holder = cards.Single(c => Equals(c.Tag, Rail)).Children.OfType<Grid>().Single();
            var at = holder.TranslatePoint(new Point(holder.Width / 2, holder.Height / 2), dialog)!.Value;
            dialog.MouseWheel(at, new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();
            var railCard = cards.Single(c => Equals(c.Tag, Rail));
            var readout = railCard.Children.OfType<TextBlock>().Last();
            Assert.Contains("1.12x", readout.Text);
            Click(railCard.Children.OfType<Button>().Single());
            Assert.Contains("1x", readout.Text);
            dialog.MouseWheel(at, new Vector(0, 1));
            Click(dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Reset all")));
            Assert.Contains("1x", readout.Text);
            dialog.MouseWheel(at, new Vector(0, 1));
            Click(dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(w.BuildManifestFromForm().ArtFraming);
            Click(buttons[Slot]);
            dialog = Assert.Single(w.OwnedWindows);
            Dispatcher.UIThread.RunJobs();
            holder = dialog.GetLogicalDescendants().OfType<StackPanel>().Single(c => Equals(c.Tag, Rail)).Children.OfType<Grid>().Single();
            at = holder.TranslatePoint(new Point(holder.Width / 2, holder.Height / 2), dialog)!.Value;
            dialog.MouseWheel(at, new Vector(0, 1));
            dialog.MouseDown(at, MouseButton.Left);
            dialog.MouseMove(new Point(at.X + 20, at.Y));
            dialog.MouseUp(new Point(at.X + 20, at.Y), MouseButton.Left);
            Click(dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "OK")));
            Dispatcher.UIThread.RunJobs();
            var framing = w.BuildManifestFromForm().ArtFraming![Slot];
            Assert.Single(framing);
            Assert.True(framing[Rail].Zoom > 1);
            Assert.True(framing[Rail].CenterX < .5);
            Assert.Equal("Framed", buttons[Slot].Content);
            Call("ClearImageSlot", Slot);
            Assert.False(buttons[Slot].IsVisible);
            Assert.Null(w.BuildManifestFromForm().ArtFraming);
        }
        finally { w.Close(); File.Delete(path); }
    });
}

using System;
using Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class GazeRefineCandidateTests
{
    [Fact]
    public void AmbiguousChoicesUseBaseScoresAndClearWhenFaceIsLost() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var settings = CoreSettings.Current;
        settings.BubbleGazePopEnabled = true;
        settings.FlashGazePopEnabled = settings.FlashGazeLingerEnabled = settings.VideoGazeClickEnabled = false;
        settings.RestrictGazeContentToCalibratedScreen = false;
        var gaze = new GazeFocus { CanRun = () => true, DwellMs = 600000 };
        var first = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
        var second = new AmbientBubble { X = 130, Y = 100, Size = 100, Clickable = true };
        BubbleOverlay.Field.Bubbles.Add(first);
        BubbleOverlay.Field.Bubbles.Add(second);
        try
        {
            gaze.MasterEnabled = true;
            gaze.HandleGazeMove(new Point(150, 150));
            gaze.Tick();
            Assert.Equal(new object[] { second, first }, gaze.RefineCandidates);
            second.X = 1000;
            gaze.Tick();
            Assert.Empty(gaze.RefineCandidates);
            second.X = 130;
            gaze.Tick();
            Assert.Empty(gaze.RefineCandidates); // sticky winner is not the base-score leader
            gaze.HandleFaceLost();
            gaze.Tick();
            Assert.Empty(gaze.RefineCandidates);
            gaze.HandleFaceFound();
            gaze.Tick();
            Assert.Equal(2, gaze.RefineCandidates.Count);
            gaze.HandleFaceLost();
            gaze.Tick();
            Assert.Empty(gaze.RefineCandidates);
            gaze.HandleFaceFound();
            gaze.Tick();
            Assert.Equal(2, gaze.RefineCandidates.Count);
            gaze.Stop();
            Assert.Empty(gaze.RefineCandidates);
        }
        finally
        {
            gaze.Stop();
            BubbleOverlay.Field.Bubbles.Remove(first);
            BubbleOverlay.Field.Bubbles.Remove(second);
        }
    });
    [Fact]
    public void LingerOnlyFlashIsNotAnActivatableChoice() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var settings = CoreSettings.Current;
        settings.BubbleGazePopEnabled = true;
        settings.FlashGazePopEnabled = false;
        settings.FlashGazeLingerEnabled = true;
        settings.VideoGazeClickEnabled = false;
        settings.RestrictGazeContentToCalibratedScreen = false;
        var gaze = new GazeFocus { CanRun = () => true, DwellMs = 600000 };
        var first = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
        var second = new AmbientBubble { X = 130, Y = 100, Size = 100, Clickable = true };
        var flash = new FlashOverlayWindow();
        BubbleOverlay.Field.Bubbles.Add(first);
        BubbleOverlay.Field.Bubbles.Add(second);
        FlashOverlay.Active.Add((flash, new PixelRect(100, 100, 100, 100)));
        try
        {
            gaze.MasterEnabled = true;
            gaze.HandleGazeMove(new Point(150, 150));
            gaze.Tick();
            Assert.Equal(2, gaze.RefineCandidates.Count);
            Assert.DoesNotContain(flash, gaze.RefineCandidates);
            BubbleOverlay.Field.Bubbles.Remove(first);
            gaze.Tick();
            Assert.Empty(gaze.RefineCandidates);
        }
        finally
        {
            gaze.Stop();
            FlashOverlay.Active.RemoveAll(x => ReferenceEquals(x.Item1, flash));
            flash.Close();
            BubbleOverlay.Field.Bubbles.Remove(first);
            BubbleOverlay.Field.Bubbles.Remove(second);
        }
    });
}

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiDeskArtTests
{
    [Fact]
    public Task PosesAndFaceReachTheWidget() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var desk = new EmiDeskWindow();
        try
        {
            var image = desk.FindControl<Image>("BodyImage")!;
            var face = desk.FindControl<EmiFace>("FaceView")!;
            foreach (var key in EmiChains.BodyFrameFile.Keys)
            {
                Assert.NotNull(EmiChains.BodyPath(key));
                var previous = image.Source;
                desk.SetPose(key);
                Assert.NotNull(image.Source);
                if (key != "idle") Assert.NotSame(previous, image.Source);
            }
            var retained = image.Source;
            desk.BodyPath = _ => null;
            desk.SetPose("idle");
            Assert.Same(retained, image.Source);
            desk.BodyPath = key => EmiChains.BodyPath(key);
            desk.SetPose("idle");
            Assert.NotSame(retained, image.Source);
            desk.DrawFace(":)", true, true);
            Assert.Equal(":)", face.Face);
            Assert.True(face.Small);
            Assert.True(face.Flat);
            desk.DrawFace(":)");
            Assert.False(face.Small);
            Assert.False(face.Flat);
        }
        finally { desk.Close(); }
    });
}

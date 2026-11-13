using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHubMenuArtTests
{
    [Fact]
    public Task Frames_click_and_view_lifetime() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ChaosHubWindow? hub = null;
        try
        {
            for (int i = 1; i <= 3; i++)
            {
                using var image = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96));
                image.Save(Path.Combine(root, $"menu_{i}.png"));
            }
            ChaosArt.RootsOverride = () => new[] { root };
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            hub = new ChaosHubWindow();
            var type = typeof(ChaosHubWindow);
            object Field(string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hub)!;
            void Invoke(string name, params object?[] args) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(hub, args);
            Assert.Equal(new[] { (0, 6000), (1, 1900), (0, 6000), (0, 6000), (2, 3000), (0, 6000), (0, 6000) }, ((int, int)[])Field("_menuSeq"));
            Assert.Same(((Bitmap?[])Field("_menuFrames"))[0], ((global::Avalonia.Media.ImageBrush)Field("_menuBase")).Source);
            void SetField(string name, object value) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(hub, value);
            SetField("_menuMotionStart", Environment.TickCount64 - 6500);
            Invoke("StepMenuMotion");
            var brush = (global::Avalonia.Media.ImageBrush)Field("_menuBase");
            var transforms = (global::Avalonia.Media.TransformGroup)brush.Transform!;
            Assert.InRange(((global::Avalonia.Media.ScaleTransform)transforms.Children[0]).ScaleX, 1.034, 1.036);
            var panel = (global::Avalonia.Controls.Border)Field("_menuArtPanel");
            Assert.InRange(((global::Avalonia.Media.DropShadowEffect)panel.Effect!).BlurRadius, 29, 32);
            hub.AdvanceMenuClick(10000);
            SetField("_menuFadeStart", Environment.TickCount64 - 275);
            Invoke("StepMenuMotion");
            Assert.InRange(((global::Avalonia.Controls.Border)Field("_menuTopBox")).Opacity, .45, .6);
            hub.AdvanceMenuClick(10001);
            Assert.Equal(1, (int)Field("_menuPos"));
            var timer = (DispatcherTimer)Field("_menuTimer");
            Assert.True(timer.IsEnabled);
            Invoke("Menu_Options_Click", null, new global::Avalonia.Interactivity.RoutedEventArgs());
            Assert.False(timer.IsEnabled);
            Invoke("Options_Back_Click", null, new global::Avalonia.Interactivity.RoutedEventArgs());
            Assert.True(timer.IsEnabled);
            Invoke("ShowDollhouseView");
            Assert.False(timer.IsEnabled);
            Invoke("ShowMenuView");
            Assert.True(timer.IsEnabled);
            hub.Show();
            hub.Close();
            Assert.False(timer.IsEnabled);
        }
        finally
        {
            hub?.Close();
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = false;
            ChaosArt.ResetForTest();
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task Click_during_automatic_fade_returns_to_idle() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ChaosHubWindow? hub = null;
        bool oldRendering = global::ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        try
        {
            for (int i = 1; i <= 3; i++)
            {
                using var image = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96));
                image.Save(Path.Combine(root, $"menu_{i}.png"));
            }
            ChaosArt.RootsOverride = () => new[] { root };
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            hub = new ChaosHubWindow();
            hub.Show();
            var type = typeof(ChaosHubWindow);
            object Field(string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hub)!;
            void SetField(string name, object value) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(hub, value);
            void Invoke(string name) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(hub, null);
            var frames = (Bitmap?[])Field("_menuFrames");
            var baseBrush = (global::Avalonia.Media.ImageBrush)Field("_menuBase");
            var topBrush = (global::Avalonia.Media.ImageBrush)Field("_menuTop");
            var topBox = (global::Avalonia.Controls.Border)Field("_menuTopBox");

            Invoke("AdvanceMenuFrame"); // automatic idle -> blink
            SetField("_menuFadeStart", Environment.TickCount64 - 275);
            Invoke("StepMenuMotion");
            Assert.Same(frames[0], baseBrush.Source);
            Assert.Same(frames[1], topBrush.Source);
            Assert.InRange(topBox.Opacity, .45, .6);

            hub.AdvanceMenuClick(Environment.TickCount64); // blink -> idle during the fade
            Assert.Same(frames[1], baseBrush.Source);
            Assert.Same(frames[0], topBrush.Source);
            Assert.Equal(0, topBox.Opacity);
            Assert.Equal(TimeSpan.FromMilliseconds(6000), ((DispatcherTimer)Field("_menuTimer")).Interval);
            SetField("_menuFadeStart", Environment.TickCount64 - 550);
            Invoke("StepMenuMotion");
            Assert.Same(frames[0], baseBrush.Source);
            Assert.Equal(0, topBox.Opacity);
        }
        finally
        {
            hub?.Close();
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = oldRendering;
            ChaosArt.ResetForTest();
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Still_then_banner_fallback() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (string name in new[] { "menu.png", "banner.png" })
            {
                using var image = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96));
                image.Save(Path.Combine(root, name));
            }
            ChaosArt.RootsOverride = () => new[] { root };
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            void Check(string name, global::Avalonia.Media.Stretch stretch)
            {
                var hub = new ChaosHubWindow();
                hub.Show();
                try
                {
                    var brush = (global::Avalonia.Media.ImageBrush)typeof(ChaosHubWindow)
                        .GetField("_menuBase", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hub)!;
                    Assert.Same(ChaosArt.TryLoad(Path.Combine(root, name)), brush.Source);
                    Assert.Equal(stretch, brush.Stretch);
                }
                finally { hub.Close(); }
            }
            Check("menu.png", global::Avalonia.Media.Stretch.UniformToFill);
            File.Delete(Path.Combine(root, "menu.png"));
            ChaosArt.ResetForTest();
            ChaosArt.RootsOverride = () => new[] { root };
            Check("banner.png", global::Avalonia.Media.Stretch.Uniform);
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = false;
            ChaosArt.ResetForTest();
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });

}

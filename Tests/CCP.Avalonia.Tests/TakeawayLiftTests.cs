using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TakeawayLiftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RemovalResetsLiftBeforeAndAfterVisualAttachment(bool attach) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var motion = CoreSettings.Current.MotionLevel;
            var chip = new Border();
            MainShellWindow.PrepareTakeawayLift(chip);
            var shelf = new TakeawayShelfPanel();
            shelf.Children.Add(chip);
            var window = new Window();
            try
            {
                CoreSettings.Current.MotionLevel = MotionLevel.Off;
                if (attach) { window.Content = shelf; window.Show(); Dispatcher.UIThread.RunJobs(); }
                Assert.Equal(attach, chip.IsAttachedToVisualTree());
                chip.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, chip,
                    new Pointer(1, PointerType.Mouse, true), window, default, 0, default, KeyModifiers.None));
                var scale = Assert.IsType<ScaleTransform>(chip.RenderTransform);
                Assert.Equal(1.02, scale.ScaleX, 3);
                Assert.Equal(1.02, scale.ScaleY, 3);
                shelf.Children.Remove(chip);
                Assert.Null(chip.Parent);
                Assert.Equal(1, scale.ScaleX);
                Assert.Equal(1, scale.ScaleY);
                Assert.Null(scale.Transitions);
            }
            finally { window.Close(); CoreSettings.Current.MotionLevel = motion; }
            return Task.CompletedTask;
        });
}

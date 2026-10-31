using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosHudShimmerTests
{
    // WPF ChaosHudWindow.xaml.cs:105-145: gate, palette, relative mapping and 2.4s repeat.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeatBarSweepsOnlyWithSkiaFx(bool enabled)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            CoreSettings.Current.ChaosSkiaFxEnabled = enabled;
            var hud = new ChaosHudWindow(new ChaosRunState(new ChaosRunConfig()) { Heat = 1 });
            try
            {
                hud.Show();
                hud.SetPreRunExpanded(true);
                hud.UpdateLayout();
                var bar = hud.FindControl<ProgressBar>("BarLust")!;
                var orange = Color.FromRgb(0xFF, 0x7A, 0x3D);
                if (!enabled)
                {
                    Assert.Equal(orange, Assert.IsAssignableFrom<ISolidColorBrush>(bar.Foreground).Color);
                    Step(hud, 1200);
                    Assert.IsAssignableFrom<ISolidColorBrush>(bar.Foreground);
                }
                else
                {
                    var brush = Assert.IsType<LinearGradientBrush>(bar.Foreground);
                    Assert.Equal(GradientSpreadMethod.Pad, brush.SpreadMethod);
                    Assert.Collection(brush.GradientStops,
                        s => Assert.Equal((orange, 0.0), (s.Color, s.Offset)),
                        s => Assert.Equal((orange, 0.4), (s.Color, s.Offset)),
                        s => Assert.Equal((Color.FromRgb(0xFF, 0xE6, 0xB0), 0.5), (s.Color, s.Offset)),
                        s => Assert.Equal((orange, 0.6), (s.Color, s.Offset)),
                        s => Assert.Equal((orange, 1.0), (s.Color, s.Offset)));
                    void At(double x)
                    {
                        Assert.Equal(new RelativePoint(x, 0.5, RelativeUnit.Relative), brush.StartPoint);
                        Assert.Equal(new RelativePoint(x + 1, 0.5, RelativeUnit.Relative), brush.EndPoint);
                    }
                    At(-1);
                    Step(hud, 600); At(-0.5);
                    Step(hud, 600); At(0);
                    Step(hud, 1199); At(-1 + 2 * 2399.0 / 2400);
                    Step(hud, 1); At(-1); // repeat, not autoreverse
                    hud.Close();
                    Step(hud, 600); At(-1); // no forever tween survives close
                }
            }
            finally { hud.Close(); }
            return Task.CompletedTask;
        });
    }

    private static void Step(ChaosHudWindow hud, double ms) =>
        typeof(ChaosHudWindow).GetMethod("StepTweens", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(hud, new object[] { ms });
}

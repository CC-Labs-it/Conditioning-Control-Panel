using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Remix;
using Xunit;

namespace CCP.Avalonia.Tests;

public class JackpotRemixFlowTests
{
    [Fact]
    public Task OwnedToggleAndCachedRemixReachAnimatedFlashAndCleanUp() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.JackpotRemixEnabled, s.MotionLevel, s.FlashClickable, s.CorruptionMode);
        var own = PrizeOwnership.IsGranted;
        var present = FlashOverlay.Present;
        var multiply = FlashOverlay.Multiply;
        var xp = CoreProgression.AddXPProvider;
        var dir = Directory.CreateTempSubdirectory("ccp-remix-flow-").FullName;
        var fixture = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "Assets/web/dtrh/assets/bubbles/effects/spirals/sp6.gif"));
        var cached = Path.Combine(dir, "remix-fixture.gif");
        File.Copy(fixture, cached);
        var card = new FlashFeatureControl();
        var shell = new Window { Content = card };
        try
        {
            PrizeOwnership.IsGranted = _ => true;
            s.JackpotRemixEnabled = false;
            shell.Show();
            // Refresh after the ownership grant, as the purchase row does.
            shell.Close();
            card = new FlashFeatureControl(); shell = new Window { Content = card }; shell.Show();
            Assert.True(card.FindControl<Grid>("RowJackpotRemix")!.IsVisible);
            card.FindControl<CheckBox>("ChkJackpotRemix")!.IsChecked = true;
            Assert.True(s.JackpotRemixEnabled);
            s.MotionLevel = MotionLevel.Full; s.FlashClickable = true; s.CorruptionMode = true;
            FlashOverlay.Present = (_, _, _) => true;
            var children = 0; FlashOverlay.Multiply = (_, _, _, _) => children++;
            var payments = 0; CoreProgression.AddXPProvider = (_, _) => payments++;
            FlashOverlay._remix = new JackpotRemixDirector(() => new[] { cached }, () => false, () => false,
                () => s.JackpotRemixEnabled, dir, () => true, () => MotionLevel.Full, () => false,
                (_, _, _) => Task.FromResult<string?>(null), () => ulong.MaxValue, odds: 1);
            var screen = shell.Screens.All[0];
            Assert.True(await FlashOverlay.TryShowRemix(s, new[] { screen }, null, true, FlashOverlay.Generation));
            Assert.False(File.Exists(cached));
            var window = Assert.Single(FlashOverlay.Active).Window;
            Assert.True(window.IsRemix);
            Assert.Equal(1, payments);
            var first = window.Picture;
            for (var i = 0; i < 30 && ReferenceEquals(first, window.Picture); i++) await Task.Delay(100);
            Assert.NotSame(first, window.Picture);
            FlashOverlay.Pop(window);
            Assert.Equal(0, children);
            FlashOverlay.CloseAll(false);
            Assert.Empty(FlashOverlay.Active);
        }
        finally
        {
            FlashOverlay.CloseAll(false); shell.Close();
            PrizeOwnership.IsGranted = own; FlashOverlay.Present = present; FlashOverlay.Multiply = multiply;
            CoreProgression.AddXPProvider = xp;
            (s.JackpotRemixEnabled, s.MotionLevel, s.FlashClickable, s.CorruptionMode) = old;
            Directory.Delete(dir, true);
        }
    });

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesVaultClickTests
{
    [Fact]
    public Task CardOpensImmediatelyWithoutParticlesAndAfterBurstWithParticles() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode);
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            settings.PerformanceMode = false;
            shell.Show();
            shell.ShowTab("premium");
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var card = view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "ExCard");
            var feature = ((ExclusiveCardRow)card.DataContext!).Feature;
            var canvas = view.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            int opened = 0;
            settings.MotionLevel = MotionLevel.Off;
            view.OnVaultCardClicked(card, feature, () => opened++);
            Assert.Equal(1, opened);
            Assert.Equal(0, canvas.BurstCount);

            settings.MotionLevel = MotionLevel.Full;
            Assert.True(AmbientFxCanvas.Env.AllowParticles);
            view.OnVaultCardClicked(card, feature, () => opened++);
            Assert.Equal(1, opened);
            Assert.True(canvas.BurstCount > 0);
            for (int i = 0; i < 50 && opened == 1; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.Post(() => { });
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(2, opened);
        }
        finally
        {
            shell.Close();
            (settings.MotionLevel, settings.PerformanceMode) = old;
        }
    });
}

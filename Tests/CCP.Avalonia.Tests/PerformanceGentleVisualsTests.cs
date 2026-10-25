using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class PerformanceGentleVisualsTests
{
    [Fact]
    public Task GentleEditorSeedsWithoutWritingAndAppliesChoicesAndMotionCap() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previousProvider = CoreSettings.ServiceProvider;
        var service = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var saved = (s.GentleVisuals, s.GentleFadeSeconds, s.GentleSpeed, s.MotionLevel);
        Window? window = null;
        var notifications = 0;
        void Changed() => notifications++;
        try
        {
            (s.GentleVisuals, s.GentleFadeSeconds, s.GentleSpeed, s.MotionLevel) = (false, 0.9, 0.6, MotionLevel.Full);
            var section = new PerformanceSettingsSection();
            window = new Window { Content = section };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var box = section.FindControl<CheckBox>("ChkGentleVisuals");
            Assert.NotNull(box);
            var choices = section.FindControl<StackPanel>("PnlGentleChoices")!;
            var fade = section.FindControl<ComboBox>("CmbGentleFade")!;
            var speed = section.FindControl<ComboBox>("CmbGentleSpeed")!;
            Assert.False(choices.IsVisible);
            Assert.Equal(3, fade.SelectedIndex); // nearest 1 s; seeding must not round the stored value
            Assert.Equal(2, speed.SelectedIndex);
            Assert.Equal(0.9, s.GentleFadeSeconds);
            Assert.Equal(0.6, s.GentleSpeed);
            Assert.Equal(GentleVisuals.FadeChoices.Length, fade.ItemCount);
            Assert.Equal(GentleVisuals.SpeedChoices.Length, speed.ItemCount);
            AmbientFxCanvas.Env.MotionGateChanged += Changed;
            box.IsChecked = true;
            Assert.True(s.GentleVisuals);
            Assert.True(choices.IsVisible);
            Assert.Equal(1, notifications);
            Assert.Equal(MotionLevel.Reduced, AmbientFxCanvas.Env.Level);
            Assert.Equal(MotionLevel.Full, s.MotionLevel); // cap the effective value, preserve the editor
            Assert.False(AmbientFxCanvas.Env.AllowAmbientLoops);
            fade.SelectedIndex = 5;
            speed.SelectedIndex = 3;
            Assert.Equal(2, s.GentleFadeSeconds);
            Assert.Equal(0.25, s.GentleSpeed);
            Assert.Equal(2, GentleVisuals.FadeSeconds);
            Assert.Equal(0.25, GentleVisuals.Speed);
            s.MotionLevel = MotionLevel.Off;
            Assert.Equal(MotionLevel.Off, AmbientFxCanvas.Env.Level);
            s.MotionLevel = MotionLevel.Full;
            box.IsChecked = false;
            Assert.False(choices.IsVisible);
            Assert.Equal(MotionLevel.Full, AmbientFxCanvas.Env.Level);
            Assert.Equal(2, notifications);
        }
        finally
        {
            AmbientFxCanvas.Env.MotionGateChanged -= Changed;
            window?.Close();
            (s.GentleVisuals, s.GentleFadeSeconds, s.GentleSpeed, s.MotionLevel) = saved;
            CoreSettings.ServiceProvider = previousProvider;
        }
        return Task.CompletedTask;
    });
}

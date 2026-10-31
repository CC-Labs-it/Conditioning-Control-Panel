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

// WPF PerformanceSettingsSection.xaml:163 and .xaml.cs:111,320-364.
public sealed class PerformanceGentleVisualsTests
{
    [Theory]
    [InlineData("switch"), InlineData("visibility"), InlineData("refresh"), InlineData("save")]
    [InlineData("fade"), InlineData("speed"), InlineData("fadeSave"), InlineData("speedSave")]
    [InlineData("fadeChoices"), InlineData("speedChoices")]
    public Task GentleActionsReachLiveSettings(string action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => service; // assembly-wide isolation restores this seam
        var s = CoreSettings.Current;
        s.GentleVisuals = false;
        s.GentleFadeSeconds = 0.9;
        s.GentleSpeed = 0.6;
        var section = new PerformanceSettingsSection();
        var notifications = 0;
        void Changed() => notifications++;
        AmbientFxCanvas.Env.MotionGateChanged += Changed;
        try
        {
            if (action == "fadeChoices")
                Assert.Equal(GentleVisuals.FadeChoices.Length, section.FindControl<ComboBox>("CmbGentleFade")!.ItemCount);
            else if (action == "speedChoices")
                Assert.Equal(GentleVisuals.SpeedChoices.Length, section.FindControl<ComboBox>("CmbGentleSpeed")!.ItemCount);
            else if (action == "fade" || action == "fadeSave")
            {
                section.FindControl<ComboBox>("CmbGentleFade")!.SelectedIndex = 5;
                if (action == "fadeSave") Assert.True(service.HasPendingSave);
                else Assert.Equal(2, s.GentleFadeSeconds);
            }
            else if (action == "speed" || action == "speedSave")
            {
                section.FindControl<ComboBox>("CmbGentleSpeed")!.SelectedIndex = 3;
                if (action == "speedSave") Assert.True(service.HasPendingSave);
                else Assert.Equal(0.25, s.GentleSpeed);
            }
            else
            {
                section.FindControl<CheckBox>("ChkGentleVisuals")!.IsChecked = true;
                if (action == "refresh") Assert.Equal(1, notifications);
                else if (action == "save") Assert.True(service.HasPendingSave);
                else if (action == "visibility") Assert.True(section.FindControl<StackPanel>("PnlGentleChoices")!.IsVisible);
                else Assert.True(s.GentleVisuals);
            }
        }
        finally { AmbientFxCanvas.Env.MotionGateChanged -= Changed; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0.625, 1)] // exact tie keeps the first choice, WPF .xaml.cs:329
    [InlineData(0.9, 3)]
    public void FadeChoiceUsesWpfNearestStep(double value, int expected) =>
        Assert.Equal(expected, PerformanceSettingsSection.NearestIndex(GentleVisuals.FadeChoices, value));

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
            Assert.False(service.HasPendingSave);
            AmbientFxCanvas.Env.MotionGateChanged += Changed;
            s.GentleVisuals = true;
            section.OnSectionShown();
            Assert.True(box.IsChecked);
            Assert.True(choices.IsVisible);
            s.GentleVisuals = false;
            section.OnSectionShown();
            Assert.False(box.IsChecked);
            Assert.False(choices.IsVisible);
            Assert.Equal(0, notifications);
            Assert.False(service.HasPendingSave);
            box.IsChecked = true;
            Assert.True(s.GentleVisuals);
            Assert.True(service.HasPendingSave);
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

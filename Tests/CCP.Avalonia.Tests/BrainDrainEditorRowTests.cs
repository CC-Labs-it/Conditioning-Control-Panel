using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using Xunit;

namespace CCP.Avalonia.Tests;

// Both sliders use the WPF 0-100 range. WPF BrainDrainFeatureControl.xaml:153-192,200-212,234-265;
// .xaml.cs:88-110,207-263,291-319,346-380.
public sealed class BrainDrainEditorRowTests
{
    [Theory]
    [InlineData("BrainDrainEnabled", "ChkEnable", null)]
    [InlineData("BrainDrainIntensity", "SliderIntensity", "TxtIntensity")]
    [InlineData("BrainDrainBlurStrength", "SliderBlurStrength", "TxtBlurStrength")]
    [InlineData("BrainDrainMeltEnabled", "ChkMelt", null)]
    [InlineData("BrainDrainHighRefresh", "ChkHighRefresh", null)]
    [InlineData("AllowOverlayCapture", "ChkAllowCapture", null)]
    public Task EditorLoadsWritesAndFollowsSettings(string property, string control, string? label) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont()
                    .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                    .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var setting = typeof(AppSettings).GetProperty(property)!;
            bool slider = label is not null;
            object initial = slider ? 34 : true;
            object edited = slider ? (property == "BrainDrainBlurStrength" ? 0 : 1) : false;
            object external = slider ? 77 : true;
            setting.SetValue(service.Current, initial);
            var card = new BrainDrainFeatureControl();
            var host = new Window { Content = card };
            object Read() => slider ? (object)(int)card.FindControl<Slider>(control)!.Value
                                    : card.FindControl<CheckBox>(control)!.IsChecked!.Value;
            void Check(object expected)
            {
                Assert.Equal(expected, Read());
                if (slider) Assert.Equal($"{expected}%", card.FindControl<TextBlock>(label!)!.Text);
            }
            try
            {
                host.Show();
                Dispatcher.UIThread.RunJobs();
                Check(initial);
                if (slider)
                {
                    Assert.Equal(0, card.FindControl<Slider>(control)!.Minimum);
                    Assert.Equal(100, card.FindControl<Slider>(control)!.Maximum);
                }
                service.SaveImmediate();
                if (slider) card.FindControl<Slider>(control)!.Value = (int)edited;
                else card.FindControl<CheckBox>(control)!.IsChecked = (bool)edited;
                Assert.Equal(edited, setting.GetValue(service.Current));
                Check(edited);
                Assert.True(service.HasPendingSave);

                setting.SetValue(service.Current, external);
                Dispatcher.UIThread.RunJobs();
                Check(external);

                var old = service.Current;
                var replacement = new AppSettings();
                setting.SetValue(replacement, edited);
                service.RestoreFrom(replacement);
                Dispatcher.UIThread.RunJobs();
                Check(edited);
                Assert.False(service.HasPendingSave);
                setting.SetValue(service.Current, external);
                Dispatcher.UIThread.RunJobs();
                Check(external);
                setting.SetValue(service.Current, edited);
                Dispatcher.UIThread.RunJobs();
                Check(edited);
                setting.SetValue(old, initial);
                Dispatcher.UIThread.RunJobs();
                Check(edited);

                host.Content = null;
                Dispatcher.UIThread.RunJobs();
                setting.SetValue(service.Current, external);
                Dispatcher.UIThread.RunJobs();
                Check(edited);
                host.Content = card;
                Dispatcher.UIThread.RunJobs();
                Check(external);
            }
            finally
            {
                host.Close();
                CoreSettings.ServiceProvider = null;
            }
            return Task.CompletedTask;
        });
}

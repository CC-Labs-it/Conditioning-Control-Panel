using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiDeskGlassTests
{
    // WPF settings :152-157, Glass.cs:206-215/256-283/385-412, EmiChannels.cs:58-82.
    [Fact]
    public Task SettingsControlsTheIdleSaverAndItsTenSecondLife() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var savedBedtime = EmiState.Current.BedtimeUntil;
        var language = LocalizationManager.Instance.CurrentLanguage;
        var section = new EmiDeskSettingsSection();
        var host = new Window { Content = section };
        var desk = new EmiDeskWindow { GlassWrongRoll = () => false };
        try
        {
            LocalizationManager.Instance.SetLanguage("en");
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = false;
            host.Show(); desk.Show(); Dispatcher.UIThread.RunJobs();
            var toggle = section.FindControl<CheckBox>("ChkGlass")!;
            var now = DateTime.UtcNow;
            desk.PollGlass(now.AddSeconds(61));
            Assert.False(desk.GlassIsLive);
            toggle.IsChecked = true;
            Assert.True(CoreSettings.Current.EmiDeskGlass);
            desk.PollGlass(now);
            Assert.False(desk.GlassIsLive);
            var flip = now.AddSeconds(61);
            desk.PollGlass(flip);
            Assert.True(desk.GlassIsLive);
            desk.AdvanceGlass(flip.AddMilliseconds(219));
            Assert.False(desk.GlassHasLanded);
            desk.AdvanceGlass(flip.AddMilliseconds(220));
            Assert.True(desk.GlassHasLanded);
            var layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            Assert.Contains(layer.Children.OfType<TextBlock>(), t => t.Text == "brb");
            Assert.Contains(layer.Children.OfType<TextBlock>(), t => t.Text == "NO SIGNAL");
            desk.AdvanceGlass(flip.AddMilliseconds(10_219));
            Assert.True(desk.GlassIsLive);
            desk.AdvanceGlass(flip.AddMilliseconds(10_220));
            Assert.False(desk.GlassIsLive);
            Assert.Empty(desk.GlassHost.Children);
            toggle.IsChecked = false;
            Assert.False(CoreSettings.Current.EmiDeskGlass);
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.False(desk.GlassIsLive);
        }
        finally
        {
            desk.ShutDown(); host.Close(); EmiState.Current.BedtimeUntil = savedBedtime;
            LocalizationManager.Instance.SetLanguage(language);
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task GlassRunsOnTheSharedBeatAndStopsWhenClosed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var savedBedtime = EmiState.Current.BedtimeUntil;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var desk = new EmiDeskWindow { GlassWrongRoll = () => false };
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.True(desk.GlassIsLive);
            Assert.Contains(BeatLoop.Running, loop => loop.Owner == desk);
            // WPF EmiDeskWindow.Glass.cs:321-332: four distinct tears over 220 ms.
            var layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            var previousTear = layer.Children[0];
            int paints = 1;
            for (int i = 0; i < 7; i++)
            {
                time.Advance(TimeSpan.FromMilliseconds(i == 6 ? 21 : 33));
                BeatLoop.StepAll();
                if (!ReferenceEquals(previousTear, layer.Children[0]))
                {
                    paints++;
                    previousTear = layer.Children[0];
                }
            }
            Assert.Equal(4, paints);
            Assert.False(desk.GlassHasLanded);
            time.Advance(TimeSpan.FromMilliseconds(1)); BeatLoop.StepAll();
            Assert.True(desk.GlassHasLanded);
            time.Advance(TimeSpan.FromMilliseconds(9999)); BeatLoop.StepAll();
            Assert.True(desk.GlassIsLive);
            time.Advance(TimeSpan.FromMilliseconds(1)); BeatLoop.StepAll();
            Assert.False(desk.GlassIsLive);
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == desk);
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.Contains(BeatLoop.Running, loop => loop.Owner == desk);
            desk.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.False(desk.GlassIsLive);
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == desk);
        }
        finally
        {
            desk.ShutDown();
            BeatLoop.Time = TimeProvider.System;
            EmiState.Current.BedtimeUntil = savedBedtime;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    // WPF Glass.cs:174-185/256-278 and Presentation.cs:23: activity and competing work take precedence.
    [Fact]
    public Task ActivityBedtimeAndPresentationKeepTheGlassQuiet() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var savedBedtime = EmiState.Current.BedtimeUntil;
        var desk = new EmiDeskWindow { GlassWrongRoll = () => false };
        try
        {
            CoreSettings.Current.EmiDeskGlass = true;
            EmiState.Current.BedtimeUntil = DateTime.UtcNow.AddHours(1);
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var later = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(later);
            Assert.False(desk.GlassIsLive);
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            desk.PollGlass(later);
            Assert.True(desk.GlassIsLive);
            desk.NoteGlassActivity();
            Assert.False(desk.GlassIsLive);
            desk.PollGlass(DateTime.UtcNow);
            Assert.False(desk.GlassIsLive);
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.True(desk.GlassIsLive);
            desk.BeginPresentation(() => { });
            Assert.False(desk.GlassIsLive);
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.False(desk.GlassIsLive);
            desk.EndPresentation();
        }
        finally
        {
            desk.ShutDown(); EmiState.Current.BedtimeUntil = savedBedtime;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
    // WPF Glass.cs:188-215: the real one-second watch reaches the saver on a visible desk.
    [Fact]
    public Task VisibleDeskRunsTheIdleWatchAndStopsOnHide() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var savedBedtime = EmiState.Current.BedtimeUntil;
        var desk = new EmiDeskWindow { GlassWrongRoll = () => false };
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            typeof(EmiDeskWindow).GetField("_glassActivity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(desk, DateTime.UtcNow.AddSeconds(-61));
            for (int i = 0; i < 150 && !desk.GlassHasLanded; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(desk.GlassHasLanded);
            desk.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.False(desk.GlassIsLive);
            Assert.Empty(desk.GlassHost.Children);
        }
        finally
        {
            desk.ShutDown(); EmiState.Current.BedtimeUntil = savedBedtime;
            CoreSettings.ServiceProvider = old;
        }
    });
    // WPF EmiChannels.Screensavers.cs:371-389: retain the English card copy.
    [Fact]
    public Task OffAirUsesTheSelectedLanguage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var language = LocalizationManager.Instance.CurrentLanguage;
        var savedBedtime = EmiState.Current.BedtimeUntil;
        var desk = new EmiDeskWindow { GlassWrongRoll = () => false };
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var labels = new (string lang, string brb, string signal)[]
            {
                ("en", "brb", "NO SIGNAL"), ("de", "gleich da", "KEIN SIGNAL"),
                ("es", "vuelvo", "SIN SEÑAL"), ("fr", "de retour", "PAS DE SIGNAL"),
                ("it", "torno subito", "NESSUN SEGNALE"), ("pt-BR", "já volto", "SEM SINAL"),
                ("ru", "скоро вернусь", "НЕТ СИГНАЛА"), ("ja", "すぐ戻る", "信号なし"),
                ("ko", "곧 돌아옴", "신호 없음"), ("zh-CN", "马上回来", "无信号")
            };
            foreach (var (lang, brb, signal) in labels)
            {
                LocalizationManager.Instance.SetLanguage(lang);
                desk.NoteGlassActivity();
                var flip = DateTime.UtcNow.AddSeconds(61);
                desk.PollGlass(flip);
                desk.AdvanceGlass(flip.AddMilliseconds(220));
                var layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
                Assert.Contains(layer.Children.OfType<TextBlock>(), t => t.Text == brb);
                Assert.Contains(layer.Children.OfType<TextBlock>(), t => t.Text == signal);
            }
        }
        finally
        {
            desk.ShutDown(); EmiState.Current.BedtimeUntil = savedBedtime;
            LocalizationManager.Instance.SetLanguage(language);
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}

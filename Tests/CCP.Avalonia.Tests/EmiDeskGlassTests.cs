using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
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
        EmiLineEngine.Instance.ResetForTests();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var savedBedtime = EmiState.Current.BedtimeUntil;
        var language = LocalizationManager.Instance.CurrentLanguage;
        var section = new EmiDeskSettingsSection();
        var host = new Window { Content = section };
        var oldPick = EmiDeskWindow.GlassPick; EmiDeskWindow.GlassPick = _ => "offair";
        var desk = new EmiDeskWindow();
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
            // WPF EmiDeskWindow.Glass.cs:472-477: an untouched channel ends in the wordless effectDeclined hold.
            Assert.True(EmiLineEngine.Instance.HoldActive);
            Assert.Empty(desk.BubbleHost.Children);
            toggle.IsChecked = false;
            Assert.False(CoreSettings.Current.EmiDeskGlass);
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.False(desk.GlassIsLive);
        }
        finally
        {
            EmiLineEngine.Instance.ResetForTests();
            EmiDeskWindow.GlassPick = oldPick;
            desk.ShutDown(); host.Close(); EmiState.Current.BedtimeUntil = savedBedtime;
            LocalizationManager.Instance.SetLanguage(language);
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task ExpiryTestStartsAndEndsWithAClearLineEngine()
    {
        var engine = EmiLineEngine.Instance;
        engine.ResetForTests();
        try
        {
            engine.Draw("effectDeclined");
            Assert.True(engine.HoldActive);
            await SettingsControlsTheIdleSaverAndItsTenSecondLife();
            Assert.False(engine.HoldActive);
        }
        finally { engine.ResetForTests(); }
    }

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
        var oldPick = EmiDeskWindow.GlassPick; EmiDeskWindow.GlassPick = _ => "offair";
        var desk = new EmiDeskWindow();
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
            EmiDeskWindow.GlassPick = oldPick;
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
        var oldPick = EmiDeskWindow.GlassPick; EmiDeskWindow.GlassPick = _ => "offair";
        var desk = new EmiDeskWindow();
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
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(desk.GlassHasLanded);
            desk.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.False(desk.GlassIsLive);
            Assert.Empty(desk.GlassHost.Children);
        }
        finally
        {
            EmiDeskWindow.GlassPick = oldPick;
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
        var oldPick = EmiDeskWindow.GlassPick; EmiDeskWindow.GlassPick = _ => "offair";
        var desk = new EmiDeskWindow();
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
            EmiDeskWindow.GlassPick = oldPick;
            desk.ShutDown(); EmiState.Current.BedtimeUntil = savedBedtime;
            LocalizationManager.Instance.SetLanguage(language);
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task GlassFramesUseTheSharedBeatAndStopOnActivity() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var savedBedtime = EmiState.Current.BedtimeUntil;
        var oldTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var oldPick = EmiDeskWindow.GlassPick; EmiDeskWindow.GlassPick = _ => "offair";
        EmiLineEngine.Instance.ResetForTests();
        var desk = new EmiDeskWindow();
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var now = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(now);
            var layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            var loop = Assert.Single(BeatLoop.Running, b => ReferenceEquals(b.Owner, layer));
            Assert.True(loop.OnFrameBeat);
            time.Advance(TimeSpan.FromMilliseconds(219)); BeatLoop.StepAll();
            Assert.False(desk.GlassHasLanded);
            time.Advance(TimeSpan.FromMilliseconds(1)); BeatLoop.StepAll();
            Assert.True(desk.GlassHasLanded);
            time.Advance(TimeSpan.FromSeconds(10)); BeatLoop.StepAll();
            Assert.False(desk.GlassIsLive);
            Assert.DoesNotContain(loop, BeatLoop.Running);
            Assert.True(EmiLineEngine.Instance.HoldActive);   // the expiry hold keeps the next flip back
            EmiLineEngine.Instance.ResetForTests();

            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            loop = Assert.Single(BeatLoop.Running, b => ReferenceEquals(b.Owner, layer));
            desk.NoteGlassActivity();
            Assert.False(desk.GlassIsLive);
            Assert.DoesNotContain(loop, BeatLoop.Running);
        }
        finally
        {
            EmiLineEngine.Instance.ResetForTests();
            EmiDeskWindow.GlassPick = oldPick;
            desk.ShutDown(); EmiState.Current.BedtimeUntil = savedBedtime;
            BeatLoop.Time = oldTime; CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });
    // WPF EmiDeskWindow.Bubble.cs:545-555: a hold (panic, intake, an ignored offer) closes a live channel first.
    [Fact]
    public Task AHoldFaceClosesALiveSaver() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var savedBedtime = EmiState.Current.BedtimeUntil;
        var oldPick = EmiDeskWindow.GlassPick; EmiDeskWindow.GlassPick = _ => "offair";
        var engine = EmiLineEngine.Instance;
        engine.ResetForTests();
        var desk = new EmiDeskWindow();
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.True(desk.GlassIsLive);
            var line = engine.Draw("effectDeclined");
            Assert.NotNull(line);
            Assert.True(line!.Hold);
            desk.SpeakLine(line, engine);
            Assert.False(desk.GlassIsLive);
            Assert.Empty(desk.GlassHost.Children);
            Assert.True(desk.HoldPending);
            Assert.Empty(desk.BubbleHost.Children);
        }
        finally
        {
            engine.ResetForTests();
            EmiDeskWindow.GlassPick = oldPick;
            desk.ShutDown(); EmiState.Current.BedtimeUntil = savedBedtime;
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });
    // WPF EmiChannels.Screensavers.cs:371-411: both labels fit, including the signal's last step.
    [Fact]
    public Task OffAirLabelsFitTheSmallestGlassInEveryLanguage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var language = LocalizationManager.Instance.CurrentLanguage;
        var savedBedtime = EmiState.Current.BedtimeUntil;
        var oldPick = EmiDeskWindow.GlassPick; EmiDeskWindow.GlassPick = _ => "offair";
        var desk = new EmiDeskWindow();
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            desk.ApplyBodyWidth(EmiDeskWindow.MinBodyWidth);
            desk.Show(); Dispatcher.UIThread.RunJobs();
            foreach (var lang in new[] { "en", "de", "es", "fr", "it", "pt-BR", "ru", "ja", "ko", "zh-CN" })
            {
                LocalizationManager.Instance.SetLanguage(lang);
                desk.NoteGlassActivity();
                var flip = DateTime.UtcNow.AddSeconds(61);
                desk.PollGlass(flip);
                desk.AdvanceGlass(flip.AddMilliseconds(220));
                var layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
                var labels = layer.Children.OfType<TextBlock>().ToArray();
                Assert.Equal(2, labels.Length);
                double VisibleWidth(TextBlock label)
                {
                    var probe = new TextBlock { Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize };
                    probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    return probe.DesiredSize.Width * (label.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1);
                }
                var maxSignalOffset = 30 * Math.Max(.1, layer.Width / 152);
                Assert.True(VisibleWidth(labels[0]) <= layer.Width + .01, $"{lang}: centre label exceeds glass");
                Assert.True(Math.Abs(Canvas.GetLeft(labels[0]) + VisibleWidth(labels[0]) / 2 - layer.Width / 2) < .1, $"{lang}: centre label is not centered");
                Assert.True(VisibleWidth(labels[1]) + maxSignalOffset <= layer.Width + .01, $"{lang}: signal exceeds glass at its last step");
                for (int step = 0; step <= 5; step++)
                {
                    desk.AdvanceGlass(flip.AddMilliseconds(220).AddSeconds(step));
                    Assert.True(Canvas.GetLeft(labels[1]) + VisibleWidth(labels[1]) <= layer.Width + .01, $"{lang}: signal exceeds glass at step {step}");
                }
            }
        }
        finally
        {
            EmiDeskWindow.GlassPick = oldPick;
            desk.ShutDown(); EmiState.Current.BedtimeUntil = savedBedtime;
            LocalizationManager.Instance.SetLanguage(language); CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });
}

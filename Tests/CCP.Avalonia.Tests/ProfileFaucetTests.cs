using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Services.Haptics.Core;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileFaucetTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("release")]
    [InlineData("tab")]
    [InlineData("logout")]
    [InlineData("pointer")]
    [InlineData("focus")]
    [InlineData("reduced")]
    [InlineData("compact")]
    [InlineData("muted")]
    [InlineData("brim")]
    [InlineData("enter")]
    [InlineData("repeat")]
    [InlineData("off")]
    public Task TrainerCardHoldsUntilACompletedKeyboardCharge(string action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        bool completed = action is "complete" or "reduced" or "compact" or "muted" or "brim" or "enter" or "repeat" or "off";
        int earned = action == "brim" ? 5000 : 1000;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        s.AvatarEnabled = false; s.PerformanceMode = true; s.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.UnifiedId = "faucet-test"; s.OfflineMode = false;
        s.MasterVolume = action == "muted" ? 0 : 100; s.VatPouredTodayXp = 0; s.VatPouredAccount = "";
        CoreSecrets.RetrieveProvider = _ => "test-token";
        CoreSecrets.StoreProvider = (_, _) => { };
        var priorHaptics = CoreHaptics.Service;
        using var haptics = new ConditioningControlPanel.Services.HapticService(s.Haptics);
        var rule = haptics.Settings.V2.Rule(HapticEventKind.LevelUp); rule.Enabled = action != "muted"; rule.Intensity = action == "reduced" ? 0 : 1;
        CoreHaptics.Service = haptics; var pulses = new List<string>(); haptics.HapticTriggered += (_, value) => pulses.Add(value);
        MainShellWindow? shell = null;
        var sounds = new List<string>(); Exception? feedbackError = null; int responses = 0;
        void Step(double ms) => typeof(MainShellWindow).GetMethod("UpdateFaucetFeedback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, new object[] { ms });
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) =>
        {
            var file = Path.GetFileName(path); sounds.Add(file);
            if (file is not ("faucet_pour.wav" or "faucet_charge_drop.wav")) return;
            responses++;
            try
            {
                var page = shell!.ProfilePage!; var sparks = page.FindControl<Canvas>("ProfileVatSparks")!;
                if (action == "off") { Assert.Empty(sparks.Children); Assert.Equal(1, (page.FindControl<Grid>("ProfileVatHandle")!.RenderTransform as ScaleTransform)?.ScaleX ?? 1); Assert.False((typeof(MainShellWindow).GetField("_faucetFeedbackTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell) as DispatcherTimer)?.IsEnabled ?? false); return; }
                var transforms = (TransformGroup)page.FindControl<Grid>("ProfileVatFaucet")!.RenderTransform!;
                if (file == "faucet_pour.wav")
                {
                    Assert.Equal(2.1, ((ScaleTransform)page.FindControl<Grid>("ProfileVatHandle")!.RenderTransform!).ScaleX);
                    Assert.InRange(sparks.Children.Count, action is "compact" or "reduced" ? 0 : 5, action is "compact" or "reduced" ? 0 : 9);
                    Step(112); Assert.InRange(((ScaleTransform)page.FindControl<Grid>("ProfileVatHandle")!.RenderTransform!).ScaleX, .86, 1);
                    Assert.InRange(((RotateTransform)transforms.Children[0]).Angle, 1, 14);
                }
                else
                {
                    Step(30); Assert.InRange(((TranslateTransform)transforms.Children[1]).X, -2.6, -.1);
                    Assert.True(page.FindControl<Control>("ProfileVatFaucetDrop")!.Opacity > 0);
                    Assert.True(((TranslateTransform)page.FindControl<Control>("ProfileVatFaucetDrop")!.RenderTransform!).Y > 0);
                    Step(520); Assert.Equal(0, ((TranslateTransform)transforms.Children[1]).X); Assert.Equal(0, page.FindControl<Control>("ProfileVatFaucetDrop")!.Opacity);
                }
            }
            catch (Exception ex) { feedbackError = ex; } // CoreAudio isolates providers, so assert outside its callback.
        };
        var reply = DescentReader.ParseWire("{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":4000,\"today_xp\":1000,\"fill_lip_pct\":120}}}")!;
        if (action == "brim") reply["descent"]!["vat"]!["today_xp"] = earned;
        var service = new DescentService(_ => Task.FromResult<JObject?>(reply));
        AvApp.Descent = service;
        try
        {
            await service.RefreshAsync("seed", force: true);
            shell = new MainShellWindow(); shell.Show(); shell.Activate();
            shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            var page = shell.ProfilePage!;
            var glass = page.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            var ring = page.FindControl<Grid>("ProfileVatChargeRing")!;
            Assert.True(glass.IsVisible); Assert.True(tap.IsVisible);
            Assert.Equal(0, glass.Fill); Assert.Equal(0, s.VatPouredTodayXp);
            if (action == "off") s.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            if (action == "reduced") s.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Reduced;
            if (action == "compact")
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(MainShellWindow).GetField("_faucetDay", flags)!.SetValue(shell, DateTime.UtcNow.Date);
                typeof(MainShellWindow).GetField("_faucetPours", flags)!.SetValue(shell, 3);
            }
            tap.Focus();
            if (action == "pointer") shell.MouseDown(tap.TranslatePoint(new Point(15, 18), shell)!.Value, MouseButton.Left);
            else tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = action == "enter" ? Key.Enter : Key.Space });
            Assert.True(ring.IsVisible);
            if (action == "reduced") Assert.IsType<global::Avalonia.Media.EllipseGeometry>(page.FindControl<global::Avalonia.Controls.Shapes.Path>("ProfileVatChargeArc")!.Data);
            Assert.Equal(action != "muted", sounds.Contains("faucet_charge_1.wav"));
            var budget = (double)typeof(MainShellWindow).GetField("_faucetBudget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell)!;
            Assert.Equal(action == "compact" ? 570 : action == "brim" ? 1400 : 950, budget);
            Assert.Equal(0, s.VatPouredTodayXp); // press alone cannot pour
            if (action == "release") tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
            if (action == "pointer") shell.MouseUp(tap.TranslatePoint(new Point(15, 18), shell)!.Value, MouseButton.Left);
            if (action == "focus") tap.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            if (action == "tab") shell.ShowTab("settings");
            if (action == "logout") { s.OfflineMode = true; service.Reset(); Dispatcher.UIThread.RunJobs(); }
            await Task.Delay(1600);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ring.IsVisible); Assert.Null(feedbackError);
            Assert.Equal(action != "muted" && (completed || action is "release" or "pointer") ? 1 : 0, responses);
            Assert.Equal(completed && action is not ("muted" or "reduced"), pulses.Contains("LevelUp: 35%"));
            if (completed && action != "off") Step(620);
            Assert.Empty(page.FindControl<Canvas>("ProfileVatSparks")!.Children);
            if (completed && action != "off")
            {
                Assert.Equal(1, ((ScaleTransform)page.FindControl<Grid>("ProfileVatHandle")!.RenderTransform!).ScaleX);
                glass.SnapTo(glass.Fill); Step(2000); Step(2240);
                Assert.Equal(0, ((RotateTransform)((TransformGroup)tap.RenderTransform!).Children[0]).Angle);
            }
            if (action == "repeat")
            {
                glass.SnapTo(.25); // finish the visual before accepting a fresh earn
                reply["descent"]!["vat"]!["today_xp"] = 1200;
                await service.RefreshAsync("new earn", force: true);
                tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
                Assert.False(ring.IsVisible); // holding the same key cannot start a second charge
            }
            if (completed && action != "muted")
                for (int rung = 1; rung <= 6; rung++) Assert.Contains($"faucet_charge_{rung}.wav", sounds);
            if (action == "brim") Assert.Contains("faucet_brim.wav", sounds);
            Assert.Equal(completed ? earned : 0, s.VatPouredTodayXp);
            Assert.Equal(completed && action != "muted", sounds.Contains("faucet_pour.wav"));
            if (action == "release") Assert.Contains("faucet_charge_drop.wav", sounds);
            if (action == "logout") { Assert.False(glass.IsVisible); Assert.False(tap.IsVisible); }
            else
            {
                if (action == "enter") { typeof(MainShellWindow).GetMethod("StartFaucetFeedback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, new object[] { true }); Assert.True((typeof(MainShellWindow).GetField("_faucetFeedbackTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell) as DispatcherTimer)!.IsEnabled); }
                shell.Close(); Assert.False((typeof(MainShellWindow).GetField("_faucetFeedbackTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell) as DispatcherTimer)?.IsEnabled ?? false);
                shell = new MainShellWindow(); shell.Show(); shell.ShowTab("discord");
                glass = shell.ProfilePage!.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
                Assert.Equal(completed ? action == "brim" ? 1.22 : .25 : 0, glass.Fill, 3);
            }
        }
        finally { shell?.Close(); service.Reset(); CoreHaptics.Service = priorHaptics; }
    });
}

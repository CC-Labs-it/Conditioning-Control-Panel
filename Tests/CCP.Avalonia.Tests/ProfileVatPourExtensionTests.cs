using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileVatPourExtensionTests
{
    // Hand-ported from #3923 (restack b167): WPF MainWindow.ProfileVat.cs:320-332, a server earn during a
    // user-gesture pour extends the pour while the glass is seen and settles silently when it is not.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ServerEarnDuringPourExtendsTheGestureOrSettlesSilently(bool presenting) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new global::ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        s.UnifiedId = "vat-extension";
        s.OfflineMode = false;
        s.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
        s.VatPouredAccount = s.UnifiedId;
        s.VatPouredDayUtc = DateTime.UtcNow.ToString("yyyy-MM-dd");
        s.VatPouredTodayXp = 600;
        s.SeenFeatureIntros.Add("profile-hub");
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "test-token" : null;
        CoreSecrets.StoreProvider = (_, _) => { };
        JObject? reply = Wire(1000);
        var service = new DescentService(_ => Task.FromResult(reply));
        global::ConditioningControlPanel.Avalonia.App.Descent = service;
        MainShellWindow? shell = null;
        try
        {
            await service.RefreshAsync("seed", force: true);
            shell = new MainShellWindow(); shell.Show(); shell.ShowTab("discord"); shell.Activate();
            Dispatcher.UIThread.RunJobs();
            var glass = shell.ProfilePage.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            Dispatcher.UIThread.RunJobs();   // the reading arms the jar (ApplyFaucetReading)
            Assert.True(glass.IsPresenting);
            Assert.Equal(.15, glass.Fill, 6);
            s.VatPouredTodayXp = 1000; // A completed charge has committed the initial pour.
            glass.PourTo(.25, userGesture: true);
            Assert.True(glass.IsPouring);
            if (!presenting)
            {
                // Simulate a reading racing with a surface becoming hidden. Normally the
                // glass settles the in-flight pour as soon as it is no longer presenting.
                glass.IsVisible = false;
                typeof(VatGlassCanvas).GetField("_pourT", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(glass, 1d);
                Assert.False(glass.IsPresenting);
            }
            reply = Wire(1400);
            await service.RefreshAsync("mid-pour earn", force: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1400, s.VatPouredTodayXp);   // the earn joined the pour, nothing held back
            if (presenting)
            {
                Assert.True(glass.IsPouring);
                Assert.True(glass.IsTicking); // Gesture remains animated even with motion off.
                Assert.True(glass.Fill < .35); // It did not silently snap to the new target.
                shell.WindowState = WindowState.Minimized; // Settling exposes the new target.
            }
            Assert.Equal(.35, glass.Fill, 6);
        }
        finally { s.OfflineMode = true; service.Reset(); shell?.Close(); }
    });

    private static JObject Wire(int xp) => JObject.Parse(
        "{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":4000,\"today_xp\":" + xp + ",\"fill_lip_pct\":120}}}");
}

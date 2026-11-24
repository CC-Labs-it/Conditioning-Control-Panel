using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class EmiDeskSummonBeatsTests
{
    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(1, 0, false, true)]
    [InlineData(1, 1, false, false)]
    [InlineData(1, 0, true, false)]
    public Task SummonBookOfferHonoursFirstOpenAndSpentGates(int priorSummons, int opens, bool spent, bool expected) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        using var state = new EmiTestScope();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        var oldFire = EmiBookOffer.Fire;
        var oldContent = EmiBookOffer.HasContent;
        var oldOpen = EmiBookOffer.IsOpen;
        var oldOpens = EmiBookOffer.Opens;
        var oldDelay = EmiBookOffer.DelayMs;
        var desk = new EmiDeskService();
        var delivered = new List<object?>();
        try
        {
            EmiBookOffer.Stop();
            CoreSettings.ServiceProvider = () => settings;
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            EmiState.Current.SummonCount = priorSummons;
            EmiState.Current.CodexOpens = opens;
            if (spent) EmiState.Current.Limits["bookOffer|ever"] = 1;
            EmiBookOffer.HasContent = () => true;
            EmiBookOffer.IsOpen = () => false;
            EmiBookOffer.Opens = () => 0; // observe the summon gate separately from the tick brake
            EmiBookOffer.DelayMs = 20;
            EmiBookOffer.Fire = (id, ctx) => { if (id == "bookOffer") delivered.Add(ctx); };
            await desk.Summon();
            Assert.True(desk.IsOut);
            await Task.Delay(1000);
            if (expected)
            {
                var ctx = Assert.Single(delivered);
                Assert.Equal("newuser", ctx!.GetType().GetProperty("why")!.GetValue(ctx));
            }
            else Assert.Empty(delivered);
        }
        finally
        {
            EmiBookOffer.Stop();
            desk.Window?.ShutDown();
            Dispatcher.UIThread.RunJobs();
            EmiBookOffer.Fire = oldFire; EmiBookOffer.HasContent = oldContent;
            EmiBookOffer.IsOpen = oldOpen; EmiBookOffer.Opens = oldOpens; EmiBookOffer.DelayMs = oldDelay;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });
}

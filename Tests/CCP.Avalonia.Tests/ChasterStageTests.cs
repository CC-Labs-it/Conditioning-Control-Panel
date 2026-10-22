using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>wire-views-tab-chaster-b: the page's ground and the hero's art (WPF ChasterTabView.Fx.cs
/// loops) and the paper tag (ChasterTabView.xaml.cs RefreshTag), against a fake Chaster.</summary>
public sealed class ChasterStageTests
{
    private sealed class FakeChaster : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.RequestUri!.AbsolutePath == "/locks"
                ? "[{\"_id\":\"l1\",\"title\":\"Test Cage\",\"status\":\"locked\",\"role\":\"wearer\",\"endDate\":\"" + DateTime.UtcNow.AddDays(2).ToString("o") + "\"}]"
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class MemoryTokens : IChasterTokenStore
    {
        private ChasterStoredTokens? _tokens = new("acc", "ref", DateTime.UtcNow.AddHours(1));
        public ChasterStoredTokens? Read() => _tokens;
        public void Write(ChasterStoredTokens tokens) => _tokens = tokens;
        public void Clear() => _tokens = null;
    }

    private static async Task Run(Func<ChasterService, ChasterTabView, Window, Task> body)
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldHead = ChasterHead.Service;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        var old = (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPaused, s.ChasterPrices, s.MotionLevel, s.PerformanceMode);
        s.ChasterLockId = "l1"; s.ChasterTabEnabled = true; s.ChasterPaused = false;
        s.ChasterPrices = new List<string> { "attention", "typo" };
        s.MotionLevel = MotionLevel.Full;   // the loops run only on AllowAmbientLoops (WPF Fx.cs:298)
        s.PerformanceMode = false;
        var directory = Directory.CreateTempSubdirectory("ccp-chaster-stage-").FullName;
        var chaster = new ChasterService(new ChasterClient(new FakeChaster()), new MemoryTokens(), Path.Combine(directory, "tab.json"),
            () => new ChasterOptions(s.ChasterTabEnabled, s.ChasterLockId, new HashSet<string>(s.ChasterPrices), Paused: s.ChasterPaused));
        ChasterHead.Service = chaster;
        var tab = new ChasterTabView();
        var window = new Window { Width = 1200, Height = 1400, Content = tab };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await body(chaster, tab, window);
        }
        finally
        {
            window.Close();
            ChasterHead.Service = oldHead;
            chaster.Dispose();
            (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPaused, s.ChasterPrices, s.MotionLevel, s.PerformanceMode) = old;
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = oldProvider;
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    [Fact]
    public Task TheGroundTurnsWhileShownAndParksWhenHidden() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, tab, _) =>
    {
        Assert.NotNull(tab.FindControl<global::Avalonia.Controls.Shapes.Path>("SpiralPath")!.Data);
        Assert.True(tab.StageTicking);
        Assert.NotNull(tab.HeroSheen);

        tab.IsVisible = false;                               // the shell hides tabs with IsVisible (FxPark)
        Dispatcher.UIThread.RunJobs();
        Assert.False(tab.StageTicking);
        Assert.Null(tab.HeroSheen);
        Assert.Equal((ChasterTabView.TagSwingFrom + ChasterTabView.TagSwingTo) / 2, tab.TagSwing.Angle, 3);
        Assert.Equal(1, tab.HeroArtScale.ScaleX);

        var from = tab.SpiralTurn.Angle;                     // parked: the next turn continues from here
        tab.StepStage(45);                                   // half of the 90 s turn
        Assert.Equal((from + 180) % 360, tab.SpiralTurn.Angle, 3);
        Assert.Equal(-5, tab.TagSwing.Angle, 3);             // 45 s = 6.25 swings: halfway up
        Assert.InRange(tab.HeroArtScale.ScaleX, 1.05, 1.06); // 45 s into the 38 s drift's way back
        tab.StepStage(1.8);                                  // to the top of the swing
        Assert.Equal(ChasterTabView.TagSwingTo, tab.TagSwing.Angle, 3);
        var held = tab.SpiralTurn.Angle;

        CoreSettings.Current.MotionLevel = MotionLevel.Off;  // no ambient loops: shown, but still
        tab.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(tab.StageTicking);
        Assert.Null(tab.HeroSheen);
        tab.StepStage(10);
        Assert.Equal(held, tab.SpiralTurn.Angle, 3);
        Assert.Equal((ChasterTabView.TagSwingFrom + ChasterTabView.TagSwingTo) / 2, tab.TagSwing.Angle, 3);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task ThePaperTagReadsTheTabAndOpensTheBill() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, tab, window) =>
    {
        var tag = tab.FindControl<Panel>("PaperTag")!;
        Assert.True(tag.IsVisible);
        Assert.Equal(Loc.Get("chaster_tag_clear"), tab.FindControl<TextBlock>("TxtTagStamp")!.Text);

        Assert.True(chaster.Note("attention").Booked);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CircesTab.Format(chaster.BalanceSeconds), tab.FindControl<TextBlock>("TxtTagAmount")!.Text);
        Assert.Equal(Loc.Get("chaster_tag_unpaid"), tab.FindControl<TextBlock>("TxtTagStamp")!.Text);
        Assert.False(string.IsNullOrEmpty(tab.FindControl<TextBlock>("TxtTagLands")!.Text));

        var receipt = tab.FindControl<Border>("ReceiptHost")!;
        Assert.False(receipt.IsVisible);
        tag.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        ChasterBillTests.Click(window, tag);                 // WPF MouseLeftButtonUp="StatRun_Click"
        Assert.True(receipt.IsVisible);
        await Task.CompletedTask;
    }));
}

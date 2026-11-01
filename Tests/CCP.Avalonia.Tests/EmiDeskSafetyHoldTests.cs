using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiDeskSafetyHoldTests
{
    private static void Setup() => AmbientBeatLoopsTests.Setup();

    private static async Task SettleSummon(EmiDeskWindow desk)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (desk.Transiting && DateTime.UtcNow < until)
        {
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(desk.Transiting);
    }

    // WPF csproj:663-671 and EmiLineEngine.cs:188-200,250-255: real installs need the file beside the exe.
    [Fact]
    public void LinesShipBesideTheApplication()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "emi", "desk-lines.json");
        Assert.True(File.Exists(path));
        Assert.Equal(300000, (int)JObject.Parse(File.ReadAllText(path))["moments"]!["panicPressed"]!["tailMs"]!);
    }

    // WPF IntakeHostService.cs:110-114,1070-1077; EmiDeskService.cs:674-689,715-719.
    [Fact]
    public Task IntakeHoldsWhileAwayAndReleasesAfterTheLastWindowCloses() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var engine = EmiLineEngine.Instance;
        var svc = EmiDeskService.Instance;
        var first = new IntakeHostWindow();
        var second = new IntakeHostWindow();
        var bedtime = EmiState.Current.BedtimeUntil;
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            CoreSettings.Current.EmiDeskGlass = true;
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            first.Show();
            Assert.Equal("intakeRunning", engine.HeldBy);
            await svc.Summon();
            var desk = Assert.IsType<EmiDeskWindow>(svc.Window);
            await SettleSummon(desk);
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.False(desk.GlassIsLive);
            second.Show();
            first.Close();
            Assert.Equal("intakeRunning", engine.HeldBy);
            second.Close();
            Assert.Null(engine.HeldBy);
            Assert.False(engine.HoldActive);
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.True(desk.GlassIsLive);
        }
        finally
        {
            first.Close(); second.Close(); svc.Window?.ShutDown();
            EmiState.Current.BedtimeUntil = bedtime;
            engine.ResetForTests();
        }
    });

    // WPF EmiDeskWindow.Bubble.cs:545-565: an on-screen hold closes the channel and shows a quiet face.
    [Fact]
    public Task IntakeClosesAnActiveGlassChannel() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var svc = EmiDeskService.Instance;
        var engine = EmiLineEngine.Instance;
        var bedtime = EmiState.Current.BedtimeUntil;
        var intake = new IntakeHostWindow();
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            CoreSettings.Current.EmiDeskGlass = true;
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            await svc.Summon();
            var desk = Assert.IsType<EmiDeskWindow>(svc.Window);
            await SettleSummon(desk);
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.True(desk.GlassIsLive);
            desk.DrawFace("probe");
            intake.Show();
            Assert.False(desk.GlassIsLive);
            Assert.Equal("intakeRunning", engine.HeldBy);
            Assert.NotEqual("probe", desk.Face);
            await Task.Delay(2600); // all shipped common.hold rows last at most 2400 ms
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(EmiChains.RestFace, desk.Face);
            svc.FireHold("intakeRunning");
            desk.BeginPresentation(() => { });
            desk.DrawFace("stage");
            await Task.Delay(2600);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("stage", desk.Face);
            desk.EndPresentation();
        }
        finally
        {
            intake.Close(); svc.Window?.ShutDown();
            EmiState.Current.BedtimeUntil = bedtime;
            engine.ResetForTests();
        }
    });

    // WPF EmiDeskService.cs:692-701,731-733: a queued visible-desk hold is dropped if the desk closes first.
    [Fact]
    public Task QueuedVisibleHoldDoesNotRearmAfterDismissal() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var svc = EmiDeskService.Instance;
        var engine = EmiLineEngine.Instance;
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            await svc.Summon();
            Task.Run(() => svc.FireHold("intakeRunning")).GetAwaiter().GetResult();
            svc.Window!.ShutDown();
            Dispatcher.UIThread.RunJobs();
            Assert.False(engine.HoldActive);
        }
        finally { svc.Window?.ShutDown(); engine.ResetForTests(); }
    });

    // WPF MainWindow.xaml.cs:1613-1620 stops the show before arming the five-minute hold.
    [Fact]
    public Task PanicKeyStopsTheWelcomeShowBeforeArmingTheHold() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var engine = EmiLineEngine.Instance;
        var shell = new MainShellWindow();
        var surfaces = PanicSurfaces.All;
        try
        {
            CoreSettings.Current.PanicKeyEnabled = true;
            CoreSettings.Current.PanicKey = "F8";
            CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            CoreSettings.Current.MasterVolume = 0;
            FirstShowService.RequireClickThrough = false;
            shell.Show();
            Assert.True(FirstShowService.Open(shell));
            Assert.True(EmiDeskService.Instance.Window!.PresentationActive);
            Assert.False(engine.HoldActive);
            // No later stop-pass cleanup can mask a missing early stop.
            PanicSurfaces.All = Array.Empty<PanicSurfaces.Surface>();
            shell.HandlePanicKeyPress(DateTime.Now);
            Assert.False(FirstShowService.IsActive);
            Assert.False(EmiDeskService.Instance.Window!.PresentationActive);
            Assert.True(engine.HoldActive);
        }
        finally
        {
            PanicSurfaces.All = surfaces;
            FirstShowService.Stop(); FirstShowService.RequireClickThrough = true;
            EmiDeskService.Instance.Window?.ShutDown(); shell.Close(); engine.ResetForTests();
        }
        return Task.CompletedTask;
    });

    // WPF MainWindow.xaml.cs:1617-1620 and MainWindow.RemoteControl.cs:1475-1479: both panic routes arm five minutes.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PanicArmsTheQuietEvenWhileAway(bool voice) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var engine = EmiLineEngine.Instance;
        var shell = new MainShellWindow();
        try
        {
            CoreSettings.Current.PanicKeyEnabled = true;
            CoreSettings.Current.PanicKey = "F8";
            shell.Show();
            if (voice) Assert.True(shell.RunVoicePanic());
            else shell.HandlePanicKeyPress(DateTime.Now);
            Assert.True(engine.HoldActive);
            Assert.Null(engine.Draw("firstContact"));
        }
        finally { shell.Close(); engine.ResetForTests(); }
        return Task.CompletedTask;
    });
}

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The one-time licence card (parity lane reports; WPF Dialogs/OnlineLicenceCard, #2503), modelled on WPF
/// OnlineLicenceCardHoldTests: the copy, the plan line, "Nice", and the hold the card asks itself because the
/// startup modal route never waits for a session or a Lockdown.
/// </summary>
public class OnlineLicenceCardTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task TheCardCarriesTheFourFlowLinesAndThePlanLine(int planValue) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var plan = (ScrolllerPlan)planValue;   // Free, Basic, Prime
        var card = new OnlineLicenceCard(plan);
        var lines = card.FlowLines.ItemsSource!.Cast<string>().ToArray();
        Assert.Equal(OnlineLicenceCard.FlowKeys.Select(k => Loc.Get(k)), lines);
        Assert.Equal(4, lines.Length);
        Assert.Equal(Loc.Get(OnlineMeterRules.OfficialPlanKey(plan)), card.TxtPlan.Text);
        Assert.True(card.BtnNice.IsDefault);
        return Task.CompletedTask;
    });

    [Fact]
    public Task NiceClosesTheCard() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var card = new OnlineLicenceCard(ScrolllerPlan.Free);
        card.Show();
        Dispatcher.UIThread.RunJobs();
        var closed = false;
        card.Closed += (_, _) => closed = true;
        card.BtnNice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        return Task.CompletedTask;
    });

    [Fact]
    public void TheCardIsNeverQueuedWhileAHoldIsOnAndStepsAsideAtShowTime()
    {
        var src = Source("CCP.Avalonia", "Views", "Windows", "MainShellWindow.OnlineLicence.cs");
        Assert.Contains("if (!st.ShowOfficialNote || _onlineLicenceCardQueued || FriendsLanding.World().Holding) return;", src);
        const string recheck = "if (FriendsLanding.World().Holding) { _onlineLicenceCardQueued = false; return; }";
        Assert.Contains(recheck, src);
        Assert.True(src.IndexOf(recheck, StringComparison.Ordinal) < src.IndexOf("new OnlineLicenceCard(now.Plan)", StringComparison.Ordinal));
        // "Nice" marks the note seen for both surfaces (the Assets tab's note shares the flag).
        Assert.Contains("ScrolllerOfficialNoteSeen = true", src);
        // Offered from every meter refresh, as WPF RefreshOnlineMeter does.
        Assert.Contains("OfferOnlineLicenceCard(st);", Source("CCP.Avalonia", "Views", "Windows", "MainShellWindow.OnlineMeter.cs"));
    }

    [Fact]
    public void PanicClosesTheCardUnseen()
    {
        Assert.Contains(PanicSurfaces.All, s => s.Id == "online-licence");
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "CCP.Avalonia.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}

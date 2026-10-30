using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileSpiralTests
{
    private static void Run(Action<MainShellWindow, DescentService, SettingsService> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.DescentMigrationCompleted = false;
        settings.Current.DescentMigrationOffered = false;
        settings.Current.PendingDescentMigrationChoice = null;
        settings.Current.DescentSpiralRailEnabled = false;
        var service = new DescentService();
        AvApp.Descent = service;
        var shell = new MainShellWindow();
        try { shell.Show(); shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs(); body(shell, service, settings); }
        finally { shell.Close(); }
    });

    private static void Block(DescentService service, int? day)
    {
        // Accepted server state without a network request. Notify through the real service event.
        typeof(DescentService).GetProperty(nameof(DescentService.Current))!.SetValue(service,
            day == null ? null : DescentReader.ParseWire("{\"descent\":{\"devotion_days\":" + day + ",\"stage\":{\"n\":2,\"next_at\":30}}}") is { } wire
                ? DescentReader.ParseFromUserNode(wire) : null);
        ((EventHandler?)typeof(DescentService).GetField("BlockChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service))?.Invoke(service, EventArgs.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Plate(MainShellWindow s) => s.ProfilePage!.FindControl<Border>("ProfileSpiralPlate")!;
    private static Button Row(MainShellWindow s) => s.FindControl<Button>("ProfileMenuSpiralRow")!;

    [Fact]
    public void ServerBlockShowsBothDoorsWithoutRailFlagAndWithdrawalHidesThem() => Run((s, service, _) =>
    {
        Assert.False(Plate(s).IsVisible); Assert.False(Row(s).IsVisible);
        Block(service, 12);
        Assert.True(Plate(s).IsVisible); Assert.True(Row(s).IsVisible);
        Assert.Equal(string.Format(Loc.Get("programs_card_day"), 12) + " · " + DescentStageCopy.Name(2),
            s.FindControl<TextBlock>("ProfileMenuSpiralSummary")!.Text);
        Assert.Equal("II", typeof(SpiralGlyph).GetField("_numeral", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(s.ProfilePage!.FindControl<SpiralGlyph>("ProfileSpiralGlyph")) is TextBlock numeral ? numeral.Text : null);
        Block(service, null);
        Assert.False(Plate(s).IsVisible); Assert.False(Row(s).IsVisible);
    });

    [Fact]
    public void SearchedCardNeverShowsOurSpiralButHeaderRemainsOurs() => Run((s, service, _) =>
    {
        Block(service, 12); s.SetProfileViewingSelf(false);
        Assert.False(Plate(s).IsVisible); Assert.True(Row(s).IsVisible);
        s.SetProfileViewingSelf(true); Assert.True(Plate(s).IsVisible);
    });

    [Fact]
    public void OfferWithholdsUntilValidChoiceAndCloudRestoreRebinds() => Run((s, service, settings) =>
    {
        Block(service, 12);
        settings.Current.DescentMigrationOffered = true; Dispatcher.UIThread.RunJobs();
        Assert.False(Plate(s).IsVisible); Assert.False(Row(s).IsVisible);
        settings.Current.PendingDescentMigrationChoice = "invalid"; Dispatcher.UIThread.RunJobs();
        Assert.False(Row(s).IsVisible);
        settings.Current.PendingDescentMigrationChoice = DescentMigrationChoices.Restore; Dispatcher.UIThread.RunJobs();
        Assert.True(Row(s).IsVisible); Assert.True(Plate(s).IsVisible);
        settings.RestoreFrom(new AppSettings { DescentMigrationOffered = true }); Dispatcher.UIThread.RunJobs();
        Assert.False(Row(s).IsVisible);
        settings.Current.DescentMigrationCompleted = true; Dispatcher.UIThread.RunJobs();
        Assert.True(Row(s).IsVisible);
    });

    [Fact]
    public void MenuClosesAndBothDoorsEnterRoomEvenAfterStaleBlock() => Run((s, service, _) =>
    {
        Block(service, 12); s.OpenProfileBubbleMenu();
        Row(s).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Assert.Equal("spiral", s.CurrentTab);
        Assert.False(s.FindControl<Popup>("ProfileBubblePopup")!.IsOpen);
        s.ShowTab("discord"); Block(service, null);
        Plate(s).RaiseEvent(new PointerReleasedEventArgs(Plate(s),
            new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), Plate(s), default, 0, default, KeyModifiers.None, MouseButton.Left));
        Dispatcher.UIThread.RunJobs(); Assert.Equal("spiral", s.CurrentTab);
    });

    [Fact]
    public void MotionChangeStopsBothGlyphBreaths() => Run((s, service, settings) =>
    {
        Block(service, 12);
        settings.Current.MotionLevel = MotionLevel.Full;
        settings.Current.GentleVisuals = false;
        settings.Current.PerformanceMode = false;
        s.OpenProfileBubbleMenu(); Dispatcher.UIThread.RunJobs();
        var field = typeof(SpiralGlyph).GetField("_breathing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var plate = s.ProfilePage!.FindControl<SpiralGlyph>("ProfileSpiralGlyph")!;
        var menu = s.FindControl<SpiralGlyph>("ProfileMenuSpiralGlyph")!;
        // Simulate active breaths so the test also works when the OS caps motion on Linux.
        field.SetValue(plate, true); field.SetValue(menu, true);
        settings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False((bool)field.GetValue(plate)!); Assert.False((bool)field.GetValue(menu)!);
    });

    [Fact]
    public void CloseDropsServiceAndSettingsCallbacks() => Run((s, service, settings) =>
    {
        Block(service, 12); s.Close();
        var handlers = (EventHandler?)typeof(DescentService).GetField("BlockChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service);
        Assert.DoesNotContain(handlers?.GetInvocationList() ?? Array.Empty<Delegate>(), h => ReferenceEquals(h.Target, s));
        Block(service, null);
        settings.Current.DescentMigrationOffered = true; Dispatcher.UIThread.RunJobs();
        Assert.True(Row(s).IsVisible);
    });
}

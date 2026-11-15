using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Invites;
using ConditioningControlPanel.Services.Vault;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VaultCompareReviewTests
{
    private static void Click(Window dialog, string text)
    {
        dialog.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        dialog.UpdateLayout();
    }

    private static T Named<T>(Window dialog, string name) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(x => x.Name == name);

    [Fact]
    public Task SavingsFollowTheRefreshedSnapshotForBothTiers() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var url = InviteApi.DefaultBaseUrl;
        var fetch = PlanCatalog.FetchOverride;
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        InviteApi.DefaultBaseUrl = () => null;
        PlanCatalog.Reset();
        var dialog = new VaultGateDialog();
        try
        {
            dialog.Show();
            Click(dialog, Loc.Get("vaultgate_compare"));
            var plans = PlanCatalog.Fallback with
            {
                Basic = PlanCatalog.Fallback.Basic with { MonthlyCents = 1000, YearlyCents = 9000 },
                Prime = PlanCatalog.Fallback.Prime with { MonthlyCents = 2000, YearlyCents = 12000 },
            };
            PlanCatalog.FetchOverride = _ => Task.FromResult<string?>(PlanCatalog.ToWire(plans).ToString());
            await PlanCatalog.GetAsync("https://plans.test");
            await dialog.FillPlansAsync();
            Assert.Equal(Loc.GetF("vaultgate_stamp_off", 25), Named<TextBlock>(dialog, "LeverSaving").Text);
            var basic = Named<Border>(dialog, "BasicSaving");
            var prime = Named<Border>(dialog, "PrimeSaving");
            Assert.Equal(0, basic.Opacity);
            Assert.Equal(0, prime.Opacity);
            Click(dialog, Loc.Get("vaultgate_billing_yearly"));
            Assert.Equal(Loc.GetF("vaultgate_stamp_off", 25), ((TextBlock)basic.Child!).Text);
            Assert.Equal(Loc.GetF("vaultgate_stamp_off", 50), ((TextBlock)prime.Child!).Text);
            Assert.Equal(0.9, basic.Opacity);
            Assert.Equal(0.9, prime.Opacity);
            Click(dialog, Loc.Get("vaultgate_billing_monthly"));
            Assert.Equal(0, basic.Opacity);
            Assert.Equal(0, prime.Opacity);
        }
        finally { dialog.Close(); PlanCatalog.Reset(); PlanCatalog.FetchOverride = fetch; InviteApi.DefaultBaseUrl = url; CoreSettings.ServiceProvider = provider; }
    });

    [Fact]
    public Task LeverHandlesKeysWithoutLosingFocus() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var dialog = new VaultGateDialog();
        try
        {
            dialog.Show();
            Click(dialog, Loc.Get("vaultgate_compare"));
            var lever = Named<StackPanel>(dialog, "BillingLever");
            Assert.True(lever.Focus());
            void KeyPress(Key key, bool yearly)
            {
                var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = lever };
                lever.RaiseEvent(e);
                Assert.True(e.Handled);
                Assert.Equal(yearly ? 0.9 : 0, Named<Border>(dialog, "BasicSaving").Opacity);
                Assert.True(lever.IsFocused);
            }
            KeyPress(Key.Right, true);
            KeyPress(Key.Left, false);
            KeyPress(Key.Enter, true);
            KeyPress(Key.Space, false);
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void FaceResizePreservesCenterAndClampsInPhysicalPixels()
    {
        var area = new PixelRect(0, 0, 1920, 1080);
        Assert.Equal(new PixelPoint(940, 100), VaultGateDialog.Recenter(new PixelPoint(1700, 200), new Size(460, 400), new Size(980, 600), area, 1));
        Assert.Equal(new PixelPoint(460, 200), VaultGateDialog.Recenter(new PixelPoint(200, 100), new Size(980, 600), new Size(460, 400), area, 1));
        Assert.Equal(new PixelPoint(-1920, 150), VaultGateDialog.Recenter(new PixelPoint(-1850, 200), new Size(460, 400), new Size(980, 500), new PixelRect(-1920, 0, 1920, 1080), 1));
    }

    [Fact]
    public Task SwitchingFacesKeepsTheWindowInsideTheScreen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var dialog = new VaultGateDialog();
        try
        {
            dialog.Show();
            var screen = dialog.Screens.Primary!;
            var area = screen.WorkingArea;
            dialog.Position = new PixelPoint(area.Right - 100, area.Y + 100);
            Click(dialog, Loc.Get("vaultgate_compare"));
            Assert.True(dialog.Position.X >= area.X);
            Assert.True(dialog.Position.X + 980 * screen.Scaling <= area.Right + 1, $"position {dialog.Position}, area {area}");
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full, -3)]
    [InlineData(MotionLevel.Reduced, 0)]
    [InlineData(MotionLevel.Off, 0)]
    public Task TicketHoverUsesMotionPreference(MotionLevel level, double expected) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var provider = CoreSettings.ServiceProvider;
        var os = UiMotionPreference.OsProbe;
        var service = new SettingsService();
        service.Current.MotionLevel = level;
        CoreSettings.ServiceProvider = () => service;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var dialog = new VaultGateDialog();
        try
        {
            dialog.Show();
            Click(dialog, Loc.Get("vaultgate_compare"));
            await Task.Delay(450);
            var ticket = Named<Border>(dialog, "BasicTicket");
            var move = (TranslateTransform)ticket.RenderTransform!;
            ticket.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, ticket, null!, dialog, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.Equal(expected, move.Y);
            ticket.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, ticket, null!, dialog, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.Equal(0, move.Y);
        }
        finally { dialog.Close(); CoreSettings.ServiceProvider = provider; UiMotionPreference.OsProbe = os; UiMotionPreference.CacheStamp = long.MinValue; }
    });

    [Theory]
    [InlineData(MotionLevel.Full, false)]
    [InlineData(MotionLevel.Reduced, false)]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Off, false)]
    public Task CloseHonorsMotionPreference(MotionLevel level, bool gentle) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var provider = CoreSettings.ServiceProvider;
        var os = UiMotionPreference.OsProbe;
        var service = new SettingsService();
        service.Current.MotionLevel = level;
        service.Current.GentleVisuals = gentle;
        CoreSettings.ServiceProvider = () => service;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var dialog = new VaultGateDialog();
        try
        {
            dialog.Show();
            Click(dialog, Loc.Get("vaultgate_compare"));
            dialog.Close();
            Assert.Equal(level != MotionLevel.Off, dialog.IsVisible);
            if (level != MotionLevel.Off)
            {
                await Task.Delay(gentle ? 950 : 350);
                Dispatcher.UIThread.RunJobs();
                Assert.False(dialog.IsVisible);
            }
        }
        finally { dialog.Close(); CoreSettings.ServiceProvider = provider; UiMotionPreference.OsProbe = os; UiMotionPreference.CacheStamp = long.MinValue; }
    });
}

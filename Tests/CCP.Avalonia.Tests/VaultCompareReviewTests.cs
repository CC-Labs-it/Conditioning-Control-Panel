using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
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

    private static readonly FieldInfo Cache = typeof(PlanCatalog).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ReadUtc = typeof(PlanCatalog).GetField("_readUtc", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public Task SavingsFollowTheRefreshedSnapshotForBothTiers() => AvaloniaTestDispatcher.RunAsync(ExerciseSavings);

    private static async Task ExerciseSavings()
    {
        var cached = Cache.GetValue(null);
        var readUtc = ReadUtc.GetValue(null);
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
        finally { dialog.Close(); Cache.SetValue(null, cached); ReadUtc.SetValue(null, readUtc); PlanCatalog.FetchOverride = fetch; InviteApi.DefaultBaseUrl = url; CoreSettings.ServiceProvider = provider; }
    }

    [Fact]
    public Task SavingsTestPreservesExistingCacheAndTimestamp() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var before = Cache.GetValue(null);
        var beforeTime = ReadUtc.GetValue(null);
        var seeded = PlanCatalog.Fallback with { Currency = "EUR" };
        var time = DateTime.UtcNow.AddMinutes(-3);
        try
        {
            Cache.SetValue(null, seeded);
            ReadUtc.SetValue(null, time);
            await ExerciseSavings();
            Assert.Same(seeded, PlanCatalog.Current);
            Assert.Equal(time, ReadUtc.GetValue(null));
        }
        finally { Cache.SetValue(null, before); ReadUtc.SetValue(null, beforeTime); }
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
    [InlineData("heading")][InlineData("perforation")][InlineData("notches")][InlineData("foil")]
    [InlineData("ink")][InlineData("hover")][InlineData("press")]
    public Task TicketDetailsMatchTheStockAndStub(string part) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var dialog = new VaultGateDialog();
        try
        {
            dialog.Show();
            Click(dialog, Loc.Get("vaultgate_compare"));
            var card = (Control)dialog.Content!;
            card.Measure(new Size(980, double.PositiveInfinity));
            card.Arrange(new Rect(card.DesiredSize));
            card.UpdateLayout();
            foreach (var lab in new[] { false, true })
            {
                var ticket = Named<Border>(dialog, lab ? "PrimeTicket" : "BasicTicket");
                if (part == "heading")
                {
                    var top = ticket.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "TicketHeading");
                    var price = top.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "TicketPrice");
                    Assert.Equal(1, Grid.GetColumn(price));
                    Assert.Contains(top.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get(lab ? "vaultgate_tier_lab_name" : "vaultgate_tier_vault_name"));
                }
                if (part == "perforation")
                {
                    var perf = ticket.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Rectangle>().Single(x => x.Name == "Perforation");
                    Assert.Equal(2, perf.StrokeDashArray!.Count);
                    Assert.True(perf.Bounds.Width > 100 && perf.Bounds.Height > 0, $"perforation: {perf.Bounds}");
                }
                if (part == "notches") Assert.Equal(2, ticket.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>().Count(x => x.Name == "TicketNotch"));
                if (part == "foil" && lab) Assert.Equal(5, Assert.IsType<LinearGradientBrush>(ticket.BorderBrush).GradientStops.Count);
                var choose = Named<Button>(dialog, lab ? "PrimeChoose" : "BasicChoose");
                var face = choose.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "TicketFace");
                if (part == "ink") Assert.Equal(Color.Parse(lab ? "#E5C76B" : "#C8235F"), Assert.IsType<SolidColorBrush>(face.Background).Color);
                var faceMove = Assert.IsType<TranslateTransform>(face.RenderTransform);
                if (part == "hover")
                {
                    choose.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, choose, null!, dialog, default, 0, PointerPointProperties.None, KeyModifiers.None));
                    Assert.Equal(-1, faceMove.Y);
                    choose.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, choose, null!, dialog, default, 0, PointerPointProperties.None, KeyModifiers.None));
                    Assert.Equal(0, faceMove.Y);
                }
                if (part == "press" && lab)
                {
                    var pointer = new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
                    choose.RaiseEvent(new PointerPressedEventArgs(choose, pointer, dialog, default, 0,
                        new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
                    Assert.Equal(4, faceMove.Y);
                }
            }
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full, false)]
    [InlineData(MotionLevel.Reduced, true)]
    [InlineData(MotionLevel.Off, false)]
    public Task BackWaitsForTicketExit(MotionLevel level, bool gentle) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var provider = CoreSettings.ServiceProvider;
        var os = UiMotionPreference.OsProbe;
        var service = new SettingsService();
        service.Current.MotionLevel = level;
        service.Current.GentleVisuals = gentle;
        service.Current.GentleFadeSeconds = 0.5;
        CoreSettings.ServiceProvider = () => service;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var dialog = new VaultGateDialog();
        try
        {
            dialog.Show();
            Click(dialog, Loc.Get("vaultgate_compare"));
            var back = dialog.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.Get("vaultgate_back")));
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (level != MotionLevel.Off)
            {
                Assert.Equal(980, dialog.Width);
                Assert.False(((Control)dialog.Content!).IsHitTestVisible);
                Assert.NotNull(Named<Border>(dialog, "BasicTicket"));
                await Task.Delay(60);
                Assert.Equal(980, dialog.Width);
                await Task.Delay(gentle ? 600 : 300);
            }
            Assert.Equal(460, dialog.Width);
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            Assert.True(((Control)dialog.Content!).IsHitTestVisible);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("vaultgate_signin"));
        }
        finally { dialog.Close(); CoreSettings.ServiceProvider = provider; UiMotionPreference.OsProbe = os; UiMotionPreference.CacheStamp = long.MinValue; }
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

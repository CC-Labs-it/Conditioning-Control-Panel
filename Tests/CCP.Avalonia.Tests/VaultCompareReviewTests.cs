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
using static CCP.Avalonia.Tests.VaultCompareTests;

namespace CCP.Avalonia.Tests;

public class VaultCompareReviewTests
{
    private static T Named<T>(Window dialog, string name) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(x => x.Name == name);

    // One scope keeps setup and cleanup identical for each headless dialog test.
    private static Task WithDialog(Func<VaultGateDialog, Task> action) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var dialog = new VaultGateDialog();
            try { dialog.Show(); await action(dialog); }
            finally { dialog.Close(); }
        });

    private static readonly FieldInfo Cache = typeof(PlanCatalog).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ReadUtc = typeof(PlanCatalog).GetField("_readUtc", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public Task SavingsFollowTheRefreshedSnapshotForBothTiers() => AvaloniaTestDispatcher.RunAsync(async () =>
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
            Assert.Equal(0, basic.GetBaseValue(Visual.OpacityProperty).Value);
            Assert.Equal(0, prime.GetBaseValue(Visual.OpacityProperty).Value);
            Click(dialog, Loc.Get("vaultgate_billing_yearly"));
            Assert.Equal(Loc.GetF("vaultgate_stamp_off", 25), basic.GetVisualDescendants().OfType<TextBlock>().Single().Text);
            Assert.Equal(Loc.GetF("vaultgate_stamp_off", 50), prime.GetVisualDescendants().OfType<TextBlock>().Single().Text);
            Assert.Equal(0.9, basic.GetBaseValue(Visual.OpacityProperty).Value);
            Assert.Equal(0.9, prime.GetBaseValue(Visual.OpacityProperty).Value);
            Click(dialog, Loc.Get("vaultgate_billing_monthly"));
            Assert.Equal(0, basic.GetBaseValue(Visual.OpacityProperty).Value);
            Assert.Equal(0, prime.GetBaseValue(Visual.OpacityProperty).Value);
        }
        finally { dialog.Close(); Cache.SetValue(null, cached); ReadUtc.SetValue(null, readUtc); PlanCatalog.FetchOverride = fetch; InviteApi.DefaultBaseUrl = url; CoreSettings.ServiceProvider = provider; }
    });

    [Fact]
    public Task LeverHandlesKeysWithoutLosingFocus() => WithDialog(dialog =>
    {
        Click(dialog, Loc.Get("vaultgate_compare"));
        var lever = Named<StackPanel>(dialog, "BillingLever");
        Assert.True(lever.Focus());
        void KeyPress(Key key, bool yearly)
        {
            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = lever };
            lever.RaiseEvent(e);
            Assert.True(e.Handled);
            Assert.Equal(yearly ? 0.9 : 0, Named<Border>(dialog, "BasicSaving").GetBaseValue(Visual.OpacityProperty).Value);
            Assert.True(lever.IsFocused);
        }
        KeyPress(Key.Right, true);
        KeyPress(Key.Left, false);
        KeyPress(Key.Enter, true);
        KeyPress(Key.Space, false);
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
    public Task SwitchingFacesKeepsTheWindowInsideTheScreen() => WithDialog(dialog =>
    {
        var screen = dialog.Screens.Primary!;
        var area = screen.WorkingArea;
        dialog.Position = new PixelPoint(area.Right - 100, area.Y + 100);
        Click(dialog, Loc.Get("vaultgate_compare"));
        Assert.True(dialog.Position.X >= area.X);
        Assert.True(dialog.Position.X + 980 * screen.Scaling <= area.Right + 1, $"position {dialog.Position}, area {area}");
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("heading")][InlineData("perforation")][InlineData("notches")][InlineData("foil")]
    [InlineData("ink")][InlineData("hover")][InlineData("press")][InlineData("stamp")]
    public Task TicketDetailsMatchTheStockAndStub(string part) => WithDialog(dialog =>
    {
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
            if (part == "stamp")
            {
                // WPF Compare.cs:431-456: double rule tilted -4, round saving stamp overlapping its left edge.
                var stamp = ticket.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "PriceStamp");
                Assert.Equal(-4, Assert.IsType<RotateTransform>(stamp.RenderTransform).Angle);
                Assert.Equal(2.5, stamp.BorderThickness.Left);
                Assert.Equal(1, Assert.IsType<Border>(stamp.Child).BorderThickness.Left);
                var saving = Named<Border>(dialog, lab ? "PrimeSaving" : "BasicSaving");
                Assert.Same(stamp.Parent, saving.Parent);
                Assert.Equal(-11, Assert.IsType<TransformGroup>(saving.RenderTransform).Children.OfType<RotateTransform>().Single().Angle);
                Assert.True(saving.Bounds.X < stamp.Bounds.X && saving.Bounds.Right > stamp.Bounds.X, $"saving {saving.Bounds}, stamp {stamp.Bounds}");
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
        return Task.CompletedTask;
    });
}

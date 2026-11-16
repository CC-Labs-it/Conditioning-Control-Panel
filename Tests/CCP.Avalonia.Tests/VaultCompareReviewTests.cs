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
    private static Task WithDialog(Func<VaultGateDialog, Task> action, MotionLevel? level = null, bool gentle = false, double? fade = null, bool owned = false) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var provider = CoreSettings.ServiceProvider;
            var os = UiMotionPreference.OsProbe;
            if (level is { } motion)
            {
                var service = new SettingsService();
                service.Current.MotionLevel = motion;
                service.Current.GentleVisuals = gentle;
                if (fade is { } seconds) service.Current.GentleFadeSeconds = seconds;
                CoreSettings.ServiceProvider = () => service;
                UiMotionPreference.OsProbe = () => true;
                UiMotionPreference.CacheStamp = long.MinValue;
            }
            var dialog = new VaultGateDialog();
            var owner = owned ? new Window() : null;
            try { if (owner != null) { owner.Show(); dialog.Show(owner); } else dialog.Show(); await action(dialog); }
            finally
            {
                dialog.Close();
                owner?.Close();
                if (level != null) { CoreSettings.ServiceProvider = provider; UiMotionPreference.OsProbe = os; UiMotionPreference.CacheStamp = long.MinValue; }
            }
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

    // WPF Compare.cs:156-205: the words, track and note are mouse-only, so the row is the one Tab stop
    // and Enter on "Monthly" cannot flip billing to yearly.
    [Fact]
    public Task LeverPartsTakeNoFocus() => WithDialog(dialog =>
    {
        Click(dialog, Loc.Get("vaultgate_compare"));
        var parts = Named<StackPanel>(dialog, "BillingLever").GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(4, parts.Count);
        Assert.All(parts, b => Assert.False(b.Focus(NavigationMethod.Tab)));
        return Task.CompletedTask;
    });

    // WPF Compare.cs:134-136: the desk swaps the pink glow for a dark drop shadow, the offer gets it back
    // only after the Back fade (WPF Compare.cs:106-112 runs FaceChrome(false) in the fade's callback).
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Off)]
    public Task CompareDeskDropsThePinkGlow(MotionLevel level) => WithDialog(async dialog =>
    {
        var card = Assert.IsType<Border>(dialog.Content);
        var glow = card.BoxShadow;
        var desk = BoxShadows.Parse("4 4 18 0 #B3060210");
        Click(dialog, Loc.Get("vaultgate_compare"));
        Assert.Equal(desk, card.BoxShadow);
        Click(dialog, Loc.Get("vaultgate_back"));
        if (level != MotionLevel.Off)
        {
            Assert.Equal(desk, card.BoxShadow);
            await Task.Delay(60);
            Assert.Equal(desk, card.BoxShadow);
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(glow, card.BoxShadow);
    }, level);

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

    [Theory]
    [InlineData(MotionLevel.Full, false)]
    [InlineData(MotionLevel.Reduced, true)]
    [InlineData(MotionLevel.Off, false)]
    public Task BackWaitsForTicketExit(MotionLevel level, bool gentle) => WithDialog(async dialog =>
    {
        Click(dialog, Loc.Get("vaultgate_compare"));
        var back = dialog.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.Get("vaultgate_back")));
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var body = ((Border)dialog.Content!).Child!;
        if (level != MotionLevel.Off)
        {
            Assert.Equal(0, body.GetBaseValue(Visual.OpacityProperty).GetValueOrDefault(1.0)); // WPF Compare.cs:588: held faded, no flash back
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
        Assert.Equal(1, body.GetBaseValue(Visual.OpacityProperty).GetValueOrDefault(1.0));
        if (level == MotionLevel.Off) Assert.Equal(1, body.Opacity);
    }, level, gentle, 0.5);

    [Theory]
    [InlineData(MotionLevel.Full, false)]
    [InlineData(MotionLevel.Reduced, true)]
    [InlineData(MotionLevel.Off, false)]
    public Task BillingRollsTheAmount(MotionLevel level, bool gentle) => WithDialog(async dialog =>
    {
        Click(dialog, Loc.Get("vaultgate_compare"));
        var ticket = Named<Border>(dialog, "BasicTicket");
        var window = ticket.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "AmountWindow");
        var monthly = window.Children.OfType<TextBlock>().Single().Text;
        var stamp = ticket.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PriceStamp");
        var subs = () => stamp.GetVisualDescendants().OfType<TextBlock>().Where(t => t.FontSize == 11).Select(t => t.Text).ToList();
        var monthSub = Assert.Single(subs());
        Click(dialog, Loc.Get("vaultgate_billing_yearly"));
        window = ticket.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "AmountWindow");
        Assert.True(window.ClipToBounds);
        var rolling = window.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
        if (level == MotionLevel.Off)
        {
            Assert.NotEqual(monthly, Assert.Single(rolling));
            Assert.NotEqual(monthSub, Assert.Single(subs()));
            return;
        }
        Assert.Equal(2, rolling.Count);
        Assert.Equal(monthly, rolling[0]);
        Assert.Equal(monthSub, Assert.Single(subs()));
        var price = window.GetVisualAncestors().OfType<StackPanel>().First();
        Assert.Equal(1, price.Opacity);
        for (var i = 0; i < 100 && (window.Children.Count > 1 || subs()[0] == monthSub); i++) await Task.Delay(20);
        Assert.NotEqual(monthly, window.Children.OfType<TextBlock>().Single().Text);
        Assert.NotEqual(monthSub, Assert.Single(subs()));
    }, level, gentle, 0.5);

    [Fact]
    public Task BackFadesTheOfferIn() => WithDialog(async dialog =>
    {
        Click(dialog, Loc.Get("vaultgate_compare"));
        Click(dialog, Loc.Get("vaultgate_back"));
        for (var i = 0; i < 300 && !dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.Get("vaultgate_signin")); i++)
            await Task.Delay(20);
        await Task.Delay(300);
        var body = ((Border)dialog.Content!).Child!;
        Assert.InRange(body.Opacity, 0.01, 0.9);
        await Task.Delay(2200);
        Assert.Equal(1, body.Opacity);
    }, MotionLevel.Reduced, true, 2.0);

    // WPF Compare.cs:370-380, 640-643: hover lifts -3 and a press on Choose sets the ticket down +2 at
    // every level; it tweens unless Off or Gentle.
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public Task TicketHoverUsesMotionPreference(MotionLevel level) => WithDialog(async dialog =>
    {
        Click(dialog, Loc.Get("vaultgate_compare"));
        await Task.Delay(450);
        var ticket = Named<Border>(dialog, "BasicTicket");
        var move = (TranslateTransform)ticket.RenderTransform!;
        ticket.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, ticket, null!, dialog, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.Equal(-3, move.Y);
        var choose = Named<Button>(dialog, "BasicChoose");
        var pointer = new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
        choose.RaiseEvent(new PointerPressedEventArgs(choose, pointer, dialog, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        Assert.Equal(2, move.Y);
        choose.RaiseEvent(new PointerReleasedEventArgs(choose, pointer, dialog, default, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Assert.Equal(-3, move.Y);
        ticket.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, ticket, null!, dialog, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.Equal(0, move.Y);
    }, level);

    // WPF Compare.cs:560-571: each ticket is hidden and lowered until its 80 ms turn; Reduced still moves.
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public Task PrimeStaysHiddenUntilItsTurn(MotionLevel level) => WithDialog(async dialog =>
    {
        // Build the desk once first so JIT and first layout do not eat Prime's 80 ms turn.
        Click(dialog, Loc.Get("vaultgate_compare"));
        Click(dialog, Loc.Get("vaultgate_back"));
        for (var i = 0; i < 100 && dialog.Width != 460; i++) await Task.Delay(20);
        Dispatcher.UIThread.RunJobs();
        dialog.UpdateLayout();
        Click(dialog, Loc.Get("vaultgate_compare"));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var prime = Named<Border>(dialog, "PrimeTicket");
        var samples = new System.Collections.Generic.List<(double Opacity, double Y)>();
        // Drive frames by hand: every rendered frame before Prime's 80 ms turn must show it hidden.
        while (clock.ElapsedMilliseconds < 50)
        {
            global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (clock.ElapsedMilliseconds < 70) samples.Add((prime.Opacity, ((TranslateTransform)prime.RenderTransform!).Y));
            await Task.Delay(5);
        }
        Assert.NotEmpty(samples);
        Assert.All(samples, s => Assert.Equal((0, 26), s));
        for (var i = 0; i < 100 && prime.Opacity < 1; i++) await Task.Delay(20);
        Assert.Equal(1, prime.Opacity);
    }, level);

    [Theory]
    [InlineData(MotionLevel.Full, false)]
    [InlineData(MotionLevel.Reduced, false)]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Off, false)]
    public Task CloseHonorsMotionPreference(MotionLevel level, bool gentle) => WithDialog(async dialog =>
    {
        // WPF Compare.cs:582, 596-636: the card fades and holds 0; tickets slide down unless Off or Gentle.
        Click(dialog, Loc.Get("vaultgate_compare"));
        var card = (Border)dialog.Content!;
        var ticket = (TranslateTransform)Named<Border>(dialog, "BasicTicket").RenderTransform!;
        dialog.Close();
        Assert.Equal(level != MotionLevel.Off, dialog.IsVisible);
        var moved = false;
        for (var i = 0; i < 75 && dialog.IsVisible; i++)
        {
            Assert.Equal(0, card.GetBaseValue(Visual.OpacityProperty).GetValueOrDefault(1.0));
            moved |= ticket.Y > 0;
            await Task.Delay(20);
        }
        Assert.False(dialog.IsVisible);
        Assert.Equal(level != MotionLevel.Off && !gentle, moved);
    }, level, gentle);

    private static async Task<System.Collections.Generic.List<double>> Sample(Func<double> read, int ms)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var seen = new System.Collections.Generic.List<double>();
        while (clock.ElapsedMilliseconds < ms)
        {
            global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            seen.Add(read());
            await Task.Delay(5);
        }
        return seen;
    }

    // WPF Compare.cs:231-232, 569-570: deal-in and knob use BackEase EaseOut (0.3), so both pass the rest point.
    [Fact]
    public Task DealAndKnobSettleWithOvershoot() => WithDialog(async dialog =>
    {
        Click(dialog, Loc.Get("vaultgate_compare"));
        var deal = (TranslateTransform)Named<Border>(dialog, "BasicTicket").RenderTransform!;
        Assert.True((await Sample(() => deal.Y, 600)).Min() < -0.3);
        var knob = (TranslateTransform)dialog.GetVisualDescendants().OfType<Border>().Single(b => b.Width == 25 && b.Height == 20).RenderTransform!;
        Click(dialog, Loc.Get("vaultgate_billing_yearly"));
        Assert.True((await Sample(() => knob.X, 400)).Max() > 26.2);
    }, MotionLevel.Full);

    // WPF Compare.cs:241, 622-635: the yearly note fades both ways; Off snaps.
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Off)]
    public Task YearlyNoteFadesWithBilling(MotionLevel level) => WithDialog(async dialog =>
    {
        Click(dialog, Loc.Get("vaultgate_compare"));
        var note = dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Loc.Get("vaultgate_yearly_note"));
        foreach (var (word, end) in new[] { ("vaultgate_billing_yearly", 1.0), ("vaultgate_billing_monthly", 0.0) })
        {
            Click(dialog, Loc.Get(word));
            Assert.Equal(end, note.GetBaseValue(Visual.OpacityProperty).GetValueOrDefault(1.0));
            var seen = await Sample(() => note.Opacity, 450);
            Assert.Equal(level != MotionLevel.Off, seen.Any(o => o is > 0.02 and < 0.98));
            Assert.Equal(end, seen[^1]);
        }
    }, level);

    // Avalonia asks owned windows first, so the compare fade must not cancel the shell's own X.
    [Fact]
    public Task OwnerCloseIsNotCancelledByTheCompareFade() => WithDialog(dialog =>
    {
        Click(dialog, Loc.Get("vaultgate_compare"));
        var owner = (Window)dialog.Owner!;
        owner.Close();
        Assert.False(owner.IsVisible);
        Assert.False(dialog.IsVisible);
        return Task.CompletedTask;
    }, MotionLevel.Full, owned: true);

}

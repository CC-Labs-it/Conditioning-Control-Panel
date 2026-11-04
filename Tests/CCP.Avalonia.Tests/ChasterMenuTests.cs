using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The menu, the how-it-works card, the price box and Reset times (WPF ChasterTabView.xaml.cs
/// :1176-1290, ChasterTabView.Help.cs, ChasterTabView.PriceEdit.cs; WPF ChasterTabRenderTests
/// The_menu_is_one_row_per_price..., Clicking_the_figure_opens_a_box..., Escape_in_the_open_price_box...),
/// against a fake Chaster and a memory-only SecretStore.</summary>
public sealed class ChasterMenuTests
{
    private const string NoLock = "[]";
    private static readonly string Locked =
        "[{\"_id\":\"l1\",\"title\":\"Test Cage\",\"status\":\"locked\",\"role\":\"wearer\",\"endDate\":\"" + DateTime.UtcNow.AddDays(2).ToString("o") + "\"}]";

    private sealed class FakeChaster : HttpMessageHandler
    {
        public volatile string Locks = NoLock;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(r.RequestUri!.AbsolutePath switch
                {
                    "/auth/profile" => "{\"username\":\"kitten\"}",
                    "/locks" => Locks,
                    _ => "{}",
                }),
            });
    }

    private static async Task Run(Func<ChasterService, FakeChaster, ChasterTabView, Window, Task> body, double height = 3000)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var old = (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPrices, s.ChasterPriceOverrides, s.ChasterFlashDodge);
        var (oldGet, oldSet) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>();   // memory only: never the OS keyring
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPrices, s.ChasterPriceOverrides, s.ChasterFlashDodge) =
            ("l1", true, new List<string> { "attention" }, new Dictionary<string, int>(), false);
        var dir = Directory.CreateTempSubdirectory("ccp-chaster-menu-").FullName;
        new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
        var fake = new FakeChaster();
        var chaster = new ChasterService(new ChasterClient(fake), new SecretChasterTokenStore(), Path.Combine(dir, "chaster_tab.json"),
            () => new ChasterOptions(CoreSettings.Current.ChasterTabEnabled, CoreSettings.Current.ChasterLockId,
                new HashSet<string>(CoreSettings.Current.ChasterPrices ?? new List<string>())));
        ChasterHead.Service = chaster;
        var tab = new ChasterTabView();
        var w = new Window { Width = 1200, Height = height, Content = tab };
        try
        {
            w.Show();
            await chaster.RefreshLockAsync();
            tab.OnTabShown();
            Dispatcher.UIThread.RunJobs();
            await body(chaster, fake, tab, w);
        }
        finally
        {
            w.Close();
            ChasterHead.Service = null;
            chaster.Dispose();
            new SecretChasterTokenStore().Clear();
            try { Directory.Delete(dir, true); } catch { }
            (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPrices, s.ChasterPriceOverrides, s.ChasterFlashDodge) = old;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (oldGet, oldSet);
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
    }

    private static void Click(Window w, Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(Window w, Key key, PhysicalKey physical)
    {
        w.KeyPress(key, RawInputModifiers.None, physical, "");
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class PopClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int milliseconds) => Now = Now.AddMilliseconds(milliseconds);
    }

    [Fact]
    public Task RowPopContinuesFromItsCurrentScaleOnAnotherClick() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, _, tab, w) =>
    {
        var oldClock = ChasterTabView.RowPopTime;
        var oldMotion = CoreSettings.Current.MotionLevel;
        var clock = new PopClock();
        try
        {
            ChasterTabView.RowPopTime = clock;
            CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            var row = tab.PriceRows["session"];
            Click(w, row);
            var scale = Assert.IsType<ScaleTransform>(row.RenderTransform);
            Assert.Contains(BeatLoop.Running, beat => beat.Owner == row && beat.IsTicking);
            clock.Advance(80);
            BeatLoop.StepAll();
            var before = scale.ScaleX;
            Assert.InRange(before, 1.001, 1.059);
            Click(w, row);
            BeatLoop.StepAll();
            Assert.Equal(before, scale.ScaleX, 8);
            clock.Advance(133);
            BeatLoop.StepAll();
            Assert.InRange(scale.ScaleX, 1.059, 1.061);
            clock.Advance(247);
            BeatLoop.StepAll();
            Assert.Equal(1, scale.ScaleX, 8);
            Assert.Equal(scale.ScaleX, scale.ScaleY, 8);
            Assert.DoesNotContain(BeatLoop.Running, beat => beat.Owner == row);

            Click(w, row);
            clock.Advance(80);
            BeatLoop.StepAll();
            Assert.True(scale.ScaleX > 1);
            row.IsVisible = false;
            Assert.Equal(1, scale.ScaleX, 8);
            Assert.DoesNotContain(BeatLoop.Running, beat => beat.Owner == row);
            row.IsVisible = true;
            Click(w, row);
            clock.Advance(80);
            BeatLoop.StepAll();
            Assert.True(scale.ScaleX > 1);
            Assert.IsAssignableFrom<Panel>(row.Parent).Children.Remove(row);
            Assert.Equal(1, scale.ScaleX, 8);
            Assert.DoesNotContain(BeatLoop.Running, beat => beat.Owner == row);
        }
        finally
        {
            ChasterTabView.RowPopTime = oldClock;
            CoreSettings.Current.MotionLevel = oldMotion;
        }
        await Task.CompletedTask;
    }));

    [Fact]
    public Task HeroArtIsPresentAtWideAndNarrowWidths() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, _, tab, w) =>
    {
        var art = tab.FindControl<Image>("HeroArt")!;
        Assert.NotNull(art.Source);
        var plate = tab.FindControl<Grid>("HeroArtPlate")!;
        Assert.False(plate.IsHitTestVisible);
        foreach (var linked in new[] { true, false })
        {
            if (!linked) { new SecretChasterTokenStore().Clear(); tab.RefreshHero(); }
            Assert.Equal(linked, tab.FindControl<Control>("HeroTitle")!.IsVisible);
            foreach (var width in new[] { 1200d, 480d })
            {
                w.Width = width;
                Dispatcher.UIThread.RunJobs();
                Assert.True(art.Bounds.Width > 0);
                var hero = tab.FindControl<Border>("HeroCard")!;
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(hero.Bounds.Width), (int)Math.Ceiling(hero.Bounds.Height)));
                bitmap.Render(hero);
            }
        }
        await Task.CompletedTask;
    // WPF ChasterTabView.xaml.cs:283-303, 1193-1206 and Fx.cs:206-248.
    [Fact]
    public Task EmptyMenuHintFindsTheKeysAndTracksRealRowChanges() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, fake, tab, w) =>
    {
        fake.Locks = Locked;
        await chaster.RefreshLockAsync();
        var settings = CoreSettings.Current;
        settings.ChasterPrices = new List<string>();
        settings.PerformanceMode = false;
        settings.AutoPerformanceMode = false;
        settings.GentleVisuals = false;
        settings.MotionLevel = MotionLevel.Full;
        tab.Refresh();
        Dispatcher.UIThread.RunJobs();
        var hint = tab.FindControl<TextBlock>("SetupHint")!;
        var keys = tab.FindControl<global::Avalonia.Controls.Primitives.UniformGrid>("PresetRow")!;
        var scroll = tab.FindControl<ScrollViewer>("PageScroll")!;
        Assert.Equal(Loc.Get("chaster_setup_nothing"), hint.Text);
        Assert.Equal(Color.FromRgb(0xE0, 0xB0, 0x52), ((ISolidColorBrush)hint.Foreground!).Color);
        Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), hint.Cursor?.ToString());
        Assert.IsType<DropShadowEffect>(keys.Effect);
        Assert.Contains(BeatLoop.Running, l => l.Owner == tab);

        w.UpdateLayout();
        using (w.CaptureRenderedFrame()) { } // hit testing uses the rendered viewport
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Assert.Equal(0, scroll.Offset.Y);
        Click(w, hint);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0);
        // The row's actual click must clear the hint immediately, not at the 30 s tick.
        var row = tab.PriceRows["attention"];
        row.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        using (w.CaptureRenderedFrame()) { }
        Click(w, row);
        Assert.Contains("attention", settings.ChasterPrices!);
        Assert.False(hint.IsVisible);
        Assert.Null(keys.Effect);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == tab);
        Click(w, row);
        Assert.Empty(settings.ChasterPrices!);
        Assert.True(hint.IsVisible);
        Assert.IsType<DropShadowEffect>(keys.Effect);
        settings.MotionLevel = MotionLevel.Reduced;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.IsType<DropShadowEffect>(keys.Effect);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == tab);
        settings.PerformanceMode = true;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.Null(keys.Effect);
        Assert.Equal(1, tab.FindControl<Control>("PresetRow")!.Opacity);
        settings.PerformanceMode = false;
        settings.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.Contains(BeatLoop.Running, l => l.Owner == tab);
        tab.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == tab);
        tab.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(BeatLoop.Running, l => l.Owner == tab);
    }, height: 700));

    // WPF FxNudgeKeys puts the shadow on the four key shapes, including the gaps.
    [Fact]
    public Task KeyGlowRendersInsideTheGridGaps() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, fake, tab, _) =>
    {
        fake.Locks = Locked;
        await chaster.RefreshLockAsync();
        var settings = CoreSettings.Current;
        settings.ChasterPrices = new List<string>();
        settings.PerformanceMode = false;
        settings.AutoPerformanceMode = false;
        settings.GentleVisuals = false;
        settings.MotionLevel = MotionLevel.Reduced; // static 0.8, no frame-dependent breath
        tab.Refresh();
        Dispatcher.UIThread.RunJobs();
        var slot = tab.FindControl<StackPanel>("KeysSlot")!;
        var key = tab.FindControl<Control>("BtnPresetGentle")!;
        Assert.IsType<DropShadowEffect>(tab.FindControl<Control>("PresetRow")!.Effect);
        var at = key.TranslatePoint(new Point(key.Bounds.Width + 2, key.Bounds.Height / 2), slot)!.Value;
        var lit = KeyGapPixels(slot, at);
        settings.PerformanceMode = true;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        var dark = KeyGapPixels(slot, at);
        Assert.True(lit > dark + 5, $"no key-shaped glow in the inner gap: {lit} vs {dark}");
    }));

    private static int KeyGapPixels(Control slot, Point at)
    {
        var size = new PixelSize((int)Math.Ceiling(slot.Bounds.Width), (int)Math.Ceiling(slot.Bounds.Height));
        using var image = new RenderTargetBitmap(size);
        image.Render(slot);
        var pixel = Marshal.AllocHGlobal(4);
        try
        {
            image.CopyPixels(new PixelRect((int)at.X, (int)at.Y, 1, 1), pixel, 4, 4);
            // Gold raises red and green more than blue, independent of the dark background's hue.
            return Marshal.ReadByte(pixel, 2) + Marshal.ReadByte(pixel, 1) - 2 * Marshal.ReadByte(pixel, 0);
        }
        finally { Marshal.FreeHGlobal(pixel); }
    }

    [Fact]
    public Task TheMenuIsTwoBoardsOfRowsThatWriteThePricesAndTheRedFlashLineFollowsNatasha() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, _, tab, w) =>
    {
        Assert.True(tab.FindControl<StackPanel>("MenuPanel")!.IsVisible);
        Assert.Equal(TabPrices.All.Select(p => p.Id).OrderBy(x => x), tab.PriceRows.Keys.OrderBy(x => x));
        var costs = tab.FindControl<global::Avalonia.Controls.Primitives.UniformGrid>("CostRows")!.Children.Cast<Control>().Select(c => (string)c.Tag!).ToList();
        Assert.NotEmpty(costs);
        Assert.All(costs, id => Assert.True(TabPrices.Find(id)!.Seconds > 0, id + " is on the wrong board"));
        Assert.True(tab.PriceRows["attention"].IsChecked);   // the saved set is pushed onto the rows

        // a row flips its price into the saved set and out again, and the fourth key follows
        Click(w, tab.PriceRows["session"]);
        Assert.Contains("session", CoreSettings.Current.ChasterPrices!);
        Click(w, tab.PriceRows["session"]);
        Assert.DoesNotContain("session", CoreSettings.Current.ChasterPrices!);

        // red flashes need Natasha's row, and print its figure once it is on
        var sub = tab.FindControl<TextBlock>("TxtFlashDodgeSub")!;
        Assert.Equal(Loc.Get("chaster_flash_dodge_needs"), sub.Text);
        Click(w, tab.PriceRows[NatashasFavourite.EventId]);
        Assert.Equal(Loc.GetF("chaster_flash_dodge_sub", NatashasFavourite.DodgeMs / 1000,
            CircesTab.Format(TabPriceEdit.Effective(NatashasFavourite.EventId, null), signed: false)), sub.Text);
        Click(w, tab.FindControl<CheckBox>("ChkFlashDodge")!);
        Assert.True(CoreSettings.Current.ChasterFlashDodge);

        // how it works: behind a link, opens and closes
        var card = tab.FindControl<Border>("HowItWorksCard")!;
        Assert.False(card.IsVisible);
        Click(w, tab.FindControl<Button>("BtnHowItWorks")!);
        Assert.True(card.IsVisible);
        Click(w, tab.FindControl<Button>("BtnHowItWorks")!);
        Assert.False(card.IsVisible);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task ClickingAFigureOpensABoxEnterKeepsEscDropsResetClearsAndALockFreezesIt() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, fake, tab, w) =>
    {
        var hint = tab.FindControl<TextBlock>("TxtMenuHint")!;
        var reset = tab.FindControl<Button>("BtnResetPrices")!;
        Assert.Equal(LockLookup.None, chaster.LockLookup);
        Assert.Equal(Loc.Get("chaster_menu_hint_edit"), hint.Text);
        Assert.False(reset.IsVisible);

        // a click on the figure edits and never flips the row
        var stamp = tab.StampFor("typo")!;
        Assert.False(EscapeClaim.InASurface(stamp));
        Click(w, stamp);
        Assert.Equal("typo", tab.EditingPriceId);
        Assert.False(stamp.IsVisible);
        Assert.DoesNotContain("typo", CoreSettings.Current.ChasterPrices!);
        var box = tab.PriceEditor!;
        Assert.True(EscapeClaim.InASurface(box));   // its Esc is the box's, not a panic press
        box.Text = "2";
        Key(w, global::Avalonia.Input.Key.Enter, PhysicalKey.Enter);
        Assert.Null(tab.EditingPriceId);
        Assert.Equal(120, CoreSettings.Current.ChasterPriceOverrides["typo"]);
        Assert.True(stamp.IsVisible);
        Assert.Contains("+2:00", stamp.Text);
        Assert.Equal(FontStyle.Italic, stamp.FontStyle);
        Assert.True(reset.IsVisible);

        // Esc drops what was typed
        Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "9";
        Key(w, global::Avalonia.Input.Key.Escape, PhysicalKey.Escape);
        Assert.Null(tab.EditingPriceId);
        Assert.Equal(120, CoreSettings.Current.ChasterPriceOverrides["typo"]);

        // sizes picked elsewhere never open a box
        Assert.False(tab.BeginPriceEdit("leash"));
        Assert.False(tab.BeginPriceEdit(CircesMisses.EventId));
        Assert.False(tab.BeginPriceEdit(TabDayEnd.StreakEventId));

        // Reset times puts every figure back
        Click(w, reset);
        Assert.Empty(CoreSettings.Current.ChasterPriceOverrides);
        Assert.Contains("+0:30", stamp.Text);
        Assert.False(reset.IsVisible);

        // a lock starting closes an open box and freezes the figures
        Assert.True(tab.BeginPriceEdit("typo"));
        fake.Locks = Locked;
        await chaster.RefreshLockAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(LockLookup.Chosen, chaster.LockLookup);
        Assert.Null(tab.EditingPriceId);
        Assert.Equal(Loc.Get("chaster_menu_hint_locked"), hint.Text);
        Assert.False(tab.BeginPriceEdit("typo"));
        Assert.Equal(Loc.Get("chaster_price_edit_locked"), ToolTip.GetTip(stamp));
    }));
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Xunit;
namespace CCP.Avalonia.Tests;

/// <summary>Menu order/key sync, price-box keys, lock guard, the limits drawer and a language switch
/// (wire-views-tab-chaster; the price-box and help basics are in ChasterMenuTests).</summary>
public sealed class ChasterMenuLimitsTests
{
    private static void Click(Window window, Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        ChasterBillTests.Click(window, control);
    }

    private sealed class FakeChaster(bool locked) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.RequestUri!.AbsolutePath == "/locks"
                ? !locked ? "[]" : "[{\"_id\":\"l1\",\"title\":\"Test Cage\",\"status\":\"locked\",\"role\":\"wearer\",\"endDate\":\"" + DateTime.UtcNow.AddDays(2).ToString("o") + "\"}]" : "{}";
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

    private static async Task Run(Func<ChasterService, ChasterTabView, Window, Task> body, bool locked = true)
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldHead = ChasterHead.Service;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        var old = (s.ChasterPriceOverrides, s.ChasterDayLimit, s.ChasterBacklogLimit, s.ChasterRelockPastEnd, s.ChasterFlashDodge,
            s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPaused, s.ChasterPrices);
        var oldMotion = s.MotionLevel;
        s.MotionLevel = MotionLevel.Off; // These tests prove menu rules/input, not the deferred page FX.
        s.ChasterLockId = "l1"; s.ChasterTabEnabled = true; s.ChasterPaused = false;
        s.ChasterPrices = new List<string> { "attention", "typo" };
        s.ChasterPriceOverrides = new();
        s.ChasterRelockPastEnd = false;
        s.ChasterFlashDodge = false;
        var directory = Directory.CreateTempSubdirectory("ccp-chaster-menu-").FullName;
        var chaster = new ChasterService(new ChasterClient(new FakeChaster(locked)), new MemoryTokens(), Path.Combine(directory, "tab.json"),
            () => new ChasterOptions(s.ChasterTabEnabled, s.ChasterLockId, new HashSet<string>(s.ChasterPrices),
                TabLimits.FromMinutes(LimitChange.Effective(s.ChasterDayLimit, DateTime.UtcNow), LimitChange.Effective(s.ChasterBacklogLimit, DateTime.UtcNow)),
                Paused: s.ChasterPaused, PriceOverrides: s.ChasterPriceOverrides));
        ChasterHead.Service = chaster;
        var tab = new ChasterTabView();
        var window = new Window { Width = 1200, Height = 2600, Content = tab };
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
            (s.ChasterPriceOverrides, s.ChasterDayLimit, s.ChasterBacklogLimit, s.ChasterRelockPastEnd, s.ChasterFlashDodge,
                s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPaused, s.ChasterPrices) = old;
            s.MotionLevel = oldMotion;
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = oldProvider;
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    [Fact]
    public Task MenuOrdersRowsAndTogglesTheSavedSetAndMatchingKey() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, tab, w) =>
    {
        var (costs, earns) = TabPageText.Split(TabPrices.All);
        var costRows = tab.FindControl<UniformGrid>("CostRows")!.Children.OfType<ToggleButton>().ToList();
        Assert.Equal(costs.Select(p => p.Id), costRows.Select(r => r.Tag));
        Assert.Equal(earns.Select(p => p.Id), tab.FindControl<UniformGrid>("EarnRows")!.Children.Select(r => r.Tag));
        var attention = costRows.Single(r => (string)r.Tag! == "attention");
        var missesTip = Assert.IsType<TextBlock>(ToolTip.GetTip(tab.FindControl<UniformGrid>("EarnRows")!.Children.OfType<ToggleButton>()
            .Concat(costRows).Single(r => (string)r.Tag! == CircesMisses.EventId)));
        Assert.Equal((Loc.Get("chaster_misses_hint"), 320d, TextWrapping.Wrap), (missesTip.Text, missesTip.MaxWidth, missesTip.TextWrapping));
        Assert.True(attention.IsChecked);
        var before = CoreSettings.Current.ChasterPrices;
        Click(w, attention);
        Assert.False(attention.IsChecked);
        Assert.DoesNotContain("attention", CoreSettings.Current.ChasterPrices);
        Assert.NotSame(before, CoreSettings.Current.ChasterPrices);
        var gentle = tab.FindControl<ToggleButton>("BtnPresetGentle")!;
        Click(w, gentle);
        Assert.All(costRows.Concat(tab.FindControl<UniformGrid>("EarnRows")!.Children.OfType<ToggleButton>()), row =>
            Assert.Equal(CoreSettings.Current.ChasterPrices.Contains((string)row.Tag!), row.IsChecked == true));
        Assert.True(gentle.IsChecked);
        Assert.Equal(Loc.Get("chaster_menu_hint_locked"), tab.FindControl<TextBlock>("TxtMenuHint")!.Text);
        Click(w, tab.FindControl<Button>("BtnHowItWorks")!);
        Assert.True(tab.FindControl<Border>("HowItWorksCard")!.IsVisible);
        Click(w, tab.FindControl<Button>("BtnHowItWorks")!);
        Assert.False(tab.FindControl<Border>("HowItWorksCard")!.IsVisible);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task StampEnterCommitsEscapeCancelsAndEmptyRestoresDefault() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, tab, w) =>
    {
        // WPF shows the menu only while linked (LinkedPanel, ChasterTabView.xaml:642), so editing runs linked with no lock.
        ChasterHead.Service = null;
        try
        {
            tab.Refresh();
            Assert.False(tab.FindControl<StackPanel>("MenuPanel")!.IsVisible);
        }
        finally { ChasterHead.Service = chaster; }
        await chaster.RefreshLockAsync();
        Assert.Equal(LockLookup.None, chaster.LockLookup);
        {
            CoreSettings.Current.ChasterPriceOverrides = new();
            tab.Refresh();
            Assert.True(tab.FindControl<StackPanel>("MenuPanel")!.IsVisible);
            var stamp = tab.StampFor("attention")!;
            var row = stamp.GetVisualAncestors().OfType<ToggleButton>().First();
            var rowOn = row.IsChecked;
            Click(w, stamp);
            Assert.Equal("attention", tab.EditingPriceId);
            Assert.Equal(rowOn, row.IsChecked); // the stamp's tunnel handler claims the row click
            var editor = ((Grid)row.Content!).Children.OfType<TextBox>().Single();
            editor.Text = "7"; w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, ""); Dispatcher.UIThread.RunJobs();
            Assert.Null(tab.EditingPriceId);
            Assert.Equal(420, TabPriceEdit.Effective("attention", CoreSettings.Current.ChasterPriceOverrides));
            Assert.Equal(FontStyle.Italic, stamp.FontStyle);
            Assert.True(tab.FindControl<Button>("BtnResetPrices")!.IsVisible);
            Assert.True(tab.BeginPriceEdit("attention"));
            ((Grid)row.Content!).Children.OfType<TextBox>().Single().Text = "8";
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
            Assert.Null(tab.EditingPriceId);
            Assert.Equal(420, TabPriceEdit.Effective("attention", CoreSettings.Current.ChasterPriceOverrides));
            tab.BeginPriceEdit("attention");
            w.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Equal(rowOn, row.IsChecked);
            ((Grid)row.Content!).Children.OfType<TextBox>().Single().Text = "6";
            tab.FindControl<Button>("BtnHowItWorks")!.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(tab.EditingPriceId);
            Assert.Equal(360, TabPriceEdit.Effective("attention", CoreSettings.Current.ChasterPriceOverrides));
            tab.BeginPriceEdit("attention");
            ((Grid)row.Content!).Children.OfType<TextBox>().Single().Text = "";
            tab.CommitPriceEdit();
            Assert.Empty(CoreSettings.Current.ChasterPriceOverrides);
            Assert.False(tab.FindControl<Button>("BtnResetPrices")!.IsVisible);
            Assert.False(tab.BeginPriceEdit(CircesMisses.EventId));
            tab.BeginPriceEdit("attention");
            ((Grid)row.Content!).Children.OfType<TextBox>().Single().Text = "9";
            tab.CommitPriceEdit();
            Click(w, tab.FindControl<Button>("BtnResetPrices")!);
            Assert.Empty(CoreSettings.Current.ChasterPriceOverrides);
        }
    }, locked: false));

    [Fact]
    public Task ARunningLockRefusesEditingAndClosesAnExistingEditor() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, tab, _) =>
    {
        await chaster.RefreshLockAsync();
        tab.RepaintForLock();
        Assert.False(tab.BeginPriceEdit("attention"));
        ChasterHead.Service = null;
        Assert.True(tab.BeginPriceEdit("attention"));
        ChasterHead.Service = chaster;
        tab.RepaintForLock();
        Assert.Null(tab.EditingPriceId);
        Assert.False(tab.FindControl<Button>("BtnResetPrices")!.IsVisible);
    }));

    [Fact] // P07 / WPF ChasterTabView.xaml.cs:173: the 30 s hero tick leaves the stamps alone
    public Task TheHeroTickDoesNotRepaintStampsALockChangeDoes() => AvaloniaTestDispatcher.RunAsync(() => Run((_, tab, _) =>
    {
        var stamp = tab.StampFor("attention")!;
        stamp.Text = "sentinel";
        tab.RefreshHero(); // what SlowTick runs
        Assert.Equal("sentinel", stamp.Text);
        tab.RepaintForLock();
        Assert.NotEqual("sentinel", stamp.Text);
        return Task.CompletedTask;
    }));

    [Fact]
    public Task LimitsLowerImmediatelyRaiseAfterADayAndBacklogFollowsDay() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, tab, w) =>
    {
        var s = CoreSettings.Current;
        s.ChasterDayLimit = new LimitSetting(120, 0, null);
        s.ChasterBacklogLimit = new LimitSetting(360, 0, null);
        tab.Refresh();
        Click(w, tab.FindControl<Button>("BtnLimits")!);
        Assert.True(tab.FindControl<Border>("LimitsCard")!.IsVisible);
        var day = tab.FindControl<Slider>("SliderDayLimit")!;
        var backlog = tab.FindControl<Slider>("SliderBacklogLimit")!;
        day.Value = 60;
        Assert.Equal(60, s.ChasterDayLimit.Minutes);
        Assert.False(s.ChasterDayLimit.HasPending);
        day.Value = 600;
        Assert.Equal(60, s.ChasterDayLimit.Minutes);
        Assert.Equal(600, s.ChasterDayLimit.PendingMinutes);
        Assert.Equal(600, s.ChasterBacklogLimit.PendingMinutes);
        Assert.Equal(600, backlog.Value);
        Assert.True(s.ChasterDayLimit.PendingAtUtc > DateTime.UtcNow.AddHours(23));
        Assert.True(tab.FindControl<TextBlock>("TxtDayPending")!.IsVisible);
        tab.FindControl<CheckBox>("ChkRelock")!.IsChecked = true;
        Assert.True(s.ChasterRelockPastEnd);
        tab.FindControl<CheckBox>("ChkFlashDodge")!.IsChecked = true;
        Assert.True(s.ChasterFlashDodge);
        await Task.CompletedTask;
    }));
    [Fact]
    public Task LanguageSwitchRepaintsTheMenuNamesStampsAndHelp() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, tab, _) =>
    {
        try
        {
            LocalizationManager.Instance.SetLanguage("de");
            Dispatcher.UIThread.RunJobs();
            var row = tab.FindControl<UniformGrid>("CostRows")!.Children.OfType<ToggleButton>().Single(r => (string)r.Tag! == "attention");
            var words = ((Grid)row.Content!).Children.OfType<StackPanel>().Single();
            Assert.Equal(TabMenuCopy.ShortName("attention", Loc.Get), words.Children.OfType<TextBlock>().First().Text);
            Assert.Equal(TabPageText.Price(TabPriceEdit.Shown(TabPrices.Find("attention")!, CoreSettings.Current.ChasterPriceOverrides), Loc.Get("chaster_each")), tab.StampFor("attention")!.Text);
            Assert.Equal(Loc.Get("chaster_menu_hint_locked"), tab.FindControl<TextBlock>("TxtMenuHint")!.Text);
        }
        finally { LocalizationManager.Instance.SetLanguage("en"); }
        await Task.CompletedTask;
    }));

}

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PresetsModRefreshTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ModRefreshTestsRestoreProviders(int test)
    {
        var (oldMod, oldText) = (CoreMods.ActiveModIdProvider, CoreMods.MakeModAwareProvider);
        Func<string?> mod = () => BuiltInMods.BambiSleepId;
        Func<string, string?> text = value => value;
        CoreMods.ActiveModIdProvider = mod;
        CoreMods.MakeModAwareProvider = text;
        try
        {
            if (test == 0) VisibleModChangesCoalesceAndRefreshTheSelectedPresetAndRack();
            else if (test == 1) HiddenModChangeWaitsForShowAndKeepsTheSessionDetail();
            else AncestorHideDefersUntilItIsVisibleAgain();
            Assert.Same(mod, CoreMods.ActiveModIdProvider);
            Assert.Same(text, CoreMods.MakeModAwareProvider);
        }
        finally
        {
            CoreMods.ActiveModIdProvider = oldMod;
            CoreMods.MakeModAwareProvider = oldText;
        }
    }

    [Fact]
    public void VisibleModChangesCoalesceAndRefreshTheSelectedPresetAndRack() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var (oldMod, oldText) = (CoreMods.ActiveModIdProvider, CoreMods.MakeModAwareProvider);
        var preset = new Preset { Id = "mod-refresh-test", Name = "My preset", Description = "My description" };
        CoreSettings.Current.UserPresets.Add(preset);
        string prefix = "Old";
        string modId = BuiltInMods.CCPDefaultId;
        int nameReads = 0;
        CoreMods.ActiveModIdProvider = () => modId;
        CoreMods.MakeModAwareProvider = text => { if (text == preset.Name) nameReads++; return prefix + " " + text; };
        var view = new PresetsTabView();
        var host = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            host.Show(); Settle();
            var chip = Chip(view, preset.Id);
            var p = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), host)!.Value;
            host.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
            host.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
            Settle();
            Assert.Equal("Old My preset", Text(view, "TxtDetailTitle"));
            var oldChip = Chip(view, preset.Id);
            var oldRow = FirstRow(view);
            var builtIn = Preset.GetDefaultPresets().First();
            var oldBuiltInName = ChipName(view, builtIn.Id);
            Assert.Equal(PresetNaming.DisplayName(builtIn), oldBuiltInName);
            nameReads = 0;
            modId = BuiltInMods.DronificationId;
            prefix = "First"; RaiseMod();
            prefix = "New"; RaiseMod();
            Assert.Same(oldChip, Chip(view, preset.Id));
            Settle();
            Assert.NotSame(oldChip, Chip(view, preset.Id));
            Assert.NotSame(oldRow, FirstRow(view));
            Assert.Equal(2, nameReads); // one chip and one detail title, despite two signals
            Assert.Equal(PresetNaming.DisplayName(builtIn), ChipName(view, builtIn.Id));
            Assert.NotEqual(oldBuiltInName, ChipName(view, builtIn.Id));
            Assert.Equal("New My preset", ChipName(view, preset.Id));
            Assert.Equal("New My preset", Text(view, "TxtDetailTitle"));
            Assert.Equal("New My description", Text(view, "TxtDetailSubtitle"));
            Assert.True(view.FindControl<Control>("PresetDetailScroller")!.IsVisible);
            var builtInChip = Chip(view, builtIn.Id);
            var point = builtInChip.TranslatePoint(new Point(builtInChip.Bounds.Width / 2, builtInChip.Bounds.Height / 2), host)!.Value;
            host.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            host.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            Settle();
            Assert.Equal(PresetNaming.DisplayName(builtIn), Text(view, "TxtDetailTitle"));
        }
        finally { host.Close(); CoreSettings.Current.UserPresets.Remove(preset); CoreMods.ActiveModIdProvider = oldMod; CoreMods.MakeModAwareProvider = oldText; }
    });

    [Fact]
    public void HiddenModChangeWaitsForShowAndKeepsTheSessionDetail() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var (oldMod, oldText) = (CoreMods.ActiveModIdProvider, CoreMods.MakeModAwareProvider);
        var preset = new Preset { Id = "mod-refresh-hidden", Name = "My preset", Description = "Description" };
        CoreSettings.Current.UserPresets.Add(preset);
        string prefix = "Old";
        string modId = BuiltInMods.BambiSleepId;
        CoreMods.ActiveModIdProvider = () => modId;
        CoreMods.MakeModAwareProvider = text => prefix + " " + text;
        var view = new PresetsTabView();
        var host = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            host.Show(); Settle();
            var row = FirstRow(view);
            row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Settle();
            var title = Text(view, "TxtDetailTitle");
            var rowTitle = RowTitle(row);
            view.FindControl<Control>("SessionSpoilerPanel")!.IsVisible = true;
            view.IsVisible = false; Settle();
            var oldChip = Chip(view, preset.Id);
            modId = BuiltInMods.DronificationId;
            prefix = "New"; RaiseMod(); Settle();
            Assert.Same(oldChip, Chip(view, preset.Id));
            Assert.Equal("Old My preset", ChipName(view, preset.Id));
            view.IsVisible = true; Settle();
            Assert.NotSame(oldChip, Chip(view, preset.Id));
            Assert.Equal("New My preset", ChipName(view, preset.Id));
            Assert.NotSame(row, FirstRow(view));
            Assert.NotEqual(rowTitle, RowTitle(FirstRow(view)));
            Assert.True(view.FindControl<Control>("SessionDetailScroller")!.IsVisible);
            Assert.True(view.FindControl<Control>("SessionSpoilerPanel")!.IsVisible);
            Assert.Equal(title, Text(view, "TxtDetailTitle"));
        }
        finally { host.Close(); CoreSettings.Current.UserPresets.Remove(preset); CoreMods.ActiveModIdProvider = oldMod; CoreMods.MakeModAwareProvider = oldText; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuiltInSessionRackUsesModeAwareCopyAfterModChange(bool description) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldMod = CoreMods.ActiveModIdProvider;
        var oldText = CoreMods.MakeModAwareProvider;
        string modId = BuiltInMods.BambiSleepId;
        CoreMods.ActiveModIdProvider = () => modId;
        CoreMods.MakeModAwareProvider = text => text;
        var view = new PresetsTabView();
        var host = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            host.Show(); Settle();
            Border Row() => view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>()
                .Single(r => r.Tag is Session { Id: "morning_drift" });
            var session = Assert.IsType<Session>(Row().Tag);
            var before = description ? session.GetModeAwareDescription() : session.GetModeAwareName();
            modId = BuiltInMods.DronificationId;
            RaiseMod(); Settle();
            var after = description ? session.GetModeAwareDescription() : session.GetModeAwareName();
            Assert.NotEqual(before, after);
            var row = Row();
            if (description)
            {
                var blurb = Assert.IsType<Grid>(row.Child).Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 3);
                Assert.Equal(after.Split('\n')[0].Trim(), blurb.Text);
                Assert.Equal(after, ToolTip.GetTip(blurb));
            }
            else Assert.Equal(after, RowTitle(row));
            row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Settle();
            if (description) Assert.StartsWith(after, Text(view, "TxtSessionDescription"));
            else Assert.Equal($"{session.Icon} {after}", Text(view, "TxtDetailTitle"));
        }
        finally
        {
            host.Close();
            CoreMods.ActiveModIdProvider = oldMod;
            CoreMods.MakeModAwareProvider = oldText;
        }
    });

    [Fact]
    public void LanguageChangeReappliesTheActiveSessionSearch() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldMod = CoreMods.ActiveModIdProvider;
        var oldDate = CoreSettings.Current.InstallDate;
        CoreMods.ActiveModIdProvider = () => BuiltInMods.CCPDefaultId;
        CoreSettings.Current.InstallDate = "2000-01-01";
        var view = new PresetsTabView();
        var host = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            host.Show(); Settle();
            var panel = view.FindControl<StackPanel>("SessionRackPanel")!;
            var search = view.FindControl<TextBox>("TxtRackSearch")!;
            var session = Assert.IsType<Session>(panel.Children.OfType<Border>()
                .Single(r => r.Tag is Session { Id: "morning_drift" }).Tag);
            var query = session.GetModeAwareName();
            Assert.Equal("Morning Drift", query);
            search.Text = query; Settle(); search.Focus();
            Assert.Contains(panel.Children.OfType<Border>(), r => r.Tag == session);
            LocalizationManager.Instance.SetLanguage("de"); Settle();
            Assert.Equal(query, search.Text);
            Assert.True(search.IsFocused);
            Assert.DoesNotContain(panel.Children.OfType<Border>(), r => r.Tag == session);
            LocalizationManager.Instance.SetLanguage("en"); Settle();
            Assert.Contains(panel.Children.OfType<Border>(), r => r.Tag == session);
            Assert.True(search.IsFocused);
        }
        finally
        {
            host.Close(); CoreMods.ActiveModIdProvider = oldMod;
            CoreSettings.Current.InstallDate = oldDate;
            LocalizationManager.Instance.SetLanguage("en");
        }
    });

    [Fact]
    public void LanguageChangeResortsSessionNamesAndKeepsRowFocus() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldMod = CoreMods.ActiveModIdProvider;
        var oldDate = CoreSettings.Current.InstallDate;
        CoreMods.ActiveModIdProvider = () => BuiltInMods.CCPDefaultId;
        CoreSettings.Current.InstallDate = "2000-01-01";
        var view = new PresetsTabView();
        var host = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            host.Show(); Settle();
            var sort = view.FindControl<ComboBox>("CmbRackSort")!;
            sort.SelectedItem = sort.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "name");
            Settle();
            var panel = view.FindControl<StackPanel>("SessionRackPanel")!;
            var english = panel.Children.OfType<Border>().Select(r => (Session)r.Tag!).ToArray();
            var focused = english.Single(s => s.Id == "morning_drift");
            var focusedRow = panel.Children.OfType<Border>().Single(r => r.Tag == focused);
            focusedRow.Focus();
            LocalizationManager.Instance.SetLanguage("de"); Settle();
            var actual = panel.Children.OfType<Border>().Select(r => (Session)r.Tag!).ToArray();
            var expected = SessionRackQuery.SortRackSessions(english, english, "name");
            Assert.NotEqual(english.Select(s => s.Id), expected.Select(s => s.Id));
            Assert.Equal(expected.Select(s => s.Id), actual.Select(s => s.Id));
            Assert.Same(focusedRow, panel.Children.OfType<Border>().Single(r => r.Tag == focused));
            Assert.True(focusedRow.IsFocused);
            var actions = Assert.IsType<Grid>(focusedRow.Child).Children.OfType<StackPanel>()
                .Single(c => Grid.GetColumn(c) == 8);
            var action = actions.Children.OfType<Button>().First();
            action.Focus();
            Assert.True(action.IsFocused);
            LocalizationManager.Instance.SetLanguage("en"); Settle();
            Assert.Equal(english.Select(s => s.Id), panel.Children.OfType<Border>().Select(r => ((Session)r.Tag!).Id));
            Assert.Same(focusedRow, panel.Children.OfType<Border>().Single(r => r.Tag == focused));
            Assert.True(action.IsFocused);
        }
        finally
        {
            host.Close(); CoreMods.ActiveModIdProvider = oldMod;
            CoreSettings.Current.InstallDate = oldDate;
            LocalizationManager.Instance.SetLanguage("en");
        }
    });

    [Fact]
    public void AncestorHideDefersUntilItIsVisibleAgain() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var (oldMod, oldText) = (CoreMods.ActiveModIdProvider, CoreMods.MakeModAwareProvider);
        var preset = new Preset { Id = "mod-refresh-parent", Name = "My preset" };
        CoreSettings.Current.UserPresets.Add(preset);
        string prefix = "Old";
        CoreMods.MakeModAwareProvider = text => prefix + " " + text;
        var view = new PresetsTabView();
        var parent = new Grid { Children = { view } };
        var host = new Window { Width = 1200, Height = 900, Content = parent };
        try
        {
            host.Show(); Settle();
            parent.IsVisible = false; Settle();
            var oldChip = Chip(view, preset.Id);
            prefix = "New"; RaiseMod(); Settle();
            Assert.Same(oldChip, Chip(view, preset.Id));
            parent.IsVisible = true; Settle();
            Assert.NotSame(oldChip, Chip(view, preset.Id));
            Assert.Equal("New My preset", ChipName(view, preset.Id));
        }
        finally { host.Close(); CoreSettings.Current.UserPresets.Remove(preset); CoreMods.ActiveModIdProvider = oldMod; CoreMods.MakeModAwareProvider = oldText; }
    });

    [Fact]
    public void LanguageChangeRefreshesUnselectedPresetChips() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var view = new PresetsTabView();
        var host = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            host.Show(); Settle();
            var old = view.FindControl<WrapPanel>("PresetCardsPanel")!.Children.OfType<Border>().First(b => b.Tag is string);
            LocalizationManager.Instance.SetLanguage("de"); Settle();
            var fresh = view.FindControl<WrapPanel>("PresetCardsPanel")!.Children.OfType<Border>().First(b => b.Tag is string);
            Assert.NotSame(old, fresh);
            var badge = Assert.IsType<StackPanel>(fresh.Child).Children.OfType<Border>().Single();
            Assert.Equal(Loc.Get("preset_tag_default"), Assert.IsType<TextBlock>(badge.Child).Text);
        }
        finally { host.Close(); LocalizationManager.Instance.SetLanguage("en"); }
    });

    // From #4047 (restack b173): the trunk rack reconcile (#3937 RefreshLocalizedRack) under its query and focus cases.
    [Fact]
    public void LanguageRefreshReconcilesSearchAndModeAwareRows() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var view = new PresetsTabView();
        var host = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            host.Show(); Settle();
            var all = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().Select(r => (Session)r.Tag!).ToArray();
            var row = FirstRow(view);
            var session = (Session)row.Tag!;
            Assert.Equal(session.GetModeAwareName(), RowTitle(row));
            var description = Assert.IsType<Grid>(row.Child).Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 3);
            Assert.Equal(session.GetModeAwareDescription().Split('\n')[0].Trim(), description.Text);
            var search = view.FindControl<TextBox>("TxtRackSearch")!;
            search.Text = session.GetModeAwareName(); Settle();
            Assert.Contains(view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>(), r => ReferenceEquals(r.Tag, session));
            LocalizationManager.Instance.SetLanguage("zh-CN"); Settle();
            var actual = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().Select(r => (Session)r.Tag!).ToArray();
            var expected = all.Where(item => item.GetModeAwareName().Contains(search.Text!, StringComparison.OrdinalIgnoreCase) || item.GetModeAwareDescription().Contains(search.Text!, StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.Equal(expected.OrderBy(x => x.Id), actual.OrderBy(x => x.Id));
            search.Text = ""; Settle();
            var sort = view.FindControl<ComboBox>("CmbRackSort")!;
            sort.SelectedItem = sort.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "name");
            Settle();
            LocalizationManager.Instance.SetLanguage("de"); Settle();
            var names = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().Select(r => ((Session)r.Tag!).GetModeAwareName()).ToArray();
            Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
        }
        finally { host.Close(); LocalizationManager.Instance.SetLanguage("en"); }
    });

    [Fact]
    public void LanguageChangeReappliesRackSearch() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var view = new PresetsTabView();
        var host = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            host.Show(); Settle();
            var row = FirstRow(view);
            var session = (Session)row.Tag!;
            var search = view.FindControl<TextBox>("TxtRackSearch")!;
            search.Text = session.GetModeAwareName(); Settle();
            Assert.Contains(view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>(), r => ReferenceEquals(r.Tag, session));
            LocalizationManager.Instance.SetLanguage("zh-CN"); Settle();
            Assert.DoesNotContain(view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>(), r =>
            {
                var item = (Session)r.Tag!;
                return !item.GetModeAwareName().Contains(search.Text!, StringComparison.OrdinalIgnoreCase) && !item.GetModeAwareDescription().Contains(search.Text!, StringComparison.OrdinalIgnoreCase);
            });
        }
        finally { host.Close(); LocalizationManager.Instance.SetLanguage("en"); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LanguageQueryChangeKeepsFocusOnRetainedRow(bool sortByName) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var builtIn = Session.MorningDrift;
        var custom = new Session { Id = "focus-query", Name = "Zzz", Description = builtIn.GetModeAwareName(), Source = SessionSource.Custom, IsAvailable = true };
        var manager = new SessionManager();
        manager.AllSessions.Add(builtIn);
        manager.AllSessions.Add(custom);
        var view = new PresetsTabView();
        view.UseSessionManager(manager);
        var host = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            host.Show(); Settle();
            var panel = view.FindControl<StackPanel>("SessionRackPanel")!;
            if (sortByName)
            {
                var sort = view.FindControl<ComboBox>("CmbRackSort")!;
                sort.SelectedItem = sort.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "name");
            }
            else view.FindControl<TextBox>("TxtRackSearch")!.Text = builtIn.GetModeAwareName();
            Settle();
            var before = panel.Children.OfType<Border>().ToArray();
            Assert.Equal(2, before.Length);
            var kept = before.Single(r => ReferenceEquals(r.Tag, custom));
            kept.Focus();
            Assert.True(kept.IsFocused);
            LocalizationManager.Instance.SetLanguage("zh-CN"); Settle();
            var after = panel.Children.OfType<Border>().ToArray();
            Assert.Same(kept, after.Single(r => ReferenceEquals(r.Tag, custom)));
            Assert.True(kept.IsFocused);
            if (sortByName) Assert.NotEqual(Array.IndexOf(before, kept), Array.IndexOf(after, kept));
            else Assert.Single(after);
        }
        finally { host.Close(); LocalizationManager.Instance.SetLanguage("en"); }
    });

    private static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }
    private static void RaiseMod() => CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true));
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static Border Chip(PresetsTabView view, string id) => view.FindControl<WrapPanel>("PresetCardsPanel")!.Children.OfType<Border>().Single(b => Equals(b.Tag, id));
    private static string? ChipName(PresetsTabView view, string id) => Assert.IsType<StackPanel>(Chip(view, id).Child).Children.OfType<TextBlock>().Single().Text;
    private static string? RowTitle(Border row) => Assert.IsType<Grid>(row.Child).Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 2).Text;
    private static Border FirstRow(PresetsTabView view) => view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
    private static string? Text(PresetsTabView view, string name) => view.FindControl<TextBlock>(name)!.Text;
}

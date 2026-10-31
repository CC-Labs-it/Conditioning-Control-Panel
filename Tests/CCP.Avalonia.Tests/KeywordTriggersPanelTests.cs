using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Helpers;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using IconGlyph = ConditioningControlPanel.Avalonia.Controls.IconGlyph;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

/// <summary>
/// views-companion-keyword-triggers / shell-keyword-triggers: the custom trigger list on the
/// Awareness tab drives AppSettings.KeywordTriggers like MainWindow.KeywordTriggers.cs:160-575.
/// </summary>
public sealed class KeywordTriggersPanelTests
{
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Find<T>(Control c, string name) where T : Control => c.FindControl<T>(name)!;

    private static List<Border> Rows(KeywordTriggersPanel p) =>
        Find<StackPanel>(p, "KeywordTriggerListPanel").Children.OfType<Border>().ToList();

    private static Task Run(Action<AwarenessTabView, KeywordTriggersPanel, AppSettings> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggers.ToList(), s.CustomTriggers.ToList(), s.ScreenOcrEnabled, s.OcrConfirmationScans,
                     s.OcrHighlightAll, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider,
                     KeywordTriggersPanel.Inform, KeywordTriggersPanel.PickAudioFile);
        var host = new Window();
        try
        {
            s.KeywordTriggers.Clear();
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            var view = new AwarenessTabView();
            host.Content = view;
            host.Show();
            Dispatcher.UIThread.RunJobs();
            body(view, view.FindControl<KeywordTriggersPanel>("KeywordPanel")!, s);
        }
        finally
        {
            host.Close();
            s.KeywordTriggers.Clear(); s.KeywordTriggers.AddRange(saved.Item1);
            s.CustomTriggers.Clear(); s.CustomTriggers.AddRange(saved.Item2);
            (s.ScreenOcrEnabled, s.OcrConfirmationScans, s.OcrHighlightAll) = (saved.Item3, saved.Item4, saved.Item5);
            CoreEntitlement.HasPremiumProvider = saved.Item6;
            CoreEntitlement.IsFreeTodayProvider = saved.Item7;
            KeywordTriggersPanel.Inform = saved.Item8;
            KeywordTriggersPanel.PickAudioFile = saved.Item9;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OcrIntervalEditsPersistMillisecondsAndSyncDoesNotRoundStoredValue() => Run((_, panel, s) =>
    {
        var savedInterval = s.ScreenOcrIntervalMs;
        try
        {
            // WPF MainWindow/MainWindow.KeywordTriggers.cs:111-117: loading is not an edit;
            // user edits display whole seconds and persist milliseconds.
            s.ScreenOcrIntervalMs = 4500;
            panel.SyncFromSettings();
            Assert.Equal(4500, s.ScreenOcrIntervalMs);
            Find<Slider>(panel, "SliderScreenOcrInterval").Value = 7;
            Assert.Equal(7000, s.ScreenOcrIntervalMs);
            Assert.Equal("7s", Find<TextBlock>(panel, "TxtScreenOcrInterval").Text);
            panel.SyncFromSettings();
            Assert.Equal(7000, s.ScreenOcrIntervalMs);
            Find<Slider>(panel, "SliderScreenOcrInterval").Value = 6;
            Assert.Equal(6000, s.ScreenOcrIntervalMs);
            Assert.Equal("6s", Find<TextBlock>(panel, "TxtScreenOcrInterval").Text);
        }
        finally { s.ScreenOcrIntervalMs = savedInterval; }
    });

    [Fact]
    public Task AddEditAndDeleteRowsWriteTheTriggerAndSkipPresetClones() => Run((_, panel, s) =>
    {
        s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:pack:1", Keyword = "pack" });
        panel.SyncFromSettings();
        Assert.Empty(Rows(panel));                                   // preset clones stay with their dialog

        Click(Find<Button>(panel, "BtnAddKeywordTrigger"));
        var t = Assert.Single(s.KeywordTriggers, x => !x.Id.StartsWith("preset:"));
        Assert.Equal((30, 80, KeywordVisualEffect.SubliminalFlash, 10), (t.CooldownSeconds, t.AudioVolume, t.VisualEffect, t.XPAward));
        Assert.NotEmpty(t.Actions);                                  // RebuildActionsFromFlatFields ran
        var row = Assert.Single(Rows(panel));
        Assert.Equal(Loc.Get("kwt_no_audio"), row.GetLogicalDescendants().OfType<TextBlock>().First(b => b.FontSize == 10).Text);

        var box = row.GetLogicalDescendants().OfType<TextBox>().Single();
        box.Text = "good girl";
        Find<Expander>(panel, "KeywordTriggersExpander").IsExpanded = true;
        ((Window)TopLevel.GetTopLevel(panel)!).Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(box.Focus());
        Find<Button>(panel, "BtnAddKeywordTrigger").Focus();          // tab away: LostFocus commits
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("good girl", t.Keyword);

        var checks = row.GetLogicalDescendants().OfType<CheckBox>().ToList();   // enable, haptic, duck
        checks[0].IsChecked = false;
        checks[1].IsChecked = false;
        Assert.False(t.Enabled);
        Assert.False(t.HapticEnabled);
        Assert.DoesNotContain(t.Actions, a => a is HapticAction);

        row.GetLogicalDescendants().OfType<ComboBox>().Single().SelectedIndex = 6;
        Assert.Equal(KeywordVisualEffect.MindWipe, t.VisualEffect);
        var sliders = row.GetLogicalDescendants().OfType<Slider>().ToList();   // cooldown, volume
        sliders[0].Value = 120;
        sliders[1].Value = 40;
        Assert.Equal((120, 40), (t.CooldownSeconds, t.AudioVolume));
        Assert.Contains(row.GetLogicalDescendants().OfType<TextBlock>(), b => b.Text == "120s");

        Click(row.GetLogicalDescendants().OfType<Button>().Single(b => b.Content is IconGlyph { Kind: IconKind.Dismiss }));   // WPF ✖
        Assert.Empty(Rows(panel));
        Assert.Equal("preset:pack:1", Assert.Single(s.KeywordTriggers).Id);
    });

    [Fact]
    public Task ImportSkipsKnownPhrasesAndBrowseSetsTheClip() => Run((_, panel, s) =>
    {
        var told = new List<string>();
        KeywordTriggersPanel.Inform = (_, _, text) => { told.Add(text); return Task.CompletedTask; };
        KeywordTriggersPanel.PickAudioFile = _ => Task.FromResult<string?>("/clips/obey.mp3");
        s.CustomTriggers.Clear();
        s.CustomTriggers.AddRange(new[] { "Obey", "relax", " ", "known" });
        s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "KNOWN" });

        Click(Find<Button>(panel, "BtnImportFromCustomTriggers"));
        Assert.Equal(new[] { "KNOWN", "Obey", "relax" }, s.KeywordTriggers.Select(t => t.Keyword));
        Assert.Equal(Loc.GetF("msg_imported_0_trigger_s_from_your_trigger_mode_l", 2), Assert.Single(told));
        Assert.Equal(3, Rows(panel).Count);

        Click(Find<Button>(panel, "BtnImportFromCustomTriggers"));
        Assert.Equal(3, s.KeywordTriggers.Count);
        Assert.Equal(Loc.Get("msg_no_new_triggers_to_import_all_existing_trigge"), told[1]);

        var browse = Rows(panel)[1].GetLogicalDescendants().OfType<Button>()
            .Single(b => b.Content is TextBlock { Text: var x } && x == Loc.Get("btn_browse"));
        Click(browse);
        Assert.Equal("/clips/obey.mp3", s.KeywordTriggers[1].AudioFilePath);
        Assert.Contains(Rows(panel)[1].GetLogicalDescendants().OfType<TextBlock>(), b => b.Text == "obey.mp3");
    });

    [Fact]
    public Task OcrCombosPersistAndOcrDetailNeedsAccess() => Run((view, panel, s) =>
    {
        var told = new List<string>();
        KeywordTriggersPanel.Inform = (_, title, text) => { told.Add(title + "|" + text); return Task.CompletedTask; };
        var ocr = view.FindControl<CheckBox>("ChkAwarenessOcr")!;
        var detail = Find<StackPanel>(panel, "ScreenOcrIntervalPanel");
        ocr.IsChecked = false;
        ocr.IsChecked = true;                                         // no access: WPF bounces the box back
        Assert.False(ocr.IsChecked == true);
        Assert.False(s.ScreenOcrEnabled);
        Assert.Equal(Loc.Get("title_patreon_feature") + "|" + Loc.Get("msg_screen_ocr_patreon_only"), Assert.Single(told));

        CoreEntitlement.HasPremiumProvider = () => true;
        ocr.IsChecked = true;                                         // master re-syncs the panel (WPF :475)
        Assert.True(s.ScreenOcrEnabled);
        Assert.True(detail.IsVisible);

        CoreEntitlement.HasPremiumProvider = () => false;             // lapsed: detail hides though the flag stays
        panel.SyncFromSettings();
        Assert.False(detail.IsVisible);
        CoreEntitlement.HasPremiumProvider = () => true;

        Find<ComboBox>(panel, "CmbOcrConfirmation").SelectedIndex = 2;
        Find<ComboBox>(panel, "CmbOcrHighlightMode").SelectedIndex = 1;
        Assert.Equal((3, false), (s.OcrConfirmationScans, s.OcrHighlightAll));
        (s.OcrConfirmationScans, s.OcrHighlightAll) = (2, true);       // a stored value seeds the combos
        panel.SyncFromSettings();
        Assert.Equal(1, Find<ComboBox>(panel, "CmbOcrConfirmation").SelectedIndex);
        Assert.Equal(0, Find<ComboBox>(panel, "CmbOcrHighlightMode").SelectedIndex);
        Assert.Equal((2, true), (s.OcrConfirmationScans, s.OcrHighlightAll));   // seeding is not an edit

        ocr.IsChecked = false;
        Assert.False(s.ScreenOcrEnabled);
        Assert.False(detail.IsVisible);
    });
    [Theory]
    [InlineData(0, -1)]
    [InlineData(120, 1)]
    public Task WheelOverScrollableTriggerListMovesOuterViewport(double innerOffset, double delta) => Run((view, panel, s) =>
    {
        // Real user path: the panel inside the Awareness tab, wheel over its trigger list.
        s.KeywordTriggers.AddRange(Enumerable.Range(0, 20).Select(i => new KeywordTrigger { Keyword = $"word {i}" }));
        panel.SyncFromSettings();
        var host = (Window)TopLevel.GetTopLevel(panel)!;
        (host.Width, host.Height) = (900, 500);
        Find<Expander>(panel, "KeywordTriggersExpander").IsExpanded = true;
        Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
        var inner = Find<StackPanel>(panel, "KeywordTriggerListPanel").GetVisualAncestors().OfType<ScrollViewer>().First();
        var outer = inner.GetVisualAncestors().OfType<ScrollViewer>().First();
        Assert.Same(view.GetVisualDescendants().OfType<ScrollViewer>().First(v => panel.GetVisualAncestors().Contains(v)), outer);
        Assert.True(inner.Extent.Height > inner.Viewport.Height + 120);
        var max = outer.Extent.Height - outer.Viewport.Height;
        var target = outer.Offset.Y + inner.TranslatePoint(default, outer)!.Value.Y - 100;
        outer.Offset = new Vector(0, delta < 0 ? Math.Clamp(target, 0, max - 130) : Math.Clamp(target, 130, max));
        inner.Offset = new Vector(0, innerOffset);
        host.UpdateLayout();
        var before = outer.Offset.Y;
        Assert.InRange(before - delta * 120, 0, max);                 // room to move: the assert below is not vacuous
        var point = inner.TranslatePoint(new Point(8, 20), host)!.Value;
        Assert.InRange(point.Y, 0, host.Bounds.Height);
        host.MouseWheel(point, new Vector(0, delta));
        Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
        // WPF PreviewMouseWheel always redirects to the ancestor, even before the list's edge.
        Assert.Equal(before - delta * 120, outer.Offset.Y, 1);
        Assert.Equal(innerOffset, inner.Offset.Y, 1);
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public Task RevealPulsesTwiceAndReclickRestartsThenDetachStops(MotionLevel motion) => Run((view, panel, s) =>
    {
        var saved = (s.MotionLevel, BeatLoop.Time);
        var presets = s.KeywordTriggerPresets.ToList();
        var time = new SteppedTime();
        try
        {
            s.MotionLevel = motion;
            BeatLoop.Time = time;
            s.KeywordTriggerPresets.Clear();
            Click(Find<Button>(view, "LnkAwarenessAdvanced"));
            var glow = panel.FindControl<Border>("KeywordDrawerGlow");
            Assert.NotNull(glow);
            Assert.True(Find<Expander>(panel, "KeywordTriggersExpander").IsExpanded);
            Assert.False(glow.IsHitTestVisible);
            Assert.Null(Find<Expander>(panel, "KeywordTriggersExpander").Effect);
            var beat = Assert.Single(BeatLoop.Running, b => ReferenceEquals(b.Owner, panel));
            Assert.Equal(0, glow.Opacity);
            time.Advance(TimeSpan.FromMilliseconds(110)); BeatLoop.StepAll();
            Assert.Equal(0.45, glow.Opacity, 5);
            time.Advance(TimeSpan.FromMilliseconds(110)); BeatLoop.StepAll();
            Assert.Equal(0.9, glow.Opacity, 5);
            time.Advance(TimeSpan.FromMilliseconds(220)); BeatLoop.StepAll();
            Assert.Equal(0, glow.Opacity, 5);
            time.Advance(TimeSpan.FromMilliseconds(220)); BeatLoop.StepAll();
            Assert.Equal(0.9, glow.Opacity, 5);
            Click(Find<Button>(view, "LnkAwarenessAdvanced"));
            Assert.Equal(0, glow.Opacity);
            time.Advance(TimeSpan.FromMilliseconds(880)); BeatLoop.StepAll();
            Assert.Equal(0, glow.Opacity);
            Assert.False(beat.IsRunning);
            panel.RevealTriggerEditor();
            ((Window)TopLevel.GetTopLevel(panel)!).Content = null;
            Assert.False(beat.IsRunning);
            Assert.Equal(0, glow.Opacity);
        }
        finally
        {
            (s.MotionLevel, BeatLoop.Time) = saved;
            s.KeywordTriggerPresets.Clear(); s.KeywordTriggerPresets.AddRange(presets);
        }
    });

    [Fact]
    public Task RevealUnderMotionOffStillExpandsAndDoesNotStartAClock() => Run((view, panel, s) =>
    {
        var saved = s.MotionLevel;
        var presets = s.KeywordTriggerPresets.ToList();
        try
        {
            s.MotionLevel = MotionLevel.Off;
            s.KeywordTriggerPresets.Clear();
            Click(Find<Button>(view, "LnkAwarenessAdvanced"));
            view.UpdateLayout();
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First(v => panel.GetVisualAncestors().Contains(v));
            // The real tab's ScrollViewer moves; the drawer sits in its last page, so the 24px target clamps to the end
            // (exact 24px is asserted against a taller host in RevealScrollsTheHeaderTo24Pixels...).
            Assert.True(scroll.Offset.Y > 0);
            Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);
            Assert.True(Find<Expander>(panel, "KeywordTriggersExpander").IsExpanded);
            Assert.Equal(0, Find<Border>(panel, "KeywordDrawerGlow").Opacity);
            Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, panel));
        }
        finally
        {
            s.MotionLevel = saved;
            s.KeywordTriggerPresets.Clear(); s.KeywordTriggerPresets.AddRange(presets);
        }
    });

    [Fact]
    public Task RevealScrollsTheHeaderTo24PixelsEvenWhenAlreadyExpanded() => Run((_, _, s) =>
    {
        var saved = s.MotionLevel;
        var panel = new KeywordTriggersPanel();
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 600 });
        content.Children.Add(panel);
        content.Children.Add(new Border { Height = 900 });
        var scroll = new ScrollViewer { Content = content };
        var host = new Window { Width = 900, Height = 500, Content = scroll };
        try
        {
            s.MotionLevel = MotionLevel.Off;
            host.Show(); Dispatcher.UIThread.RunJobs();
            scroll.Offset = new Vector(0, 400);
            host.UpdateLayout();
            panel.RevealTriggerEditor();
            host.UpdateLayout();
            Assert.Equal(576, scroll.Offset.Y, 1);
            Assert.Equal(24, panel.TranslatePoint(default, scroll)!.Value.Y, 1);
            scroll.Offset = new Vector(0, 200); host.UpdateLayout();
            panel.RevealTriggerEditor(); host.UpdateLayout();
            Assert.Equal(576, scroll.Offset.Y, 1);
        }
        finally { host.Close(); s.MotionLevel = saved; }
    });

}

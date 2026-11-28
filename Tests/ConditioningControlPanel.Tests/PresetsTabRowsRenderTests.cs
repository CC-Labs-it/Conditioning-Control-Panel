using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Session Door (Presets tab) off screen, for the 7.4.1 lift (ccp-bugs #1743): seeded preset
/// chips and session rows are built by MainWindow's OWN builders (CreatePresetCard,
/// BuildSessionRackRow, the rack toolbar) on a MainWindow that never ran its constructor, the same
/// trick PremiumPageTests uses, so the stills show what the app would draw. With env
/// CCP_ROWS_DUMP=&lt;dir&gt; it saves session-door-*.png at the real tab size (1447 x 790), at 1200 px,
/// and at 1200 px with the detail panel dragged to its widest (the narrowest the rack gets).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PresetsTabRowsRenderTests
{
    private const double TabHeight = 790;

    [Fact]
    public void The_session_door_keeps_presets_and_sessions_apart_and_dumps_when_asked()
    {
        // Env CCP_ROWS_LANG=<code> renders the stills in that language (RowsRender.InLanguage).
        WpfRenderHarness.OnStaThread(() => RowsRender.InLanguage(null, () =>
        {
            string lang = Environment.GetEnvironmentVariable("CCP_ROWS_LANG") is { Length: > 0 } l ? "-" + l : "";
            foreach (var (name, width, detail) in new[]
            {
                ("session-door-1447", 1447.0, 430.0),
                ("session-door-1200", 1200.0, 430.0),
                ("session-door-1200-narrow-rack", 1200.0, 700.0),
                ("session-door-1200-narrow-detail", 1200.0, 300.0),
            })
            {
                foreach (var (sessionView, reveal) in new[] { (false, false), (true, false), (true, true) })
                {
                    if (reveal && (width != 1200 || detail != 430)) continue;
                    var (view, host) = Paint(width, detail, sessionView, reveal);
                    string still = name + (!sessionView ? "-preset" : reveal ? "-session-revealed" : "-session");
                    RowsRender.Dump(host, still + lang);
                    if (width == 1200 && detail == 430 && !sessionView) RowsRender.Dump(host, still + lang + "-144", 144);
                    Check(view, sessionView, reveal);
                    RowsRender.AssertSilent(still);
                }
            }
            return 0;
        }));
    }

    [Fact]
    public void Import_feeds_the_drop_path()
    {
        // One path for a picked file and a dropped file (#1743): the button's handler ends in
        // HandlePresetDrop, never a second importer.
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.PresetIO.cs"));
        int at = src.IndexOf("internal void BtnImportPreset_Click", StringComparison.Ordinal);
        Assert.True(at > 0, "no BtnImportPreset_Click in MainWindow.PresetIO.cs");
        int end = src.IndexOf("\n        }", at, StringComparison.Ordinal);
        var body = src.Substring(at, end - at);
        Assert.Contains("OpenFileDialog", body);
        Assert.Contains("*.preset.json;*.json", body);
        Assert.Contains("HandlePresetDrop(dialog.FileName)", body);
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static void Check(PresetsTabView view, bool sessionView, bool reveal)
    {
        // Every seeded chip and row made it in (the builders ran against the tab's own styles).
        Assert.True(view.PresetCardsPanel.Children.OfType<Border>().Count(b => b.Tag is string) >= 5);
        Assert.True(view.SessionRackPanel.Children.OfType<Border>().Count() >= 5);

        // The import door (#1743 point 1) sits with the preset actions, and is never gated on a selection.
        Assert.True(view.BtnImportPreset.IsEnabled);
        Assert.Same(view.BtnExportPreset.Parent, view.BtnImportPreset.Parent);

        // Session doors sit with the sessions: Export session under Start, Recent and New session on
        // the Sessions zone's header line, never on the page header or the Takeaway strip.
        Assert.True(IsUnder(view.BtnExportSession, view.SessionButtonsPanel));
        Assert.True(IsUnder(view.BtnCreateSession, view.SessionsZone));
        Assert.True(IsUnder(view.BtnSessionHistory, view.SessionsZone));
        Assert.False(IsUnder(view.BtnExportSession, view.TakeawayZone));

        // Each zone wears its own hue, and the detail rows it opens wear the same one.
        var presetHue = RowHue.GetHue(view.PresetsZone);
        var sessionHue = RowHue.GetHue(view.SessionsZone);
        Assert.NotEqual(Colors.Transparent, presetHue);
        Assert.NotEqual(Colors.Transparent, sessionHue);
        Assert.NotEqual(presetHue, sessionHue);
        Assert.Equal(presetHue, RowHue.GetHue(view.PresetDetailScroller));
        Assert.Equal(sessionHue, RowHue.GetHue(view.SessionDetailScroller));

        // The code-built names wear the zone inks (the RowLabel mix of TextLight and the hue).
        var chipName = RowsRender.Descendants(view.PresetCardsPanel).OfType<TextBlock>()
            .First(t => t.Text == "Desk hours");
        Assert.Equal(16, chipName.FontSize);
        Assert.Equal(view.Resources["SdPresetInk"], chipName.Foreground);
        var rowName = RowsRender.Descendants(view.SessionRackPanel).OfType<ConditioningControlPanel.Helpers.EmojiTextBlock>()
            .First(t => t.Text == "Brainwash");
        Assert.Equal(view.Resources["SdSessionInk"], rowName.Foreground);

        // Readable rows in the detail panel: six label rows a side, each with an emblem and untrimmed.
        var labels = RowsRender.Descendants(sessionView ? view.SessionDetailScroller : view.PresetDetailScroller)
            .OfType<RowLabel>().ToList();
        Assert.Equal(6, labels.Count);
        Assert.All(labels, l => Assert.True(l.Emblem.HasValue, $"'{l.Text}' has no emblem"));
        Assert.All(labels, l => Assert.Equal(sessionView ? sessionHue : presetHue, l.EffectiveHue));
        Assert.All(labels.Where(Shown), l => Assert.Null(RowsRender.Clipped(l)));

        // The session's spoiler rows stay behind the veil until Reveal (a product decision).
        if (sessionView && !reveal) Assert.All(labels, l => Assert.False(Shown(l)));
        if (reveal) Assert.All(labels, l => Assert.True(Shown(l)));
    }

    /// <summary>Visible on screen: the element and every ancestor are Visible (IsVisible needs a window).</summary>
    private static bool Shown(DependencyObject d)
    {
        for (var n = d; n != null; n = VisualTreeHelper.GetParent(n) ?? LogicalTreeHelper.GetParent(n))
            if (n is UIElement u && u.Visibility != Visibility.Visible) return false;
        return true;
    }

    private static bool IsUnder(DependencyObject child, DependencyObject ancestor)
    {
        for (var n = child; n != null; n = LogicalTreeHelper.GetParent(n) ?? VisualTreeHelper.GetParent(n))
            if (ReferenceEquals(n, ancestor)) return true;
        return false;
    }

    /// <summary>The tab with seeded presets and sessions, laid out at <paramref name="width"/> x 790.</summary>
    private static (PresetsTabView View, FrameworkElement Host) Paint(double width, double detailWidth, bool sessionView, bool reveal)
    {
        var view = new PresetsTabView();
        var host = new Grid { Width = width, Height = TabHeight, Background = new SolidColorBrush(RowsRender.PageBack) };
        host.Children.Add(view);

        var mw = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        foreach (var f in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            var t = f.FieldType;
            if (f.GetValue(mw) != null || t.IsValueType || t.IsAbstract || t.IsInterface) continue;
            bool collection = t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t)
                && t.Namespace == "System.Collections.Generic";
            if (!collection) continue;
            try { f.SetValue(mw, Activator.CreateInstance(t)); } catch { /* not needed here */ }
        }
        Field("PresetsTab").SetValue(mw, view);

        // Presets: the built-ins plus two of the player's own, the second one selected.
        var presets = Preset.GetDefaultPresets();
        presets.Add(new Preset { Id = "seed-late", Name = "Late night drift", IsDefault = false, MandatoryVideosEnabled = true, SpiralEnabled = true });
        presets.Add(new Preset { Id = "seed-desk", Name = "Desk hours", IsDefault = false, FlashEnabled = true, SubliminalEnabled = true });
        Field("_selectedPreset").SetValue(mw, presets[^1]);
        var card = Method("CreatePresetCard");
        int at = 0;
        foreach (var p in presets)
            view.PresetCardsPanel.Children.Insert(at++, (UIElement)card.Invoke(mw, new object[] { p })!);

        // Sessions: the built-ins, one of the player's own and one from the catalogue.
        var sessions = Session.GetAllSessions();
        var own = Session.GetAllSessions()[0];
        own.Id = "seed-own"; own.Name = "Brainwash"; own.Source = SessionSource.Custom; own.Description = "";
        sessions.Add(own);
        var cat = Session.GetAllSessions()[1];
        cat.Id = "seed-cat"; cat.Name = "Night shift"; cat.Source = Enum.GetValues<SessionSource>().Last();
        sessions.Add(cat);
        if (sessionView) Field("_selectedSession").SetValue(mw, sessions[1]);
        try { Method("EnsureSessionRackToolbar").Invoke(mw, null); } catch { /* the toolbar needs more of the window */ }
        var row = Method("BuildSessionRackRow");
        foreach (var s in sessions)
            view.SessionRackPanel.Children.Add((UIElement)row.Invoke(mw, new object[] { s })!);
        view.TxtRackCount.Text = $"{sessions.Count}";
        try { Method("UpdateRackToolbarCounts").Invoke(mw, new object[] { sessions, sessions.Count }); } catch { /* counts stay 0 */ }

        // The detail panel as SelectPreset / SelectSession leave it (they need the whole window).
        if (!sessionView)
        {
            var sel = presets[^1];
            view.TxtDetailTitle.Text = sel.Name;
            view.TxtDetailSubtitle.Text = "Your own preset.";
            view.TxtDetailFlash.Text = "On, 2 an hour, 5 at once";
            view.TxtDetailVideo.Text = "Off";
            view.TxtDetailSubliminal.Text = "On, 5 a minute";
            view.TxtDetailAudio.Text = "Master 32%, video 50%";
            view.TxtDetailOverlays.Text = "Spiral off, Pink filter off";
            view.TxtDetailAdvanced.Text = "Bubbles off, Lock cards off";
            view.BtnLoadPreset.IsEnabled = view.BtnSaveOverPreset.IsEnabled = view.BtnDeletePreset.IsEnabled = true;
            view.BtnExportPreset.IsEnabled = view.BtnSharePreset.IsEnabled = true;
        }
        else
        {
            var sel = sessions[1];
            var blurb = sel.GetModeAwareDescription() ?? "";
            view.PresetDetailScroller.Visibility = Visibility.Collapsed;
            view.SessionDetailScroller.Visibility = Visibility.Visible;
            view.PresetButtonsPanel.Visibility = Visibility.Collapsed;
            view.SessionButtonsPanel.Visibility = Visibility.Visible;
            view.TxtDetailTitle.Text = $"{sel.Icon} {sel.GetModeAwareName()}";
            view.TxtDetailSubtitle.Text = blurb.Split('\n')[0];
            view.TxtSessionDuration.Text = $"{sel.DurationMinutes} min";
            view.TxtSessionXP.Text = $"+{sel.BonusXP} XP";
            view.TxtSessionDifficulty.Text = sel.GetDifficultyText();
            view.TxtSessionDescription.Text = blurb;
            view.BtnExportSession.IsEnabled = true;
            if (reveal)
            {
                view.SessionSpoilerPanel.Visibility = Visibility.Visible;
                view.TxtSessionFlash.Text = "On, 4 an hour";
                view.TxtSessionSubliminal.Text = "On, focus words";
                view.TxtSessionAudio.Text = "Whispers low";
                view.TxtSessionOverlays.Text = "Pink filter from minute 10";
                view.TxtSessionExtras.Text = "Corner GIF";
                view.TxtSessionTimeline.Text = "0 to 45 min, slow ramp";
                view.SessionDetailScroller.ScrollToEnd();
            }
        }

        // The splitter's right column, as the player can drag it.
        var main = (Grid)((Grid)view.Content).Children[1];
        main.ColumnDefinitions[2].Width = new GridLength(detailWidth);

        RowsRender.Layout(host, width);
        host.Measure(new Size(width, TabHeight));
        host.Arrange(new Rect(0, 0, width, TabHeight));
        host.UpdateLayout();
        if (reveal)
        {
            view.SessionDetailScroller.ScrollToEnd();
            host.UpdateLayout();
        }
        return (view, host);
    }

    private static FieldInfo Field(string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException("no MainWindow field " + name);

    private static MethodInfo Method(string name) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException("no MainWindow method " + name);
}

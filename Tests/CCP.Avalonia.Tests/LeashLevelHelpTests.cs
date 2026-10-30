using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>sync7-leash-a (WPF a12bb6c53, LeashLevelHelp.cs; WPF LeashLevelGuideTests' two UI facts): what
/// Soft, Standard and Strict allow, under the level switch, with the table one click away. The control is
/// drawn on its own: its WPF hosts (LeashSelfCard, LeashAskCard) are not on this head yet.</summary>
public sealed class LeashLevelHelpTests
{
    private static List<Control> Tagged(ILogical root, string prefix)
    {
        var list = new List<Control>();
        void Walk(ILogical d)
        {
            if (d is Control c && c.Tag is string s && s.StartsWith(prefix, StringComparison.Ordinal)) list.Add(c);
            foreach (var x in d.LogicalChildren) Walk(x);
        }
        Walk(root);
        return list;
    }

    private static string TextOf(ILogical root) =>
        string.Join(" ", root.GetSelfAndLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var was = LocalizationManager.Instance.CurrentLanguage;
        LeashLevelHelp.Open = false;
        try { LocalizationManager.Instance.SetLanguage("en"); body(); }
        finally { LeashLevelHelp.Open = false; LocalizationManager.Instance.SetLanguage(was); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LeashedCard_SaysWhatEachLevelAllows_AndUnfoldsTheTable() => Run(() =>
    {
        var help = new LeashLevelHelp(LeashIntensity.Standard, lines: true);
        var host = new Window { Width = 320, Height = 900, Content = help };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        var lines = Tagged(help, "leash-guide-line:");
        Assert.Equal(new[] { "leash-guide-line:soft", "leash-guide-line:standard:on", "leash-guide-line:strict" }, lines.Select(l => (string)l.Tag!));
        foreach (var level in LeashLevelGuide.Levels)
            Assert.Contains(LeashLevelGuide.Summary(level, Loc.Get), TextOf(lines[(int)level]));
        Assert.DoesNotContain(Loc.Get(LeashLevelGuide.NameKey(PunishKind.Chaster)), TextOf(lines[0]));
        Assert.Equal(Loc.Get(LeashLevelGuide.TaglineKey(LeashIntensity.Strict)), ToolTip.GetTip(lines[2]));

        // The table is one click away, never open by itself.
        Assert.False(help.TableShown);
        var toggle = (Button)Tagged(help, "leash-guide-toggle").Single();
        Assert.Equal(Loc.Get("leash_guide_toggle"), ((TextBlock)toggle.Content!).Text);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(help.TableShown);
        Assert.Equal(LeashLevelGuide.Order.Select(k => "leash-guide-row:" + k.ToString().ToLowerInvariant()),
            Tagged(help, "leash-guide-row:").Select(r => (string)r.Tag!));
        Assert.Equal(3, Tagged(help, "leash-guide-level:").Count);
        Assert.Equal(LeashLevelGuide.AlwaysKeys.Select(k => "leash-guide-rule:" + k), Tagged(help, "leash-guide-rule:").Select(r => (string)r.Tag!));
        var limits = (TextBlock)Tagged(help, "leash-guide-limits").Single();
        Assert.Equal(Loc.GetF(LeashLevelGuide.LimitsKey, LeashLevelGuide.PunishmentsPerDay, LeashLevelGuide.ExpiresAfterHours), limits.Text);
        Assert.Contains(LeashLevelGuide.Detail(LeashLevelGuide.Row(PunishKind.Chaster), Loc.Get), TextOf(help));

        // Picking a level redraws the card: the table stays open and the lit line moves.
        var strict = new LeashLevelHelp(LeashIntensity.Strict, lines: true);
        Assert.True(strict.TableShown);
        Assert.Single(Tagged(strict, "leash-guide-line:strict:on"));
        host.Close();
    });

    [Fact]
    public Task AskCard_SaysThePickedLevelInAFewWords_AndOffersTheSameTable() => Run(() =>
    {
        var help = new LeashLevelHelp(LeashIntensity.Standard, lines: false, maxTable: 180);
        Assert.Empty(Tagged(help, "leash-guide-line:"));
        var tagline = (TextBlock)Tagged(help, "leash-guide-tagline").Single();
        Assert.Equal(Loc.Get(LeashLevelGuide.TaglineKey(LeashIntensity.Standard)), tagline.Text);
        Assert.False(help.TableShown);

        help.Toggle();
        var strict = new LeashLevelHelp(LeashIntensity.Strict, lines: false, maxTable: 180);
        Assert.True(strict.TableShown);
        Assert.Equal(Loc.Get(LeashLevelGuide.TaglineKey(LeashIntensity.Strict)), ((TextBlock)Tagged(strict, "leash-guide-tagline").Single()).Text);
        var table = Tagged(strict, "leash-guide-table").Single();
        Assert.Equal(180, Assert.IsType<ScrollViewer>(table).MaxHeight);
        Assert.Equal(6, Tagged(strict, "leash-guide-row:").Count);
    });
}

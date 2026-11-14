using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

public partial class ChaosHubWindow
{
    // Read at open so tests can opt in without changing the process environment.
    internal static Func<bool> DebugStripEnabled = () =>
        string.Equals(Environment.GetEnvironmentVariable("CCP_CHAOS_DEBUG"), "1", StringComparison.Ordinal);
    private TextBox? _dbgRuns, _dbgLesson, _dbgReveal;
    internal WrapPanel? DebugStrip { get; private set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        BuildDebugStrip();
    }

    private void BuildDebugStrip()
    {
        if (!DebugStripEnabled() || DebugStrip != null) return;
        try
        {
            Height += 44;
            _dollhouseView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var panel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = "DEBUG", Foreground = Brushes.Goldenrod, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _dbgRuns = Box(ChaosMeta.State.RunsCompleted.ToString(), 42); panel.Children.Add(_dbgRuns);
            panel.Children.Add(DebugButton("set runs", () => { if (int.TryParse(_dbgRuns.Text?.Trim(), out var n) && n >= 0) { ChaosMeta.State.RunsCompleted = n; ChaosMeta.Save(); DebugRefresh(); } }));
            panel.Children.Add(DebugButton("+500 sparks", () => { ChaosMeta.State.Sparks += 500; ChaosMeta.Save(); DebugRefresh(); }));
            panel.Children.Add(DebugButton("+500 gold", () => { ChaosMeta.AddGold(500); DebugRefresh(); }));
            _dbgLesson = Box("slow_fuses", 110); panel.Children.Add(_dbgLesson);
            panel.Children.Add(DebugButton("complete lesson", () => {
                var id = _dbgLesson.Text?.Trim();
                if (string.IsNullOrEmpty(id)) return;
                var def = ChaosLessons.ById(id);
                if (def == null) return;
                if (def.HighWater) ChaosLessons.RaiseTo(id, def.Target); else ChaosLessons.Tick(id, def.Target);
                DebugRefresh();
            }));
            _dbgReveal = Box(RevealIds.TabLookingGlass, 110); panel.Children.Add(_dbgReveal);
            panel.Children.Add(DebugButton("force reveal", () => {
                var id = _dbgReveal.Text?.Trim();
                if (string.IsNullOrEmpty(id)) return;
                ChaosMeta.State.SeenReveals.Remove(id); ChaosMeta.State.PendingReveals.Add(id); ChaosMeta.Save(); DebugRefresh();
            }));
            panel.Children.Add(DebugButton("reset meta", () => { ChaosMeta.DebugResetState(); DebugRefresh(); }));
            Grid.SetRow(panel, _dollhouseView.RowDefinitions.Count - 1);
            _dollhouseView.Children.Add(panel);
            DebugStrip = panel;
        }
        catch (Exception ex) { Log.Warning("Chaos debug strip build failed ({E})", ex.Message); }
    }

    private void DebugRefresh()
    {
        try
        {
            RevealService.Sync("debug");
            ApplyExtremeGate(); BuildHabits(); BuildLifetimeBoons(); BuildLoadoutTiles(); BuildBench();
            BuildHerCorner(); BuildMantras(); BuildDiary(); RefreshTopBar(); RefreshStats(); ApplyReveals(); RunRevealFlashes();
            if (_dbgRuns != null) _dbgRuns.Text = ChaosMeta.State.RunsCompleted.ToString();
        }
        catch (Exception ex) { Log.Warning("Chaos debug refresh failed ({E})", ex.Message); }
    }

    private static TextBox Box(string text, double width) => new() { Text = text, Width = width, FontSize = 11, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(4, 2) };
    private static Button DebugButton(string text, Action action)
    {
        var button = new Button { Content = text, FontSize = 11, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 3) };
        button.Click += (_, _) => action();
        return button;
    }
}

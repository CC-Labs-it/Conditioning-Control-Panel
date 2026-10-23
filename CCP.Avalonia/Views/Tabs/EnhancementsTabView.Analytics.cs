using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class EnhancementsTabView
{
    // WPF's header hosts these inside its single 430dip vertical scroller; content is built on first open.
    private static string CurrentAnalyticsSeason => EnhancementAnalytics.CurrentSeasonKey(CoreSettings.Current, DateTime.UtcNow);

    private void AddProSections(StackPanel main)
    {
        AddProSection(main, "ditzy_data_pro", IconKind.DataBarVertical, "label_pro_lifetime_title", BuildProLifetimePanel);
        AddProSection(main, "season_rewind", IconKind.History, "label_pro_rewind_title", BuildSeasonRewindPanel);
        AddProSection(main, "bestie_records", IconKind.Trophy, "label_pro_bestie_title", BuildBestieRecordsPanel);
        AddProSection(main, "brain_drain_report", IconKind.BrainCircuit, "label_pro_braindrain_title", BuildBrainDrainPanel);
    }

    private void AddProSection(StackPanel parent, string skillId, IconKind icon, string titleLocKey, Func<Control> contentFactory)
    {
        if (!SkillTreeRules.HasSkill(CoreSettings.Current, skillId)) return;

        var gold = Color.FromRgb(255, 200, 80);

        var button = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(60, 50, 30)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Name = "ProToggle_" + skillId,
            Focusable = true,
            Cursor = new Cursor(StandardCursorType.Hand),
            BorderBrush = new SolidColorBrush(gold),
            BorderThickness = new Thickness(1)
        };
        var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var glyph = Glyph(icon, IconVariant.Regular, null, 12);
        glyph.Margin = new Thickness(0, 0, 4, 0);
        buttonStack.Children.Add(glyph);
        buttonStack.Children.Add(new TextBlock
        {
            Text = Loc.Get(titleLocKey),
            Foreground = new SolidColorBrush(gold),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var chevron = Glyph(IconKind.ChevronDown, IconVariant.Regular, Rgb(176, 176, 176), 10);
        chevron.Margin = new Thickness(4, 0, 0, 0);
        buttonStack.Children.Add(chevron);
        button.Child = buttonStack;

        // No inner scroller/height cap: the panel expands fully and the header's own
        // vertical ScrollViewer (see CreateSkillTreeHeader) handles the overflow —
        // one scrollbar for the whole column.
        var panel = new Border
        {
            Name = "ProPanel_" + skillId,
            Background = new SolidColorBrush(Color.FromRgb(22, 22, 42)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 15),
            IsVisible = false
        };

        var built = false;
        void Toggle()
        {
            if (!built)
            {
                built = true;
                try
                {
                    panel.Child = contentFactory();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "PRO panel build failed: {Skill}", skillId);
                    panel.Child = new TextBlock { Text = "—", Foreground = Brushes.Gray };
                }
            }
            panel.IsVisible = !panel.IsVisible;
            chevron.Kind = panel.IsVisible ? IconKind.ChevronUp : IconKind.ChevronDown;
        }
        button.PointerPressed += (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { Toggle(); e.Handled = true; } };
        button.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Toggle(); e.Handled = true; } };

        parent.Children.Add(button);
        parent.Children.Add(panel);
    }

    /// <summary>WPF's up/down-triangle season delta (MainWindow.Enhancements.cs), as a Fluent arrow.</summary>
    private static Control TrendArrow(IconKind kind, Color color)
    {
        var g = Glyph(kind, IconVariant.Regular, new SolidColorBrush(color), 9);
        g.Margin = new Thickness(3, 0, 0, 0);
        return g;
    }

    /// <summary>Minutes → "H.h h" / "M m" using the existing time loc units.</summary>
    private static string FormatProMinutes(double minutes) => Services.EnhancementAnalytics.FormatMinutes(minutes);

    private static TextBlock ProLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
        FontSize = 9
    };

    private static TextBlock ProValue(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
        FontSize = 10,
        FontWeight = FontWeight.Bold
    };

    // ---- chart primitives (single-series, app accent colors, ink-colored text) ----

    private static readonly Color ChartInkDim = Color.FromRgb(140, 140, 140);
    private static readonly Color ChartInk = Color.FromRgb(230, 230, 235);
    private static readonly Color ChartBaseline = Color.FromRgb(58, 58, 85);
    private static readonly Color ChartPink = Color.FromRgb(255, 105, 180);
    private static readonly Color ChartGold = Color.FromRgb(255, 200, 80);
    private static readonly Color ChartSurface = Color.FromRgb(22, 22, 42);   // panel bg — used as mark ring
    private static readonly Color ChartCellIdle = Color.FromRgb(38, 38, 64);

    /// <summary>
    /// All seasons with data, oldest → newest (snapshots from disk + the live bucket),
    /// capped to the most recent <paramref name="cap"/>.
    /// </summary>
    private List<(string Key, double Minutes, int Level, List<string> ActiveDays)> GetSeasonSeriesAscending(int cap = 12) =>
        CoreSettings.Current is { } s ? Services.EnhancementAnalytics.Series(s, CurrentAnalyticsSeason,
            Services.EnhancementAnalytics.ReadSnapshots(), cap) : new();

    private static string SeasonMonthLabel(string seasonKey) => Services.EnhancementAnalytics.MonthLabel(seasonKey);

    /// <summary>
    /// Minutes-per-season bar chart: thin pink bars, rounded data ends, 2px gaps,
    /// month labels in muted ink, values only on the max and newest bars, tooltip per bar.
    /// </summary>
    private Control BuildSeasonBarChart(List<(string Key, double Minutes, int Level, List<string> ActiveDays)> series)
    {
        const double plotH = 78, barW = 20, gap = 10, labelH = 14, valueH = 13;
        var maxVal = Math.Max(1.0, series.Max(s => s.Minutes));
        var maxIdx = series.FindIndex(s => s.Minutes == series.Max(x => x.Minutes));

        var canvas = new Canvas
        {
            Height = valueH + plotH + labelH,
            Width = series.Count * (barW + gap) - gap,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // recessive baseline
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(0, valueH + plotH), EndPoint = new Point(canvas.Width, valueH + plotH),
            Stroke = new SolidColorBrush(ChartBaseline), StrokeThickness = 1
        });

        for (int i = 0; i < series.Count; i++)
        {
            var s = series[i];
            var h = Math.Max(2, plotH * s.Minutes / maxVal);
            var x = i * (barW + gap);

            var bar = new Border
            {
                Width = barW,
                Height = h,
                Background = new SolidColorBrush(ChartPink),
                CornerRadius = new CornerRadius(3, 3, 0, 0),
            };
            ToolTip.SetTip(bar, $"{s.Key} · {FormatProMinutes(s.Minutes)}");
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, valueH + plotH - h);
            canvas.Children.Add(bar);

            // selective direct labels: max bar + newest bar only, in ink (not series color)
            if (i == maxIdx || i == series.Count - 1)
            {
                var value = new TextBlock
                {
                    Text = FormatProMinutes(s.Minutes),
                    Foreground = new SolidColorBrush(ChartInk),
                    FontSize = 8.5,
                    FontWeight = FontWeight.SemiBold
                };
                value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(value, x + barW / 2 - value.DesiredSize.Width / 2);
                Canvas.SetTop(value, valueH + plotH - h - 12);
                canvas.Children.Add(value);
            }

            var month = new TextBlock
            {
                Text = SeasonMonthLabel(s.Key),
                Foreground = new SolidColorBrush(ChartInkDim),
                FontSize = 8.5
            };
            month.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(month, x + barW / 2 - month.DesiredSize.Width / 2);
            Canvas.SetTop(month, valueH + plotH + 2);
            canvas.Children.Add(month);
        }

        return canvas;
    }

    /// <summary>
    /// Peak-level-per-season sparkline: 2px gold line, 8px dots with a 2px surface ring,
    /// only the newest point carries a value label, tooltip per dot.
    /// </summary>
    private Control BuildLevelSparkline(List<(string Key, double Minutes, int Level, List<string> ActiveDays)> series)
    {
        const double plotH = 52, stepW = 30, padTop = 14, labelH = 14, dotR = 4;
        var pts = series.Where(s => s.Level > 0).ToList();
        if (pts.Count == 0)
            return new TextBlock { Text = "—", Foreground = new SolidColorBrush(ChartInkDim), FontSize = 9 };

        var maxLevel = Math.Max(1, pts.Max(p => p.Level));
        var canvas = new Canvas
        {
            Height = padTop + plotH + labelH,
            Width = Math.Max(stepW, (pts.Count - 1) * stepW) + dotR * 2 + 2,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        double X(int i) => dotR + 1 + i * stepW;
        double Y(int level) => padTop + plotH - plotH * level / maxLevel;

        if (pts.Count > 1)
        {
            var line = new Polyline
            {
                Stroke = new SolidColorBrush(ChartGold),
                StrokeThickness = 2,
                StrokeJoin = PenLineJoin.Round
            };
            for (int i = 0; i < pts.Count; i++)
                line.Points.Add(new Point(X(i), Y(pts[i].Level)));
            canvas.Children.Add(line);
        }

        for (int i = 0; i < pts.Count; i++)
        {
            var dot = new Ellipse
            {
                Width = dotR * 2, Height = dotR * 2,
                Fill = new SolidColorBrush(ChartGold),
                Stroke = new SolidColorBrush(ChartSurface),
                StrokeThickness = 2,
            };
            ToolTip.SetTip(dot, $"{pts[i].Key} · {Loc.Get("label_rewind_level")} {pts[i].Level}");
            Canvas.SetLeft(dot, X(i) - dotR);
            Canvas.SetTop(dot, Y(pts[i].Level) - dotR);
            canvas.Children.Add(dot);

            var month = new TextBlock
            {
                Text = SeasonMonthLabel(pts[i].Key),
                Foreground = new SolidColorBrush(ChartInkDim),
                FontSize = 8.5
            };
            month.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(month, X(i) - month.DesiredSize.Width / 2);
            Canvas.SetTop(month, padTop + plotH + 2);
            canvas.Children.Add(month);

            if (i == pts.Count - 1)
            {
                var value = new TextBlock
                {
                    Text = pts[i].Level.ToString(),
                    Foreground = new SolidColorBrush(ChartInk),
                    FontSize = 8.5,
                    FontWeight = FontWeight.SemiBold
                };
                value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(value, X(i) - value.DesiredSize.Width / 2);
                Canvas.SetTop(value, Y(pts[i].Level) - dotR - 13);
                canvas.Children.Add(value);
            }
        }

        return canvas;
    }

    /// <summary>
    /// GitHub-style activity heatmap: one mini-grid per month (weeks as columns,
    /// Mon–Sun as rows), pink = active day, dim = inactive, tooltip per cell.
    /// Months come from the live bucket + snapshot active_days (schema 2+).
    /// </summary>
    private Control BuildActivityHeatmap()
    {
        const double cell = 11, cellGap = 2;
        // Months without per-day data (schema-1 snapshots only stored the count) are
        // skipped — an all-dim grid would read as "zero activity" when it wasn't.
        var months = GetSeasonSeriesAscending(6)
            .Where(s => s.ActiveDays.Count > 0 && Models.SeasonNumbering.TryParse(s.Key, out _, out _))
            .ToList();
        if (months.Count == 0)
            return new TextBlock { Text = "—", Foreground = new SolidColorBrush(ChartInkDim), FontSize = 9 };

        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var m in months)
        {
            Models.SeasonNumbering.TryParse(m.Key, out var year, out var month);
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var active = new HashSet<string>(m.ActiveDays);
            // Monday-first row index; weeks advance by column (GitHub layout)
            var firstDow = ((int)new DateTime(year, month, 1).DayOfWeek + 6) % 7;
            var weeks = (int)Math.Ceiling((firstDow + daysInMonth) / 7.0);

            var monthCanvas = new Canvas
            {
                Width = weeks * (cell + cellGap) - cellGap,
                Height = 7 * (cell + cellGap) - cellGap
            };
            for (int day = 1; day <= daysInMonth; day++)
            {
                var slot = firstDow + day - 1;
                var col = slot / 7;
                var row = slot % 7;
                var dateStr = $"{year:D4}-{month:D2}-{day:D2}";
                var isActive = active.Contains(dateStr);
                var box = new Border
                {
                    Width = cell, Height = cell,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(isActive ? ChartPink : ChartCellIdle),
                    };
                ToolTip.SetTip(box, dateStr);
                Canvas.SetLeft(box, col * (cell + cellGap));
                Canvas.SetTop(box, row * (cell + cellGap));
                monthCanvas.Children.Add(box);
            }

            var monthStack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            monthStack.Children.Add(monthCanvas);
            monthStack.Children.Add(new TextBlock
            {
                Text = SeasonMonthLabel(m.Key),
                Foreground = new SolidColorBrush(ChartInkDim),
                FontSize = 8.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            strip.Children.Add(monthStack);
        }

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = strip
        };
        return scroller;
    }

    /// <summary>Muted section caption used above each PRO chart.</summary>
    private static TextBlock ProChartTitle(string locKey) => new()
    {
        Text = Loc.Get(locKey),
        Foreground = new SolidColorBrush(ChartInkDim),
        FontSize = 9,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 0, 0, 4)
    };

    /// <summary>PRO Lifetime Dashboard — every all-time counter the app tracks, 3-column grid.</summary>
    private Control BuildProLifetimePanel()
    {
        var progress = App.Achievements?.Progress;
        var settings = CoreSettings.Current;
        var stack = new StackPanel();
        if (progress == null || settings == null) return stack;

        // Activity heatmap calendar (months from live bucket + snapshot active_days)
        stack.Children.Add(ProChartTitle("label_heatmap_title"));
        stack.Children.Add(BuildActivityHeatmap());
        stack.Children.Add(new Border { Height = 10 });

        var grid = new Grid();
        for (int c = 0; c < 3; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int row = -1, col = 0;
        void Add(string labelKey, string value)
        {
            if (col == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row++;
            }
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            cell.Children.Add(ProLabel(Loc.Get(labelKey)));
            cell.Children.Add(ProValue(value));
            Grid.SetColumn(cell, col);
            Grid.SetRow(cell, row);
            grid.Children.Add(cell);
            col = (col + 1) % 3;
        }

        Add("label_pro_total_time", FormatProMinutes(settings.TotalConditioningMinutes));
        Add("label_total_xp_earned_stat", progress.TotalXPEarned.ToString("N0"));
        Add("label_skill_points_earned", progress.TotalSkillPointsEarned.ToString("N0"));

        Add("label_pro_points_spent", progress.LifetimeSkillPointsSpent.ToString("N0"));
        Add("label_pro_highest_level", settings.HighestLevelEver.ToString("N0"));
        Add("label_pro_highest_streak", settings.HighestStreak.ToString("N0"));

        Add("label_pro_chaos_rounds", progress.EnhancementsPlayed.ToString("N0"));
        Add("label_pro_deeper_time", FormatProMinutes(progress.DeeperMinutes));
        Add("label_pro_quizzes", progress.QuizzesPassed.ToString("N0"));

        Add("label_pro_keyword_triggers", progress.KeywordTriggersFired.ToString("N0"));
        Add("label_pro_blinks", progress.BlinkTrainerBlinks.ToString("N0"));
        Add("label_pro_gaze_pops", progress.GazePops.ToString("N0"));

        Add("label_pro_fastest_lock", progress.FastestLockCardSeconds < double.MaxValue
            ? $"{progress.FastestLockCardSeconds:F1}s" : "—");
        Add("label_pro_night_uses", settings.NightTimeUsageCount.ToString("N0"));
        Add("label_pro_morning_uses", settings.EarlyMorningUsageCount.ToString("N0"));

        stack.Children.Add(grid);
        return stack;
    }

    /// <summary>
    /// Season Rewind — one row per season (live bucket first, then disk snapshots, newest
    /// first). The time column carries an up/down delta vs the season before it. The Spent
    /// column appears once certified_data_bimbo is owned (its capstone perk).
    /// </summary>
    private Control BuildSeasonRewindPanel()
    {
        var settings = CoreSettings.Current;
        var stack = new StackPanel();
        if (settings == null) return stack;

        // Trend charts above the table (one measure per chart — never dual-axis)
        var chartSeries = GetSeasonSeriesAscending(10);
        if (chartSeries.Count > 0)
        {
            stack.Children.Add(ProChartTitle("label_chart_time_per_season"));
            stack.Children.Add(BuildSeasonBarChart(chartSeries));
            stack.Children.Add(new Border { Height = 8 });
            if (chartSeries.Any(s => s.Level > 0))
            {
                stack.Children.Add(ProChartTitle("label_chart_level_per_season"));
                stack.Children.Add(BuildLevelSparkline(chartSeries));
                stack.Children.Add(new Border { Height = 8 });
            }
        }

        var showSpent = SkillTreeRules.HasSkill(settings, "certified_data_bimbo");

        var rows = Services.EnhancementAnalytics.Rewind(settings, CurrentAnalyticsSeason,
            Services.EnhancementAnalytics.ReadSnapshots(8));

        var colCount = showSpent ? 8 : 7;
        var grid = new Grid();
        for (int c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void AddCell(Control el, int r, int c)
        {
            Grid.SetRow(el, r);
            Grid.SetColumn(el, c);
            grid.Children.Add(el);
        }

        // Header row
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headers = new List<string> {
            "label_rewind_season", "label_rewind_time", "label_rewind_sessions", "label_rewind_days",
            "label_rewind_streak", "label_rewind_top", "label_rewind_level" };
        if (showSpent) headers.Add("label_rewind_spent");
        for (int c = 0; c < headers.Count; c++)
            AddCell(ProLabel(Loc.Get(headers[c])), 0, c);

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var gridRow = i + 1;

            var seasonText = ProValue(r.Key + (r.IsLive ? " •" : ""));
            if (r.IsLive) seasonText.Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 80));
            AddCell(seasonText, gridRow, 0);

            // Time with delta vs the season before it (next row down = older)
            var timeStack = new StackPanel { Orientation = Orientation.Horizontal };
            timeStack.Children.Add(ProValue(FormatProMinutes(r.Minutes)));
            if (i + 1 < rows.Count)
            {
                var older = rows[i + 1];
                if (r.Minutes > older.Minutes)
                    timeStack.Children.Add(TrendArrow(IconKind.ArrowUp, Color.FromRgb(100, 255, 150)));
                else if (r.Minutes < older.Minutes)
                    timeStack.Children.Add(TrendArrow(IconKind.ArrowDown, Color.FromRgb(255, 110, 110)));
            }
            AddCell(timeStack, gridRow, 1);

            AddCell(ProValue(r.Sessions.ToString("N0")), gridRow, 2);
            AddCell(ProValue(r.Days.ToString("N0")), gridRow, 3);
            AddCell(ProValue(r.Streak.ToString("N0")), gridRow, 4);
            AddCell(ProValue(r.Pct > 0 ? $"{r.Pct}%" : "—"), gridRow, 5);
            AddCell(ProValue(r.HasV2 && r.Level > 0 ? r.Level.ToString("N0") : "—"), gridRow, 6);
            if (showSpent)
                AddCell(ProValue(r.HasV2 ? r.Spent.ToString("N0") : "—"), gridRow, 7);
        }

        stack.Children.Add(grid);

        if (rows.Count <= 1)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Loc.Get("label_rewind_empty"),
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                FontSize = 9,
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        return stack;
    }

    /// <summary>Bestie Records — all-time personal bests, with the season they happened where known.</summary>
    private Control BuildBestieRecordsPanel()
    {
        var progress = App.Achievements?.Progress;
        var settings = CoreSettings.Current;
        var stack = new StackPanel();
        if (progress == null || settings == null) return stack;

        void AddRecord(string labelKey, string value, string? when = null)
        {
            var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = ProLabel(Loc.Get(labelKey));
            label.VerticalAlignment = VerticalAlignment.Center;
            rowGrid.Children.Add(label);
            var valueStack = new StackPanel { Orientation = Orientation.Horizontal };
            valueStack.Children.Add(ProValue(value));
            if (!string.IsNullOrEmpty(when))
                valueStack.Children.Add(new TextBlock
                {
                    Text = $"  {when}",
                    Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Bottom
                });
            Grid.SetColumn(valueStack, 1);
            rowGrid.Children.Add(valueStack);
            stack.Children.Add(rowGrid);
        }

        // Straight all-time records
        AddRecord("label_longest_session", $"{progress.LongestSessionMinutes:F1} {Loc.Get("label_min_abbrev")}");
        AddRecord("label_bc_best_streak", progress.BubbleCountBestStreak.ToString("N0"));
        AddRecord("label_pro_fastest_lock", progress.FastestLockCardSeconds < double.MaxValue
            ? $"{progress.FastestLockCardSeconds:F1}s" : "—");
        AddRecord("label_pro_highest_streak", settings.HighestStreak.ToString("N0"));
        AddRecord("label_pro_highest_level", settings.HighestLevelEver.ToString("N0"));

        // Season-scoped records mined from snapshots + the live bucket
        var seasons = Services.EnhancementAnalytics.Records(settings, CurrentAnalyticsSeason,
            Services.EnhancementAnalytics.ReadSnapshots());

        var bestTime = Services.EnhancementAnalytics.BestTime(seasons);
        if (bestTime.Minutes > 0)
            AddRecord("label_pro_best_season_time", FormatProMinutes(bestTime.Minutes), bestTime.Key);

        if (Services.EnhancementAnalytics.BestRank(seasons) is { } ranked)
            AddRecord("label_pro_best_rank", $"#{ranked.Rank}", ranked.Key);
        if (Services.EnhancementAnalytics.BestLevel(seasons) is { } levelled)
            AddRecord("label_pro_best_season_level", levelled.Level.ToString("N0"), levelled.Key);

        return stack;
    }

    /// <summary>
    /// Brain Drain Report — per-feature engagement, this season and lifetime
    /// (lifetime = live bucket + every snapshot on disk), with proportional bars.
    /// </summary>
    private Control BuildBrainDrainPanel()
    {
        var settings = CoreSettings.Current;
        var stack = new StackPanel();
        if (settings == null) return stack;

        var lifetime = Services.EnhancementAnalytics.LifetimeFeatures(settings, Services.EnhancementAnalytics.ReadSnapshots());

        var maxLifetime = Math.Max(1, lifetime.Count > 0 ? lifetime.Values.Max() : 1);
        var accentLight = (Color)Color.Parse(CoreMods.Service?.GetAccentLightColorHex() ?? "#FFB6C1");

        // Column headers
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        var seasonHeader = ProLabel(Loc.Get("label_braindrain_season_lifetime"));
        Grid.SetColumn(seasonHeader, 1);
        headerGrid.Children.Add(ProLabel(Loc.Get("recap_badges_title")));
        headerGrid.Children.Add(seasonHeader);
        stack.Children.Add(headerGrid);

        foreach (var def in Services.EnhancementAnalytics.OrderedFeatures(lifetime))
        {
            lifetime.TryGetValue(def.Key, out var life);
            settings.SeasonFeatureUse.TryGetValue(def.Key, out var season);

            var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var left = new StackPanel();
            left.Children.Add(ProLabel(Loc.Get(def.LabelLocKey)));
            left.Children.Add(new Border
            {
                Height = 5,
                Width = 8 + 160.0 * life / maxLifetime,
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(accentLight),
                Opacity = life > 0 ? 0.9 : 0.25,
                Margin = new Thickness(0, 2, 0, 0)
            });
            rowGrid.Children.Add(left);

            var counts = ProValue($"{season:N0} / {life:N0}");
            counts.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(counts, 1);
            rowGrid.Children.Add(counts);

            stack.Children.Add(rowGrid);
        }

        return stack;
    }
}

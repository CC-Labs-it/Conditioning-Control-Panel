// Adapted from parity/trunk 7b6f94800e90a90fa443fb47efcfaeedc17eff0e and WPF MainWindow.QuestStamps.cs.
// ponytail: finite stamp animations and popup shadow remain in wire-shell-quest-stamps-b.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;
namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {

        internal const double QuestStampDailySize = 20;
        internal const double QuestStampWeeklySize = 23;
        internal static readonly double[] QuestStampOffsets = { 0, 16, 32, 48 };
        internal static readonly double[] QuestStampAngles = { -4, 3, -2, 5 };
        private const double QuestStampFillInset = 2.5;
        private const double QuestStampFillHeight = 2.5;
        internal const double QuestStampHoverScale = 1.5;
        private const double QuestStampPopWidth = 300;
        private const double QuestStampPopTrackWidth = QuestStampPopWidth - 28;
        private static IconKind StampIcon(string? value) =>
            IconMap.TryGet(value, out var entry) ? entry.Kind : IconKind.Sparkle;
        private static IBrush StampInk(uint argb) => new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromUInt32(argb));
        private static readonly IBrush QuestStampGoldStroke = StampInk(0xCCFFD700);
        private static readonly IBrush QuestStampGoldFill = StampInk(0x30FFD700);
        private static readonly IBrush QuestStampGoldInk = StampInk(0xFFFFD700);
        private static readonly IBrush QuestStampGoldWash = StampInk(0x888A6D00);
        private static readonly IBrush QuestStampPinkStroke = StampInk(0xFFFF69B4);
        private static readonly IBrush QuestStampPanelFill = StampInk(0xFF252542);
        private static readonly IBrush QuestStampDarkFill = StampInk(0xFF1A1A2E);
        private static readonly IBrush QuestStampPurpleStroke = StampInk(0xFFB57EDC);
        private static readonly IBrush QuestStampPurpleFill = StampInk(0xFF2A1F3D);
        private static readonly IBrush QuestStampGhostStroke = StampInk(0xFF3D3D60);
        private static readonly IBrush QuestStampGhostFill = StampInk(0x221A1A2E);
        private static readonly IBrush QuestStampArtScrim = StampInk(0x2E0A0A18);
        private static readonly IBrush QuestStampDoneInk = StampInk(0xFF00E676);
        private static readonly IBrush QuestStampMutedInk = StampInk(0xFFA0A0C0);
        private static readonly IBrush QuestStampWhiteInk = StampInk(0xFFFFFFFF);
        private sealed class QuestStampInfo
        {
            public string Key = "";              // "d0".."d2", "w"
            public string Label = "";
            public bool IsWeekly;
            public QuestDefinition? Def;
            public ActiveQuest? Quest;
            public bool Completed;
            public double Fraction;
            public Panel? Cell;
            public ScaleTransform? Scale;
            public RotateTransform? Rotate;
        }
        private readonly Dictionary<string, QuestStampInfo> _stampInfos = new(StringComparer.Ordinal);
        private string? _hoveredStampKey;
        private bool _questStampsWired, _questStampsClosed;
        private QuestService? _questStampsService;
        private readonly ScaleTransform _stampHostScale = new(1, 1);
        private Popup? _stampPopup;
        private Border? _stampPopCard;
        private TextBlock? _stampPopKind, _stampPopXp, _stampPopName, _stampPopDesc, _stampPopProgress, _stampPopRemaining;
        private IconGlyph? _stampPopIcon;
        private Image? _stampPopArt;
        private Border? _stampPopDone;
        private static readonly IBrush QuestStampChipFill = StampInk(0xB01A1A2E);
        private Border? _stampPopFill;

        internal void DropArtCache()
        {
            if (_questStampsClosed) return;
            try
            {
                if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(DropArtCache); return; }
                QuestsTabView.ClearQuestArtCache();
                HideStampPopup();
                RefreshQuestStamps();
                if (Named<QuestsTabView>("QuestsTab") is { IsVisible: true } questsTab) questsTab.RefreshQuestUI();
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] mod switch: {E}", ex.Message); }
        }

        internal void InitializeQuestStamps()
        {
            try
            {
                if (!_questStampsWired)
                {
                    _questStampsWired = true;
                    if (Named<Grid>("QuestStampHost") is { } host) host.RenderTransform = _stampHostScale;
                    LocalizationManager.Instance.LanguageChanged += OnQuestStampsLanguageChanged;
                    EventHandler<ConditioningControlPanel.Models.ModPackage> modChanged = (_, _) => DropArtCache();
                    CoreMods.ModChanged += modChanged;
                    Closed += (_, _) => CoreMods.ModChanged -= modChanged;
                    Opened += (_, _) => InitializeQuestStamps();
                    Activated += (_, _) => { if (!ReferenceEquals(_questStampsService, App.Quests)) InitializeQuestStamps(); };
                    Deactivated += (_, _) => HideStampPopup();
                    PositionChanged += (_, _) => HideStampPopup();
                    Closed += (_, _) =>
                    {
                        _questStampsClosed = true;
                        try
                        {
                            UnhookQuestStampService();
                            LocalizationManager.Instance.LanguageChanged -= OnQuestStampsLanguageChanged;
                            HideStampPopup();
                        }
                        catch { /* teardown */ }
                    };
                }
                if (!ReferenceEquals(_questStampsService, App.Quests))
                {
                    UnhookQuestStampService();
                    _questStampsService = App.Quests;
                    if (_questStampsService is { } q)
                    {
                        q.QuestCompleted += OnQuestStampsQuestCompleted;
                        q.QuestProgressChanged += OnQuestStampsProgressChanged;
                        q.QuestsRefreshed += OnQuestStampsRefreshed;
                    }
                }
                RefreshQuestStamps();
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] cluster could not be wired: {E}", ex.Message); }
        }

        private void UnhookQuestStampService()
        {
            if (_questStampsService is not { } q) return;
            q.QuestCompleted -= OnQuestStampsQuestCompleted;
            q.QuestProgressChanged -= OnQuestStampsProgressChanged;
            q.QuestsRefreshed -= OnQuestStampsRefreshed;
            _questStampsService = null;
        }

        private void OnQuestStampsQuestCompleted(object? sender, QuestCompletedEventArgs e) => QueueQuestStampRepaint();

        private void OnQuestStampsProgressChanged(object? sender, QuestProgressEventArgs e) => QueueQuestStampRepaint();

        private void OnQuestStampsRefreshed(object? sender, EventArgs e) => QueueQuestStampRepaint();

        private void OnQuestStampsLanguageChanged(object? sender, EventArgs e) => QueueQuestStampRepaint();

        private void QueueQuestStampRepaint()
        {
            try { Dispatcher.UIThread.Post(() => { if (!_questStampsClosed) RefreshQuestStamps(); }); }
            catch { /* fire-and-forget */ }
        }

        internal void RefreshQuestStamps()
        {
            if (_questStampsClosed || Named<Grid>("QuestStampHost") is not { } host) return;
            try
            {
                var hovered = _hoveredStampKey;
                _stampInfos.Clear();
                foreach (var old in host.Children.Where(c => c is not Popup).ToArray()) host.Children.Remove(old);
                _hoveredStampKey = hovered;
                var quests = _questStampsService ?? App.Quests;
                if (quests == null && !RenderProof.Rendering)
                {
                    HideStampPopup();
                    host.IsVisible = false;
                    return;
                }
                host.IsVisible = true;
                var board = quests?.GetDailySlots() ?? QuestDefinition.DailyQuests.Take(3)
                    .Select(d => ((ActiveQuest?)new ActiveQuest(d.Id), (QuestDefinition?)d)).ToList();
                for (int slot = 0; slot < 3; slot++)
                {
                    var quest = slot < board.Count ? board[slot].Quest : null;
                    var def = slot < board.Count ? board[slot].Definition : null;
                    AddStamp(host, new QuestStampInfo
                    {
                        Key = "d" + slot,
                        Label = $"{Loc.Get("quest_daily")} {slot + 1}",
                        Def = def,
                        Quest = quest,
                        Completed = quest?.IsCompleted == true,
                        Fraction = quest != null && def != null ? QuestStampFraction(quest.CurrentProgress, def.TargetValue) : 0,
                    }, QuestStampDailySize, QuestStampOffsets[slot], QuestStampAngles[slot]);
                }
                var weeklyDef = quests?.GetCurrentWeeklyDefinition() ?? (RenderProof.Rendering ? QuestDefinition.WeeklyQuests[0] : null);
                var weeklyActive = quests?.Progress?.WeeklyQuest ?? (RenderProof.Rendering ? new ActiveQuest(weeklyDef!.Id) : null);
                AddStamp(host, new QuestStampInfo
                {
                    Key = "w",
                    Label = Loc.Get("quest_weekly"),
                    IsWeekly = true,
                    Def = weeklyDef,
                    Quest = weeklyActive,
                    Completed = weeklyActive?.IsCompleted == true,
                    Fraction = weeklyActive != null && weeklyDef != null ? QuestStampFraction(weeklyActive.CurrentProgress, weeklyDef.TargetValue) : 0,
                }, QuestStampWeeklySize, QuestStampOffsets[3], QuestStampAngles[3]);
                RestoreStampHover();
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] repaint failed: {E}", ex.Message); }
        }

        private void AddStamp(Panel host, QuestStampInfo info, double size, double left, double angle)
        {
            var cell = BuildQuestStamp(info, size, left, angle);
            info.Cell = cell;
            _stampInfos[info.Key] = info;
            host.Children.Add(cell);
        }

        internal static double QuestStampFraction(int current, int target) =>
            QuestStampRules.Fraction(current, target);

        private Panel BuildQuestStamp(QuestStampInfo info, double size, double left, double angle)
        {
            bool ghost = info.Def == null || info.Quest == null;
            bool done = info.Completed;
            IBrush stroke = ghost ? QuestStampGhostStroke : done ? QuestStampGoldStroke : info.IsWeekly ? QuestStampPurpleStroke : QuestStampPinkStroke;
            IBrush plainFill = ghost ? QuestStampGhostFill : done ? QuestStampGoldFill : info.IsWeekly ? QuestStampPurpleFill : QuestStampPanelFill;
            var art = ghost ? null : QuestsTabView.GetQuestArt(info.Def!);
            info.Scale = new ScaleTransform(1, 1);
            info.Rotate = new RotateTransform(angle);
            var cell = new Panel
            {
                Width = size,
                Height = size,
                Opacity = ghost ? 0.5 : done ? 0.85 : 1.0,
                Margin = new Thickness(left, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new TransformGroup { Children = { info.Scale, info.Rotate } },
                Background = Brushes.Transparent,   // the plate's own gaps still hover
                Tag = info.Key,
            };
            var plate = new Rectangle { RadiusX = 3, RadiusY = 3, Fill = plainFill, Stroke = stroke, StrokeThickness = 1.5 };
            if (ghost)
            {
                plate.StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 2, 1.6 };
                plate.StrokeThickness = 1.0;
            }
            else if (art != null)
            {
                plate.Fill = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            }
            cell.Children.Add(plate);
            if (art != null)
                cell.Children.Add(new Rectangle { RadiusX = 3, RadiusY = 3, Fill = done ? QuestStampGoldWash : QuestStampArtScrim, IsHitTestVisible = false });
            bool showGlyph = done || (art == null && !ghost && !string.IsNullOrWhiteSpace(info.Def!.Icon));
            if (showGlyph)
                cell.Children.Add(new IconGlyph
                {
                    Kind = done ? IconKind.Checkmark : StampIcon(info.Def?.Icon),
                    Size = size >= QuestStampWeeklySize ? 12 : 10.5,
                    Foreground = done ? QuestStampGoldInk : stroke,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                });
            double fraction = done ? 1.0 : info.Fraction;
            if (fraction > 0 && !ghost)
                cell.Children.Add(new Border
                {
                    Height = QuestStampFillHeight,
                    Width = Math.Max(1.0, (size - QuestStampFillInset * 2) * fraction),
                    CornerRadius = new CornerRadius(1),
                    Background = done ? QuestStampGoldStroke : stroke,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(QuestStampFillInset, 0, 0, QuestStampFillInset),
                    IsHitTestVisible = false,
                });
            cell.PointerEntered += (s, _) => StampEnter((Panel)s!);
            cell.PointerExited += (s, _) => StampLeave((Panel)s!);
            return cell;
        }

        private static string QuestStampText(string localized, string plain)
        {
            var text = QuestStampRules.Text(localized, plain);
            return CoreMods.MakeModAware(text) ?? text;
        }

        private void ZoomStamp(QuestStampInfo? info, bool on)
        {
            if (info?.Cell == null || info.Scale == null || info.Rotate == null) return;
            info.Cell.ZIndex = on ? 20 : 0;
            info.Scale.ScaleX = info.Scale.ScaleY = on ? QuestStampHoverScale : 1;
            info.Rotate.Angle = QuestStampRestAngle(info.Key) * (on ? 0.25 : 1);
        }

        private static double QuestStampRestAngle(string key) => key switch
        {
            "d0" => -4, "d1" => 3, "d2" => -2, _ => 5,
        };

        private void LiftStampHost(bool on) => _stampHostScale.ScaleX = _stampHostScale.ScaleY = on ? 1.02 : 1;

        private void EnsureStampPopup()
        {
            if (_stampPopup != null || Named<Grid>("QuestStampHost") is not { } host) return;
            _stampPopArt = new Image { Stretch = Stretch.UniformToFill };
            var artScrim = new Border
            {
                Height = 48,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xB0, 0, 0, 0), 1) },
                },
            };
            _stampPopKind = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.Bold };
            _stampPopXp = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.Bold };
            _stampPopDone = new Border
            {
                Background = StampInk(0x992D4A2D),
                IsVisible = false,
                Child = new IconGlyph
                {
                    Kind = IconKind.Checkmark, Foreground = QuestStampDoneInk, Size = 60,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            static Border Chip(Control content, HorizontalAlignment side) => new()
            {
                Background = QuestStampChipFill, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8), HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Top, Child = content,
            };
            var artFrame = new Border
            {
                Height = 132,
                CornerRadius = new CornerRadius(11, 11, 0, 0),
                Background = QuestStampDarkFill,
                ClipToBounds = true,
                Tag = "stamp-card-art",
                Child = new Panel
                {
                    Children = { _stampPopArt, artScrim, Chip(_stampPopKind, HorizontalAlignment.Left), Chip(_stampPopXp, HorizontalAlignment.Right), _stampPopDone },
                },
            };
            _stampPopIcon = new IconGlyph
            {
                Size = 15, Foreground = QuestStampWhiteInk,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            };
            _stampPopName = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, Foreground = QuestStampWhiteInk, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            _stampPopDesc = new TextBlock { FontSize = 11.5, Foreground = QuestStampMutedInk, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 9) };
            _stampPopFill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _stampPopProgress = new TextBlock { FontSize = 12, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Left };
            _stampPopRemaining = new TextBlock { FontSize = 11, Foreground = QuestStampMutedInk, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var track = new Border
            {
                Height = 10,
                Width = QuestStampPopTrackWidth,
                CornerRadius = new CornerRadius(4),
                Background = QuestStampDarkFill,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = _stampPopFill,
            };
            var title = new DockPanel();
            DockPanel.SetDock(_stampPopIcon, Dock.Left);
            title.Children.Add(_stampPopIcon);
            title.Children.Add(_stampPopName);
            var body = new StackPanel
            {
                Margin = new Thickness(14, 11, 14, 12),
                Children =
                {
                    title, _stampPopDesc, track,
                    new Panel { Margin = new Thickness(0, 7, 0, 0), Children = { _stampPopProgress, _stampPopRemaining } },
                    new TextBlock
                    {
                        Text = ConditioningControlPanel.Localization.Loc.Get("tooltip_quest_stamps"), Foreground = QuestStampMutedInk, FontSize = 10,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0), Opacity = 0.75,
                    },
                },
            };
            _stampPopCard = new Border
            {
                Width = QuestStampPopWidth,
                CornerRadius = new CornerRadius(12),
                Background = QuestStampPanelFill,
                BorderThickness = new Thickness(1.5),
                IsHitTestVisible = false,
                Child = new StackPanel { Children = { artFrame, body } },
            };
            _stampPopup = new Popup
            {
                PlacementTarget = host,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 10,
                HorizontalOffset = -6,
                IsLightDismissEnabled = false,
                IsHitTestVisible = false,
                Child = _stampPopCard,
            };
            host.Children.Add(_stampPopup);   // a Popup needs a place in the tree; it takes no room
        }

        private void PaintStampPopup(QuestStampInfo info)
        {
            EnsureStampPopup();
            if (_stampPopCard == null) return;
            bool ghost = info.Def == null || info.Quest == null;
            IBrush accent = info.Completed ? QuestStampDoneInk : ghost ? QuestStampGhostStroke : info.IsWeekly ? QuestStampPurpleStroke : QuestStampPinkStroke;
            _stampPopCard.BorderBrush = accent;
            _stampPopKind!.Text = info.Label.ToUpperInvariant();
            _stampPopKind.Foreground = accent;
            if (ghost)
            {
                _stampPopArt!.Source = null;
                _stampPopDone!.IsVisible = false;
                _stampPopIcon!.IsVisible = false;
                _stampPopName!.Text = info.IsWeekly ? "No weekly quest yet" : "No quest in this slot";
                _stampPopDesc!.Text = "It will be here after the next roll.";
                _stampPopXp!.Inlines = null;
                _stampPopXp.Text = "";
                _stampPopFill!.Width = 0;
                _stampPopProgress!.Text = "";
                _stampPopRemaining!.Text = "";
                return;
            }
            var def = info.Def!;
            var quest = info.Quest!;
            _stampPopArt!.Source = QuestsTabView.GetQuestArt(def);
            _stampPopDone!.IsVisible = info.Completed;
            _stampPopIcon!.Kind = StampIcon(def.Icon);
            _stampPopIcon.IsVisible = true;
            _stampPopName!.Text = QuestStampText(def.LocalizedName, def.Name);
            _stampPopDesc!.Text = QuestStampText(def.LocalizedDescription, def.Description);
            var (xp, streak, reroll) = QuestsTabView.ComputeQuestXpDisplay(def, CoreSettings.Current);
            _stampPopXp!.Inlines = new InlineCollection
            {
                new InlineUIContainer(new IconGlyph { Kind = IconKind.Gift, Size = 11, Foreground = accent }),
                new Run($" {xp} XP"),
            };
            if (streak > 0) AddBonus(streak, IconKind.Fire);
            if (reroll > 0) AddBonus(reroll, IconKind.ArrowSync);
            void AddBonus(int percent, IconKind kind)
            {
                _stampPopXp.Inlines.Add(new Run($" +{percent}%"));
                _stampPopXp.Inlines.Add(new InlineUIContainer(new IconGlyph { Kind = kind, Size = 11, Foreground = accent }));
            }
            _stampPopXp.Foreground = accent;
            var display = QuestStampRules.Progress(quest.CurrentProgress, def.TargetValue, info.Completed);
            double fraction = display.Fraction;
            int current = display.Current;
            _stampPopFill!.Background = info.Completed ? QuestStampDoneInk : accent;
            _stampPopFill.Width = Math.Max(0, QuestStampPopTrackWidth * fraction);
            _stampPopProgress!.Foreground = info.Completed ? QuestStampDoneInk : accent;
            _stampPopProgress.Text = $"{current} / {def.TargetValue}";
            int left = display.Remaining;
            _stampPopRemaining!.Text = info.Completed ? "done" : left > 0 ? $"{left} to go" : "";
        }

        private void ShowStampPopup(QuestStampInfo info)
        {
            try
            {
                EnsureStampPopup();
                if (_stampPopup == null || _stampPopCard == null) return;
                PaintStampPopup(info);
                _stampPopup.IsOpen = true;
                _stampPopCard.Opacity = 1;
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] card open failed: {E}", ex.Message); }
        }

        private void HideStampPopup()
        {
            try
            {
                if (_stampPopup != null) _stampPopup.IsOpen = false;
            }
            catch { /* teardown */ }
        }

        private void StampEnter(Panel cell)
        {
            try
            {
                if (cell.Tag is not string key || !_stampInfos.TryGetValue(key, out var info)) return;
                if (_hoveredStampKey != null && _hoveredStampKey != key && _stampInfos.TryGetValue(_hoveredStampKey, out var previous))
                    ZoomStamp(previous, false);
                _hoveredStampKey = key;
                ZoomStamp(info, true);
                ShowStampPopup(info);
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] hover in: {E}", ex.Message); }
        }

        private void StampLeave(Panel cell)
        {
            try
            {
                if (cell.Tag is not string key) return;
                if (!_stampInfos.TryGetValue(key, out var info) || !ReferenceEquals(info.Cell, cell)) return;
                ZoomStamp(info, false);
                if (_hoveredStampKey != key) return;     // already moved on to another stamp
                _hoveredStampKey = null;
                Dispatcher.UIThread.Post(() => { if (_hoveredStampKey == null) HideStampPopup(); }, DispatcherPriority.Input);
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] hover out: {E}", ex.Message); }
        }

        private void RestoreStampHover()
        {
            if (_hoveredStampKey == null) return;
            if (Named<Grid>("QuestStampHost")?.IsPointerOver == true && _stampInfos.TryGetValue(_hoveredStampKey, out var info))
            {
                ZoomStamp(info, true);
                if (_stampPopup?.IsOpen == true) PaintStampPopup(info);
                else ShowStampPopup(info);
                return;
            }
            _hoveredStampKey = null;
            HideStampPopup();
        }

        private void QuestStamps_Click(object? sender, global::Avalonia.Input.PointerReleasedEventArgs e)
        {
            try
            {
                if (e.InitialPressMouseButton != global::Avalonia.Input.MouseButton.Left) return;
                HideStampPopup();
                e.Handled = true;
                ShowTab("quests");
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] navigation failed: {E}", ex.Message); }
        }

        private void QuestStamps_MouseEnter(object? sender, global::Avalonia.Input.PointerEventArgs e)
        {
            try { LiftStampHost(true); } catch { }
        }

        private void QuestStamps_MouseLeave(object? sender, global::Avalonia.Input.PointerEventArgs e)
        {
            try
            {
                LiftStampHost(false);
                if (_hoveredStampKey != null && _stampInfos.TryGetValue(_hoveredStampKey, out var info)) ZoomStamp(info, false);
                _hoveredStampKey = null;
                HideStampPopup();
            }
            catch { }
        }
    }
}

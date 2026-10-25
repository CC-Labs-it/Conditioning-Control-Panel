using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "Advanced" panel of the mod creator: avatar tube layout offsets and
    /// enhancement tree label/tooltip overrides. Partial-class side file of
    /// <see cref="ModCreatorWindow"/> (see ModCreatorWindow.xaml.cs).
    /// </summary>
    public partial class ModCreatorWindow
    {
        // ─── Advanced Section State ──────────────────────────────
        // Avatar tube layout (blank = app default; ranges mirror ModService.SanitizeManifest)
        private TextBox? _advAvatarOffsetX, _advAvatarDetachedOffsetX, _advAvatarScale;
        private TextBox? _advAvatarOffsetY, _advAvatarDetachedOffsetY;

        // Bubble Pop sprite scale (blank = the app's own size band; range mirrors
        // BubbleSizing.ModScaleMin/Max, which is what actually honours the number).
        private TextBox? _advBubbleScale;

        // Enhancement tree label overrides
        private TextBox? _advTreeTitle, _advTreeSubtitle, _advTreeWarning;
        private TextBox? _advPointsLabel, _advStatsTitle, _advTabTooltip;
        private TextBox? _advPinkRushName, _advPinkRushDescription;
        private TextBox? _advLuckyFlashLabel, _advLuckyBubbleLabel;

        // Tooltip override rows (skill id → tooltip text)
        private readonly List<(TextBox Key, TextBox Value)> _advBoostTooltipRows = new();
        private readonly List<(TextBox Key, TextBox Value)> _advStatPillTooltipRows = new();
        private StackPanel? _advBoostTooltipsPanel, _advStatPillTooltipsPanel;

        // ─── Advanced Section ────────────────────────────────────
        private void BuildAdvancedSection()
        {
            var panel = CreateSectionPanel("advanced");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Advanced"));
            stack.Children.Add(CreateSectionDescription("Fine-tuning for power users. Adjust where the avatar sits inside a custom tube image, and rename the enhancement (skill) tree's labels and tooltips. Everything here is optional -- leave fields blank to keep the app defaults."));

            // ── Sub-group 1: Avatar Tube Layout ──
            stack.Children.Add(CreateSubHeader("Avatar Tube Layout"));
            stack.Children.Add(CreateSectionDescription("Pixel offsets for the avatar inside the tube window, for tube images whose glass area is not in the default position. Positive X shifts right, positive Y shifts down. Blank = default (0)."));

            stack.Children.Add(CreateFieldLabel("Avatar Offset X (-1000 to 1000)"));
            _advAvatarOffsetX = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarOffsetX);

            stack.Children.Add(CreateFieldLabel("Avatar Detached Offset X (-1000 to 1000)"));
            _advAvatarDetachedOffsetX = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarDetachedOffsetX);

            stack.Children.Add(CreateFieldLabel("Avatar Scale (0.1 to 3.0, e.g. 1.25)"));
            _advAvatarScale = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarScale);

            stack.Children.Add(CreateFieldLabel("Avatar Offset Y (-500 to 500)"));
            _advAvatarOffsetY = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarOffsetY);

            stack.Children.Add(CreateFieldLabel("Avatar Detached Offset Y (-500 to 500)"));
            _advAvatarDetachedOffsetY = CreateAdvNumberBox();
            stack.Children.Add(_advAvatarDetachedOffsetY);

            // ── Sub-group 2: Bubble Size ──
            stack.Children.Add(CreateSubHeader("Bubble Size"));
            stack.Children.Add(CreateSectionDescription("Multiplies the on-screen size of the bubble.png you supplied under UI Assets. Worth setting because perceived size comes from how much transparent margin the sprite has, not from the box it is drawn in: the app's own bubble has a soft padded rim, so a full-bleed replacement reads dramatically bigger at the same box size. Correct it once here instead of leaving every user of your mod to find the bubble size slider and compensate by hand. Blank = no change. This multiplies with the user's own setting, and they keep the final say."));

            stack.Children.Add(CreateFieldLabel("Bubble Scale (0.5 to 1.5, e.g. 0.85)"));
            _advBubbleScale = CreateAdvNumberBox();
            stack.Children.Add(_advBubbleScale);

            // ── Sub-group 3: Skill Tree Overrides ──
            // (manifest field names stay `treeTitle`/`tabTooltip` etc. — only the wording changed)
            stack.Children.Add(CreateSubHeader("Skill Tree Overrides"));
            stack.Children.Add(CreateSectionDescription("Rename the labels of the skill tree tab. The grey hint text shows the default wording each field replaces. Max 200 characters per label."));

            stack.Children.Add(CreateFieldLabel("Tree Title"));
            _advTreeTitle = CreateDarkTextBox("Bimbo Skill Tree");
            stack.Children.Add(_advTreeTitle);

            stack.Children.Add(CreateFieldLabel("Tree Subtitle"));
            _advTreeSubtitle = CreateDarkTextBox("you earn sparkle points from leveling up + every 100 bubbles popped~");
            stack.Children.Add(_advTreeSubtitle);

            stack.Children.Add(CreateFieldLabel("Tree Warning"));
            _advTreeWarning = CreateDarkTextBox("once you pick a path, there's no going back~");
            stack.Children.Add(_advTreeWarning);

            stack.Children.Add(CreateFieldLabel("Points Label"));
            _advPointsLabel = CreateDarkTextBox("Sparkle Points");
            stack.Children.Add(_advPointsLabel);

            stack.Children.Add(CreateFieldLabel("Stats Title"));
            _advStatsTitle = CreateDarkTextBox("Ditzy Data Stats");
            stack.Children.Add(_advStatsTitle);

            stack.Children.Add(CreateFieldLabel("Tab Tooltip"));
            _advTabTooltip = CreateDarkTextBox("Bimbo Skill Tree");
            stack.Children.Add(_advTabTooltip);

            stack.Children.Add(CreateFieldLabel("Pink Rush Name"));
            _advPinkRushName = CreateDarkTextBox("PINK RUSH!");
            stack.Children.Add(_advPinkRushName);

            stack.Children.Add(CreateFieldLabel("Pink Rush Description"));
            _advPinkRushDescription = CreateDarkTextBox("3x XP for 60 seconds!");
            stack.Children.Add(_advPinkRushDescription);

            stack.Children.Add(CreateFieldLabel("Lucky Flash Label"));
            _advLuckyFlashLabel = CreateDarkTextBox("Lucky Flash");
            stack.Children.Add(_advLuckyFlashLabel);

            stack.Children.Add(CreateFieldLabel("Lucky Bubble Label"));
            _advLuckyBubbleLabel = CreateDarkTextBox("Lucky Bubble");
            stack.Children.Add(_advLuckyBubbleLabel);

            // ── Boost tooltips (skill id → tooltip) ──
            stack.Children.Add(CreateSubHeader("Boost Tooltips"));
            stack.Children.Add(CreateSectionDescription("Custom hover tooltips for skill tree boost nodes, keyed by skill id (e.g. pink_rush, lucky_bimbo). Max 30 entries, 500 characters per tooltip."));

            _advBoostTooltipsPanel = new StackPanel();
            stack.Children.Add(_advBoostTooltipsPanel);
            stack.Children.Add(CreateAdvTooltipAddButton("+ Add boost tooltip",
                () => AddAdvTooltipRow(_advBoostTooltipsPanel, _advBoostTooltipRows, "", "")));

            // ── Stat pill tooltips (skill id → tooltip) ──
            stack.Children.Add(CreateSubHeader("Stat Pill Tooltips"));
            stack.Children.Add(CreateSectionDescription("Custom hover tooltips for the stat pills in the stats panel, keyed by skill id. Max 30 entries, 500 characters per tooltip."));

            _advStatPillTooltipsPanel = new StackPanel();
            stack.Children.Add(_advStatPillTooltipsPanel);
            stack.Children.Add(CreateAdvTooltipAddButton("+ Add stat pill tooltip",
                () => AddAdvTooltipRow(_advStatPillTooltipsPanel, _advStatPillTooltipRows, "", "")));
        }

        // ─── UI Helpers ──────────────────────────────────────────
        private TextBox CreateAdvNumberBox()
        {
            var tb = CreateDarkTextBox();
            tb.Width = 100;
            return tb;
        }

        private Button CreateAdvTooltipAddButton(string label, Action onClick)
        {
            var btn = new Button
            {
                Content = label,
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
            };
            btn.Click += (_, _) => onClick();
            return btn;
        }

        private void AddAdvTooltipRow(StackPanel? hostPanel, List<(TextBox Key, TextBox Value)> rows, string keyText, string valueText)
        {
            if (hostPanel == null) return;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var keyBox = new TextBox
            {
                Style = (Style)FindResource("DarkTextBox"),
                Text = keyText,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = "Skill id",
            };
            Grid.SetColumn(keyBox, 0);
            row.Children.Add(keyBox);

            var arrow = new TextBlock
            {
                Text = "→",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080")),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetColumn(arrow, 1);
            row.Children.Add(arrow);

            var valueBox = new TextBox
            {
                Style = (Style)FindResource("DarkTextBox"),
                Text = valueText,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ToolTip = "Tooltip text (max 500 characters)",
            };
            Grid.SetColumn(valueBox, 2);
            row.Children.Add(valueBox);

            var delBtn = new Button
            {
                Content = "×",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 16,
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            delBtn.Click += (_, _) =>
            {
                var idx = hostPanel.Children.IndexOf(row);
                if (idx >= 0)
                {
                    hostPanel.Children.Remove(row);
                    if (idx < rows.Count)
                        rows.RemoveAt(idx);
                }
            };
            Grid.SetColumn(delBtn, 3);
            row.Children.Add(delBtn);

            hostPanel.Children.Add(row);
            rows.Add((keyBox, valueBox));
        }

        // ─── Apply to Manifest ───────────────────────────────────
        private void ApplyAdvancedToManifest(Models.ModManifest manifest)
        {
            manifest.TubeLayout = ModAdvancedRules.TubeLayout(
                GetTextBoxValue(_advAvatarOffsetX), GetTextBoxValue(_advAvatarDetachedOffsetX),
                GetTextBoxValue(_advAvatarScale), GetTextBoxValue(_advAvatarOffsetY),
                GetTextBoxValue(_advAvatarDetachedOffsetY));

            // ── Bubble scale ──
            // Parsed through ModBubbleScaleRules rather than read raw so a hand-written value is
            // corrected on the way out as well as on the way in. Until this line existed the field
            // was write-only from the editor's point of view: BuildManifestFromForm builds a FRESH
            // ModManifest, so an author who typed bubbleScale into mod.json by hand lost it silently
            // the next time they opened their mod here and exported.
            manifest.BubbleScale = ModBubbleScaleRules.Parse(GetTextBoxValue(_advBubbleScale));

            // ── Enhancement overrides: only emit if anything is non-empty ──
            var eo = new Models.ModEnhancementOverrides
            {
                TreeTitle = GetAdvString(_advTreeTitle, 200),
                TreeSubtitle = GetAdvString(_advTreeSubtitle, 200),
                TreeWarning = GetAdvString(_advTreeWarning, 200),
                PointsLabel = GetAdvString(_advPointsLabel, 200),
                StatsTitle = GetAdvString(_advStatsTitle, 200),
                TabTooltip = GetAdvString(_advTabTooltip, 200),
                PinkRushName = GetAdvString(_advPinkRushName, 200),
                PinkRushDescription = GetAdvString(_advPinkRushDescription, 200),
                LuckyFlashLabel = GetAdvString(_advLuckyFlashLabel, 200),
                LuckyBubbleLabel = GetAdvString(_advLuckyBubbleLabel, 200),
                BoostTooltips = CollectAdvTooltips(_advBoostTooltipRows),
                StatPillTooltips = CollectAdvTooltips(_advStatPillTooltipRows),
            };

            bool anyOverride =
                eo.TreeTitle != null || eo.TreeSubtitle != null || eo.TreeWarning != null ||
                eo.PointsLabel != null || eo.StatsTitle != null || eo.TabTooltip != null ||
                eo.PinkRushName != null || eo.PinkRushDescription != null ||
                eo.LuckyFlashLabel != null || eo.LuckyBubbleLabel != null ||
                eo.BoostTooltips != null || eo.StatPillTooltips != null;

            manifest.EnhancementOverrides = anyOverride ? eo : null;
        }

        // ─── Populate from Manifest ──────────────────────────────
        private void PopulateAdvancedFromManifest(Models.ModManifest manifest)
        {
            ClearAdvancedSection();

            if (manifest.TubeLayout is { } tl)
            {
                // 0 is the app default for offsets -- leave the box blank so
                // re-export doesn't pin values the author never touched.
                if (tl.AvatarOffsetX != 0)
                    SetTextBoxValue(_advAvatarOffsetX, tl.AvatarOffsetX.ToString(CultureInfo.InvariantCulture));
                if (tl.AvatarDetachedOffsetX != 0)
                    SetTextBoxValue(_advAvatarDetachedOffsetX, tl.AvatarDetachedOffsetX.ToString(CultureInfo.InvariantCulture));
                if (tl.AvatarScale.HasValue)
                    SetTextBoxValue(_advAvatarScale, tl.AvatarScale.Value.ToString(CultureInfo.InvariantCulture));
                if (tl.AvatarOffsetY != 0)
                    SetTextBoxValue(_advAvatarOffsetY, tl.AvatarOffsetY.ToString(CultureInfo.InvariantCulture));
                if (tl.AvatarDetachedOffsetY != 0)
                    SetTextBoxValue(_advAvatarDetachedOffsetY, tl.AvatarDetachedOffsetY.ToString(CultureInfo.InvariantCulture));
            }

            // Blank when the manifest said nothing usable, so a mod that never declared a bubble
            // scale does not gain one on re-export -- and a garbage value is scrubbed rather than
            // shown, because the box is what the author will trust.
            SetTextBoxValue(_advBubbleScale, ModBubbleScaleRules.Format(manifest.BubbleScale));

            if (manifest.EnhancementOverrides is { } eo)
            {
                SetTextBoxValue(_advTreeTitle, eo.TreeTitle);
                SetTextBoxValue(_advTreeSubtitle, eo.TreeSubtitle);
                SetTextBoxValue(_advTreeWarning, eo.TreeWarning);
                SetTextBoxValue(_advPointsLabel, eo.PointsLabel);
                SetTextBoxValue(_advStatsTitle, eo.StatsTitle);
                SetTextBoxValue(_advTabTooltip, eo.TabTooltip);
                SetTextBoxValue(_advPinkRushName, eo.PinkRushName);
                SetTextBoxValue(_advPinkRushDescription, eo.PinkRushDescription);
                SetTextBoxValue(_advLuckyFlashLabel, eo.LuckyFlashLabel);
                SetTextBoxValue(_advLuckyBubbleLabel, eo.LuckyBubbleLabel);

                if (eo.BoostTooltips != null)
                    foreach (var kvp in eo.BoostTooltips)
                        AddAdvTooltipRow(_advBoostTooltipsPanel, _advBoostTooltipRows, kvp.Key, kvp.Value);

                if (eo.StatPillTooltips != null)
                    foreach (var kvp in eo.StatPillTooltips)
                        AddAdvTooltipRow(_advStatPillTooltipsPanel, _advStatPillTooltipRows, kvp.Key, kvp.Value);
            }
        }

        // ─── Clear ───────────────────────────────────────────────
        private void ClearAdvancedSection()
        {
            SetTextBoxValue(_advAvatarOffsetX, null);
            SetTextBoxValue(_advAvatarDetachedOffsetX, null);
            SetTextBoxValue(_advAvatarScale, null);
            SetTextBoxValue(_advAvatarOffsetY, null);
            SetTextBoxValue(_advAvatarDetachedOffsetY, null);
            SetTextBoxValue(_advBubbleScale, null);

            SetTextBoxValue(_advTreeTitle, null);
            SetTextBoxValue(_advTreeSubtitle, null);
            SetTextBoxValue(_advTreeWarning, null);
            SetTextBoxValue(_advPointsLabel, null);
            SetTextBoxValue(_advStatsTitle, null);
            SetTextBoxValue(_advTabTooltip, null);
            SetTextBoxValue(_advPinkRushName, null);
            SetTextBoxValue(_advPinkRushDescription, null);
            SetTextBoxValue(_advLuckyFlashLabel, null);
            SetTextBoxValue(_advLuckyBubbleLabel, null);

            _advBoostTooltipsPanel?.Children.Clear();
            _advBoostTooltipRows.Clear();
            _advStatPillTooltipsPanel?.Children.Clear();
            _advStatPillTooltipRows.Clear();
        }

        private string? GetAdvString(TextBox? tb, int maxLength) =>
            ModAdvancedRules.Text(GetTextBoxValue(tb), maxLength);

        private static Dictionary<string, string>? CollectAdvTooltips(List<(TextBox Key, TextBox Value)> rows) =>
            ModAdvancedRules.Tooltips(rows.Select(row => ((string?)row.Key.Text, (string?)row.Value.Text)));
    }
}

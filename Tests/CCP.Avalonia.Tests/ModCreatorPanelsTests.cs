using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Mod Creator's Pools &amp; Triggers and Personalities panels (WPF ModCreatorWindow.Pools.cs /
/// .Personalities.cs), driven through the sidebar and the "+ Add" buttons, then read back through the
/// same BuildManifestFromForm the Export button writes into mod.json.
/// </summary>
public sealed class ModCreatorPanelsTests
{
    private static void Run(Action<ModCreatorWindow> body) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var window = new ModCreatorWindow();
            try { body(window); }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Label(Button b) => b.Content as string ?? (b.Content as TextBlock)?.Text ?? "";

    private static void Navigate(ModCreatorWindow w, string key) =>
        Click(w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, key)));

    /// <summary>The section's StackPanel, found from a sub-header it alone draws.</summary>
    private static StackPanel Section(ModCreatorWindow w, string text) =>
        (StackPanel)w.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == text).Parent!;

    [Fact]
    public void AdvancedPanelEditsLayoutLabelsAndTooltips()
    {
        Run(w =>
        {
            Navigate(w, "advanced");
            var stack = Section(w, "Avatar Tube Layout");
            Assert.True(((Control)stack.Parent!).IsVisible);
            var boxes = stack.Children.OfType<TextBox>().ToList();
            Assert.Equal(16, boxes.Count);
            boxes[0].Text = "2001";
            boxes[1].Text = "-2001";
            boxes[2].Text = "1.25";
            boxes[3].Text = "501";
            boxes[4].Text = "-501";
            boxes[5].Text = "0.7543";
            boxes[6].Text = "  Keeper Skills  ";
            boxes[15].Text = new string('x', 201);
            Click(stack.Children.OfType<Button>().Single(b => Label(b) == "+ Add boost tooltip"));
            var row = stack.GetLogicalDescendants().OfType<Grid>().Single();
            var tooltipBoxes = row.Children.OfType<TextBox>().ToList();
            tooltipBoxes[0].Text = " pink_rush ";
            tooltipBoxes[1].Text = "  Rush now  ";
            var result = w.BuildManifestFromForm();
            Assert.Equal(1000, result.TubeLayout!.AvatarOffsetX);
            Assert.Equal(-1000, result.TubeLayout.AvatarDetachedOffsetX);
            Assert.Equal(1.25, result.TubeLayout.AvatarScale);
            Assert.Equal(500, result.TubeLayout.AvatarOffsetY);
            Assert.Equal(-500, result.TubeLayout.AvatarDetachedOffsetY);
            Assert.Equal(0.754, result.BubbleScale);
            Assert.Equal("Keeper Skills", result.EnhancementOverrides!.TreeTitle);
            Assert.Equal(200, result.EnhancementOverrides.LuckyBubbleLabel!.Length);
            Assert.Equal("Rush now", result.EnhancementOverrides.BoostTooltips!["pink_rush"]);
            var del = row.Children.OfType<Button>().Single();
            Assert.Equal("Remove", global::Avalonia.Automation.AutomationProperties.GetName(del));
            Click(del);
            Assert.Null(w.BuildManifestFromForm().EnhancementOverrides!.BoostTooltips);
        });
    }

    [Fact]
    public void AdvancedLoadedValuesCanBeClearedAndReplaced()
    {
        Run(w =>
        {
            w.PopulateFromManifest(new ModManifest
            {
                TubeLayout = new ModTubeLayout { AvatarOffsetX = 17, AvatarScale = 1.2 },
                BubbleScale = 5,
                EnhancementOverrides = new ModEnhancementOverrides { TreeTitle = "Changed",
                    StatPillTooltips = new() { ["lucky_bimbo"] = "Lucky" } },
            });
            Navigate(w, "advanced");
            var stack = Section(w, "Avatar Tube Layout");
            var boxes = stack.Children.OfType<TextBox>().ToList();
            Assert.Equal("17", boxes[0].Text);
            Assert.Equal("1.5", boxes[5].Text);
            Assert.Equal("Changed", boxes[6].Text);
            Assert.Equal("Lucky", w.BuildManifestFromForm().EnhancementOverrides!.StatPillTooltips!["lucky_bimbo"]);
            w.PopulateFromManifest(new ModManifest());
            var cleared = w.BuildManifestFromForm();
            Assert.Null(cleared.TubeLayout);
            Assert.Null(cleared.BubbleScale);
            Assert.Null(cleared.EnhancementOverrides);
            Assert.All(boxes, b => Assert.True(string.IsNullOrEmpty(b.Text)));
            Assert.Empty(stack.GetLogicalDescendants().OfType<Grid>());
        });
    }

    [Fact]
    public void PoolsPanelRowsExportLikeWpf()
    {
        Run(w =>
        {
            Navigate(w, "pools");
            var stack = Section(w, "Subliminal Pool");
            Assert.True(((Control)stack.Parent!).IsVisible);
            var adds = stack.Children.OfType<Button>().Where(b => Label(b) == "+ Add").ToList();
            Assert.Equal(3, adds.Count);

            Click(adds[0]); Click(adds[0]); Click(adds[0]);
            var sub = stack.GetLogicalDescendants().OfType<TextBox>().ToList();
            sub[0].Text = "  good girl  ";
            sub[1].Text = "";                      // blank: skipped
            sub[2].Text = "good girl";             // duplicate: last row's checkbox wins
            stack.GetLogicalDescendants().OfType<CheckBox>().Last().IsChecked = false;

            Click(stack.Children.OfType<Button>().Single(b => Label(b) == "+ Add Trigger"));
            Click(stack.Children.OfType<Button>().Single(b => Label(b) == "+ Add Trigger"));
            var boxes = stack.GetLogicalDescendants().OfType<TextBox>().ToList();
            boxes[^2].Text = "drop";
            boxes[^1].Text = "drop";

            var m = w.BuildManifestFromForm();
            Assert.Equal(new Dictionary<string, bool> { ["good girl"] = false }, m.SubliminalPool);
            Assert.Null(m.LockCardPhrases);
            Assert.Null(m.BouncingTextPool);
            Assert.Equal(new[] { "drop" }, m.CustomTriggers);
        });
    }

    [Fact]
    public void PersonalityCardExportsIdFromName()
    {
        Run(w =>
        {
            Navigate(w, "personalities");
            var stack = Section(w, "Personalities");
            Click(stack.Children.OfType<Button>().Single(b => Label(b) == "+ Add Personality"));
            Click(stack.Children.OfType<Button>().Single(b => Label(b) == "+ Add Personality"));
            var cards = stack.GetLogicalDescendants().OfType<Border>().Where(b => b.MaxWidth == 560).ToList();
            Assert.Equal(2, cards.Count);

            var first = cards[0].GetLogicalDescendants().OfType<TextBox>().ToList();
            Assert.Equal(8, first.Count);          // name, description, six prompt fields
            first[0].Text = "Soft Keeper";
            first[2].Text = "Be gentle.";
            // Second card has no name: skipped on export.

            var p = Assert.Single(w.BuildManifestFromForm().Personalities!);
            Assert.Equal("soft-keeper", p.Id);
            Assert.Equal("Soft Keeper", p.Name);
            Assert.Null(p.Description);
            Assert.Equal(new Dictionary<string, string> { ["Personality"] = "Be gentle." }, p.PromptSettings);

            Click(cards[0].GetLogicalDescendants().OfType<Button>().Single(b => Label(b) == "✕ Remove"));
            Assert.Null(w.BuildManifestFromForm().Personalities);
        });
    }

    [Fact]
    public void LoadedSectionsSurviveAReExport()
    {
        Run(w =>
        {
            var loaded = new ModManifest
            {
                Id = "x", Name = "X", Author = "a",
                SubliminalPool = new() { ["obey"] = true, ["sink"] = false },
                LockCardPhrases = new() { ["I obey"] = true },
                BouncingTextPool = new() { ["drift"] = false },
                CustomTriggers = new() { "snap" },
                Personalities = new() { new ModPersonality { Id = "k", Name = "Keeper", Description = "d",
                    PromptSettings = new() { ["OutputRules"] = "short" } } },
                TubeLayout = new ModTubeLayout { AvatarOffsetX = 12 },
                BubbleScale = 1.25,
            };
            w.PopulateFromManifest(loaded);
            var again = w.BuildManifestFromForm();

            string J(object? o) => JsonConvert.SerializeObject(o);
            Assert.Equal(J(loaded.SubliminalPool), J(again.SubliminalPool));
            Assert.Equal(J(loaded.LockCardPhrases), J(again.LockCardPhrases));
            Assert.Equal(J(loaded.BouncingTextPool), J(again.BouncingTextPool));
            Assert.Equal(J(loaded.CustomTriggers), J(again.CustomTriggers));
            Assert.Equal("keeper", again.Personalities![0].Id);   // WPF re-derives the id from the name
            Assert.Equal(J(loaded.Personalities[0].PromptSettings), J(again.Personalities[0].PromptSettings));
            Assert.Equal(J(loaded.TubeLayout), J(again.TubeLayout));
            Assert.Equal(1.25, again.BubbleScale);
        });
    }

    [Fact]
    public void AchievementSlotsComeFromTheRegistry()
    {
        Run(w =>
        {
            var slots = ModAchievementSlots.Build();
            Assert.True(slots.Length > 16);        // the old hand-list stopped at 16
            Navigate(w, "achievements");
            var texts = w.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToHashSet();
            Assert.Contains(System.IO.Path.GetFileName(slots[^1].Key), texts);
        });
    }
}

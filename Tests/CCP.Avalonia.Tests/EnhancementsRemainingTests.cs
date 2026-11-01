using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class EnhancementsRemainingTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static string[] Texts(Control control) => control.GetLogicalDescendants().OfType<TextBlock>()
        .Select(t => t.Text ?? "").ToArray();

    private static void CheckCard(Control card, string name, string flavor, string description)
    {
        Assert.Contains(name, Texts(card));
        var tip = Assert.IsType<Border>(ToolTip.GetTip(card));
        var lines = Texts(tip);
        Assert.Contains(flavor, lines);
        Assert.Contains(description, lines);
    }

    [Fact]
    public Task NodesAndSecretCardsUseRawModVocabularyWithLocalizedFallback() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var loc = LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        var prop = typeof(AvApp).GetProperty("Mods", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = prop.GetValue(null);
        var mods = new ModService();
        var replacements = mods.ActiveMod.Manifest.TextReplacements;
        var normal = SkillDefinition.All.Single(s => s.Id == "pink_hours");
        var secret = SkillDefinition.All.Single(s => s.Id == "night_shift");
        try
        {
            loc.SetLanguage("fr");
            mods.ActiveMod.Manifest.TextReplacements = new Dictionary<string, string>
            {
                [normal.Name] = "Normal name", [normal.FlavorText] = "Normal flavor", [normal.Description] = "Normal description",
                [secret.Name] = "Secret name", [secret.FlavorText] = "Secret flavor", [secret.Description] = "Secret description",
            };
            prop.SetValue(null, mods);
            var tab = new EnhancementsTabView();
            CheckCard(tab.Node(normal), "Normal name", "Normal flavor", "Normal description");
            CheckCard(tab.SecretSkillCard(secret), "Secret name", "Secret flavor", "Secret description");
            prop.SetValue(null, null);
            CheckCard(tab.Node(normal), normal.LocalizedName, normal.LocalizedFlavorText, normal.LocalizedDescription);
            CheckCard(tab.SecretSkillCard(secret), secret.LocalizedName, secret.LocalizedFlavorText, secret.LocalizedDescription);
        }
        finally
        {
            mods.ActiveMod.Manifest.TextReplacements = replacements;
            prop.SetValue(null, previous);
            loc.SetLanguage(language);
        }
        return Task.CompletedTask;
    });

    private sealed class AlwaysLucky : Random
    {
        public override double NextDouble() => 0;
    }

    [Fact]
    public Task LuckyBubbleNotifiesEvenWhenTheDailyBucketIsEmpty() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var randomField = typeof(AmbientBubbleField).GetField("Random")!;
        var random = randomField.GetValue(BubbleOverlay.Field);
        var skills = s.UnlockedSkills;
        var (day, paid, suppressed, gameProbe) = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday, s.SuppressPerkNotifications, LuckyToast.GameOwnsScreen);
        try
        {
            randomField.SetValue(BubbleOverlay.Field, new AlwaysLucky());
            LuckyToast.Close();
            LuckyToast.GameOwnsScreen = () => false;
            s.SuppressPerkNotifications = false;
            s.AmbientBubbleXpDayKey = DateTime.Now.ToString("yyyy-MM-dd");
            s.AmbientBubbleXpPaidToday = 300;
            s.UnlockedSkills = new List<string>();
            BubbleOverlay.Pop(new AmbientBubble());
            Dispatcher.UIThread.RunJobs();
            Assert.Null(LuckyToast.Popup);
            s.UnlockedSkills = new List<string> { "lucky_bubbles" };
            var bubble = new AmbientBubble();
            BubbleOverlay.Pop(bubble);
            Dispatcher.UIThread.RunJobs();
            Assert.False(bubble.Lucky); // a dry bucket suppresses gold effects, not the proc notification
            Assert.NotNull(LuckyToast.Popup);
            Assert.True(LuckyToast.Popup!.IsVisible);
            Assert.Contains(Loc.GetF("lucky_toast_xp", 20), Texts(LuckyToast.Popup));
        }
        finally
        {
            LuckyToast.Close();
            randomField.SetValue(BubbleOverlay.Field, random);
            s.UnlockedSkills = skills;
            s.AmbientBubbleXpDayKey = day;
            s.AmbientBubbleXpPaidToday = paid;
            s.SuppressPerkNotifications = suppressed;
            LuckyToast.GameOwnsScreen = gameProbe;
        }
        return Task.CompletedTask;
    });
}

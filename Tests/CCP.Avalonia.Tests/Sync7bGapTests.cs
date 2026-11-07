using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class Sync7bGapTests
{
    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        body();
        return Task.CompletedTask;
    });

    [Fact]
    public Task SelfCardOffersLevelGuideAndKeepsTableOpenOnLevelChange() => Run(() =>
    {
        LeashLevelHelp.Open = false;
        try
        {
            var card = new LeashSelfCard();
            var help = Assert.Single(card.GetLogicalDescendants().OfType<LeashLevelHelp>());
            Assert.False(help.TableShown);
            help.Toggle();
            card.SetLevelAsync(ConditioningControlPanel.Services.Leash.LeashIntensity.Strict).GetAwaiter().GetResult();
            help = Assert.Single(card.GetLogicalDescendants().OfType<LeashLevelHelp>());
            Assert.True(help.TableShown);
            Assert.Contains(help.GetLogicalDescendants().OfType<Control>(), c => Equals(c.Tag, "leash-guide-line:strict:on"));
        }
        finally { LeashLevelHelp.Open = false; }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task PromptEditorUsesRunningFieldsAndPreservesDisabledCustom(bool openedOn, bool saveOn) => Run(() =>
    {
        var settings = CoreSettings.Current.CompanionPrompt;
        settings.UseCustomPrompt = openedOn;
        settings.Personality = "stored personality";
        settings.ContextReactions = "stored reactions";
        var dialog = new CompanionPromptEditorDialog();
        try
        {
            var personality = dialog.FindControl<TextBox>("TxtPersonality")!;
            var reactions = dialog.FindControl<TextBox>("TxtContextReactions")!;
            if (openedOn)
            {
                Assert.Equal("stored personality", personality.Text);
                Assert.Equal("stored reactions", reactions.Text);
            }
            else
            {
                Assert.NotEqual("stored personality", personality.Text);
                Assert.NotEqual("stored reactions", reactions.Text);
            }
            dialog.FindControl<CheckBox>("ChkUseCustom")!.IsChecked = saveOn;
            if (saveOn) reactions.Text = "edited awareness";
            typeof(CompanionPromptEditorDialog).GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
            Assert.Equal(saveOn ? "edited awareness" : "stored reactions", settings.ContextReactions);
            Assert.Equal(openedOn || !saveOn ? "stored personality" : "", settings.Personality);
        }
        finally { dialog.Close(); }
    });
}

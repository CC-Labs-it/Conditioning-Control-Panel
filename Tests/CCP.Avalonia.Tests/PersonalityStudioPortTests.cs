using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;
public class PersonalityStudioPortTests
{
    [Fact]
    public void OnlyOwnedIdsCountAndDraftsStayDetached()
    {
        var mine = new PersonalityPreset { Id = "mine", Name = "Same name" };
        var users = new[] { mine };
        Assert.Same(mine, PersonalityStudio.OwnPreset(mine, users));
        Assert.Null(PersonalityStudio.OwnPreset(new PersonalityPreset { Id = "mine", IsBuiltIn = true }, users));
        Assert.Null(PersonalityStudio.OwnPreset(new PersonalityPreset { Id = "mod", Name = mine.Name }, users));
        mine.PromptSettings = new CompanionPromptSettings { Personality = "old voice" };
        var draft = PersonalityStudio.Draft(mine);
        draft.PromptSettings!.Personality = "edited";
        Assert.Equal("old voice", mine.PromptSettings.Personality);
        Assert.NotEqual(mine.Id, draft.Id);
    }

    [Fact]
    public Task FooterFitsEveryLanguageAtMinimumAndNormalWidth() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var state = new TestSettingsScope(); var settings = state.Settings;
        var mine = new PersonalityPreset { Id = "studio-owned", Name = "Mine", PromptSettings = new CompanionPromptSettings { Personality = "voice" } };
        settings.Current.UserPersonalityPresets.Add(mine);
        settings.Current.ActivePersonalityPresetId = mine.Id;
        var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
        var owner = new Window();
        try
        {
            owner.Show();
            foreach (var language in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
            foreach (var owned in new[] { true, false })
            {
                LocalizationManager.Instance.SetLanguage(language);
                settings.Current.CompanionPrompt.UseCustomPrompt = !owned;
                var shown = PersonalityStudio.ShowAsync(owner);
                Dispatcher.UIThread.RunJobs();
                var studio = Assert.Single(owner.OwnedWindows);
                try
                {
                    var row = studio.GetLogicalDescendants().OfType<PersonalityStudio.StudioFooter>().Single();
                    Assert.Equal(owned ? 2 : 1, row.Children.Count);
                    foreach (var width in new[] { 412d, 612d }) // minimum and normal dialog widths, less margins
                    {
                        row.Measure(new Size(width, double.PositiveInfinity));
                        row.Arrange(new Rect(0, 0, width, row.DesiredSize.Height));
                        var actions = row.Children[0]; var delete = owned ? row.Children[1] : null;
                        if (delete != null) Assert.Equal(0, delete.Bounds.Left);
                        Assert.Equal(width, actions.Bounds.Right, 3);
                        Assert.True(delete == null || delete.Bounds.Right <= actions.Bounds.Left || delete.Bounds.Bottom <= actions.Bounds.Top);
                        if (width == 612) Assert.Equal(0, actions.Bounds.Top);
                        foreach (var button in row.GetLogicalDescendants().OfType<Button>())
                        {
                            var allocated = button.Bounds.Width;
                            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            Assert.True(allocated + button.Margin.Left + button.Margin.Right + .01 >= button.DesiredSize.Width,
                                $"{language} {width}: {button.Name} allocated={allocated}, desired={button.DesiredSize.Width}, margin={button.Margin}");
                        }
                    }
                }
                finally { studio.Close(false); }
                await shown;
            }
        }
        finally
        {
            owner.Close();
            LocalizationManager.Instance.SetLanguage(previousLanguage);
        }
        Assert.Equal(previousLanguage, LocalizationManager.Instance.CurrentLanguage);
    });
}

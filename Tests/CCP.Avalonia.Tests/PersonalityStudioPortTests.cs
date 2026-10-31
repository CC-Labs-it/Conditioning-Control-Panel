using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;
public class PersonalityStudioPortTests
{
    [Fact]
    public void OnlyOwnedIdsCanBeUpdatedAndOverridesAlwaysFork()
    {
        var mine = new PersonalityPreset { Id = "mine", Name = "Same name" };
        var users = new[] { mine };
        Assert.Same(mine, PersonalityStudio.EditablePreset(mine, users, false));
        Assert.Null(PersonalityStudio.EditablePreset(mine, users, true));
        Assert.Null(PersonalityStudio.OwnPreset(new PersonalityPreset { Id = "mine", IsBuiltIn = true }, users));
        Assert.Null(PersonalityStudio.OwnPreset(new PersonalityPreset { Id = "mod", Name = mine.Name }, users));
        mine.PromptSettings = new CompanionPromptSettings { Personality = "old voice" };
        var draft = PersonalityStudio.Draft(mine);
        draft.PromptSettings!.Personality = "edited";
        Assert.Equal("old voice", mine.PromptSettings.Personality);
        Assert.NotEqual(mine.Id, draft.Id);
    }

    [Fact]
    public Task SaveCopyAndConfirmedDeleteWithoutLayout() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var mine = new PersonalityPreset { Id = "studio-data", Name = "Mine", PromptSettings = new CompanionPromptSettings { Personality = "own voice" } };
        settings.Current.UserPersonalityPresets.Add(mine);
        settings.Current.ActivePersonalityPresetId = mine.Id;
        settings.Current.CompanionPrompt.UseCustomPrompt = false;
        bool answer = false;
        var edit = PersonalityStudio.Create((_, _, _) => Task.FromResult(answer));
        var fields = edit.GetLogicalDescendants().OfType<TextBox>().ToArray();
        Assert.Equal(TextWrapping.NoWrap, fields[0].TextWrapping);
        Assert.True(double.IsNaN(fields[0].Height));
        fields[0].Text = "Edited";
        fields[2].Text = null;
        edit.GetLogicalDescendants().OfType<Button>().ElementAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Edited", mine.Name);
        Assert.Equal("", mine.PromptSettings!.OutputRules);
        Assert.Single(settings.Current.UserPersonalityPresets);
        edit.Close();
        settings.Current.CompanionPrompt.UseCustomPrompt = true;
        settings.Current.CompanionPrompt.Personality = "override voice";
        var fork = PersonalityStudio.Create();
        fork.GetLogicalDescendants().OfType<Button>().ElementAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, settings.Current.UserPersonalityPresets.Count);
        Assert.Equal("own voice", mine.PromptSettings.Personality);
        Assert.Equal("override voice", settings.Current.UserPersonalityPresets.Last().PromptSettings!.Personality);
        fork.Close();
        settings.Current.ActivePersonalityPresetId = mine.Id;
        settings.Current.CompanionPrompt.UseCustomPrompt = false;
        edit = PersonalityStudio.Create((_, _, _) => Task.FromResult(answer));
        var delete = edit.GetLogicalDescendants().OfType<PersonalityStudio.StudioFooter>().Single().Children.OfType<Button>().Single();
        delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains(mine, settings.Current.UserPersonalityPresets);
        answer = true;
        delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain(mine, settings.Current.UserPersonalityPresets);
        Assert.NotEqual(mine.Id, settings.Current.ActivePersonalityPresetId);
        edit.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task OverrideSavesACopyAndDeleteWaitsForConfirmation() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var mine = new PersonalityPreset { Id = "studio-owned", Name = "Mine", PromptSettings = new CompanionPromptSettings { Personality = "own voice" } };
        settings.Current.UserPersonalityPresets.Add(mine);
        settings.Current.ActivePersonalityPresetId = mine.Id;
        settings.Current.CompanionPrompt.UseCustomPrompt = true;
        settings.Current.CompanionPrompt.Personality = "community voice";
        var fork = PersonalityStudio.Create();
        var actions = fork.GetLogicalDescendants().OfType<Button>().ToArray();
        Assert.Equal(2, actions.Length); // no Delete while an override holds the wire
        actions[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, settings.Current.UserPersonalityPresets.Count);
        Assert.Equal("own voice", mine.PromptSettings.Personality);
        Assert.Equal("community voice", settings.Current.UserPersonalityPresets.Last().PromptSettings!.Personality);
        fork.Close();
        settings.Current.ActivePersonalityPresetId = mine.Id;
        settings.Current.CompanionPrompt.UseCustomPrompt = false;
        var edit = PersonalityStudio.Create();
        try
        {
            edit.Show();
            var delete = edit.GetLogicalDescendants().OfType<PersonalityStudio.StudioFooter>().Single().Children.OfType<Button>().Single();
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(mine, settings.Current.UserPersonalityPresets);
            var confirm = edit.OwnedWindows.OfType<MessageDialog>().Single();
            confirm.Close(false);
            await Task.Yield();
            Assert.Contains(mine, settings.Current.UserPersonalityPresets);
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            edit.OwnedWindows.OfType<MessageDialog>().Single().Close(true);
            await Task.Yield();
            Assert.DoesNotContain(mine, settings.Current.UserPersonalityPresets);
            Assert.NotEqual(mine.Id, settings.Current.ActivePersonalityPresetId);
        }
        finally { edit.Close(); }
    });

    [Fact]
    public Task OwnedSaveUpdatesWithoutDuplicatesAndNameAndFooterFit() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var mine = new PersonalityPreset { Id = "studio-owned", Name = "Before", PromptSettings = new CompanionPromptSettings { Personality = "voice" } };
        settings.Current.UserPersonalityPresets.Add(mine);
        settings.Current.ActivePersonalityPresetId = mine.Id;
        settings.Current.CompanionPrompt.UseCustomPrompt = false;
        var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
        var window = PersonalityStudio.Create();
        try
        {
            var fields = window.GetLogicalDescendants().OfType<TextBox>().ToArray();
            var name = fields[0];
            Assert.Equal(TextWrapping.NoWrap, name.TextWrapping);
            Assert.True(double.IsNaN(name.Height));
            name.Text = "After";
            var footer = window.GetLogicalDescendants().OfType<PersonalityStudio.StudioFooter>().Single();
            Assert.Equal(2, footer.Children.Count);
            var save = footer.GetLogicalDescendants().OfType<Button>().ElementAt(1);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(mine, Assert.Single(settings.Current.UserPersonalityPresets));
            Assert.Equal("After", mine.Name);
            Assert.Equal(mine.Id, settings.Current.ActivePersonalityPresetId);
            foreach (var language in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
            foreach (var owned in new[] { true, false })
            {
                LocalizationManager.Instance.SetLanguage(language);
                settings.Current.CompanionPrompt.UseCustomPrompt = !owned;
                var translated = PersonalityStudio.Create();
                try
                {
                    translated.Show();
                    var row = translated.GetLogicalDescendants().OfType<PersonalityStudio.StudioFooter>().Single();
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
                            Assert.True(allocated + button.Margin.Left + button.Margin.Right + .01 >= button.DesiredSize.Width, $"{language} {width}: {button.Content} allocated={allocated}, desired={button.DesiredSize.Width}, margin={button.Margin}");
                        }
                    }
                }
                finally { translated.Close(); }
            }

        }
        finally
        {
            window.Close();
            LocalizationManager.Instance.SetLanguage(previousLanguage);
        }
        Assert.Equal(previousLanguage, LocalizationManager.Instance.CurrentLanguage);
        return Task.CompletedTask;
    });
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PersonalityStudioTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.True(ready(), "Timed out waiting for studio action");
    }

    [Fact]
    public Task PickerHasWpfCard() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var page = new PersonalityPage();
        var card = page.FindControl<Border>("PickerCard")!;
        Assert.NotNull(card);
        Assert.Equal(Color.Parse("#14FFFFFF"), Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color);
        Assert.Equal(new CornerRadius(10), card.CornerRadius);
        Assert.Equal(new Thickness(16), card.Padding);
        Assert.Equal("PickerHost", Assert.IsType<ContentControl>(card.Child).Name);
        return Task.CompletedTask;
    });

    // WPF Services/Companion/PersonalityStudio.cs:40-145: real picker button, detached edits and service persistence.
    [Fact]
    public Task PickerButtonCancelsCopiesUpdatesAndDeletes() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settingsService = new SettingsService();
        CoreSettings.ServiceProvider = () => settingsService;
        var settings = CoreSettings.Current;
        settings.UserPersonalityPresets.Clear();
        settings.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
        settings.CompanionPrompt.UseCustomPrompt = false;
        settings.Welcomed = settings.HasAcceptedAgeVerification = true;
        settings.AwarenessModeEnabled = false;
        settings.WhatMovedCardShown = 1;
        settings.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        var service = PersonalityService.Shared;
        var original = service.GetActivePreset();
        var originalVoice = original.PromptSettings!.Personality;
        var owner = new MainShellWindow { Width = 1400, Height = 800 };
        try
        {
            owner.Show(); owner.ShowTab("personality"); Dispatcher.UIThread.RunJobs();
            var page = owner.Named<PersonalityPage>("CompanionPersonalityTab")!;
            var picker = page.GetLogicalDescendants().OfType<CompanionPickerCard>().Single();
            async Task<Window> Open()
            {
                Click(picker.FindControl<Button>("BtnMorePersonality")!);
                await Until(() => owner.OwnedWindows.Any());
                var dialog = Assert.Single(owner.OwnedWindows);
                Assert.Equal("PersonalityStudio", dialog.Name);
                return dialog;
            }
            var studio = await Open();
            Named<TextBox>(studio, "Voice").Text = "cancelled voice";
            Click(Named<Button>(studio, "Cancel"));
            await Until(() => !studio.IsVisible);
            Assert.Empty(settings.UserPersonalityPresets);
            Assert.Equal(originalVoice, original.PromptSettings.Personality);

            studio = await Open();
            Named<TextBox>(studio, "Name").Text = " ";
            Dispatcher.UIThread.RunJobs();
            Assert.False(Named<Button>(studio, "Save").IsEnabled);
            Named<TextBox>(studio, "Name").Text = "  My voice  ";
            Named<TextBox>(studio, "Voice").Text = "  saved voice  ";
            Named<TextBox>(studio, "Awareness").Text = "screen response";
            Named<TextBox>(studio, "Style").Text = "short replies";
            Named<TextBox>(studio, "Background").Text = "background";
            Named<TextBox>(studio, "Boundaries").Text = "boundaries";
            Named<TextBox>(studio, "Samples").Text = "first sample\nsecond sample";
            Dispatcher.UIThread.RunJobs();
            Click(Named<Button>(studio, "Save"));
            await Until(() => !studio.IsVisible);
            var own = Assert.Single(settings.UserPersonalityPresets);
            Assert.Equal(("My voice", "saved voice", "screen response"),
                (own.Name, own.PromptSettings!.Personality, own.PromptSettings.ContextReactions));
            Assert.Equal(("short replies", "background", "boundaries"),
                (own.PromptSettings.OutputRules, own.PromptSettings.KnowledgeBase, own.PromptSettings.ExplicitReaction));
            Assert.Equal(new[] { "first sample", "second sample" }, own.SampleLines);
            Assert.NotEqual(original.Id, own.Id);
            Assert.Equal(own.Id, settings.ActivePersonalityPresetId);
            Assert.Equal(originalVoice, original.PromptSettings.Personality);

            studio = await Open();
            Named<TextBox>(studio, "Voice").Text = "updated voice";
            Dispatcher.UIThread.RunJobs();
            Click(Named<Button>(studio, "Save"));
            await Until(() => !studio.IsVisible);
            Assert.Same(own, Assert.Single(settings.UserPersonalityPresets));
            Assert.Equal("updated voice", own.PromptSettings.Personality);
            settingsService.SaveImmediate();
            Assert.Equal("updated voice", Assert.Single(new SettingsService().Current.UserPersonalityPresets).PromptSettings!.Personality);

            // Custom override text must become a copy, never overwrite the user's own preset.
            settings.CompanionPrompt.UseCustomPrompt = true;
            settings.CompanionPrompt.Personality = "override voice";
            studio = await Open();
            Assert.Equal("override voice", Named<TextBox>(studio, "Voice").Text);
            Assert.DoesNotContain(studio.GetLogicalDescendants().OfType<Button>(), b => b.Name == "Delete");
            Dispatcher.UIThread.RunJobs();
            Click(Named<Button>(studio, "Save"));
            await Until(() => !studio.IsVisible);
            Assert.Equal(2, settings.UserPersonalityPresets.Count);
            Assert.Equal("updated voice", own.PromptSettings.Personality);
            Assert.False(settings.CompanionPrompt.UseCustomPrompt);
            service.SetActivePreset(own.Id);

            studio = await Open();
            Click(Named<Button>(studio, "Delete"));
            await Until(() => studio.OwnedWindows.Any());
            Click(Assert.IsType<MessageDialog>(Assert.Single(studio.OwnedWindows)).FindControl<Button>("BtnCancel")!);
            await Until(() => !studio.OwnedWindows.Any());
            Assert.Contains(own, settings.UserPersonalityPresets);
            Click(Named<Button>(studio, "Delete"));
            await Until(() => studio.OwnedWindows.Any());
            Click(Assert.IsType<MessageDialog>(Assert.Single(studio.OwnedWindows)).FindControl<Button>("BtnOk")!);
            await Until(() => !studio.IsVisible);
            Assert.DoesNotContain(own, settings.UserPersonalityPresets);
            Assert.NotEqual(own.Id, settings.ActivePersonalityPresetId);
            settingsService.SaveImmediate();
            Assert.DoesNotContain(new SettingsService().Current.UserPersonalityPresets, p => p.Id == own.Id);
        }
        finally
        {
            foreach (var child in owner.OwnedWindows.ToList()) child.Close();
            owner.RequestExit(); Dispatcher.UIThread.RunJobs();
        }
    });
}

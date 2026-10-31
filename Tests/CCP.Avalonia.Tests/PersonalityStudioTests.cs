using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PersonalityStudioTests
{
    private static Window Boot()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = true;
        settings.Current.WhatMovedCardShown = 1;
        settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        settings.Current.AwarenessModeEnabled = false;
        settings.Current.UserPersonalityPresets.Clear();
        settings.Current.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
        settings.Current.CompanionPrompt.UseCustomPrompt = false;
        var owner = new Window();
        owner.Show();
        return owner;
    }
    private static string L(string key) => Loc.Get("companion_studio_" + key);
    private static TextBox Field(Window w, string label) => w.GetLogicalDescendants().OfType<TextBox>()
        .Single(b => AutomationProperties.GetName(b) == label);
    private static Button Button(Window w, string text) => w.GetLogicalDescendants().OfType<Button>()
        .Single(b => b.Content as string == text);
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));

    [Fact]
    public Task CreateUpdateCancelAndDeleteUseTheLivePreset() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var owner = Boot();
        try
        {
            var task = PersonalityStudio.Show(owner);
            var dialog = owner.OwnedWindows.Single();
            var name = Field(dialog, L("name"));
            name.Text = " ";
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(Button(dialog, L("save")).IsEnabled);
            name.Text = "Test voice";
            Field(dialog, L("voice")).Text = " ";
            Assert.False(Button(dialog, L("save")).IsEnabled);
            name.Text = " Test voice ";
            Field(dialog, L("voice")).Text = " New character ";
            Field(dialog, Loc.Get("setting_screen_awareness_reactions")).Text = " Notice the foreground ";
            Field(dialog, L("style")).Text = " Style ";
            Field(dialog, L("background")).Text = " Background ";
            Field(dialog, L("boundaries")).Text = " Bounds ";
            Assert.False(name.AcceptsReturn);
            Assert.Equal(global::Avalonia.Media.TextWrapping.NoWrap, name.TextWrapping);
            Field(dialog, L("samples")).Text = "First\nSecond\nThird";
            Click(Button(dialog, L("save")));
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            var preset = Assert.Single(CoreSettings.Current.UserPersonalityPresets);
            Assert.Equal("Test voice", preset.Name);
            Assert.Equal("New character", preset.PromptSettings!.Personality);
            Assert.Equal("Notice the foreground", preset.PromptSettings.ContextReactions);
            Assert.Equal("Style", preset.PromptSettings.OutputRules);
            Assert.Equal("Background", preset.PromptSettings.KnowledgeBase);
            Assert.Equal("Bounds", preset.PromptSettings.ExplicitReaction);
            Assert.Equal(new[] { "First", "Second" }, preset.SampleLines);
            Assert.Equal(preset.Id, CoreSettings.Current.ActivePersonalityPresetId);
            Assert.NotEqual(default, CoreSettings.Current.PersonaVoiceFenceUtc);

            task = PersonalityStudio.Show(owner);
            dialog = owner.OwnedWindows.Single();
            Field(dialog, L("voice")).Text = "Cancelled";
            Click(Button(dialog, L("cancel")));
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("New character", preset.PromptSettings.Personality);

            task = PersonalityStudio.Show(owner);
            dialog = owner.OwnedWindows.Single();
            Field(dialog, L("voice")).Text = "Escaped";
            dialog.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
                { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Escape });
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("New character", preset.PromptSettings.Personality);

            task = PersonalityStudio.Show(owner);
            dialog = owner.OwnedWindows.Single();
            Field(dialog, L("voice")).Text = "Updated";
            Click(Button(dialog, L("update")));
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(preset, Assert.Single(CoreSettings.Current.UserPersonalityPresets));
            Assert.Equal("Updated", preset.PromptSettings.Personality);

            task = PersonalityStudio.Show(owner);
            dialog = owner.OwnedWindows.Single();
            Click(Button(dialog, L("delete")));
            var confirmation = Assert.IsType<MessageDialog>(dialog.OwnedWindows.Single());
            confirmation.Close(false);
            await Task.Yield(); // Let the confirmation continuation finish before checking cancellation.
            Assert.Single(CoreSettings.Current.UserPersonalityPresets);
            Click(Button(dialog, L("delete")));
            confirmation = Assert.IsType<MessageDialog>(dialog.OwnedWindows.Single());
            confirmation.Close(true);
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(CoreSettings.Current.UserPersonalityPresets);
            Assert.Equal(PersonalityPresets.NeutralDefaultId, CoreSettings.Current.ActivePersonalityPresetId);
        }
        finally { PersonalityStudio.CloseForPanic(); owner.Close(); CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task PersonalityNavigationOpensPickerMountedStudio() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var bootstrap = Boot();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            shell.ShowTab("companion");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var chat = shell.GetLogicalDescendants().OfType<ConversationPage>().Single();
            Click(chat.FindControl<Button>("SettingsButton")!);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("personality", shell.CurrentTab);
            var page = shell.Named<PersonalityPage>("CompanionPersonalityTab")!;
            var picker = Assert.IsType<CompanionPickerCard>(page.FindControl<ContentControl>("PickerHost")!.Content);
            var button = picker.FindControl<Button>("BtnMorePersonality")!;
            Assert.True(button.IsEffectivelyVisible);
            Click(button);
            var studio = Assert.Single(shell.OwnedWindows);
            Field(studio, L("name")).Text = "Navigation voice";
            Click(Button(studio, L("save")));
            await Task.Yield();
            Assert.Equal("Navigation voice", Assert.Single(CoreSettings.Current.UserPersonalityPresets).Name);
            shell.ShowTab("companion");
            shell.ShowTab("personality");
            Assert.Same(picker, page.FindControl<ContentControl>("PickerHost")!.Content);
            Click(button);
            Assert.Single(shell.OwnedWindows);
            PanicSurfaces.All.Single(s => s.Id == "personality-studio").Stop(shell);
            await Task.Yield();
            Assert.Empty(shell.OwnedWindows);
        }
        finally { PersonalityStudio.CloseForPanic(); shell.Close(); bootstrap.Close(); CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task DeleteConfirmationRendersLocalizedYesAndNo() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var owner = Boot();
        var language = LocalizationManager.Instance.CurrentLanguage;
        CoreSettings.Current.UserPersonalityPresets.Add(new PersonalityPreset
            { Id = "labels-own", Name = "Own", PromptSettings = new() { Personality = "Voice" } });
        CoreSettings.Current.ActivePersonalityPresetId = "labels-own";
        try
        {
            foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
            {
                LocalizationManager.Instance.SetLanguage(code);
                var task = PersonalityStudio.Show(owner);
                var studio = owner.OwnedWindows.Single();
                Click(Button(studio, L("delete")));
                var confirmation = Assert.IsType<MessageDialog>(studio.OwnedWindows.Single());
                Assert.NotEqual("btn_no", Loc.Get("btn_no"));
                Assert.NotEqual("label_yes", Loc.Get("label_yes"));
                Assert.Equal(Loc.Get("label_yes"), Assert.IsType<TextBlock>(confirmation.FindControl<Button>("BtnOk")!.Content).Text);
                Assert.Equal(Loc.Get("btn_no"), Assert.IsType<TextBlock>(confirmation.FindControl<Button>("BtnCancel")!.Content).Text);
                confirmation.Close(false);
                await Task.Yield();
                Click(Button(studio, L("cancel")));
                await task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Single(CoreSettings.Current.UserPersonalityPresets);
            }
        }
        finally { PersonalityStudio.CloseForPanic(); owner.Close(); LocalizationManager.Instance.SetLanguage(language); CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task OverrideSavesACopyAndPanicDismissesBothWindows() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var owner = Boot();
        try
        {
            var s = CoreSettings.Current;
            var own = new PersonalityPreset { Id = "test-own", Name = "Own", PromptSettings = new() { Personality = "Original" } };
            s.UserPersonalityPresets.Add(own);
            s.ActivePersonalityPresetId = own.Id;
            s.CompanionPrompt.UseCustomPrompt = true;
            s.CompanionPrompt.Personality = new string('x', 5000);
            var task = PersonalityStudio.Show(owner);
            var dialog = owner.OwnedWindows.Single();
            var voice = Field(dialog, L("voice"));
            Assert.Equal(5400, voice.MaxLength);
            Assert.Equal(5000, voice.Text!.Length);
            Assert.DoesNotContain(dialog.GetLogicalDescendants().OfType<Button>(), b => b.Content as string == L("delete"));
            Click(Button(dialog, L("save")));
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, s.UserPersonalityPresets.Count);
            Assert.Equal("Original", own.PromptSettings!.Personality);
            Assert.NotEqual(own.Id, s.ActivePersonalityPresetId);
            Assert.False(s.CompanionPrompt.UseCustomPrompt);

            task = PersonalityStudio.Show(owner);
            dialog = owner.OwnedWindows.Single();
            Click(Button(dialog, L("delete")));
            Assert.Single(dialog.OwnedWindows);
            PanicSurfaces.StopAll("test");
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(owner.OwnedWindows);
            Assert.Equal(2, s.UserPersonalityPresets.Count);
        }
        finally { PersonalityStudio.CloseForPanic(); owner.Close(); CoreSettings.ServiceProvider = null; }
    });
}

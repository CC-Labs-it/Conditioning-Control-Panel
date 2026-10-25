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
        settings.Current.HasAcceptedAgeVerification = true;
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
            name.Text = " Test voice ";
            Field(dialog, L("voice")).Text = " New character ";
            Field(dialog, Loc.Get("setting_screen_awareness_reactions")).Text = " Notice the foreground ";
            Field(dialog, L("samples")).Text = "First\nSecond\nThird";
            Click(Button(dialog, L("save")));
            await task;
            var preset = Assert.Single(CoreSettings.Current.UserPersonalityPresets);
            Assert.Equal("Test voice", preset.Name);
            Assert.Equal("New character", preset.PromptSettings!.Personality);
            Assert.Equal("Notice the foreground", preset.PromptSettings.ContextReactions);
            Assert.Equal(2, preset.SampleLines!.Count);
            Assert.Equal(preset.Id, CoreSettings.Current.ActivePersonalityPresetId);
            Assert.NotEqual(default, CoreSettings.Current.PersonaVoiceFenceUtc);

            task = PersonalityStudio.Show(owner);
            dialog = owner.OwnedWindows.Single();
            Field(dialog, L("voice")).Text = "Cancelled";
            Click(Button(dialog, L("cancel")));
            await task;
            Assert.Equal("New character", preset.PromptSettings.Personality);

            task = PersonalityStudio.Show(owner);
            dialog = owner.OwnedWindows.Single();
            Field(dialog, L("voice")).Text = "Updated";
            Click(Button(dialog, L("update")));
            await task;
            Assert.Same(preset, Assert.Single(CoreSettings.Current.UserPersonalityPresets));
            Assert.Equal("Updated", preset.PromptSettings.Personality);

            task = PersonalityStudio.Show(owner);
            dialog = owner.OwnedWindows.Single();
            Click(Button(dialog, L("delete")));
            var confirmation = Assert.IsType<MessageDialog>(dialog.OwnedWindows.Single());
            confirmation.Close(false);
            Assert.Single(CoreSettings.Current.UserPersonalityPresets);
            Click(Button(dialog, L("delete")));
            confirmation = Assert.IsType<MessageDialog>(dialog.OwnedWindows.Single());
            confirmation.Close(true);
            await task;
            Assert.Empty(CoreSettings.Current.UserPersonalityPresets);
            Assert.Equal(PersonalityPresets.NeutralDefaultId, CoreSettings.Current.ActivePersonalityPresetId);
        }
        finally { PersonalityStudio.CloseForPanic(); owner.Close(); CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task MakeHerYoursCommandOpensTheHost() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var bootstrap = Boot();
        var shell = new MainShellWindow();
        try
        {
            var view = new MakeHerYoursView();
            shell.Content = view;
            shell.Show();
            var button = view.GetLogicalDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, view.ViewModel.PersonalityStudioCommand));
            Assert.True(button.IsEffectivelyVisible);
            button.Command!.Execute(null);
            Assert.Single(shell.OwnedWindows);
            PanicSurfaces.All.Single(s => s.Id == "personality-studio").Stop(shell);
            await Task.Yield();
            Assert.Empty(shell.OwnedWindows);
        }
        finally { PersonalityStudio.CloseForPanic(); shell.Close(); bootstrap.Close(); CoreSettings.ServiceProvider = null; }
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
            await task;
            Assert.Equal(2, s.UserPersonalityPresets.Count);
            Assert.Equal("Original", own.PromptSettings!.Personality);
            Assert.NotEqual(own.Id, s.ActivePersonalityPresetId);
            Assert.False(s.CompanionPrompt.UseCustomPrompt);

            task = PersonalityStudio.Show(owner);
            dialog = owner.OwnedWindows.Single();
            Click(Button(dialog, L("delete")));
            Assert.Single(dialog.OwnedWindows);
            PanicSurfaces.StopAll("test");
            await task;
            Assert.Empty(owner.OwnedWindows);
            Assert.Equal(2, s.UserPersonalityPresets.Count);
        }
        finally { PersonalityStudio.CloseForPanic(); owner.Close(); CoreSettings.ServiceProvider = null; }
    });
}

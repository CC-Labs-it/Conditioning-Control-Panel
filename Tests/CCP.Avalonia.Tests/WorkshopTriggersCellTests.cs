using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Assets.cs:1751-1903 (phrase presets) on the Companion Workshop's Triggers cell.</summary>
public sealed class WorkshopTriggersCellTests
{
    [Fact]
    public Task SaveSwitchAndDeletePhrasePresetsLikeWpf() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var oldSettings = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var svc = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => svc;
        var s = svc.Current;
        s.PhrasePresets.Clear();
        s.CurrentPhrasePresetId = null;
        s.CustomCompanionPhrases = new()
        {
            new CustomCompanionPhrase { Id = "c1", Text = "one", Category = "Idle", Enabled = true },
            new CustomCompanionPhrase { Id = "c2", Text = "two", Category = "Idle", Enabled = true },
        };
        var cell = new WorkshopTriggersCell();
        var window = new Window { Content = cell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var combo = cell.FindControl<ComboBox>("CmbPhrasePresets")!;
            var count = cell.FindControl<TextBlock>("TxtPhraseCount")!;
            int Active() => CompanionPhraseEditorDialog.LoadPhrases().Count(p => p.IsEnabled);
            Assert.Equal(Loc.GetF("label_0_active_2", Active()), count.Text);

            string? asked = null;
            cell.AskText = (title, prompt, def) => { asked = def; return Task.FromResult<string?>("  Mine  "); };
            cell.FindControl<Button>("BtnSavePhrasePreset")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.GetF("label_asset_preset_default_0", 1), asked);
            var preset = Assert.Single(s.PhrasePresets);
            Assert.Equal("Mine", preset.Name);
            Assert.Equal(preset.Id, s.CurrentPhrasePresetId);
            Assert.Same(preset, combo.SelectedItem);

            // Change the phrase set, then pick the preset again: it restores what was saved.
            s.CustomCompanionPhrases[0].Enabled = false;
            cell.UpdatePhraseCountDisplay();
            var before = Active();
            combo.SelectedItem = null;
            combo.SelectedItem = preset;
            Assert.True(s.CustomCompanionPhrases[0].Enabled);
            Assert.Equal(before + 1, Active());
            Assert.Equal(Loc.GetF("label_0_active_2", Active()), count.Text);

            var confirmed = "";
            cell.Confirm = (title, msg) => { confirmed = msg; return Task.FromResult(true); };
            await cell.DeletePhrasePresetAsync();
            Assert.Equal(Loc.GetF("msg_delete_phrase_preset_confirm_0", "Mine"), confirmed);
            Assert.Empty(s.PhrasePresets);
            Assert.Null(s.CurrentPhrasePresetId);

            var informed = "";
            cell.Inform = (title, msg) => { informed = msg; return Task.CompletedTask; };
            await cell.DeletePhrasePresetAsync();
            Assert.Equal(Loc.Get("msg_please_select_a_preset_to_delete"), informed);
        }
        finally
        {
            window.Close();
            svc.SealForReset();
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldSettings;
        }
    });
}

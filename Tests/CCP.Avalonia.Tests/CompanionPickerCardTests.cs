using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF CompanionPickerCard: the personality combo
/// writes through the shell's explicit-content gate, sample lines follow the pick, a non-active mod
/// gets the read-only preview face, and the perk door writes CompanionPerk.</summary>
public sealed class CompanionPickerCardTests
{
    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();   // no sleeps in tests (P08); bounded by time (P73)
        }
        Assert.True(condition(), "timed out");
    }

    private static string[] Samples(CompanionPickerCard card) =>
        card.FindControl<StackPanel>("SamplePanel")!.Children.OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    [Fact]
    public Task PickingAPersonalityWritesSettingsThroughTheGate() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        CompanionPickerHostTests.Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
        s.ActiveCommunityPromptId = null;
        s.CompanionPrompt.UseCustomPrompt = false;
        s.CompanionPrompt.ExplicitContentAcknowledged = false;
        s.SlutModeEnabled = false;
        var oldPerk = s.CompanionPerk;
        var oldLevel = s.PlayerLevel;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var host = new ModManagerDialog();
            host.Show(shell);
            var card = host.FindControl<CompanionPickerCard>("CompanionCard")!;
            Assert.NotNull(card);
            Assert.Null(host.FindControl<TextBlock>("TxtCompanion"));
            Assert.Null(host.FindControl<Button>("BtnTubeFit"));
            Dispatcher.UIThread.RunJobs();
            var combo = card.FindControl<ComboBox>("CmbPersonality")!;
            Assert.True(card.FindControl<Control>("LivePanel")!.IsVisible);   // active mod: live face
            Assert.Same(shell, card.Shell);
            Assert.False(card.FindControl<Button>("BtnMorePersonality")!.IsVisible);   // no host editor set

            // The combo lists the service's presets with the active one selected.
            var all = PersonalityService.Shared.GetAllPresets();
            Assert.Equal(all.Select(p => p.Id), combo.Items.OfType<ComboBoxItem>().Select(i => (string)i.Tag!));
            Assert.Equal(PersonalityPresets.NeutralDefaultId, (combo.SelectedItem as ComboBoxItem)?.Tag);

            // A plain preset switches, is written to settings, and the sample lines follow it.
            var plain = all.First(p => p.Id != PersonalityPresets.NeutralDefaultId
                                       && !ExplicitContentGate.RequiresAcknowledgement(p, false)
                                       && PersonalitySamples.For(p).Count > 0);
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag! == plain.Id);
            await WaitFor(() => s.ActivePersonalityPresetId == plain.Id);
            await WaitFor(() => Samples(card).Length == PersonalitySamples.For(plain).Count);
            Assert.Equal(PersonalitySamples.For(plain).Select(l => "\u201C" + CoreMods.MakeModAware(l) + "\u201D"), Samples(card));

            // Spice on: an explicit preset asks first; Cancel leaves the old preset and puts the combo back.
            s.SlutModeEnabled = true;
            var spicy = all.First(p => p.Id != plain.Id && ExplicitContentGate.RequiresAcknowledgement(p, true));
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag! == spicy.Id);
            await WaitFor(() => shell.OwnedWindows.OfType<ExplicitContentAcknowledgementDialog>().Any());
            shell.OwnedWindows.OfType<ExplicitContentAcknowledgementDialog>().Single()
                .FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => (combo.SelectedItem as ComboBoxItem)?.Tag as string == plain.Id);
            Assert.Equal(plain.Id, s.ActivePersonalityPresetId);
            s.SlutModeEnabled = false;

            // The perk door writes the choice (WPF CompanionService.SetPerk's rule).
            s.PlayerLevel = 100;
            var picker = PerkPicker.Build();
            var target = Enum.GetValues<CompanionBonusType>().First(t => t != CompanionPerks.Active(s));
            picker.GetLogicalDescendants().OfType<RadioButton>().Single(r => (CompanionBonusType)r.Tag! == target).IsChecked = true;
            Assert.Equal(target, s.CompanionPerk);

            // Another mod (WPF ModManagerDialog ShowModDetails' non-active branch): the read-only preview face, its
            // ModPersonality.SampleLines first line in quotes, nothing activated.
            var active = CoreMods.ActiveModId;
            var manifest = new ModManifest
            {
                Id = "picker-preview-mod", Name = "Preview Mod",
                Identity = new ModIdentity { CompanionName = "Pip" },
                Personalities = new() { new ModPersonality { Id = "p1", Name = "Pip", SampleLines = new() { " Hello, you. ", "" } } },
            };
            card.ShowPreview(new ModPackage(manifest, null, false), global::Avalonia.Media.Colors.HotPink);
            Assert.True(card.FindControl<Control>("PreviewPanel")!.IsVisible);
            Assert.False(card.FindControl<Control>("LivePanel")!.IsVisible);
            Assert.Equal("Pip", card.FindControl<TextBlock>("TxtPreviewName")!.Text);
            Assert.Equal("\u201CHello, you.\u201D", card.FindControl<TextBlock>("TxtPreviewSample")!.Text);
            Assert.False(card.FindControl<Button>("BtnPerkChange")!.IsVisible);
            Assert.Equal(active, CoreMods.ActiveModId);
        }
        finally
        {
            s.CompanionPerk = oldPerk;
            s.PlayerLevel = oldLevel;
            s.SlutModeEnabled = false;
            s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
    });

    /// <summary>WPF AvatarSetTitle (AvatarTubeWindow.Avatar.cs:940-941): the Look label is the
    /// companion's name, not the tube's upper-cased caption.</summary>
    [Fact]
    public void LookLabelIsNotUpperCased()
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var name = CompanionDefinition.GetById(CompanionId.CultBunny).GetDisplayName(CoreSettings.Current.SlutModeEnabled);
            Assert.NotEqual(name, name.ToUpperInvariant());
            Assert.Equal(name, global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.AvatarSetTitle(4));
        }
        finally { CoreSettings.ServiceProvider = null; }
    }
}

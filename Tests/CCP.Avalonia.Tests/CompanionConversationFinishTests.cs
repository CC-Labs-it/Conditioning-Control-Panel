using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Live user paths for the remaining pages-b conversation gaps (WPF 5681c132a).</summary>
public sealed class CompanionConversationFinishTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Click(Control host, string name) =>
        host.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.True(ready(), "Timed out waiting for the UI operation");
    }

    [Fact]
    public Task ChatSettingsHostsAndRefreshesThePickerAndPanicParksDust() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.AwarenessModeEnabled) = (true, true, false);
        (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
        s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
        var previousPerk = s.CompanionPerk;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.Activate();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var chat = shell.Named<CompanionTabView>("CompanionTab")!.FindControl<ConversationPage>("Conversation")!;
            var dust = chat.FindControl<AmbientFxCanvas>("StageDust")!;
            Assert.Equal(AmbientFxLayers.DustField, dust.Layers);
            await Until(() => dust.IsTicking);
            PanicSurfaces.All.Single(p => p.Id == "companion-conversation").Stop(shell);
            Assert.False(dust.IsTicking);

            Click(chat, "SettingsButton");
            Dispatcher.UIThread.RunJobs();
            var page = shell.Named<PersonalityPage>("CompanionPersonalityTab")!;
            var picker = Assert.IsType<CompanionPickerCard>(page.FindControl<ContentControl>("PickerHost")!.Content);
            Assert.True(picker.IsEffectivelyVisible);
            Assert.False(dust.IsTicking);
            Click(picker, "BtnMorePersonality");
            Dispatcher.UIThread.RunJobs();
            var advanced = Assert.Single(shell.OwnedWindows.OfType<CompanionPromptEditorDialog>());
            advanced.Close();
            await Until(() => !shell.OwnedWindows.Any());
            Assert.Equal(s.ActivePersonalityPresetId,
                (picker.FindControl<ComboBox>("CmbPersonality")!.SelectedItem as ComboBoxItem)?.Tag);

            // Returning to the page re-reads a changed perk without replacing the live card.
            shell.ShowTab("companion"); shell.Activate();
            Dispatcher.UIThread.RunJobs();
            await Until(() => dust.IsTicking);
            s.CompanionPerk = CompanionBonusType.XPDrain;
            shell.ShowTab("personality");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(picker, page.FindControl<ContentControl>("PickerHost")!.Content);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get(CompanionPerks.NameKey(CompanionBonusType.XPDrain)),
                picker.FindControl<TextBlock>("TxtPerk")!.Text);

            // The hosted perk door opens a real picker, registered with panic.
            Click(picker, "BtnPerkChange");
            Dispatcher.UIThread.RunJobs();
            Assert.Single(shell.OwnedWindows);
            PanicSurfaces.All.Single(p => p.Id == "companion-perk-picker").Stop(shell);
            await Until(() => !shell.OwnedWindows.Any());
        }
        finally
        {
            s.CompanionPerk = previousPerk;
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = previous;
        }
    });

    [Fact]
    public Task LinksPageEditsSaveImmediatelyAndReloadOnNavigation() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.AwarenessModeEnabled) = (true, true, false);
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.GlobalKnowledgeBaseLinks.Clear();
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.ShowTab("companionlinks");
            Dispatcher.UIThread.RunJobs();
            var page = shell.Named<LinksPage>("CompanionLinksTab")!;
            var editor = page.FindControl<KnowledgeLinksEditor>("KnowledgeLinks")!;
            Assert.True(editor.FindControl<TextBlock>("TxtKnowledgeLinksEmpty")!.IsVisible);
            await Add(editor, shell);
            Assert.Equal("A title", Assert.Single(s.GlobalKnowledgeBaseLinks).Title);
            Assert.False(editor.FindControl<TextBlock>("TxtKnowledgeLinksEmpty")!.IsVisible);

            // Cancel/panic cannot add a half-written link.
            Click(editor, "BtnAddKnowledgeLink");
            Dispatcher.UIThread.RunJobs();
            Assert.Single(shell.OwnedWindows.OfType<KnowledgeLinkEditorDialog>());
            PanicSurfaces.All.Single(p => p.Id == "companion-knowledge-editor").Stop(shell);
            await Until(() => !shell.OwnedWindows.Any());
            Assert.Single(s.GlobalKnowledgeBaseLinks);

            // Changes elsewhere appear on the next visit, without replacing the shared control.
            shell.ShowTab("companion");
            s.GlobalKnowledgeBaseLinks.Add(new KnowledgeBaseLink { Title = "Elsewhere", Url = "https://example.test/elsewhere" });
            shell.ShowTab("companionlinks");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, editor.Links.Count);
            Click(editor, "BtnRemoveKnowledgeLink");
            Dispatcher.UIThread.RunJobs();
            var notice = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("msg_please_select_a_link_to_remove"),
                notice.FindControl<TextBlock>("TxtMessage")!.Text);
            Click(notice, "BtnOk");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, s.GlobalKnowledgeBaseLinks.Count);
            editor.FindControl<ListBox>("LstKnowledgeLinks")!.SelectedIndex = 0;
            Click(editor, "BtnRemoveKnowledgeLink");
            Assert.Equal("Elsewhere", Assert.Single(s.GlobalKnowledgeBaseLinks).Title);
        }
        finally
        {
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = previous;
        }
    });

    [Fact]
    public Task PromptDialogStagesSharedLinkEditsUntilSaveAndDiscardKeepsSettings() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.CompanionPrompt.UseCustomPrompt = true;
        service.Current.GlobalKnowledgeBaseLinks.Clear();
        var owner = new Window();
        owner.Show();
        try
        {
            foreach (var save in new[] { false, true })
            {
                var dialog = new CompanionPromptEditorDialog();
                dialog.Show(owner);
                var editor = dialog.FindControl<KnowledgeLinksEditor>("KnowledgeLinks")!;
                await Add(editor, dialog);
                Assert.Empty(service.Current.GlobalKnowledgeBaseLinks);
                Click(dialog, save ? "BtnSave" : "BtnCancel");
                Dispatcher.UIThread.RunJobs();
                if (!save)
                {
                    var confirm = Assert.Single(dialog.OwnedWindows.OfType<MessageDialog>());
                    Click(confirm, "BtnOk");
                }
                await Until(() => !dialog.IsVisible);
                Assert.Equal(save ? 1 : 0, service.Current.GlobalKnowledgeBaseLinks.Count);
            }
        }
        finally
        {
            foreach (var window in owner.OwnedWindows.ToArray()) window.Close();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = previous;
        }
    });

    private static async Task Add(KnowledgeLinksEditor editor, Window owner)
    {
        var count = editor.Links.Count;
        Click(editor, "BtnAddKnowledgeLink");
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(owner.OwnedWindows.OfType<KnowledgeLinkEditorDialog>());
        Click(dialog, "BtnAdd");
        Assert.True(dialog.FindControl<TextBlock>("TxtError")!.IsVisible);
        dialog.FindControl<TextBox>("TxtUrl")!.Text = " https://example.test/content ";
        dialog.FindControl<TextBox>("TxtTitle")!.Text = " A title ";
        dialog.FindControl<TextBox>("TxtDescription")!.Text = " Description ";
        Click(dialog, "BtnAdd");
        await Until(() => editor.Links.Count == count + 1);
        Assert.Equal(("https://example.test/content", "A title", "Description"),
            (editor.Links.Last().Url, editor.Links.Last().Title, editor.Links.Last().Description));
    }
}

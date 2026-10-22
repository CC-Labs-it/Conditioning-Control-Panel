using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using Fx = ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// companion-v2-pages-c: Companion > Links' second card (WPF LinksPage.xaml "Links the companion
/// knows": KnowledgeLinksEditor SaveImmediately, reloaded by OnShown) and the chat portrait's dust
/// (WPF ConversationPage ctor StageDust.StartLayers(DustField)).
/// </summary>
public sealed class CompanionLinksKnownCardTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task LinksPageEditsTheGlobalKnowledgeLinksAtOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);   // P52
        s.AwarenessModeEnabled = false;
        s.GlobalKnowledgeBaseLinks.Clear();
        s.GlobalKnowledgeBaseLinks.Add(new KnowledgeBaseLink { Title = "Wiki", Url = "https://example.org/wiki" });

        var w = new MainShellWindow { Width = 1400, Height = 800 };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var strip = w.Named<SectionTabStrip>("SectionStrip")!;
            w.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            strip.PillFor("companionlinks")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var editor = w.Named<LinksPage>("CompanionLinksTab")!.FindControl<KnowledgeLinksEditor>("KnowledgeLinks")!;
            var list = editor.FindControl<ListBox>("LstKnowledgeLinks")!;
            var empty = editor.FindControl<TextBlock>("TxtKnowledgeLinksEmpty")!;

            // OnShown loads the settings' links; the empty hint hides.
            Assert.True(editor.IsEffectivelyVisible);
            Assert.True(editor.SaveImmediately);
            Assert.Equal("Wiki", Assert.Single(editor.Links).Title);
            Assert.False(empty.IsVisible);

            // Add: the dialog's result lands in settings at once (SaveImmediately).
            var added = new KnowledgeBaseLink { Title = "Guide", Url = "https://example.org/guide" };
            editor.PromptForLink = _ => Task.FromResult<KnowledgeBaseLink?>(added);
            editor.FindControl<Button>("BtnAddKnowledgeLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Wiki", "Guide" }, s.GlobalKnowledgeBaseLinks.Select(l => l.Title));

            // Remove selected: gone from settings too, and the empty hint returns at zero.
            list.SelectedItem = editor.Links[0];
            editor.FindControl<Button>("BtnRemoveKnowledgeLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            list.SelectedItem = editor.Links[0];
            editor.FindControl<Button>("BtnRemoveKnowledgeLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(s.GlobalKnowledgeBaseLinks);
            Assert.True(empty.IsVisible);
        }
        finally
        {
            s.GlobalKnowledgeBaseLinks.Clear();
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            w.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ConversationPortraitCarriesTheDustField() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var page = new ConversationPage();
        var dust = page.FindControl<Fx.AmbientFxCanvas>("StageDust")!;
        Assert.Equal(Fx.AmbientFxLayers.DustField, dust.Layers);
        // WPF ConversationPage.xaml.cs:29: Intensity 0.8, DustDensity 0.65, tint #F486D3.
        Assert.Equal(0.8, dust.Config.Intensity);
        Assert.Equal(0.65, dust.Config.DustDensity);
        Assert.Equal(global::Avalonia.Media.Color.FromRgb(0xF4, 0x86, 0xD3), dust.Config.Tint);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PromptEditorDialogHostsTheSharedEditorAndSavesWithTheDialog() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        // WPF CompanionPromptEditorDialog.xaml:381 hosts KnowledgeLinksEditor; Save calls SaveToSettings.
        EnsureApp();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.GlobalKnowledgeBaseLinks.Clear();
        s.GlobalKnowledgeBaseLinks.Add(new KnowledgeBaseLink { Title = "Wiki", Url = "https://example.org/wiki" });
        try
        {
            var dlg = new ConditioningControlPanel.Avalonia.Views.Dialogs.CompanionPromptEditorDialog();
            var editor = dlg.FindControl<KnowledgeLinksEditor>("KnowledgeLinks")!;
            Assert.False(editor.SaveImmediately);
            Assert.Equal("Wiki", Assert.Single(editor.Links).Title);

            editor.PromptForLink = _ => Task.FromResult<KnowledgeBaseLink?>(new KnowledgeBaseLink { Title = "Guide", Url = "https://example.org/guide" });
            editor.FindControl<Button>("BtnAddKnowledgeLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Wiki" }, s.GlobalKnowledgeBaseLinks.Select(l => l.Title));   // not until Save

            dlg.FindControl<Button>("BtnSave")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Wiki", "Guide" }, s.GlobalKnowledgeBaseLinks.Select(l => l.Title));
        }
        finally
        {
            s.GlobalKnowledgeBaseLinks.Clear();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}

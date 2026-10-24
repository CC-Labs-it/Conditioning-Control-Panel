using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionEditorTriggerPortTests
{
    private static void Setup()
    {
        var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
        if (Application.Current == null)
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        else builder.AfterSetupCallback(builder);
    }

    private static void Click(Window dialog, string name) =>
        dialog.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledCustomOpensOnPersonaAndOnlyForkSavesEdits(bool fork) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var prompt = service.Current.CompanionPrompt;
            prompt.UseCustomPrompt = false;
            prompt.Personality = "old stored voice";
            prompt.ContextReactions = "old stored reactions";
            var dialog = new CompanionPromptEditorDialog();
            Assert.NotEqual(prompt.Personality, dialog.FindControl<TextBox>("TxtPersonality")!.Text);
            Assert.NotEqual(prompt.ContextReactions, dialog.FindControl<TextBox>("TxtContextReactions")!.Text);
            if (fork)
            {
                dialog.FindControl<CheckBox>("ChkUseCustom")!.IsChecked = true;
                dialog.FindControl<TextBox>("TxtContextReactions")!.Text = "new reactions";
            }
            Click(dialog, "BtnSave");
            Assert.Equal(fork ? "" : "old stored voice", prompt.Personality);
            Assert.Equal(fork ? "new reactions" : "old stored reactions", prompt.ContextReactions);
        }
        finally { CoreSettings.ServiceProvider = old; }
    });

    [Fact]
    public void SilentMarksTrackAddAndKeepUnspokenListsUnmarked() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var data = new Dictionary<string, bool> { ["RECORDED"] = true, ["MISSING"] = false };
        var dialog = new TextEditorDialog("Spoken", data);
        var rows = dialog.FindControl<ItemsControl>("ItemList")!.Items.OfType<TextItem>().ToArray();
        Assert.All(rows, x => Assert.False(x.IsSilent));
        Assert.False(dialog.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);
        dialog.MarkSilentLines(x => x == "RECORDED");
        Assert.True(rows.Single(x => x.Text == "MISSING").IsSilent); // disabled rows still marked
        Assert.False(rows.Single(x => x.Text == "RECORDED").IsSilent);
        Assert.True(dialog.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);
        dialog.FindControl<TextBox>("TxtNewItem")!.Text = "new line";
        Click(dialog, "BtnAdd");
        Assert.True(dialog.FindControl<ItemsControl>("ItemList")!.Items.OfType<TextItem>().Single(x => x.Text == "NEW LINE").IsSilent);
        dialog.MarkSilentLines(_ => true);
        Assert.False(dialog.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);
        Click(dialog, "BtnSave");
        Assert.False(dialog.ResultData!["MISSING"]); // marking never changes enablement
    });

    [Fact]
    public void WhisperHintUsesOnlyEnabledLinesAndFollowsToggle() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var s = service.Current;
            s.SubAudioEnabled = true;
            s.SubliminalPool = new() { ["no-clip-" + Guid.NewGuid()] = false };
            var panel = new SubliminalFeatureControl();
            var host = new Window { Content = panel };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var hint = panel.FindControl<TextBlock>("TxtWhispersSilentHint")!;
            Assert.False(hint.IsVisible);
            s.SubliminalPool[s.SubliminalPool.Keys.Single()] = true;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest { Id = "test" }, "", false));
            Dispatcher.UIThread.RunJobs();
            Assert.True(hint.IsVisible);
            panel.FindControl<CheckBox>("ChkWhispers")!.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(hint.IsVisible);
            host.Close();
        }
        finally { CoreSettings.ServiceProvider = old; }
    });
}

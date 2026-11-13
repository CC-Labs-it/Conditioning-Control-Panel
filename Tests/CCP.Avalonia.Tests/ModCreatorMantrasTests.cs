using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ConditioningControlPanel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Tests;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ModCreatorMantrasTests {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object Field(object o, string name) => o.GetType().GetField(name, Flags)!.GetValue(o)!;
    private static object? Call(object o, string name, params object?[] args) => o.GetType().GetMethod(name, Flags)!.Invoke(o, args);
    private static void Click(Control c, string text) => c.GetLogicalDescendants().OfType<Button>()
        .First(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Text(object card, string field, string value) => ((TextBox)Field(card, field)).Text = value;
    private static void Run(Action<ModCreatorWindow, string> test) => AvaloniaTestDispatcher.Run(() => {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-mantras-" + Guid.NewGuid()); Directory.CreateDirectory(dir); var w = new ModCreatorWindow();
        try { test(w, dir); }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(dir, true); }
    });
    [Fact]
    public void AuthorExportReloadAndReset() => Run((w, dir) => {
        Click(w, "+ Add mantra"); Click(w, "+ Add mantra"); Click(w, "+ Add retry line"); Click(w, "+ Add timeout line");
        var cards = (IList)Field(w, "_mantraCards"); var a = cards[0]!; var b = cards[1]!;
        Text(a, "PhraseBox", "obey"); Text(a, "PromptBox", "Please obey"); Text(a, "ResponseBox", "Good");
        Text(b, "PhraseBox", "obey"); Text(b, "PromptBox", "I obey"); Text(b, "ResponseBox", "Yes");
        ((CheckBox)Field(b, "EnabledCheck")).IsChecked = false;
        var promptAudio = Field(a, "PromptAudio");
        Assert.False(((Button)Field(promptAudio, "PlayBtn")).IsVisible); Assert.False(((Button)Field(promptAudio, "ClearBtn")).IsVisible);
        var source = Path.Combine(dir, "source.wav"); File.WriteAllText(source, "clip");
        Call(w, "AttachMantraAudio", Field(a, "PromptAudio"), source, "obey", "prompt");
        Assert.True(((Button)Field(promptAudio, "PlayBtn")).IsVisible); Assert.True(((Button)Field(promptAudio, "ClearBtn")).IsVisible);
        Call(w, "AttachMantraAudio", Field(a, "ResponseAudio"), source, "obey", "response");
        Call(w, "AttachMantraAudio", Field(b, "PromptAudio"), source, "obey", "prompt");
        Text(((IList)Field(w, "_mantraRetryRows"))[0]!, "TextBox", "Again"); Text(((IList)Field(w, "_mantraTimeoutRows"))[0]!, "TextBox", "Waiting");
        var stops = 0; var old = CoreAudio.PlayStoppableProvider;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => () => stops++;
        try { var play = (Button)Field(Field(a, "PromptAudio"), "PlayBtn"); play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, stops); }
        finally { CoreAudio.PlayStoppableProvider = old; }
        ((Task)Call(w, "WriteSideFilesTo", dir)!).GetAwaiter().GetResult();
        var audio = Path.Combine(dir, "sounds", "companion_audio");
        var set = JsonConvert.DeserializeObject<MantraSet>(File.ReadAllText(Path.Combine(audio, "mantras.json")))!;
        Assert.Equal(2, set.Mantras.Count); Assert.Equal("Again", Assert.Single(set.Retry).Text);
        Assert.Equal("Waiting", Assert.Single(set.Timeout).Text); Assert.False(set.Mantras[1].Enabled);
        Assert.Equal("mantra_obey_prompt.wav", set.Mantras[0].PromptAudio); Assert.Equal("mantra_obey_prompt_2.wav", set.Mantras[1].PromptAudio);
        Assert.Equal("mantra_obey_response.wav", set.Mantras[0].ResponseAudio);
        Assert.All(new[] { set.Mantras[0].PromptAudio, set.Mantras[0].ResponseAudio, set.Mantras[1].PromptAudio },
            file => Assert.Equal("clip", File.ReadAllText(Path.Combine(audio, file!))));
        ((Button)Field(Field(a, "PromptAudio"), "ClearBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(Field(a, "PromptAudio").GetType().GetField("FileName", Flags)!.GetValue(Field(a, "PromptAudio")));
        Assert.False(((Button)Field(promptAudio, "PlayBtn")).IsVisible); Assert.False(((Button)Field(promptAudio, "ClearBtn")).IsVisible);
        w.LoadMantrasFrom(dir).GetAwaiter().GetResult(); var output = Path.Combine(dir, "out"); Assert.Empty(w.WriteMantrasTo(output));
        var reloaded = JsonConvert.DeserializeObject<MantraSet>(File.ReadAllText(Path.Combine(output, "sounds", "companion_audio", "mantras.json")))!;
        Assert.Equal("Please obey", reloaded.Mantras[0].PromptText); Assert.Equal("Good", reloaded.Mantras[0].Response);
        Assert.Equal("Again", Assert.Single(reloaded.Retry).Text); Assert.Equal("Waiting", Assert.Single(reloaded.Timeout).Text);
        Assert.Equal("clip", File.ReadAllText(Path.Combine(output, "sounds", "companion_audio", reloaded.Mantras[0].PromptAudio!)));
        set.Mantras[0].PromptAudio = "../source.wav"; set.Mantras[0].ResponseAudio = "mantras.json";
        File.WriteAllText(Path.Combine(audio, "mantras.json"), JsonConvert.SerializeObject(set)); w.LoadMantrasFrom(dir).GetAwaiter().GetResult();
        var unsafeCard = ((IList)Field(w, "_mantraCards"))[0]!; Assert.Null(Field(Field(unsafeCard, "PromptAudio"), "FileName")); Assert.Null(Field(Field(unsafeCard, "ResponseAudio"), "FileName"));
        Call(w, "ClearSideFileState"); Assert.Empty((IList)Field(w, "_mantraCards"));
        Assert.Empty((IList)Field(w, "_mantraRetryRows")); Assert.Empty((IList)Field(w, "_mantraTimeoutRows"));
    });
    [Fact]
    public void InvalidPromptShowsHintAndIsSkipped() => Run((w, dir) => {
        Click(w, "+ Add mantra"); var card = ((IList)Field(w, "_mantraCards"))[0]!;
        Text(card, "PhraseBox", "obey"); Text(card, "PromptBox", "Something else"); Dispatcher.UIThread.RunJobs();
        Assert.True(((Control)Field(card, "ContainHint")).IsVisible); Assert.Single(w.WriteMantrasTo(dir));
        Text(card, "PromptBox", "Please obey"); Dispatcher.UIThread.RunJobs();
        Assert.False(((Control)Field(card, "ContainHint")).IsVisible); Assert.Empty(w.WriteMantrasTo(dir));
    });
    [Fact]
    public void FrenchPanelAndValidationUseLocalizedCopy() {
        var loc = LocalizationManager.Instance; var previous = loc.CurrentLanguage;
        try {
            loc.SetLanguage("ja"); Run((w, _) => Assert.Equal("マントラ", w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "mantras")).Content));
            loc.SetLanguage("fr"); Run((w, dir) => {
                Assert.Contains(w.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "Relances");
                Click(w, "+ Ajouter un mantra"); var card = ((IList)Field(w, "_mantraCards"))[0]!;
                Assert.Equal("Activé", ((CheckBox)Field(card, "EnabledCheck")).Content);
                Assert.Equal("Aucun audio", ((TextBlock)Field(Field(card, "PromptAudio"), "Label")).Text);
                Text(card, "PhraseBox", "oui"); Text(card, "PromptBox", "autre"); Dispatcher.UIThread.RunJobs();
                Assert.Contains("ne contient pas la phrase", Assert.Single(w.WriteMantrasTo(dir)));
                Assert.Contains(((Control)Field(card, "ContainHint")).GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "L'invite doit contenir la phrase exacte.");
                w.Show(); var pending = (Task)Call(w, "WriteSideFilesTo", dir)!;
                var dialog = Assert.Single(w.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>());
                Assert.Equal("Mantras - Entrées ignorées", dialog.Title); Assert.Contains("ne contient pas la phrase", dialog.FindControl<TextBlock>("TxtMessage")!.Text!);
                dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); Assert.True(pending.IsCompleted);
                Call(w, "AddMantraCard", new MantraEntry { Phrase = "oui", PromptText = "oui", PromptAudio = "absent.wav" }, dir);
                var missing = ((IList)Field(w, "_mantraCards"))[1]!; Assert.Equal("absent.wav (introuvable)", ((TextBlock)Field(Field(missing, "PromptAudio"), "Label")).Text);
                Assert.False(((Button)Field(Field(missing, "PromptAudio"), "PlayBtn")).IsVisible); Assert.True(((Button)Field(Field(missing, "PromptAudio"), "ClearBtn")).IsVisible);
            });
        }
        finally { loc.SetLanguage(previous); }
    }
    [Fact]
    public void EveryLanguageContainsTheMantraKeysAndFormatArguments() {
        var path = Path.Combine(SourceRoots.RepoRoot, "CCP.Core", "Localization", "Languages");
        var english = JObject.Parse(File.ReadAllText(Path.Combine(path, "en.json")));
        var keys = english.Properties().Where(p => p.Name.StartsWith("mod_mantra_")).Select(p => p.Name).ToArray(); Assert.Equal(32, keys.Length);
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages) {
            var language = JObject.Parse(File.ReadAllText(Path.Combine(path, code + ".json")));
            foreach (var key in keys) {
                var value = language.Value<string>(key); Assert.False(string.IsNullOrWhiteSpace(value), code + ": " + key);
                if (english.Value<string>(key)!.Contains("{0}")) Assert.Contains("{0}", value!);
            }
        }
    }

}

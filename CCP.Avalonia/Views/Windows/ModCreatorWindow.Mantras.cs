using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Layout;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using System.Threading.Tasks;
using Serilog;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json;
namespace ConditioningControlPanel.Avalonia.Views.Windows {
    // Adapted from the pinned WPF Mantras panel; uses the existing head file and audio seams.
    public partial class ModCreatorWindow {
        private sealed class MantraAudioRef {
            public string? SourcePath, FileName;
            public bool Missing;
            public TextBlock Label = null!;
            public Button PlayBtn = null!, ClearBtn = null!; }
        private sealed class MantraCard {
            public string Id = "";                 // preserved from a loaded mod; generated on export otherwise
            public Border Root = null!;
            public TextBox PhraseBox = null!, PromptBox = null!;
            public DockPanel ContainHint = null!;
            public TextBox ResponseBox = null!;
            public CheckBox EnabledCheck = null!;
            public MantraAudioRef PromptAudio = null!, ResponseAudio = null!; }
        private sealed class MantraSharedLineRow {
            public StackPanel Root = null!;
            public TextBox TextBox = null!;
            public MantraAudioRef Audio = null!; }
        private readonly List<MantraCard> _mantraCards = new();
        private readonly List<MantraSharedLineRow> _mantraRetryRows = new(), _mantraTimeoutRows = new();
        private StackPanel? _mantraCardsPanel, _mantraRetryPanel, _mantraTimeoutPanel;
        private void BuildMantrasSection() {
            var panel = CreateSectionPanel("mantras"); var stack = new StackPanel();
            panel.Child = stack; stack.Children.Add(CreateSectionHeader(Loc.Get("pl6_mantra_title")));
            stack.Children.Add(CreateSectionDescription(Loc.Get("mod_mantra_intro"))); stack.Children.Add(CreateSubHeader(Loc.Get("pl6_mantra_title")));
            _mantraCardsPanel = new StackPanel(); stack.Children.Add(_mantraCardsPanel);
            var addMantraBtn = CreateMantraAddButton(Loc.Get("mod_mantra_add"));
            addMantraBtn.Click += (_, _) => AddMantraCard(null, null);
            stack.Children.Add(addMantraBtn); stack.Children.Add(CreateSubHeader(Loc.Get("mod_mantra_retry_title")));
            stack.Children.Add(CreateSectionDescription(Loc.Get("mod_mantra_retry_help"))); _mantraRetryPanel = new StackPanel();
            stack.Children.Add(_mantraRetryPanel);
            var addRetryBtn = CreateMantraAddButton(Loc.Get("mod_mantra_retry_add"));
            addRetryBtn.Click += (_, _) => AddMantraSharedLineRow(_mantraRetryPanel, _mantraRetryRows, null, null, "retry");
            stack.Children.Add(addRetryBtn); stack.Children.Add(CreateSubHeader(Loc.Get("mod_mantra_timeout_title")));
            stack.Children.Add(CreateSectionDescription(Loc.Get("mod_mantra_timeout_help"))); _mantraTimeoutPanel = new StackPanel();
            stack.Children.Add(_mantraTimeoutPanel);
            var addTimeoutBtn = CreateMantraAddButton(Loc.Get("mod_mantra_timeout_add"));
            addTimeoutBtn.Click += (_, _) => AddMantraSharedLineRow(_mantraTimeoutPanel, _mantraTimeoutRows, null, null, "timeout");
            stack.Children.Add(addTimeoutBtn); }
        private Button CreateMantraAddButton(string content) {
            var accent = CoreMods.AccentColorHex;
            return new Button {
                Content = content, Background = new SolidColorBrush(Color.Parse("#2A2A4A")),
                Foreground = new SolidColorBrush(Color.Parse(accent)), BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse(accent)), Padding = new Thickness(16, 8, 16, 8),
                Cursor = new Cursor(StandardCursorType.Hand), FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4),
            }; }
        private void AddMantraCard(MantraEntry? entry, string? companionAudioDir) {
            var card = new MantraCard { Id = entry?.Id ?? "" };
            var root = new Border {
                Background = new SolidColorBrush(Color.Parse("#232342")),
                BorderBrush = new SolidColorBrush(Color.Parse("#353560")), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 12),
                Margin = new Thickness(0, 0, 0, 10), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left,
            };
            card.Root = root;
            var body = new StackPanel();
            root.Child = body;
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.EnabledCheck = new CheckBox {
                Content = Loc.Get("session_spoiler_corner_gif_enabled"), IsChecked = entry?.Enabled ?? true,
                Foreground = new SolidColorBrush(Color.FromRgb(192, 192, 192)), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(card.EnabledCheck, 0); headerRow.Children.Add(card.EnabledCheck);
            var removeBtn = new Button {
                // WPF "\u2715 Remove" (Mantras.cs:191): the glyph becomes a Dismiss icon beside the text.
                Content = new StackPanel {
                    Orientation = Orientation.Horizontal, Spacing = 4,
                    Children = {
                        new IconGlyph { Kind = IconKind.Dismiss, Size = 10, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = Loc.Get("mod_barks_remove"), VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            removeBtn.Click += (_, _) => {
                _mantraCardsPanel?.Children.Remove(card.Root);
                _mantraCards.Remove(card);
            };
            Grid.SetColumn(removeBtn, 1); headerRow.Children.Add(removeBtn); body.Children.Add(headerRow);
            body.Children.Add(CreateFieldLabel(Loc.Get("mod_mantra_phrase_label")));
            card.PhraseBox = CreateDarkTextBox(Loc.Get("mod_mantra_phrase_example")); card.PhraseBox.Width = 350; body.Children.Add(card.PhraseBox);
            body.Children.Add(CreateFieldLabel(Loc.Get("mod_mantra_prompt_label")));
            card.PromptBox = CreateDarkTextBox(Loc.Get("mod_mantra_prompt_example"), multiline: true, height: 52); body.Children.Add(card.PromptBox);
            // WPF warning-sign prefix (Mantras.cs:221) becomes a Warning icon.
            var hintBrush = new SolidColorBrush(Color.FromRgb(255, 112, 112));
            var warnIcon = new IconGlyph { Kind = IconKind.Warning, Variant = IconVariant.Filled, Size = 11, Foreground = hintBrush, Margin = new Thickness(0, 1, 4, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(warnIcon, Dock.Left);
            card.ContainHint = new DockPanel {
                Margin = new Thickness(0, 0, 0, 4), IsVisible = false,
                Children = {
                    warnIcon,
                    new TextBlock {
                        Text = Loc.Get("mod_mantra_hint"), Foreground = hintBrush, FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            };
            body.Children.Add(card.ContainHint);
            card.PhraseBox.TextChanged += (_, _) => UpdateMantraContainHint(card);
            card.PromptBox.TextChanged += (_, _) => UpdateMantraContainHint(card);
            body.Children.Add(CreateFieldLabel(Loc.Get("mod_mantra_prompt_audio"))); card.PromptAudio = new MantraAudioRef();
            body.Children.Add(CreateMantraAudioRow(card.PromptAudio, Loc.Get("mod_mantra_prompt_picker"),
                () => GetTextBoxValue(card.PhraseBox), "prompt"));
            body.Children.Add(CreateFieldLabel(Loc.Get("mod_mantra_response_label")));
            card.ResponseBox = CreateDarkTextBox(Loc.Get("mod_mantra_response_example"), multiline: true, height: 40); body.Children.Add(card.ResponseBox);
            body.Children.Add(CreateFieldLabel(Loc.Get("mod_mantra_response_audio"))); card.ResponseAudio = new MantraAudioRef();
            body.Children.Add(CreateMantraAudioRow(card.ResponseAudio, Loc.Get("mod_mantra_response_picker"),
                () => GetTextBoxValue(card.PhraseBox), "response"));
            if (entry != null) {
                SetTextBoxValue(card.PhraseBox, entry.Phrase); SetTextBoxValue(card.PromptBox, entry.PromptText);
                SetTextBoxValue(card.ResponseBox, entry.Response);
                MantraLinkLoadedAudio(card.PromptAudio, entry.PromptAudio, companionAudioDir);
                MantraLinkLoadedAudio(card.ResponseAudio, entry.ResponseAudio, companionAudioDir);
                UpdateMantraContainHint(card); }
            _mantraCards.Add(card);
            _mantraCardsPanel?.Children.Add(root); }
        private void UpdateMantraContainHint(MantraCard card) {
            var phrase = GetTextBoxValue(card.PhraseBox).Trim(); var prompt = GetTextBoxValue(card.PromptBox).Trim();
            var bad = phrase.Length > 0 && prompt.Length > 0 && !MantraPromptContainsPhrase(prompt, phrase);
            card.ContainHint.IsVisible = bad; }
        private static bool MantraPromptContainsPhrase(string promptText, string phrase)
            => promptText.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;
        private void AddMantraSharedLineRow(StackPanel? host, List<MantraSharedLineRow> rows,
            MantraLine? line, string? companionAudioDir, string role) {
            var row = new MantraSharedLineRow();
            var root = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Root = root;
            row.TextBox = CreateDarkTextBox(role == "retry" ? Loc.Get("mod_mantra_retry_example") : Loc.Get("mod_mantra_timeout_example"));
            row.TextBox.Width = 260; row.TextBox.Margin = new Thickness(0, 0, 8, 0); row.TextBox.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(row.TextBox); row.Audio = new MantraAudioRef();
            var audioRow = CreateMantraAudioRow(row.Audio, role == "retry" ? Loc.Get("mod_mantra_retry_picker") : Loc.Get("mod_mantra_timeout_picker"),
                () => GetTextBoxValue(row.TextBox), role);
            audioRow.VerticalAlignment = VerticalAlignment.Center; root.Children.Add(audioRow);
            var removeBtn = new Button {
                Content = Loc.Get("mod_barks_remove"), Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)), BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand), FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            removeBtn.Click += (_, _) => {
                host?.Children.Remove(row.Root);
                rows.Remove(row);
            };
            root.Children.Add(removeBtn);
            if (line != null) {
                SetTextBoxValue(row.TextBox, line.Text);
                MantraLinkLoadedAudio(row.Audio, line.Audio, companionAudioDir); }
            rows.Add(row);
            host?.Children.Add(root); }
        private StackPanel CreateMantraAudioRow(MantraAudioRef audio, string browseTitle,
            Func<string> slugSource, string role) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            audio.Label = new TextBlock {
                Text = Loc.Get("kwt_no_audio"), Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128)), FontSize = 11,
                FontStyle = FontStyle.Italic, VerticalAlignment = VerticalAlignment.Center, Width = 200,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            row.Children.Add(audio.Label);
            audio.PlayBtn = new Button {
                Content = new IconGlyph { Kind = IconKind.Play, Size = 11 },
                Width = 26, Height = 26, Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)), BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand), FontSize = 11, IsVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
            };
            audio.PlayBtn.Click += (_, _) => {
                if (audio.SourcePath != null && File.Exists(audio.SourcePath))
                    ToggleAudioPreview(audio.SourcePath, audio.PlayBtn);
            };
            ToolTip.SetTip(audio.PlayBtn, Loc.Get("haptics_pattern_play")); row.Children.Add(audio.PlayBtn);
            var browseBtn = new Button {
                Content = Loc.Get("btn_browse"), Background = new SolidColorBrush(Color.FromRgb(50, 50, 80)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand), FontSize = 10, Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            browseBtn.Click += async (_, _) => {
                var files = await PickFilesAsync(browseTitle, new[] { "*.mp3", "*.wav", "*.*" }, multiple: false);
                if (files.Count == 0) return;
                AttachMantraAudio(audio, files[0], slugSource(), role);
            };
            row.Children.Add(browseBtn);
            audio.ClearBtn = new Button {
                Content = new IconGlyph { Kind = IconKind.Dismiss, Size = 10 },
                Width = 22, Height = 22, Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)), BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand), FontSize = 10, Margin = new Thickness(4, 0, 0, 0),
                IsVisible = false, VerticalAlignment = VerticalAlignment.Center,
            };
            audio.ClearBtn.Click += (_, _) => {
                audio.SourcePath = null; audio.FileName = null; audio.Missing = false;
                UpdateMantraAudioUi(audio);
            };
            ToolTip.SetTip(audio.ClearBtn, Loc.Get("mod_mantra_remove_audio")); row.Children.Add(audio.ClearBtn);
            return row; }
        private void AttachMantraAudio(MantraAudioRef audio, string path, string source, string role) {
            audio.SourcePath = path; audio.Missing = false; audio.FileName = MantraAutoFileName(source, role, Path.GetExtension(path));
            UpdateMantraAudioUi(audio); }
        private static void UpdateMantraAudioUi(MantraAudioRef audio) {
            var empty = audio.FileName == null;
            var missing = !empty && audio.Missing;
            audio.Label.Text = empty ? Loc.Get("kwt_no_audio") : missing ? Loc.GetF("mod_mantra_missing_fmt", audio.FileName!) : audio.FileName;
            audio.Label.FontStyle = empty || missing ? FontStyle.Italic : FontStyle.Normal;
            audio.Label.Foreground = empty ? new SolidColorBrush(Color.FromRgb(96, 96, 128)) : missing ? new SolidColorBrush(Color.FromRgb(255, 112, 112)) : Brushes.White;
            ToolTip.SetTip(audio.Label, empty ? null : missing ? Loc.Get("mod_mantra_missing_tip") : audio.SourcePath);
            audio.PlayBtn.IsVisible = !empty && !missing;
            audio.ClearBtn.IsVisible = !empty; }
        private static void MantraLinkLoadedAudio(MantraAudioRef audio, string? fileName, string? companionAudioDir) {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\') || fileName != Path.GetFileName(fileName) || fileName.Equals("mantras.json", StringComparison.OrdinalIgnoreCase)) return;
            audio.FileName = fileName;
            var full = companionAudioDir != null ? Path.Combine(companionAudioDir, fileName) : null;
            audio.Missing = full == null || !File.Exists(full);
            audio.SourcePath = audio.Missing ? null : full;
            UpdateMantraAudioUi(audio); }
        private static string MantraSlug(string text) {
            var slug = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            if (slug.Length > 24) slug = slug.Substring(0, 24).Trim('_');
            return string.IsNullOrEmpty(slug) ? "clip" : slug; }
        private string MantraAutoFileName(string sourceText, string role, string ext) {
            if (string.IsNullOrWhiteSpace(ext)) ext = ".mp3";
            var baseName = $"mantra_{MantraSlug(sourceText)}_{role}"; var name = baseName + ext; var n = 2;
            while (MantraFileNameTaken(name))
                name = $"{baseName}_{n++}{ext}";
            return name;
        }
        private bool MantraFileNameTaken(string fileName) {
            return AllMantraAudioRefs().Any(a =>
                a.FileName != null && a.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
        private IEnumerable<MantraAudioRef> AllMantraAudioRefs() {
            foreach (var c in _mantraCards) {
                yield return c.PromptAudio;
                yield return c.ResponseAudio;
            }
            foreach (var r in _mantraRetryRows) yield return r.Audio;
            foreach (var r in _mantraTimeoutRows) yield return r.Audio;
        }
        internal List<string> WriteMantrasTo(string resourcesDir) {
            var set = new MantraSet(); var skipped = new List<string>(); var clipsToCopy = new List<MantraAudioRef>();
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in _mantraCards) {
                var phrase = GetTextBoxValue(card.PhraseBox).Trim(); var prompt = GetTextBoxValue(card.PromptBox).Trim();
                var response = GetTextBoxValue(card.ResponseBox).Trim();
                var hasAnything = phrase.Length > 0 || prompt.Length > 0 || response.Length > 0
                                  || card.PromptAudio.FileName != null || card.ResponseAudio.FileName != null;
                if (!hasAnything) continue; // blank card, ignore silently
                if (phrase.Length == 0) {
                    var label = prompt.Length > 0 ? $"\"{MantraTruncate(prompt, 40)}\"" : Loc.Get("mod_mantra_unnamed");
                    skipped.Add(Loc.GetF("mod_mantra_no_phrase", label));
                    continue;
                }
                if (prompt.Length > 0 && !MantraPromptContainsPhrase(prompt, phrase)) {
                    skipped.Add(Loc.GetF("mod_mantra_no_contain", $"\"{MantraTruncate(phrase, 40)}\""));
                    continue;
                }
                var id = !string.IsNullOrWhiteSpace(card.Id) ? card.Id : $"mantra_{MantraSlug(phrase)}"; var uniqueId = id; var n = 2;
                while (!usedIds.Add(uniqueId))
                    uniqueId = $"{id}_{n++}";
                set.Mantras.Add(new MantraEntry {
                    Id = uniqueId, Phrase = phrase,
                    PromptText = prompt.Length > 0 ? prompt : phrase, // empty prompt falls back to the phrase itself
                    PromptAudio = card.PromptAudio.FileName, Response = response,
                    ResponseAudio = card.ResponseAudio.FileName, Enabled = card.EnabledCheck.IsChecked != false,
                });
                clipsToCopy.Add(card.PromptAudio);
                clipsToCopy.Add(card.ResponseAudio);
            }
            AppendMantraSharedLines(_mantraRetryRows, set.Retry, clipsToCopy);
            AppendMantraSharedLines(_mantraTimeoutRows, set.Timeout, clipsToCopy);
            if (set.Mantras.Count == 0 && set.Retry.Count == 0 && set.Timeout.Count == 0) return skipped;
            var audioDir = Path.Combine(resourcesDir, "sounds", "companion_audio");
            Directory.CreateDirectory(audioDir);
            var json = JsonConvert.SerializeObject(set, Formatting.Indented, new JsonSerializerSettings {
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(Path.Combine(audioDir, "mantras.json"), json);
            foreach (var clip in clipsToCopy) {
                if (clip.FileName == null || clip.SourcePath == null || !File.Exists(clip.SourcePath)) continue;
                var dest = Path.Combine(audioDir, clip.FileName);
                if (string.Equals(Path.GetFullPath(clip.SourcePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(clip.SourcePath, dest, overwrite: true);
            }
            return skipped;
        }
        private void AppendMantraSharedLines(List<MantraSharedLineRow> rows, List<MantraLine> target,
            List<MantraAudioRef> clipsToCopy) {
            foreach (var row in rows) {
                var text = GetTextBoxValue(row.TextBox).Trim();
                if (text.Length == 0 && row.Audio.FileName == null) continue; // blank row
                target.Add(new MantraLine { Text = text, Audio = row.Audio.FileName });
                clipsToCopy.Add(row.Audio);
            }
        }
        private static string MantraTruncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "…";
        internal async Task LoadMantrasFrom(string resourcesDir) {
            ClearMantrasSection();
            var audioDir = Path.Combine(resourcesDir, "sounds", "companion_audio"); var path = Path.Combine(audioDir, "mantras.json");
            if (!File.Exists(path)) return;
            MantraSet? set;
            try {
                set = JsonConvert.DeserializeObject<MantraSet>(File.ReadAllText(path));
            }
            catch (Exception ex) {
                Log.Warning(ex, "ModCreator: failed to parse mantras.json at {Path}", path);
                await MessageDialog.ShowAsync(this, Loc.Get("mod_mantra_load_title"), Loc.GetF("mod_mantra_load_error", ex.Message));
                return;
            }
            if (set == null) return;
            foreach (var entry in set.Mantras)
                AddMantraCard(entry, audioDir);
            foreach (var line in set.Retry)
                AddMantraSharedLineRow(_mantraRetryPanel, _mantraRetryRows, line, audioDir, "retry");
            foreach (var line in set.Timeout)
                AddMantraSharedLineRow(_mantraTimeoutPanel, _mantraTimeoutRows, line, audioDir, "timeout");
        }
        private void ClearMantrasSection() {
            _mantraCards.Clear();
            _mantraRetryRows.Clear();
            _mantraTimeoutRows.Clear();
            _mantraCardsPanel?.Children.Clear(); _mantraRetryPanel?.Children.Clear(); _mantraTimeoutPanel?.Children.Clear();
        }
    }
}

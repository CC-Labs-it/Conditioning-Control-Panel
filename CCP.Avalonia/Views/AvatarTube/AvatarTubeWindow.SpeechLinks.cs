// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs (WPF 7.2):
// BuildLinkedInlines :1363-1561 and ChatHistoryText_Loaded :1572-1576. One link builder for the
// live bubble (plain, preset and trigger lines) and every chat history row (restack b197, #4364).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private static readonly Regex MarkdownLink = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex MalformedLink = new(@"\s*\[Url\]|\s*\(url\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RawUrl = new(@"https?://[^\s,""'<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>WPF :1413-1440: the active mod's live pool (https only), then the full built-in
        /// catalogue. Read live, so a Workshop save or a mod switch shows at once.</summary>
        private static Dictionary<string, string> SpeechLinkTable()
        {
            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (CoreMods.Service?.GetVideoLinks() is { } pool)
                foreach (var (title, url) in pool)
                    if (!string.IsNullOrEmpty(title) && Uri.TryCreate(url, UriKind.Absolute, out var link) && link.Scheme == Uri.UriSchemeHttps)
                        table[title] = url;
            foreach (var (title, url) in HypnotubeDefaultLinks.KnownVideoTitles)
                table.TryAdd(title, url);
            return table;
        }

        /// <summary>Markdown links first (their URL is authoritative), then known titles longest first,
        /// fuzzy near-miss titles (shown under the real pool title), then bare URLs (shown under the
        /// pool title or a slug title). Clicks go through the shared CompanionLinkLauncher.</summary>
        internal void RenderSpeechLinks(TextBlock target, string? text)
        {
            text ??= "";
            var markdown = new List<(string Label, string Url)>();
            text = MarkdownLink.Replace(text, m =>
            {
                var url = m.Groups[2].Value.Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    markdown.Add((m.Groups[1].Value, url));
                return m.Groups[1].Value;
            });
            text = MalformedLink.Replace(text, "");

            var positions = new List<(int Start, int Length, string Name, string Url)>();
            bool Overlaps(int start, int length) => positions.Any(p => start < p.Start + p.Length && start + length > p.Start);
            foreach (var (label, url) in markdown)
            {
                var at = text.IndexOf(label, StringComparison.Ordinal);
                if (at >= 0 && !Overlaps(at, label.Length)) positions.Add((at, label.Length, label, url));
            }
            var table = SpeechLinkTable();
            foreach (var (title, url) in table.OrderByDescending(p => p.Key.Length))
            {
                var at = text.IndexOf(title, StringComparison.OrdinalIgnoreCase);
                if (at >= 0 && !Overlaps(at, title.Length)) positions.Add((at, title.Length, title, url));
            }
            var entries = table.Select(p => (Title: p.Key, Url: p.Value)).ToList();
            foreach (var (start, length, _) in CompanionTitleMatcher.CandidateSpans(text))
            {
                if (length < CompanionTitleMatcher.MinSpanLength || Overlaps(start, length)) continue;
                if (CompanionTitleMatcher.BestFuzzy(text.Substring(start, length), entries) is { } fuzzy)
                    positions.Add((start, length, fuzzy.Title, fuzzy.Url));
            }
            foreach (Match match in RawUrl.Matches(text))
                if (!Overlaps(match.Index, match.Length)) positions.Add((match.Index, match.Length, match.Value, match.Value));

            target.Inlines?.Clear();
            if (positions.Count == 0) { target.Text = text; return; }
            target.Text = "";
            target.Inlines ??= new InlineCollection();
            var last = 0;
            foreach (var (start, length, name, url) in positions.OrderBy(p => p.Start))
            {
                if (start > last) target.Inlines.Add(new Run(text[last..start]));
                var actual = text.Substring(start, length);
                var label = actual;
                if (actual.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    label = table.FirstOrDefault(p => string.Equals(p.Value, url, StringComparison.OrdinalIgnoreCase)).Key
                        ?? global::ConditioningControlPanel.Helpers.HtUrlHelper.DeriveTitleFromUrl(url);
                else if (!string.Equals(name, actual, StringComparison.OrdinalIgnoreCase))
                    label = name;   // a fuzzy hit shows the real pool title
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    target.Inlines.Add(new InlineUIContainer(SpeechLink(target, label, uri)) { BaselineAlignment = BaselineAlignment.Baseline });
                else target.Inlines.Add(new Run(label));
                last = start + length;
            }
            if (last < text.Length) target.Inlines.Add(new Run(text[last..]));
        }

        private static Control SpeechLink(TextBlock target, string label, Uri uri)
        {
            var link = new global::ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton
            {
                // A TextBlock, not a string: Avalonia reads "_" in string Content as an access key.
                Content = new TextBlock { Text = label, FontSize = target.FontSize, FontFamily = target.FontFamily,
                                          FontWeight = target.FontWeight, TextWrapping = TextWrapping.Wrap },
                Padding = new(0), FontSize = target.FontSize, FontFamily = target.FontFamily,
                Foreground = Brush.Parse("#ffb0e0"),
                Command = Controls.Companion.CompanionLinkLauncher.CommandFor(uri.AbsoluteUri)
            };
            // An inline control measures as one block: cap it at the line width so a long title wraps.
            link.Bind(Layoutable.MaxWidthProperty, new Binding(nameof(Bounds))
            {
                Source = target,
                Converter = new FuncValueConverter<Rect, double>(b =>
                    b.Width > 0 ? Math.Max(1, b.Width - target.Padding.Left - target.Padding.Right) : 320)
            });
            return link;
        }

        /// <summary>WPF :1572-1576: a history row carries its text in Tag and renders through the same builder.</summary>
        private void ChatHistoryText_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBlock row && row.Tag is string text) RenderSpeechLinks(row, text);
        }
    }
}

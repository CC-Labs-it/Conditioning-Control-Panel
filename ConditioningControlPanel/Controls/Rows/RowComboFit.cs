using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace ConditioningControlPanel.Controls.Rows
{
    /// <summary>
    /// Readable Rows (7.4 review, wave 6): a combo on a rows-look row shows its longest value whole.
    /// <c>RowComboBoxStyle</c> draws values in 14 Bold Nunito, about 1.4x wider than the compact face it
    /// replaced, while most row combos kept the fixed Width that fitted the old face ("Default (follow
    /// multi-monitor)" cut at the arrow on three effect pages). One rule instead of 37 hand widths:
    /// <list type="bullet">
    /// <item>The box grows (MinWidth, which beats a fixed Width) to its widest item, measured in its own
    /// face, whatever filled the items (XAML, code, monitor or device lists) and in whatever language.</item>
    /// <item>It never takes more than <see cref="RowShare"/> of its row (nor <see cref="HardCap"/>), so the
    /// label column keeps its room; the authored Width stays the floor.</item>
    /// <item>Only past that cap (a long device or file name, a narrow window) the value ends in an
    /// ellipsis and the box's tool tip carries the whole value above the box's own tip.</item>
    /// </list>
    /// A box that stretches across its column (device pickers) is never widened; it only trims.
    /// </summary>
    public static class RowComboFit
    {
        /// <summary>Presenter margin 8+2, arrow column 18, border 1+1 and one pixel of rounding.</summary>
        public const double Chrome = 8 + 2 + 18 + 2 + 1;

        /// <summary>Most of its row a box may take, so the label keeps at least the rest.</summary>
        public const double RowShare = 0.6;

        /// <summary>Most a box may take on any row.</summary>
        public const double HardCap = 360;

        public static readonly DependencyProperty FitProperty = DependencyProperty.RegisterAttached(
            "Fit", typeof(bool), typeof(RowComboFit), new PropertyMetadata(false, OnFitChanged));

        public static bool GetFit(DependencyObject d) => (bool)d.GetValue(FitProperty);
        public static void SetFit(DependencyObject d, bool value) => d.SetValue(FitProperty, value);

        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State", typeof(FitState), typeof(RowComboFit), new PropertyMetadata(null));

        private sealed class FitState
        {
            public ComboBox Box = null!;
            public double OriginalMinWidth;
            public FrameworkElement? Row;
            public bool OwnTip;
            public object? AuthoredTip;           // value or BindingBase the box wore before we set ours
            public bool Queued;
            public EventHandler? OnLanguage;
        }

        private static void OnFitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboBox c || e.NewValue is not true || c.GetValue(StateProperty) != null) return;
            var st = new FitState { Box = c, OriginalMinWidth = c.MinWidth };
            c.SetValue(StateProperty, st);
            ((INotifyCollectionChanged)c.Items).CollectionChanged += (_, _) => Queue(st);
            c.SelectionChanged += (_, _) => Queue(st);
            c.SizeChanged += (_, _) => Apply(st);
            c.Loaded += (_, _) =>
            {
                // The page re-reads its {loc:Str} item bindings on a language switch without touching
                // the item list, so the box re-measures then too. Subscribed while loaded only.
                if (st.OnLanguage == null)
                {
                    st.OnLanguage = (_, _) => Queue(st);
                    Localization.LocalizationManager.Instance.LanguageChanged += st.OnLanguage;
                }
                Apply(st);
            };
            c.Unloaded += (_, _) =>
            {
                if (st.OnLanguage != null) Localization.LocalizationManager.Instance.LanguageChanged -= st.OnLanguage;
                st.OnLanguage = null;
            };
        }

        private static void Queue(FitState st)
        {
            if (st.Queued) return;
            st.Queued = true;
            st.Box.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                st.Queued = false;
                Apply(st);
            }));
        }

        /// <summary>Re-measures now (tests, and every size change).</summary>
        public static void Refresh(ComboBox c)
        {
            if (c.GetValue(StateProperty) is FitState st) Apply(st);
        }

        private static void Apply(FitState st)
        {
            var c = st.Box;
            try
            {
                if (!GetFit(c)) return;
                HookRow(st);
                double need = Need(c);
                bool stretches = double.IsNaN(c.Width) && c.HorizontalAlignment == HorizontalAlignment.Stretch;
                if (!stretches)
                {
                    double min = Math.Max(st.OriginalMinWidth, Math.Min(need, Cap(c)));
                    if (Math.Abs(c.MinWidth - min) > 0.5) c.MinWidth = min;
                }
                Trim(c);
                Tip(st);
            }
            catch (Exception ex) { App.Logger?.Debug("RowComboFit: {E}", ex.Message); }
        }

        /// <summary>The row's width bounds the box, so a row that changes width re-measures it.</summary>
        private static void HookRow(FitState st)
        {
            var row = VisualTreeHelper.GetParent(st.Box) as FrameworkElement ?? st.Box.Parent as FrameworkElement;
            if (ReferenceEquals(row, st.Row)) return;
            if (st.Row != null) st.Row.SizeChanged -= RowSizeChanged;
            st.Row = row;
            if (row != null) row.SizeChanged += RowSizeChanged;
        }

        private static void RowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged || sender is not Panel p) return;
            foreach (var child in p.Children.OfType<ComboBox>())
                if (child.GetValue(StateProperty) is FitState st && ReferenceEquals(st.Row, p)) Queue(st);
        }

        /// <summary>
        /// The widest the box may grow: <see cref="RowShare"/> of a Grid row it sits in (the label keeps
        /// the rest), and never past <see cref="HardCap"/>. A box in a StackPanel line only meets the hard cap.
        /// </summary>
        internal static double Cap(ComboBox c)
        {
            double cap = HardCap;
            if (VisualTreeHelper.GetParent(c) is Grid g && g.ActualWidth > 0)
            {
                cap = Math.Min(cap, g.ActualWidth * RowShare);
                // ...and never so wide that the rest of the row loses what it needs: a label in a star
                // column can wrap down to its longest word, one in an Auto column keeps its width, and
                // every other part of the row (a refresh button, a value) keeps its own.
                double others = c.Margin.Left + c.Margin.Right;
                bool measured = true;
                foreach (UIElement child in g.Children)
                {
                    if (ReferenceEquals(child, c) || child is not FrameworkElement fe || fe.Visibility == Visibility.Collapsed) continue;
                    if (Grid.GetRow(fe) != Grid.GetRow(c)) continue;
                    double margins = fe.Margin.Left + fe.Margin.Right;
                    if (fe is RowLabel label && InStarColumn(g, label) && label.TextPart is { } tb && !string.IsNullOrEmpty(tb.Text))
                    {
                        if (tb.ActualWidth <= 0) { measured = false; continue; }
                        others += label.ActualWidth - tb.ActualWidth + LongestWord(tb) + 2 + margins;
                    }
                    else others += fe.ActualWidth + margins;
                }
                if (measured) cap = Math.Min(cap, g.ActualWidth - others);
            }
            return cap;
        }

        private static bool InStarColumn(Grid g, UIElement el)
        {
            int col = Grid.GetColumn(el);
            return col < g.ColumnDefinitions.Count ? g.ColumnDefinitions[col].Width.IsStar : g.ColumnDefinitions.Count == 0;
        }

        private static double LongestWord(TextBlock tb)
        {
            double word = 0;
            var face = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            foreach (var w in tb.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                word = Math.Max(word, new FormattedText(w, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    face, tb.FontSize, Brushes.White, 1.0).Width);
            return word;
        }

        /// <summary>The width the widest item needs, box chrome included.</summary>
        internal static double Need(ComboBox c)
        {
            double widest = 0;
            foreach (var item in c.Items) widest = Math.Max(widest, ItemWidth(c, item));
            return widest <= 0 ? 0 : Math.Ceiling(widest + Chrome);
        }

        /// <summary>What an item shows as text (ComboBoxItem content, a TextBlock, a row of them, a string,
        /// the DisplayMemberPath property), or null when it shows none.</summary>
        public static string? TextOf(ComboBox c, object? item)
        {
            object? content = item is ComboBoxItem ci ? ci.Content : item;
            switch (content)
            {
                case null: return null;
                case string s: return s;
                case TextBlock tb: return tb.Text;
                case Panel p:
                    var sb = new StringBuilder();
                    foreach (var t in p.Children.OfType<TextBlock>())
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(t.Text);
                    }
                    return sb.ToString();
                case UIElement: return null;
            }
            if (!string.IsNullOrEmpty(c.DisplayMemberPath))
            {
                var prop = content.GetType().GetProperty(c.DisplayMemberPath);
                if (prop != null) return prop.GetValue(content)?.ToString();
            }
            return content.ToString();
        }

        /// <summary>The width of an item's value in the box's face, plus any non-text part beside it
        /// (the V2 pill on the motion pickers).</summary>
        public static double ItemWidth(ComboBox c, object? item)
        {
            string? text = TextOf(c, item);
            double w = string.IsNullOrEmpty(text) ? 0 : TextWidth(c, text);
            if ((item is ComboBoxItem ci ? ci.Content : item) is Panel p)
            {
                foreach (UIElement child in p.Children)
                {
                    if (child is TextBlock) continue;
                    child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    w += child.DesiredSize.Width;
                }
            }
            return w;
        }

        public static double TextWidth(ComboBox c, string text)
        {
            var face = new Typeface(c.FontFamily, c.FontStyle, c.FontWeight, c.FontStretch);
            double dpi = 1.0;
            try { dpi = VisualTreeHelper.GetDpi(c).PixelsPerDip; } catch { }
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face,
                c.FontSize, Brushes.White, dpi);
            return ft.WidthIncludingTrailingWhitespace;
        }

        /// <summary>The room the box gives its value: its width less the chrome.</summary>
        public static double Room(ComboBox c) => c.ActualWidth - Chrome;

        /// <summary>The selection box's own TextBlock (the presenter's), so a value past the cap ends in an
        /// ellipsis instead of being cut at the arrow.</summary>
        internal static TextBlock? ValueText(ComboBox c)
        {
            var cp = Find<ContentPresenter>(c, x => ReferenceEquals(x.TemplatedParent, c));
            if (cp == null || VisualTreeHelper.GetChildrenCount(cp) == 0) return null;
            return VisualTreeHelper.GetChild(cp, 0) as TextBlock;
        }

        private static void Trim(ComboBox c)
        {
            if (ValueText(c) is { } tb && tb.TextTrimming != TextTrimming.CharacterEllipsis)
                tb.TextTrimming = TextTrimming.CharacterEllipsis;
        }

        /// <summary>True when the shown value does not fit the box (so it is trimmed).</summary>
        public static bool SelectionCut(ComboBox c)
        {
            if (c.SelectedItem == null || c.ActualWidth <= 0) return false;
            return ItemWidth(c, c.SelectedItem) > Room(c) + 0.5;
        }

        /// <summary>A trimmed value puts the whole value in the box's tool tip, above the box's own tip.</summary>
        private static void Tip(FitState st)
        {
            var c = st.Box;
            bool cut = SelectionCut(c);
            if (!cut)
            {
                if (st.OwnTip) Restore(st);
                return;
            }
            string value = TextOf(c, c.SelectedItem) ?? "";
            if (!st.OwnTip)
            {
                st.AuthoredTip = BindingOperations.GetBindingBase(c, FrameworkElement.ToolTipProperty)
                                 ?? c.ReadLocalValue(FrameworkElement.ToolTipProperty);
            }
            object? authored = st.OwnTip ? AuthoredText(st) : c.ToolTip;
            var tip = new StackPanel { MaxWidth = 420 };
            tip.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            if (authored is string a && a.Length > 0)
                tip.Children.Add(new TextBlock { Text = a, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            st.OwnTip = true;
            c.ToolTip = tip;
        }

        /// <summary>The authored tip's text now (a {loc:Str} binding re-read in the current language).</summary>
        private static string? AuthoredText(FitState st)
        {
            if (st.AuthoredTip is string s) return s;
            if (st.AuthoredTip is Binding b && b.Source is Localization.LocalizationManager lm && b.Path?.Path is { } path
                && path.StartsWith("[", StringComparison.Ordinal) && path.EndsWith("]", StringComparison.Ordinal))
                return lm[path.Substring(1, path.Length - 2)];
            return null;
        }

        private static void Restore(FitState st)
        {
            var c = st.Box;
            st.OwnTip = false;
            if (st.AuthoredTip is BindingBase bb) BindingOperations.SetBinding(c, FrameworkElement.ToolTipProperty, bb);
            else if (st.AuthoredTip == null || st.AuthoredTip == DependencyProperty.UnsetValue) c.ClearValue(FrameworkElement.ToolTipProperty);
            else c.ToolTip = st.AuthoredTip;
            st.AuthoredTip = null;
        }

        private static T? Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t && match(t)) return t;
                if (Find(child, match) is { } deeper) return deeper;
            }
            return null;
        }
    }
}

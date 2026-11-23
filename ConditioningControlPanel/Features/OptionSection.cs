using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Features
{
    /// <summary>What a section holds. Each theme wears one fixed section hue (never a hand-picked shade).</summary>
    public enum SectionTheme { Main, Look, Sound, Timing, Interaction, Advanced, Shop }

    /// <summary>
    /// A named, foldable group of option rows on a Studio page: a bold title in the theme's hue,
    /// a chevron, a short "2 on" summary while closed, and a body that eases open and shut.
    /// Code-only on purpose, like <see cref="MoreFold"/>: a UserControl with its own XAML is a
    /// namescope and WPF would then refuse x:Name on the host's rows inside it. The rows written
    /// inside it in XAML land in <see cref="Rows"/> (the body panel), so their names, handlers
    /// and own Visibility stay the host's; only the body is shown or hidden.
    /// Open or closed is remembered per <see cref="Key"/> on this PC (OptionSectionState).
    /// </summary>
    [ContentProperty(nameof(Rows))]
    public class OptionSection : StackPanel, IAddChild
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(OptionSection),
            new PropertyMetadata(string.Empty, (d, e) => ((OptionSection)d).OnTitleChanged()));

        public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
            nameof(Theme), typeof(SectionTheme), typeof(OptionSection),
            new PropertyMetadata(SectionTheme.Main, (d, _) => ((OptionSection)d).ApplyHue()));

        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
            nameof(IsOpen), typeof(bool), typeof(OptionSection),
            new PropertyMetadata(true, (d, e) => ((OptionSection)d).OnIsOpenChanged((bool)e.NewValue)));

        /// <summary>The title, usually <c>{loc:Str section_*}</c> (a live binding).</summary>
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

        public SectionTheme Theme { get => (SectionTheme)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }

        public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

        /// <summary>Stable id the open state is remembered under, e.g. "bubblepop.gaze". Empty = not remembered.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Open on a PC that never touched this section.</summary>
        public bool DefaultOpen { get; set; } = true;

        /// <summary>The section's rows (XAML content goes here).</summary>
        public UIElementCollection Rows => _body.Children;

        private readonly Border _header;
        private readonly TextBlock _title = new();
        private readonly TextBlock _summary = new();
        private readonly Path _chevron;
        private readonly RotateTransform _chevronTurn = new(90);
        private readonly Border _rule;
        private readonly Border _clip;
        private readonly StackPanel _body = new();
        private bool _restored;
        private int _gen;

        internal const int OpenMs = 220, CloseMs = 170;

        public OptionSection()
        {
            _rule = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 0) };
            _rule.SetResourceReference(Border.BackgroundProperty, "GlassBorderBrush");

            _chevron = new Path
            {
                Data = Geometry.Parse("M 0,0 L 4.5,4.5 L 0,9"),
                StrokeThickness = 2.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 10, Height = 10,
                Stretch = Stretch.None,
                Margin = new Thickness(1, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.3, 0.5),
                RenderTransform = _chevronTurn,
            };

            // The section name leads the rows (owner 2026-11-17, "bigger compared to the options so we got
            // hierarchy"): Readable Rows (owner 2026-11-24) sets rows at 16 Bold Nunito, so the name is 22 SemiBold Fredoka.
            _title.FontFamily = Controls.Rows.RowFonts.Fredoka;
            _title.FontSize = 22;
            _title.FontWeight = FontWeights.SemiBold;
            _title.VerticalAlignment = VerticalAlignment.Center;
            _title.TextTrimming = TextTrimming.CharacterEllipsis;

            _summary.FontSize = 11.5;
            _summary.FontWeight = FontWeights.SemiBold;
            _summary.VerticalAlignment = VerticalAlignment.Center;
            _summary.Margin = new Thickness(10, 0, 0, 0);
            _summary.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_title, 1);
            Grid.SetColumn(_summary, 2);
            row.Children.Add(_chevron);
            row.Children.Add(_title);
            row.Children.Add(_summary);

            _header = new SectionHeader(this)
            {
                Child = row,
                Background = Brushes.Transparent,   // the whole strip is the hit target
                Padding = new Thickness(0, 11, 0, 8),
                Cursor = Cursors.Hand,
                Focusable = true,
            };
            KeyboardNavigation.SetIsTabStop(_header, true);
            _header.MouseLeftButtonUp += (_, e) => { Toggle(); e.Handled = true; };
            _header.KeyDown += (_, e) =>
            {
                if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { Toggle(); e.Handled = true; }
            };
            _header.MouseEnter += (_, _) => _title.Opacity = 1;
            _header.MouseLeave += (_, _) => _title.Opacity = 0.92;
            _title.Opacity = 0.92;

            _clip = new Border { Child = _body };

            base.Children.Add(_rule);
            base.Children.Add(_header);
            base.Children.Add(_clip);

            // A toggle inside flips the closed summary live.
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => RefreshSummary()));
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => RefreshSummary()));
            // A slider or picker moved (a preset loaded while it was shut): the "changed" count follows. The row's own
            // handler runs first (it sits on the row), so the setting is already written when this reads it.
            AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((_, _) => { if (!IsOpen) RefreshSummary(); }));
            AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => { if (!IsOpen) RefreshSummary(); }));
            Loaded += (_, _) => { RestoreOnce(); OpenForChangedFold(); UpdateRule(); RefreshSummary(); };
            Initialized += (_, _) => RestoreOnce();

            ApplyHue();
            SnapTo(IsOpen);
        }

        // XAML may add content through IAddChild rather than the content property: both go to the body.
        void IAddChild.AddChild(object value)
        {
            if (value is UIElement el) _body.Children.Add(el);
            else throw new ArgumentException("OptionSection holds UIElement rows only.", nameof(value));
        }

        void IAddChild.AddText(string text) { }

        /// <summary>The fixed theme table. Keys are existing section hues, never hand-picked shades.</summary>
        internal static string HueKey(SectionTheme theme) => theme switch
        {
            SectionTheme.Look => "SectionHueDataBrush",
            SectionTheme.Sound => "SectionHueAudioBrush",
            SectionTheme.Timing => "SectionHuePerformanceBrush",
            SectionTheme.Interaction => "SectionHueDevicesBrush",
            SectionTheme.Advanced => "SectionHueNotificationsBrush",
            SectionTheme.Shop => "SectionHueStartupBrush",
            _ => "SectionHueStudioBrush",
        };

        /// <summary>The theme hue brush, held by resource reference so a mod retheme reaches the rows' <see cref="Controls.Rows.RowHue"/>.</summary>
        private static readonly DependencyProperty SectionHueBrushProperty = DependencyProperty.Register(
            "SectionHueBrush", typeof(Brush), typeof(OptionSection),
            new PropertyMetadata(null, (d, e) => ((OptionSection)d).OnSectionHueBrush(e.NewValue as Brush)));

        private void ApplyHue()
        {
            var key = HueKey(Theme);
            _title.SetResourceReference(TextBlock.ForegroundProperty, key);
            _chevron.SetResourceReference(Shape.StrokeProperty, key);
            SetResourceReference(SectionHueBrushProperty, key);
        }

        /// <summary>Readable Rows: the rows inside wear the section hue (inherited <see cref="Controls.Rows.RowHue"/>).</summary>
        private void OnSectionHueBrush(Brush? brush)
        {
            if (brush is SolidColorBrush solid) Controls.Rows.RowHue.SetHue(_body, solid.Color);
            else _body.ClearValue(Controls.Rows.RowHue.HueProperty);
        }

        private void OnTitleChanged()
        {
            _title.Text = Title ?? string.Empty;
            AutomationProperties.SetName(_header, Title ?? string.Empty);
        }

        public void Toggle()
        {
            IsOpen = !IsOpen;
            OptionSectionState.Remember(Key, IsOpen);
        }

        private void RestoreOnce()
        {
            if (_restored) return;
            _restored = true;
            bool open = OptionSectionState.Resolve(Key, DefaultOpen);
            if (open != IsOpen) { _instant = true; IsOpen = open; _instant = false; }
        }

        private bool _instant;

        /// <summary>
        /// A More options fold inside that its page opened (a row in it is off its default) opens a
        /// closed section too, unless this PC chose to close it: a changed setting is never hidden.
        /// </summary>
        private void OpenForChangedFold()
        {
            if (IsOpen || OptionSectionState.HasChoice(Key)) return;
            if (!HasOpenFold(_body)) return;
            _instant = true; IsOpen = true; _instant = false;
        }

        private static bool HasOpenFold(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is MoreFold { IsOpen: true }) return true;
                if (child is DependencyObject d && HasOpenFold(d)) return true;
            }
            return false;
        }

        private void OnIsOpenChanged(bool open)
        {
            RefreshSummary();
            if (_header != null && UIElementAutomationPeer.FromElement(_header) is SectionHeaderPeer peer)
                peer.RaiseState(!open, open);

            int ms = _instant || !IsLoaded ? 0 : DurationMs(open ? OpenMs : CloseMs);
            if (ms <= 0) { SnapTo(open); return; }
            Animate(open, ms);
        }

        /// <summary>Full motion as authored, Reduced halves, Off is instant; Gentle only lengthens (300 ms floor).</summary>
        internal static int DurationMs(int fullMs, Models.MotionLevel level, bool gentle)
        {
            int ms = Controls.NavRail.NavRailRules.Ms(fullMs, level);
            if (ms <= 0) return 0;
            return GentleVisuals.RiseMs(ms, gentle);
        }

        private static int DurationMs(int fullMs)
        {
            try { return DurationMs(fullMs, MotionFx.Level, GentleVisuals.On); }
            catch { return 0; }
        }

        private void SnapTo(bool open)
        {
            _gen++;
            _clip.BeginAnimation(HeightProperty, null);
            _body.BeginAnimation(OpacityProperty, null);
            _chevronTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            _clip.Height = double.NaN;
            _clip.ClipToBounds = false;
            _body.Opacity = 1;
            _clip.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _chevronTurn.Angle = open ? 90 : 0;
        }

        private void Animate(bool open, int ms)
        {
            int gen = ++_gen;
            double width = _clip.ActualWidth > 0 ? _clip.ActualWidth : ActualWidth;
            double from = _clip.Visibility == Visibility.Visible ? _clip.ActualHeight : 0;
            _clip.Visibility = Visibility.Visible;
            _body.Measure(new Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));
            double full = _body.DesiredSize.Height;
            double to = open ? full : 0;
            var dur = TimeSpan.FromMilliseconds(ms);
            var ease = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };

            _clip.ClipToBounds = true;
            var grow = new DoubleAnimation(from, to, dur) { EasingFunction = ease };
            grow.Completed += (_, _) =>
            {
                if (gen != _gen) return;
                SnapTo(open);
            };
            _clip.BeginAnimation(HeightProperty, grow);
            _body.BeginAnimation(OpacityProperty,
                new DoubleAnimation(open ? 0 : _body.Opacity, open ? 1 : 0, dur) { EasingFunction = ease });
            _chevronTurn.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(open ? 90 : 0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        protected override Size MeasureOverride(Size constraint)
        {
            UpdateRule();
            return base.MeasureOverride(constraint);
        }

        /// <summary>The rule above the header shows only when something sits above this section.</summary>
        private void UpdateRule()
        {
            bool above = false;
            if (Parent is Panel p)
            {
                foreach (UIElement sib in p.Children)
                {
                    if (ReferenceEquals(sib, this)) break;
                    if (sib.Visibility == Visibility.Visible) { above = true; }
                }
            }
            var vis = above ? Visibility.Visible : Visibility.Collapsed;
            if (_rule.Visibility != vis) _rule.Visibility = vis;
            var pad = new Thickness(0, above ? 11 : 4, 0, 8);
            if (_header.Padding != pad) _header.Padding = pad;
        }

        /// <summary>Closed: how many switches inside are on ("2 on") and how many sliders or pickers are off their
        /// default ("1 changed"), so a changed setting is never hidden behind a closed title.</summary>
        private void RefreshSummary()
        {
            if (_summary == null) return;
            int on = IsOpen ? 0 : SwitchesOn;
            int changed = IsOpen ? 0 : ValuesChanged;
            _summary.Text = SummaryText(on, changed,
                LocalizationManager.Instance?["studio_section_count_on"], LocalizationManager.Instance?["studio_section_count_changed"]);
        }

        /// <summary>The closed summary: "2 on", "1 changed", "2 on · 1 changed", or nothing.</summary>
        internal static string SummaryText(int on, int changed, string? onFormat, string? changedFormat)
        {
            static string Fmt(string? f, string fallback, int n)
            {
                try { return string.Format(string.IsNullOrEmpty(f) ? fallback : f, n); }
                catch (FormatException) { return string.Format(fallback, n); }
            }
            var parts = new List<string>(2);
            if (on > 0) parts.Add(Fmt(onFormat, "{0} on", on));
            if (changed > 0) parts.Add(Fmt(changedFormat, "{0} changed", changed));
            return string.Join(" · ", parts);
        }

        // ---------------------------------------------------------------- changed values

        /// <summary>
        /// The <see cref="Models.AppSettings"/> property a slider or picker row writes, e.g.
        /// <c>features:OptionSection.Setting="PinkFilterTargetMonitor"</c>. A closed section counts the rows whose
        /// setting differs from a fresh <c>new AppSettings()</c> (the real defaults, never the control's XAML value).
        /// </summary>
        public static readonly DependencyProperty SettingProperty = DependencyProperty.RegisterAttached(
            "Setting", typeof(string), typeof(OptionSection), new PropertyMetadata(null));

        public static string? GetSetting(DependencyObject d) => (string?)d.GetValue(SettingProperty);
        public static void SetSetting(DependencyObject d, string? value) => d.SetValue(SettingProperty, value);

        private static readonly Lazy<Models.AppSettings?> Defaults = new(() =>
        {
            try { return new Models.AppSettings(); } catch { return null; }
        });

        private static readonly Dictionary<string, System.Reflection.PropertyInfo?> Props = new();

        /// <summary>How many tagged rows in the body are off their default right now.</summary>
        internal int ValuesChanged
        {
            get
            {
                try { return CountChanged(_body, CurrentSettings(), Defaults.Value); }
                catch { return 0; }
            }
        }

        /// <summary>The live settings (a seam for the offscreen tests, which have no App.Settings).</summary>
        internal static Func<object?> CurrentSettings = () => App.Settings?.Current;

        /// <summary>Recount the closed summary now (tests; a page whose rows changed without a routed event).</summary>
        internal void Recount() => RefreshSummary();

        /// <summary>The closed summary as shown.</summary>
        internal string SummaryShown => _summary.Text;

        /// <summary>Rows (tagged with <see cref="SettingProperty"/>) whose setting in <paramref name="current"/> differs from
        /// <paramref name="defaults"/>, skipping rows hidden by the host. An unknown name counts as unchanged.</summary>
        internal static int CountChanged(DependencyObject root, object? current, object? defaults)
        {
            if (current == null || defaults == null) return 0;
            int n = 0;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                if (d is UIElement el && el.Visibility == Visibility.Collapsed) continue;
                if (GetSetting(d) is { Length: > 0 } name && Prop(current.GetType(), name) is { } p
                    && !SameValue(p.GetValue(current), p.GetValue(defaults))) n++;
                n += CountChanged(d, current, defaults);
            }
            return n;
        }

        private static System.Reflection.PropertyInfo? Prop(Type t, string name)
        {
            lock (Props)
            {
                if (Props.TryGetValue(name, out var p)) return p;
                p = t.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (p != null && (!p.CanRead || p.GetIndexParameters().Length > 0)) p = null;
                return Props[name] = p;
            }
        }

        /// <summary>Equal, with numbers compared as numbers (a slider writes 0.30000000000000004 for 0.3).</summary>
        internal static bool SameValue(object? a, object? b)
        {
            if (a is null || b is null) return a is null && b is null;
            if (IsNumber(a) && IsNumber(b))
            {
                double x = Convert.ToDouble(a, System.Globalization.CultureInfo.InvariantCulture);
                double y = Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture);
                return Math.Abs(x - y) <= 1e-6 * Math.Max(1, Math.Max(Math.Abs(x), Math.Abs(y)));
            }
            if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);
            return a.Equals(b);
        }

        private static bool IsNumber(object o) => o is double or float or decimal or int or long or short or byte or uint or ulong or ushort or sbyte;

        /// <summary>How many switches in the body are on.</summary>
        internal int SwitchesOn => CountOn(_body);

        /// <summary>Switches (ToggleStyle check boxes) that are on, skipping rows hidden by the host.</summary>
        internal static int CountOn(DependencyObject root)
        {
            int n = 0;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                if (d is UIElement el && el.Visibility == Visibility.Collapsed) continue;
                if (d is CheckBox cb && cb.IsChecked == true && IsSwitch(cb)) n++;
                n += CountOn(d);
            }
            return n;
        }

        private static bool IsSwitch(CheckBox cb)
        {
            if (cb.Style == null) return false;
            return ReferenceEquals(cb.Style, cb.TryFindResource("ToggleStyle"));
        }

        // ---------------------------------------------------------------- automation

        private sealed class SectionHeader : Border
        {
            private readonly OptionSection _owner;
            public SectionHeader(OptionSection owner) { _owner = owner; }
            protected override AutomationPeer OnCreateAutomationPeer() => new SectionHeaderPeer(this, _owner);
        }

        private sealed class SectionHeaderPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider
        {
            private readonly OptionSection _owner;
            public SectionHeaderPeer(FrameworkElement el, OptionSection owner) : base(el) { _owner = owner; }

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
            protected override string GetClassNameCore() => nameof(OptionSection);
            protected override bool IsKeyboardFocusableCore() => true;
            protected override string GetNameCore() => _owner.Title ?? string.Empty;

            public override object GetPattern(PatternInterface patternInterface) =>
                patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

            public ExpandCollapseState ExpandCollapseState =>
                _owner.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

            public void Expand() { if (!_owner.IsOpen) _owner.Toggle(); }
            public void Collapse() { if (_owner.IsOpen) _owner.Toggle(); }

            internal void RaiseState(bool wasOpen, bool isOpen) =>
                RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                    wasOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                    isOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    /// <summary>
    /// Where each section's open state lives: <c>AppSettings.StudioSectionsOpen</c>, this PC only
    /// (left out of backups, presets and the session journal). The rules are pure for tests.
    /// </summary>
    public static class OptionSectionState
    {
        /// <summary>The remembered choice, or the section's default when this PC never chose.</summary>
        public static bool Resolve(IReadOnlyDictionary<string, bool>? store, string? key, bool defaultOpen)
        {
            if (string.IsNullOrEmpty(key) || store == null) return defaultOpen;
            return store.TryGetValue(key, out var open) ? open : defaultOpen;
        }

        /// <summary>True when this PC ever opened or closed the section by hand.</summary>
        public static bool HasChoice(IReadOnlyDictionary<string, bool>? store, string? key) =>
            !string.IsNullOrEmpty(key) && store != null && store.ContainsKey(key);

        public static bool HasChoice(string? key)
        {
            try { return HasChoice(App.Settings?.Current?.StudioSectionsOpen, key); }
            catch { return false; }
        }

        /// <summary>Writes the choice; false when there is no key to remember it under.</summary>
        public static bool Remember(IDictionary<string, bool>? store, string? key, bool open)
        {
            if (string.IsNullOrEmpty(key) || store == null) return false;
            store[key] = open;
            return true;
        }

        public static bool Resolve(string? key, bool defaultOpen)
        {
            try { return Resolve(App.Settings?.Current?.StudioSectionsOpen, key, defaultOpen); }
            catch { return defaultOpen; }
        }

        public static void Remember(string? key, bool open)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                if (Remember(s.StudioSectionsOpen, key, open))
                    App.Settings?.Save(suppressCloudBackup: true);
            }
            catch (Exception ex) { App.Logger?.Debug("OptionSection: {E}", ex.Message); }
        }
    }
}

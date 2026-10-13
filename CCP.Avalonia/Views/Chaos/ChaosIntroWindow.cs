// PORTED from ConditioningControlPanel/Chaos/ChaosIntroWindow.cs (182 lines, code-only like the original).
using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// "The invitation" - the one-time, spoiler-free intro card shown the FIRST time the Dollhouse
    /// opens (WPF ChaosIntroWindow.cs:11-17): hero art, title, three verb lines and the rabbit line,
    /// one button. Modal over the hub; the caller marks <c>ChaosMetaState.SeenIntroGuide</c>
    /// (<see cref="ChaosHubWindow"/>, WPF ChaosHubWindow.Reveals.cs:145-153). Copy is WPF's
    /// hard-coded English, verbatim.
    /// <para>Panic closes it (PanicSurfaces "chaos-intro"); WPF had no entry, a modal just sat there.
    /// ponytail: WPF ChaosArt also looked under the user's assets folder first; only the bundled
    /// intro.png is read here.</para>
    /// </summary>
    public sealed class ChaosIntroWindow : Window
    {
        /// <summary>The open card, if any (one at a time: it is modal over the hub).</summary>
        internal static ChaosIntroWindow? Current { get; private set; }

        internal static bool IsOpen() => Current != null;

        /// <summary>PanicSurfaces "chaos-intro". Idempotent, safe when none is open.</summary>
        internal static void CloseForPanic() => Current?.Close();

        private static Bitmap? LoadHero()
        {
            try
            {
                using var s = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/chaos/guide/intro.png"));
                return Bitmap.DecodeToWidth(s, 560);
            }
            catch (Exception ex) { Log.Debug("Chaos intro art: {E}", ex.Message); return null; }   // WPF: graceful skip
        }

        public ChaosIntroWindow()
        {
            // WPF :22-29
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.Height;
            Width = 560;
            ShowInTaskbar = false;
            Title = "DOWN THE RABBIT HOLE";

            var pink = Color.FromRgb(0xE8, 0x43, 0x93);
            var stack = new StackPanel();

            // ---- hero art, rounded top (WPF :34-55) ----
            var hero = LoadHero();
            if (hero != null)
            {
                stack.Children.Add(new Border
                {
                    Height = 220,
                    CornerRadius = new CornerRadius(14, 14, 0, 0),
                    ClipToBounds = true,   // Avalonia clips to the rounded corners; WPF needed a geometry
                    Child = new Image { Source = hero, Stretch = Stretch.UniformToFill, Height = 220 },
                });
            }

            // ---- title (WPF :58-67: "🐇 DOWN THE RABBIT HOLE" with a pink glow) ----
            var title = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8,
                Margin = new Thickness(0, hero != null ? 18 : 30, 0, 2),
                Effect = new DropShadowEffect { Color = pink, BlurRadius = 18, OffsetX = 0, OffsetY = 0, Opacity = 0.8 },
            };
            title.Children.Add(new IconGlyph { Kind = IconKind.AnimalRabbit, Size = 24, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock
            {
                Text = "DOWN THE RABBIT HOLE", Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeight.Bold,
            });
            stack.Children.Add(title);
            stack.Children.Add(new TextBlock
            {
                Text = "you don't have to understand it. you just have to fall.",
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
                FontStyle = FontStyle.Italic,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16),
            });

            // ---- the rule lines (WPF :79-138) ----
            var rules = new StackPanel { Margin = new Thickness(46, 0, 46, 4) };
            void Rule(IconKind glyph, Color color, string? verb, string head, string rest)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 7) };
                row.Children.Add(new IconGlyph
                {
                    Kind = glyph, Size = 22, Width = 38, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(color),
                });
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                if (!string.IsNullOrEmpty(verb))   // the colour-coded input verb pill; the rabbit line has none
                {
                    col.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, color.R, color.G, color.B)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(7, 1, 7, 2),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, 3),
                        Child = new TextBlock
                        {
                            Text = verb, Foreground = new SolidColorBrush(color), FontWeight = FontWeight.Bold,
                            FontSize = 11, FontFamily = new FontFamily(Platform.AppFonts.Mono),   // WPF Consolas
                        },
                    });
                }
                var text = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
                text.Inlines!.Add(new Run(head) { Foreground = Brushes.White, FontWeight = FontWeight.Bold });
                text.Inlines.Add(new Run(rest) { Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xC8, 0xC8, 0xDE)) });
                col.Children.Add(text);
                row.Children.Add(col);
                rules.Children.Add(row);
            }
            Rule(IconKind.BubbleMultiple, Color.FromRgb(0xFF, 0xD0, 0xE8), "LEFT-CLICK", "pop the treats. ", "a click is enough. they feed your streak.");
            Rule(IconKind.RadioButton, Color.FromRgb(0xFF, 0xD2, 0x28), "PRESS & HOLD", "hold the burning ones. ", "press and keep pressing until they snap. let one finish its trance and it goes off.");
            Rule(IconKind.Water, Color.FromRgb(0x7A, 0xE0, 0xFF), "RIGHT-CLICK", "ripple the water. ", "a right-click near the bubbles sends out a wave — treats pop, trances snap, rabbits go flying. it takes a while to gather another.");
            Rule(IconKind.AnimalRabbit, Color.FromRgb(0xFF, 0x69, 0xB4), null, "follow the white rabbit. ", "everything else down there is yours to find out.");
            stack.Children.Add(rules);

            // ---- the one button (WPF :141-158). Own template: the theme's hover would grey the gradient. ----
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0xE8, 0x43, 0x93), 0), new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 1) },
            };
            var btn = new Button
            {
                Name = "BtnTakeMeDown",
                Margin = new Thickness(46, 16, 46, 26),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.Hand),
                Template = new FuncControlTemplate<Button>((_, _) => new Border
                {
                    Background = gradient,
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(0, 13, 0, 13),
                    Child = new TextBlock
                    {
                        Text = "i understand. take me down", FontSize = 15, FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                    },
                }),
            };
            btn.Click += (_, _) => Close(true);
            stack.Children.Add(btn);

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x2A)),
                BorderBrush = new SolidColorBrush(pink),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(16),
                Child = stack,
            };
            Content = card;

            // Soft fade-in (WPF :169-180, 420 ms SineEase out). P82: the Visual's own opacity, in [0,1].
            // A render draws the settled card; the transition needs a live clock.
            if (!RenderProof.Rendering)
            {
                card.Opacity = 0;
                card.Transitions = new Transitions
                {
                    new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = new SineEaseOut() },
                };
                Opened += (_, _) => card.Opacity = 1;
            }

            // The panic key always wins (P51): tunnel it so the focused button never takes Enter/Space
            // as a click. With the global listener up it fires on its own; otherwise route it here.
            AddHandler(KeyDownEvent, (_, e) =>
            {
                var s = CoreSettings.Current;
                // Parse, never compare names: Enter and Return are one Key value and ToString() says "Return".
                if (!s.PanicKeyEnabled || !Enum.TryParse<Key>(s.PanicKey, true, out var panic) || e.Key != panic) return;
                e.Handled = true;
                if (Platform.PanicListeners.Live) return;
                if (MainShellWindow.Current is { } shell) shell.HandlePanicKeyPress(DateTime.Now);
                else Close();
            }, RoutingStrategies.Tunnel);

            Opened += (_, _) => Current = this;
            Closed += (_, _) => { if (Current == this) Current = null; };
        }
    }
}

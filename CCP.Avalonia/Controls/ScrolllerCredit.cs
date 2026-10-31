using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// The small "via Scrolller" mark (WPF Controls/ScrolllerCreditMark.cs ScrolllerCreditBadge): 11 px,
    /// ~75% opacity, no glow, never takes input; every appearance fades (~200 ms, Gentle visuals lengthen it).
    /// </summary>
    internal sealed class ScrolllerCreditBadge : Border
    {
        internal const double ShownOpacity = 0.75;
        internal const int FadeMs = 200;

        public ScrolllerCreditBadge()
        {
            IsHitTestVisible = false;
            Focusable = false;
            Opacity = 0;
            IsVisible = false;
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(6, 2, 6, 2);
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x10, 0x14));
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Bottom;
            Child = new TextBlock { Text = ScrolllerCreditRules.Mark, FontSize = 11, Foreground = Brushes.White, IsHitTestVisible = false };
            AutomationProperties.SetName(this, ScrolllerCreditRules.Mark);
        }

        public bool Shown { get; private set; }

        /// <summary>Fade the mark in or out. Idempotent; UI thread.</summary>
        public void SetShown(bool shown)
        {
            if (Shown == shown) return;
            Shown = shown;
            var fade = TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(FadeMs));
            Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = fade } };
            if (shown) IsVisible = true;
            Opacity = shown ? ShownOpacity : 0;
            // WPF ScrolllerCreditMark.cs:58-59: collapsed once the fade-out has run.
            if (!shown) DispatcherTimer.RunOnce(() => { if (!Shown) IsVisible = false; }, fade);
        }
    }

    /// <summary>
    /// WPF AssetsTabView.xaml:459-461 / LauncherMediaDialog.xaml:105-106: "Media from Scrolller" (a link to
    /// scrolller.com) ". " and the localized line. Foreground comes from the host, the link follows it.
    /// </summary>
    public sealed class ScrolllerCreditLine : TextBlock
    {
        /// <summary>Test seam: the opener (WPF BrowserLauncher.TryOpenUrl).</summary>
        internal static Func<string, bool> Opener = ExternalOpener.Open;

        public ScrolllerCreditLine()
        {
            FontSize = 12;
            Opacity = 0.75;
            TextWrapping = TextWrapping.Wrap;
            Link = new SafeHyperlinkButton { Content = new TextBlock { Text = "Media from Scrolller" }, Padding = new Thickness(0) };
            Link[!ForegroundProperty] = this[!ForegroundProperty];
            Link.Click += (_, _) => OpenHome();
            var label = new Run();
            label.Bind(Run.TextProperty, (global::Avalonia.Data.Binding)new StrExtension("label_online_media_credit").ProvideValue(null!));
            Inlines = new InlineCollection { new InlineUIContainer(Link), new Run(". "), label };
        }

        internal SafeHyperlinkButton Link { get; }

        /// <summary>WPF ScrolllerCredit.OpenHome: refused under Strict Lock, Lockdown or a Chaos run. Never throws.</summary>
        internal static bool OpenHome()
        {
            try
            {
                if (!ScrolllerCreditRules.CanOpenLink(CoreSettings.Current?.StrictLockEnabled == true,
                        MainShellWindow.LockdownActive, ChaosRunHost.IsActive || Views.Games.GameWindow.IsDtrhRunning)) return false;
                return Opener(ScrolllerCreditRules.HomeLink);
            }
            catch (Exception ex) { Serilog.Log.Debug("ScrolllerCredit: home open failed: {E}", ex.Message); return false; }
        }
    }
}

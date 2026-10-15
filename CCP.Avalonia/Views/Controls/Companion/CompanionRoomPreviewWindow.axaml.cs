using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Views.Controls.Companion;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Debug preview harness for <see cref="CompanionRoomView"/>. See the XAML header for how it is
    /// opened and why nothing in the app opens it by itself.
    ///
    /// <para>PORTED from ConditioningControlPanel/Views/Controls/Companion/CompanionRoomPreviewWindow.xaml.cs.
    /// The strip, label, width toggle and chat/memory mock states cross. The remaining six zone
    /// mock contracts still live in WPF; see the follow-up lane in the parity ledger.</para>
    ///
    /// <para><b>Nothing constructs this window here — and nothing constructs it on WPF either.</b>
    /// Checked before assuming: its one entry point is
    /// <c>CompanionRoomPreview.MaybeShow()</c> (ConditioningControlPanel/Views/Controls/Companion/
    /// CompanionRoomPreview.cs), which is <c>#if DEBUG</c>, gated on <c>CCP_CTAB_PREVIEW=1</c>, and
    /// whose own doc comment says "Nothing calls it today"; the session driver calls
    /// <see cref="Launch"/> directly instead. So this is not an orphaned feature a user has lost —
    /// it is a debug harness that is deliberately unreachable from a normal run, and porting
    /// MaybeShow would only move the same uncalled hop across. If it ever should open from the app,
    /// the arming call belongs in App.axaml.cs's startup, exactly where WPF left the hole.</para>
    /// </summary>
    public partial class CompanionRoomPreviewWindow : Window
    {
        /// <summary>Window width used by the strip's narrow toggle — under the shelf's threshold.</summary>
        private const double NarrowWidth = 1000;

        /// <summary>…and the width it goes back to. Both sit clear of the hysteresis band.</summary>
        private const double WideWidth = 1240;

        // ponytail: the WPF page composes eight mocks; this slice seats chat, diary and attention.
        // Factories preserve the WPF strip order (MockCompanionRoomVm.cs:94-161) and create a fresh
        // exhibit on every switch. Attention: Plenty 0.72, Saving 0.30, Drained 0.0 + spent detail.
        private static AttentionGaugeViewModel Plenty() => AttentionGaugeViewModel.CreatePreview(0.72);

        private static readonly IReadOnlyDictionary<string, Func<(ChatThresholdViewModel Chat, MemoryDiaryViewModel Memory, AttentionGaugeViewModel Attention)>> Variants =
            new Dictionary<string, Func<(ChatThresholdViewModel, MemoryDiaryViewModel, AttentionGaugeViewModel)>>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = () => (ChatThresholdViewModel.CreatePreview(CompanionZoneState.Live), new(), Plenty()),
                ["freeTier"] = () => (ChatThresholdViewModel.CreatePreview(CompanionZoneState.Locked), new(), AttentionGaugeViewModel.CreatePreview(0.30)),
                ["dormant"] = () => (ChatThresholdViewModel.CreatePreview(CompanionZoneState.Dormant), MemoryDiaryViewModel.PreviewDormant(), Plenty()),
                ["empty"] = () => (ChatThresholdViewModel.CreatePreview(CompanionZoneState.Live, fresh: true), MemoryDiaryViewModel.PreviewEmpty(), Plenty()),
                ["drained"] = () => (ChatThresholdViewModel.CreatePreview(CompanionZoneState.Live), new(), AttentionGaugeViewModel.CreatePreview(0.0, "companion_attention_detail_line_spent")),
                ["disabled"] = () => (ChatThresholdViewModel.CreatePreview(CompanionZoneState.Disabled), new(), Plenty())
            };

        private readonly Dictionary<string, Button> _stateButtons = new(StringComparer.OrdinalIgnoreCase);

        public CompanionRoomPreviewWindow()
        {
            InitializeComponent();
            BuildStateStrip();
            ShowVariant(DefaultVariantKey);
        }

        /// <summary>The page state the harness opens on.</summary>
        public const string DefaultVariantKey = "default";

        /// <summary>The key currently on screen. Also rendered into the strip for the driver.</summary>
        public string CurrentVariantKey { get; private set; } = DefaultVariantKey;

        /// <summary>The composed page, for a driver that wants to poke it directly.</summary>
        public CompanionRoomView RoomView => Room;

        /// <summary>Builds, shows and returns the harness. Must be called on the UI thread.</summary>
        public static CompanionRoomPreviewWindow Launch(string? variantKey = null)
        {
            var window = new CompanionRoomPreviewWindow();
            if (!string.IsNullOrWhiteSpace(variantKey)) window.ShowVariant(variantKey!);
            window.Show();
            return window;
        }

        /// <summary>
        /// Swaps the page state. No-op on an unknown key — the current page stays exactly as it is
        /// rather than blanking, so a mistyped key in a driver script is visible but harmless.
        /// </summary>
        public bool ShowVariant(string? variantKey)
        {
            if (variantKey == null || !Variants.TryGetValue(variantKey, out var factory)) return false;
            var (chat, memory, attention) = factory();
            Room.SetPreviewZones(chat, memory, attention);
            CurrentVariantKey = variantKey;
            CurrentLabel.Text = variantKey;

            foreach (var pair in _stateButtons)
            {
                bool active = string.Equals(pair.Key, variantKey, StringComparison.OrdinalIgnoreCase);
                pair.Value.Opacity = active ? 1.0 : 0.55;
                pair.Value.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
            }
            return true;
        }

        /// <summary>One button per page state, built from the variant table rather than typed out.</summary>
        private void BuildStateStrip()
        {
            this.TryFindResource("CmpChipButtonStyle", out var chip);
            foreach (var key in Variants.Keys)
            {
                var button = new Button
                {
                    // A TextBlock, not a string: "freeTier" is safe but a Button parses "_" as an access key.
                    Content = new TextBlock { Text = Humanize(key) },
                    Tag = key,
                    Margin = new Thickness(0, 2, 8, 2),
                    Theme = chip as ControlTheme
                };
                AutomationProperties.SetAutomationId(button, "CtabPreview_" + key);
                AutomationProperties.SetName(button, key);
                button.Click += StateButton_Click;

                _stateButtons[key] = button;
                StateStrip.Children.Add(button);
            }
        }

        /// <summary>"freeTier" → "free tier". Display only; the key is what automation uses.</summary>
        private static string Humanize(string key)
        {
            var chars = new List<char>(key.Length + 2);
            foreach (char c in key)
            {
                if (char.IsUpper(c)) { chars.Add(' '); chars.Add(char.ToLowerInvariant(c)); }
                else chars.Add(c);
            }
            return new string(chars.ToArray());
        }

        private void StateButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string key }) ShowVariant(key);
        }

        /// <summary>Flips the window across the shelf's collapse threshold.</summary>
        private void WidthToggle_Click(object? sender, RoutedEventArgs e)
            => Width = Width > NarrowWidth + 1 ? NarrowWidth : WideWidth;
    }
}

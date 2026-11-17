using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>One row on the "Start here" card. <paramref name="Tab"/> is a ShowTab key;
    /// <paramref name="SettingsSection"/>, when set, opens inside the Settings page;
    /// <paramref name="Link"/>, when set, opens in the user's browser instead of navigating.</summary>
    public sealed record StartHereRow(string Id, string Glyph, string TextKey, string? Tab = null,
                                      string? SettingsSection = null, string? Link = null)
    {
        /// <summary>The panic row names the key the user actually has bound, so it reads true
        /// even after they change it and open the card again from Help.</summary>
        public string Text => Id == "panic"
            ? Loc.GetF(TextKey, StartHerePlan.PanicKeyName(App.Settings?.Current?.PanicKey))
            : Loc.Get(TextKey);

        /// <summary>Button caption: a link row opens a page, the rest show a place in the app.</summary>
        public string ButtonText => Loc.Get(Link != null ? "starthere_open" : "starthere_show");

        /// <summary>Screen-reader name of the button: the verb plus the row, so four buttons do
        /// not all read the same.</summary>
        public string ButtonName => ButtonText + ": " + Text;

        public override string ToString() => Text;
    }

    /// <summary>
    /// The card's rows and its one-time rule. Pure, so the tests read the same table the card draws.
    /// Every row names something that exists today: the #asset-packs forum (the installer ships
    /// CCP Default only), Spiral Overlay in Studio (built-in spirals, 10% opacity by default, so it
    /// works with no packs at all), the Settings section, and the panic key in Settings > Devices.
    /// </summary>
    public static class StartHerePlan
    {
        public static readonly IReadOnlyList<StartHereRow> Rows = new[]
        {
            new StartHereRow("packs", "📦", "starthere_row_packs", Link: Services.DiscordLinks.PackCatalogue),
            new StartHereRow("first", "🌀", "starthere_row_first", Tab: "studio"),
            new StartHereRow("settings", "⚙️", "starthere_row_settings", Tab: "appsettings"),
            new StartHereRow("panic", "🛑", "starthere_row_panic", Tab: "appsettings", SettingsSection: "devices"),
        };

        /// <summary>Offered once, after the first-run show, and only on a fresh install: someone
        /// who already used the app has found all four of these.</summary>
        public static bool ShouldOffer(bool alreadyShown, bool ageAccepted) => !alreadyShown && ageAccepted;

        /// <summary>The stored key name as a person reads it. "Escape" is the default.</summary>
        public static string PanicKeyName(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "Esc";
            return stored switch
            {
                "Escape" => "Esc",
                "Pause" => "Pause",
                "Scroll" => "Scroll Lock",
                _ => stored,
            };
        }
    }

    /// <summary>
    /// The first-run "Start here" card (2026-11-17). Owned, non-modal, launcher glass, the same
    /// shape as <see cref="WhatMovedCard"/>. A row's button closes the card and goes there; the
    /// pack row opens the #asset-packs forum in the browser. Replay lives in Help.
    /// </summary>
    public partial class StartHereCard : Window
    {
        private readonly Action<StartHereRow>? _go;

        public StartHereCard() : this(null) { }

        public StartHereCard(Action<StartHereRow>? go)
        {
            InitializeComponent();
            _go = go;
            Rows.ItemsSource = StartHerePlan.Rows.ToList();
        }

        private void Row_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not StartHereRow row) return;
            var go = _go;
            // A link row keeps the card open: the user comes back from the browser to the rest.
            if (row.Link == null) Close();
            try { go?.Invoke(row); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Start here: {Id} failed", row.Id); }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter closes. Escape is the panic key's job and reaches it on its own; the card
            // closes with it so the press does not leave a card over a page being torn down.
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                Close();
                if (e.Key == Key.Enter) e.Handled = true;
            }
        }
    }
}

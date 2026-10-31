using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WelcomeBackContentTests
{
    [Theory]
    [InlineData("2026-09", "2026-08", true)]
    [InlineData("2026-09", "2026-09", false)]
    [InlineData("2026-08", "2026-09", false)]
    [InlineData("", "2026-08", false)]
    [InlineData("unconfirmed", "2026-08", false)]
    [InlineData("2026-09", "", false)]
    public Task TheRealOfferCarriesReleaseNotesAndOnlyAConfirmedLaterSeason(string current, string seen, bool seasonShown)
        => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var saved = (settings.PlayerLevel, settings.CurrentSeason, settings.LastSeasonResetSeen,
            CoreAccount.UnifiedUserId, CoreReleaseContent.PatchNotesProvider, CoreReleaseContent.AppVersionProvider);
        var dir = Directory.CreateTempSubdirectory("welcome-content-").FullName;
        WelcomeBackSheetContent? offered = null;
        try
        {
            settings.PlayerLevel = 24;
            settings.CurrentSeason = current;
            settings.LastSeasonResetSeen = seen;
            CoreAccount.UnifiedUserId = "returning-account";
            CoreReleaseContent.PatchNotesProvider = null;
            CoreReleaseContent.AppVersionProvider = null;
            // Exercise the real lifecycle wiring, including when another test initialized the app first.
            new global::ConditioningControlPanel.Avalonia.App().Initialize();
            var version = typeof(global::ConditioningControlPanel.Avalonia.App).Assembly.GetName().Version!;
            var expectedVersion = $"{version.Major}.{version.Minor}.{version.Build}";
            Assert.Equal(expectedVersion, CoreReleaseContent.AppVersion);
            WelcomeBackOffer.ResetForTests();
            WelcomeBackOffer.Present = content => { offered = content; return Task.CompletedTask; };
            WelcomeBackOffer.Arm(true, dir);
            await WelcomeBackOffer.TryOfferAsync("content test");

            Assert.NotNull(offered);
            Assert.Equal(ReleaseNotes.Current, offered!.PatchNotes);
            Assert.StartsWith("v" + CoreReleaseContent.AppVersion + " ", offered.PatchNotes);
            Assert.Equal(expectedVersion, offered.VersionLabel);
            Assert.Equal(seasonShown, !string.IsNullOrWhiteSpace(offered.SeasonLine));
            if (seasonShown)
                Assert.Equal("The monthly board rolled over to 2026-09 while you were away. Your level, XP and everything you unlocked stayed.", offered.SeasonLine);
            var sheet = new WelcomeBackSheet(offered);
            Assert.True(sheet.FindControl<StackPanel>("RowWhatsNew")!.IsVisible);
            Assert.Contains(expectedVersion, sheet.FindControl<TextBlock>("TxtWhatsNewToggle")!.Text);
            Assert.Equal(offered.PatchNotes, sheet.FindControl<TextBlock>("TxtNotes")!.Text);
            Assert.False(sheet.FindControl<Border>("NotesPanel")!.IsVisible);
            sheet.FindControl<TextBlock>("TxtWhatsNewToggle")!.RaiseEvent(new KeyEventArgs
                { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.True(sheet.FindControl<Border>("NotesPanel")!.IsVisible);
            Assert.Equal(seasonShown, sheet.FindControl<TextBlock>("TxtSeasonLine")!.IsVisible);
            Assert.False(sheet.FindControl<Border>("RowRestore")!.IsVisible);
            Assert.False(sheet.FindControl<Border>("RowFlavour")!.IsVisible);
        }
        finally
        {
            WelcomeBackOffer.ResetForTests();
            (settings.PlayerLevel, settings.CurrentSeason, settings.LastSeasonResetSeen,
                CoreAccount.UnifiedUserId, CoreReleaseContent.PatchNotesProvider, CoreReleaseContent.AppVersionProvider) = saved;
            Directory.Delete(dir, true);
        }
    });
}

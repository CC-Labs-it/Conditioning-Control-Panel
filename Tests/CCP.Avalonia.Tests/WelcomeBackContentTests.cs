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
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

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
            CoreReleaseContent.PatchNotesProvider = () => ReleaseNotes.Current;
            CoreReleaseContent.AppVersionProvider = () => "7.1.5";
            WelcomeBackOffer.ResetForTests();
            WelcomeBackOffer.Present = content => { offered = content; return Task.CompletedTask; };
            WelcomeBackOffer.Arm(true, dir);
            await WelcomeBackOffer.TryOfferAsync("content test");

            Assert.NotNull(offered);
            var wpf = Path.Combine(Path.GetDirectoryName(SourcePath())!, "../../ConditioningControlPanel/Services/Update/UpdateService.cs");
            var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(wpf), "public const string CurrentPatchNotes = (@\"(?:\"\"|[^\"])*\");");
            Assert.True(match.Success);
            var literal = match.Groups[1].Value;
            Assert.Equal(literal.Substring(2, literal.Length - 3).Replace("\"\"", "\""), ReleaseNotes.Current);
            Assert.Equal(ReleaseNotes.Current, offered!.PatchNotes);
            Assert.StartsWith("v7.1.15", offered.PatchNotes);
            Assert.Equal(seasonShown, !string.IsNullOrWhiteSpace(offered.SeasonLine));
            if (seasonShown)
                Assert.Equal("The monthly board rolled over to 2026-09 while you were away. Your level, XP and everything you unlocked stayed.", offered.SeasonLine);
            var sheet = new WelcomeBackSheet(offered);
            Assert.True(sheet.FindControl<StackPanel>("RowWhatsNew")!.IsVisible);
            Assert.Contains("7.1.5", sheet.FindControl<TextBlock>("TxtWhatsNewToggle")!.Text);
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

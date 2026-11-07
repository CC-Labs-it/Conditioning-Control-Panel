using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Deeper /mine -> persistent keyed acceptance toast -> View -> library badge.
/// All HTTP is fake; TestUserDataProfile installs the isolated settings/library/secret store.</summary>
public sealed class DeeperSubmissionStatusTests
{
    private sealed class MineHandler : HttpMessageHandler
    {
        public string Status = "pending";
        public int Polls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var exchange = request.RequestUri!.AbsolutePath == "/api/auth/token-exchange";
            if (!exchange)
            {
                Assert.Equal("/api/enhancements/mine", request.RequestUri.AbsolutePath);
                Polls++;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(exchange
                    ? $"{{\"access_token\":\"test-token\",\"expires_at\":\"{DateTimeOffset.UtcNow.AddHours(1):O}\"}}"
                    : $"{{\"enhancements\":[{{\"id\":\"deeper-test\",\"status\":\"{Status}\"}}]}}"),
            });
        }
    }

    [Theory]
    [InlineData("APPROVED", true)]
    [InlineData("published", false)]
    public Task AcceptanceIsStickyUsesTheLibraryTitleOrFilenameAndViewsTheBadge(string status, bool inLibrary) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
            {
                var fonts = ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
                var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
                mappings["Segoe UI"] = mappings["Segoe UI Black"] =
                    new FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
                fonts.FontFamilyMappings = mappings;
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            }
            Directory.CreateDirectory(DeeperLocalLibrary.DefaultFolder);
            var path = Path.Combine(DeeperLocalLibrary.DefaultFolder, $"submission-{Guid.NewGuid():N}.ccpenh.json");
            if (inLibrary)
            {
                var enhancement = new Enhancement { MediaType = MediaTypes.Audio };
                enhancement.Metadata.Name = "Library display title";
                File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
            }
            var key = MainShellWindow.CanonicalCataloguePathKey(path);
            const string toastKey = "deeper_submission_accepted_deeper-test";
            var old = (AppA.Catalogue, CoreSecrets.RetrieveProvider);
            var settings = CoreSettings.Current;
            var handler = new MineHandler();
            AppA.Catalogue = new CatalogueClient(() => "test-auth", () => "test-user", "test", handler);
            CoreSecrets.RetrieveProvider = _ => null;
            var record = new DeeperSubmissionRecord { CatalogueId = "deeper-test", Status = "pending" };
            settings.DeeperSubmissions[key] = record;
            var host = new StackPanel();
            AppA.Notifications.AttachHost(host);
            var shell = new MainShellWindow();
            try
            {
                shell.Show(); // startup while signed out must not poll
                Assert.Equal(0, handler.Polls);
                var view = shell.Named<DeeperTabView>("DeeperTab")!;
                var model = (DeeperTabViewModel)view.DataContext!;
                shell.InitializeDeeperHub();
                model.SetSearch("no matching library title");
                Assert.Empty(model.FilteredEntries); // title resolution must use the full index
                CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "test-auth" : null;
                await shell.CheckDeeperSubmissionStatusesAsync(force: true);
                Assert.Equal(1, handler.Polls);
                Assert.Empty(host.Children); // pending is silent

                handler.Status = status;
                shell.ShowTab("deeper"); // repeated tab-open polls are throttled for 90 seconds
                Assert.Equal(1, handler.Polls);
                await shell.CheckDeeperSubmissionStatusesAsync(force: true);
                Assert.Equal(status, record.Status);
                Assert.True(record.AcceptedNotified);
                Assert.True(record.LastCheckedUtc > DateTime.MinValue);
                var toast = Assert.IsType<Border>(Assert.Single(host.Children));
                var expectedName = inLibrary ? "Library display title" : Path.GetFileNameWithoutExtension(path);
                Assert.Contains(toast.GetLogicalDescendants().OfType<TextBlock>(),
                    text => text.Text == Loc.GetF("deeper_submission_accepted_toast_fmt", expectedName));
                Assert.Null(toast.Tag); // regression: the old acceptance toast owned a 15 s expiry timer

                await shell.CheckDeeperSubmissionStatusesAsync(force: true);
                Assert.Equal(2, handler.Polls); // terminal, already notified: no request and no duplicate toast
                Assert.Single(host.Children);
                shell.ShowTab("settings");
                toast.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ToastAction")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(view.IsVisible);
                Assert.DoesNotContain(toastKey, settings.DismissedNotificationKeys); // View is not × dismissal
                if (inLibrary)
                {
                    model.SetSearch("");
                    var row = Assert.Single(model.FilteredEntries, r => DeeperTabViewModel.PathsEqual(r.Entry.FilePath, path));
                    Assert.True(row.ShowSubmissionBadge);
                    Assert.Equal(Loc.Get("deeper_submission_badge_published"), row.SubmissionBadgeLabel);
                }
                Assert.False(toast.IsHitTestVisible); // View closes the toast (fade removal: NotificationServiceTests)
            }
            finally
            {
                shell.Close();
                settings.DeeperSubmissions.Remove(key);
                settings.DismissedNotificationKeys.Remove(toastKey);
                (AppA.Catalogue, CoreSecrets.RetrieveProvider) = old;
                File.Delete(path);
            }
        });
}

using System;
using System.Collections.Generic;
using System.IO;
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
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using SkiaSharp;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ModCatalogueShareTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public JObject? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/api/auth/token-exchange")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    $"{{\"access_token\":\"sb\",\"expires_at\":\"{DateTimeOffset.UtcNow.AddHours(1):O}\"}}") };
            Body = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"mod-share\",\"status\":\"pending\"}") };
        }
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline) { await Task.Yield(); Dispatcher.UIThread.RunJobs(); }
        Assert.True(done());
    }

    [Fact]
    public Task ManageMods_share_requires_affirmation_and_url_then_posts_metadata_and_records_status() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var folder = Path.Combine(CorePaths.UserData, "share-mod");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "payload.txt"), "payload");
        Directory.CreateDirectory(Path.Combine(folder, "resources"));
        using (var preview = new SKBitmap(320, 160))
        {
            preview.Erase(SKColors.Magenta);
            using var image = SKImage.FromBitmap(preview);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(folder, "resources", "preview.png"), png.ToArray());
        }
        var mod = new ModPackage(new ModManifest { Id = "custom-share", Name = "Custom", Author = "Author",
            Version = "1.2", Description = "Description", PreviewImage = "preview.png", Tags = new() { "gentle", "test" } }, folder, false);
        var settings = new SettingsService();
        var oldProvider = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        settings.Current.AuthToken = "ccp-token";
        var oldCatalogue = App.Catalogue;
        var oldMods = CoreMods.InstalledModsProvider;
        var builtin = CoreMods.InstalledMods.Values.First(m => m.IsBuiltIn);
        var fake = new Fake();
        App.Catalogue = new CatalogueClient(() => settings.Current.AuthToken, () => "uid", "10.0", fake);
        CoreMods.InstalledModsProvider = () => new Dictionary<string, ModPackage> { [mod.Id] = mod, [builtin.Id] = builtin };
        var shell = new MainShellWindow();
        shell.Show();
        App.Notifications.AttachHost(shell.FindControl<Panel>("NotificationHost")!);
        var manager = new ModManagerDialog();
        _ = manager.ShowDialogSafe(shell);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var list = manager.FindControl<ListBox>("ModList")!;
            var share = manager.FindControl<Button>("BtnShare")!;
            list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag! == builtin.Id);
            Assert.False(share.IsVisible);
            list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag! == mod.Id);
            Assert.True(share.IsVisible);
            Assert.True(share.IsEnabled);
            Assert.True(manager.IsVisible);
            Assert.Same(shell, manager.Owner);
            Assert.Same(settings.Current, CoreSettings.Current);
            Assert.Equal("ccp-token", CoreSettings.Current.AuthToken);
            Click(share);
            await Until(() => manager.OwnedWindows.OfType<AssetSubmitDialog>().Any());
            Assert.False(share.IsEnabled);
            var form = manager.OwnedWindows.OfType<AssetSubmitDialog>().Single();
            Assert.Equal("gentle, test", form.FindControl<TextBox>("TxtTags")!.Text);
            Assert.Empty(fake.Paths);
            Click(form.FindControl<Button>("BtnCancel")!);
            await Until(() => share.IsEnabled);
            Assert.Empty(fake.Paths);
            // WPF refreshes the list after dismissal and reselects the active built-in mod.
            list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag! == mod.Id);
            Assert.True(share.IsVisible);

            Click(share);
            await Until(() => manager.OwnedWindows.OfType<AssetSubmitDialog>().Any());
            form = manager.OwnedWindows.OfType<AssetSubmitDialog>().Single();
            var submit = form.FindControl<Button>("BtnSubmit")!;
            Assert.False(submit.IsEnabled);
            form.FindControl<TextBox>("TxtCreator")!.Text = "Creator";
            form.FindControl<CheckBox>("ChkAffirm")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(submit.IsEnabled);
            form.FindControl<TextBox>("TxtDownloadUrl")!.Text = "https://mega.nz/file/demo#key";
            Dispatcher.UIThread.RunJobs();
            Assert.True(submit.IsEnabled);
            Click(submit);
            await Until(() => share.IsEnabled && settings.Current.CatalogueModSubmissions.ContainsKey(mod.Id));
            Assert.Equal(new[] { "/api/auth/token-exchange", "/api/catalogue/mods" }, fake.Paths);
            Assert.Equal("ccp-mod/v1", (string?)fake.Body!["bundle"]!["$schema"]);
            Assert.Equal("https://mega.nz/file/demo#key", (string?)fake.Body["bundle"]!["asset"]!["download"]!["url"]);
            Assert.Equal("custom-share", (string?)fake.Body["bundle"]!["asset"]!["mod_id"]);
            Assert.Equal("Creator", (string?)fake.Body["bundle"]!["metadata"]!["creator"]);
            Assert.Equal("mod-share", settings.Current.CatalogueModSubmissions[mod.Id].CatalogueId);
            Assert.Equal("pending", settings.Current.CatalogueModSubmissions[mod.Id].Status);
            var thumbnail = Convert.FromBase64String((string)fake.Body["bundle"]!["asset"]!["preview_thumb_b64"]!);
            using var decoded = SKBitmap.Decode(thumbnail);
            Assert.Equal(160, decoded.Width);
            Assert.Equal(80, decoded.Height);
            var row = list.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag! == mod.Id);
            Assert.Contains(row.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.EndsWith(" " + Loc.Get("catalogue_status_pending"), StringComparison.Ordinal) == true);
            Assert.Contains(shell.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("catalogue_toast_mod_success"));
            list.SelectedItem = row;
            settings.Current.AuthToken = "";
            Click(share);
            Dispatcher.UIThread.RunJobs();
            Assert.True(share.IsEnabled);
            Assert.Empty(manager.OwnedWindows);
            Assert.Equal(2, fake.Paths.Count);
            Assert.Contains(shell.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("catalogue_toast_auth_failed"));
        }
        finally
        {
            manager.Close(); shell.Close();
            App.Notifications.AttachHost(new StackPanel());
            App.Catalogue = oldCatalogue; CoreMods.InstalledModsProvider = oldMods; CoreSettings.ServiceProvider = oldProvider;
            CoreSecrets.RetrieveProvider = oldGet; CoreSecrets.StoreProvider = oldSet;
            Directory.Delete(folder, true);
        }
    });
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class FirstRunAssetsPromptTests
{
    private static readonly FieldInfo DefaultRoot = typeof(App).GetField("_defaultAssetsPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Pending = typeof(RemoteMediaOffer).GetField("_pending", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo UpdateOffering = typeof(AppUpdater).GetField("_offering", BindingFlags.Static | BindingFlags.NonPublic)!;

    private sealed class Clock
    {
        internal readonly Queue<(TimeSpan, TaskCompletionSource)> Waits = new();
        internal Task Delay(TimeSpan span)
        {
            var t = new TaskCompletionSource();
            Waits.Enqueue((span, t));
            return t.Task;
        }
        internal void Tick(int milliseconds)
        {
            var (span, t) = Waits.Dequeue();
            Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), span);
            t.SetResult();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Empty_profile_asks_once_saves_and_offers(bool yes) => Run((shell, service, root, clock) =>
    {
        var asks = 0;
        var picks = 0;
        var surfaces = new List<string>();
        shell.AskFirstRunAssets = _ => { asks++; return Task.FromResult(yes); };
        shell.PickFirstRunAssets = _ => { picks++; return Task.FromResult<string?>(null); };
        shell.OfferFirstRunAssets = (surface, owner) => { surfaces.Add(surface); RemoteMediaOffer.Offer(surface, owner); };
        shell.Show();
        service.SaveImmediate(); // clear unrelated constructor/startup saves before the answer
        Assert.Equal(0, asks);
        clock.Tick(500);
        Assert.Equal(1, asks);
        Assert.Equal(yes ? 1 : 0, picks);
        Assert.True(service.Current.FirstRunAssetsPromptShown);
        Assert.True(service.HasPendingSave);
        service.SaveImmediate();
        Assert.True((bool)JObject.Parse(File.ReadAllText(Path.Combine(CorePaths.UserData, "settings.json")))["FirstRunAssetsPromptShown"]!);
        Assert.Equal(new[] { "first-run-assets-prompt" }, surfaces);
        shell.Hide(); shell.Show(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asks);
        Assert.Single(surfaces);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("custom")]
    [InlineData("images")]
    [InlineData("videos")]
    [InlineData("spent")]
    [InlineData("fresh")]
    public Task Ineligible_profiles_never_prompt_or_offer(string reason) => Run((shell, service, root, clock) =>
    {
        // The shell is constructed after setup, so the first-run wizard can claim a fresh profile.
        Assert.False(MainShellWindow.FirstRunAssetsPromptPending);
        if (reason == "fresh") Assert.True(service.Current.FirstRunAssetsPromptShown);
        var asks = 0;
        var offers = 0;
        shell.AskFirstRunAssets = _ => { asks++; return Task.FromResult(false); };
        shell.OfferFirstRunAssets = (_, _) => offers++;
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        if (reason == "fresh") Assert.Single(shell.OwnedWindows.OfType<FirstRunWizard>());
        Assert.Empty(clock.Waits);
        Assert.Equal(0, asks);
        Assert.Equal(0, offers);
        return Task.CompletedTask;
    }, (s, root) =>
    {
        switch (reason)
        {
            case "custom": s.CustomAssetsPath = Path.Combine(root, "custom"); break;
            case "spent": s.FirstRunAssetsPromptShown = true; break;
            case "fresh": s.Welcomed = false; break;
            case "images": AddFiles(root, "images", 6, ".jpg"); break;
            case "videos": AddFiles(root, "videos", 3, ".mp4"); break;
        }
    });

    [Fact]
    public Task Real_dialog_uses_localized_yes_and_no() => Run((shell, service, root, clock) =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var oldLanguage = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("es");
            shell.Show();
            clock.Tick(500);
            var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal("Elegir carpeta de contenido", dialog.FindControl<TextBlock>("TxtTitle")!.Text);
            Assert.Contains("carpeta", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.Equal("Sí", ((TextBlock)dialog.FindControl<Button>("BtnOk")!.Content!).Text);
            Assert.Equal("No", ((TextBlock)dialog.FindControl<Button>("BtnCancel")!.Content!).Text);
            dialog.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(service.Current.FirstRunAssetsPromptShown);
        }
        finally { loc.SetLanguage(oldLanguage); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Thresholds_are_inclusive_and_picked_media_prevents_offer() => Run((shell, service, root, clock) =>
    {
        var picked = Path.Combine(root, "picked");
        AddFiles(picked, "images", 1, ".anything"); // HasAnyLocalMedia intentionally accepts any file
        var offers = 0;
        shell.AskFirstRunAssets = _ => Task.FromResult(true);
        shell.PickFirstRunAssets = _ => { service.Current.CustomAssetsPath = picked; return Task.FromResult<string?>(picked); };
        shell.OfferFirstRunAssets = (_, _) => offers++;
        shell.Show();
        clock.Tick(500);
        Assert.True(service.Current.FirstRunAssetsPromptShown);
        Assert.Equal(picked, service.Current.CustomAssetsPath);
        Assert.Equal(0, offers);
        return Task.CompletedTask;
    }, (s, root) => { AddFiles(root, "images", 5, ".PNG"); AddFiles(root, "videos", 2, ".mkv"); });

    [Fact]
    public Task Age_and_startup_modals_hold_prompt_and_pending_flush_until_offer() => Run((shell, service, root, clock) =>
    {
        var asks = 0;
        var order = new List<string>();
        RemoteMediaOffer.Park("earlier", shell);
        RemoteMediaOffer.ShowCard = _ => order.Add("card");
        shell.AskFirstRunAssets = _ => { asks++; order.Add("ask"); return Task.FromResult(false); };
        shell.OfferFirstRunAssets = (surface, owner) =>
        {
            Assert.Equal("first-run-assets-prompt", surface);
            Assert.NotNull(Pending.GetValue(null));
            order.Add("offer");
            RemoteMediaOffer.Offer(surface, owner);
        };
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        var age = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
        clock.Tick(500);
        Assert.Equal(0, asks);
        Assert.NotNull(Pending.GetValue(null));
        age.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(service.Current.HasAcceptedAgeVerification);
        Assert.NotNull(Pending.GetValue(null)); // age acceptance must not flush ahead of this prompt
        MainShellWindow.IsStartupDialogShowing = true;
        clock.Tick(100);
        Assert.Equal(0, asks);
        MainShellWindow.IsStartupDialogShowing = false;
        UpdateOffering.SetValue(null, true);
        clock.Tick(100);
        Assert.Equal(0, asks);
        UpdateOffering.SetValue(null, false);
        StartupLadder.ModalPending = true; // queued What's New, before its dialog opens
        clock.Tick(100);
        Assert.Equal(0, asks);
        StartupLadder.ResetForTests(); // isolate this offer from unrelated startup cards
        clock.Tick(100);
        Assert.Equal(1, asks);
        Assert.Equal(new[] { "ask", "offer", "card" }, order);
        Assert.Null(Pending.GetValue(null));
        return Task.CompletedTask;
    }, (s, _) => s.HasAcceptedAgeVerification = false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Fixture_restores_card_handler_even_when_body_throws(bool throws) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var original = RemoteMediaOffer.ShowCard;
        Action<Window?> sentinel = _ => { };
        RemoteMediaOffer.ShowCard = sentinel;
        try
        {
            if (throws)
                await Assert.ThrowsAsync<InvalidOperationException>(() => Run((_, _, _, _) =>
                {
                    RemoteMediaOffer.ShowCard = _ => { };
                    throw new InvalidOperationException("fixture failure");
                }));
            else
                await Age_and_startup_modals_hold_prompt_and_pending_flush_until_offer();
            Assert.Same(sentinel, RemoteMediaOffer.ShowCard);
        }
        finally { RemoteMediaOffer.ShowCard = original; }
    });

    [Fact]
    public Task Declining_age_gate_never_shows_prompt_or_flushes_offer() => Run((shell, service, root, clock) =>
    {
        var asks = 0;
        shell.AskFirstRunAssets = _ => { asks++; return Task.FromResult(false); };
        RemoteMediaOffer.Park("earlier", shell);
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        var age = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
        clock.Tick(500);
        age.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        clock.Tick(100);
        Assert.False(service.Current.HasAcceptedAgeVerification);
        Assert.False(service.Current.FirstRunAssetsPromptShown);
        Assert.Equal(0, asks);
        Assert.NotNull(Pending.GetValue(null));
        return Task.CompletedTask;
    }, (s, _) => s.HasAcceptedAgeVerification = false);

    [Fact]
    public void Prompt_copy_exists_in_all_ten_languages()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../CCP.Core/Localization/Languages"));
        var languages = new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" };
        foreach (var language in languages)
        {
            var json = JObject.Parse(File.ReadAllText(Path.Combine(dir, language + ".json")));
            Assert.False(string.IsNullOrWhiteSpace((string?)json["first_run_assets_title"]));
            Assert.False(string.IsNullOrWhiteSpace((string?)json["first_run_assets_body"]));
        }
    }

    private static void AddFiles(string root, string sub, int count, string extension)
    {
        var dir = Path.Combine(root, sub);
        Directory.CreateDirectory(dir);
        for (var i = 0; i < count; i++) File.WriteAllText(Path.Combine(dir, i + extension), "x");
    }

    private static Task Run(Func<MainShellWindow, SettingsService, string, Clock, Task> body,
        Action<ConditioningControlPanel.Models.AppSettings, string>? setup = null) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var root = Path.Combine(TestUserDataProfile.Root, "first-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var app = (App)Application.Current!;
        var oldRoot = DefaultRoot.GetValue(app);
        var oldProvider = CoreSettings.ServiceProvider;
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        var oldShowCard = RemoteMediaOffer.ShowCard;
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        var s = service.Current;
        s.PerformanceMode = true;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.FirstRunAssetsPromptShown = false;
        s.CustomAssetsPath = string.Empty;
        s.LastSeenVersion = CoreReleaseContent.AppVersion;
        s.MediaSource = "local";
        s.SeenFeatureIntros.Clear();
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        setup?.Invoke(s, root);
        CoreSettings.ServiceProvider = () => service;
        CorePaths.EffectiveAssetsProvider = () => string.IsNullOrWhiteSpace(s.CustomAssetsPath) ? root : s.CustomAssetsPath;
        DefaultRoot.SetValue(app, root);
        StartupLadder.ResetForTests();
        RemoteMediaOffer.ResetForTests();
        MainShellWindow.IsStartupDialogShowing = false;
        var clock = new Clock();
        var shell = new MainShellWindow { FirstRunAssetsDelay = clock.Delay };
        try { await body(shell, service, root, clock); }
        finally
        {
            try
            {
                foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
                shell.Close();
                Dispatcher.UIThread.RunJobs();
                service.SealForReset();
                CoreSettings.ServiceProvider = oldProvider;
                CorePaths.EffectiveAssetsProvider = oldAssets;
                DefaultRoot.SetValue(app, oldRoot);
                MainShellWindow.IsStartupDialogShowing = false;
                UpdateOffering.SetValue(null, false);
                RemoteMediaOffer.ResetForTests();
                StartupLadder.ResetForTests();
                Directory.Delete(root, true);
            }
            finally { RemoteMediaOffer.ShowCard = oldShowCard; }
        }
    });
}

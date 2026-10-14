using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class NewEnhancementSourceTests
{
    [Fact]
    public Task HypnoTubeButtonUsesTheFirstKnownTikTokVideoAndMarksTheHintSeen() => Run(() =>
    {
        var old = CoreModsHooks.KnownVideoLinksProvider;
        var seen = CoreSettings.Current.HasSeenDeeperHTInteractiveTutorial;
        var dialog = new NewEnhancementDialog();
        dialog.Show();
        try
        {
            CoreModsHooks.KnownVideoLinksProvider = () => new Dictionary<string, string>
            {
                ["Other video"] = "https://hypnotube.com/video/other.html",
                ["Custom TIKTOK title"] = "https://hypnotube.com/video/custom-tiktok.html",
                ["Second TikTok"] = "https://hypnotube.com/video/second-tiktok.html",
            };
            dialog.FindControl<Button>("BtnTryHypnoTubeTutorial")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("https://hypnotube.com/video/custom-tiktok.html", dialog.FindControl<TextBox>("TxtSource")!.Text);
            Assert.True(dialog.FindControl<RadioButton>("RbVideo")!.IsChecked);
            Assert.True(CoreSettings.Current.HasSeenDeeperHTInteractiveTutorial);
        }
        finally
        {
            dialog.Close();
            CoreModsHooks.KnownVideoLinksProvider = old;
            CoreSettings.Current.HasSeenDeeperHTInteractiveTutorial = seen;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task BrowseUsesTheRememberedFolderAndMediaFilterAndIsOwnedByTheDialog(bool video) => Run(() =>
    {
        var root = Directory.CreateTempSubdirectory("ccp-new-picker-").FullName;
        var old = CoreSettings.Current.DeeperLastDirectory;
        CoreSettings.Current.DeeperLastDirectory = root;
        var folder = DispatchProxy.Create<IStorageFolder, ItemProxy>();
        ((ItemProxy)(object)folder).PathValue = new Uri(root + Path.DirectorySeparatorChar);
        var file = DispatchProxy.Create<IStorageFile, ItemProxy>();
        ((ItemProxy)(object)file).PathValue = new Uri(Path.Combine(root, video ? "clip.mp4" : "clip.mp3"));
        var provider = DispatchProxy.Create<IStorageProvider, PickerProxy>();
        var picker = (PickerProxy)(object)provider;
        picker.Folder = folder;
        picker.Result = file;
        var factory = new Factory(provider);
        using var scope = BindStorageProvider(factory);
        var dialog = new NewEnhancementDialog();
        dialog.Show();
        try
        {
            dialog.FindControl<RadioButton>(video ? "RbVideo" : "RbAudio")!.IsChecked = true;
            dialog.FindControl<Button>("BtnBrowse")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var options = Assert.IsType<FilePickerOpenOptions>(picker.Options);
            Assert.Same(dialog, factory.Owner);
            Assert.Equal(Path.GetFullPath(root), picker.RequestedFolder!.LocalPath.TrimEnd(Path.DirectorySeparatorChar));
            Assert.Same(folder, options.SuggestedStartLocation);
            Assert.False(options.AllowMultiple);
            Assert.Equal(Loc.Get(video ? "deeper_dialog_pick_video" : "deeper_dialog_pick_audio"), options.Title);
            Assert.Equal(video ? new[] { "*.mp4", "*.webm", "*.mkv", "*.mov", "*.avi", "*.m4v" }
                : new[] { "*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac", "*.ogg" }, options.FileTypeFilter![0].Patterns);
            Assert.Equal(file.Path.LocalPath, dialog.FindControl<TextBox>("TxtSource")!.Text);
            dialog.FindControl<Button>("BtnCreate")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(video ? "video" : "audio", dialog.SelectedMediaType);
            Assert.Equal(file.Path.LocalPath, dialog.SelectedSource);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            CoreSettings.Current.DeeperLastDirectory = old;
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });

    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        await body();
    });

    // Bind the existing Avalonia native-storage seam; no second picker abstraction in the head.
    private sealed class Factory(IStorageProvider provider) : IStorageProviderFactory
    {
        internal TopLevel? Owner;
        public IStorageProvider CreateProvider(TopLevel owner) { Owner = owner; return provider; }
    }

    private static IDisposable BindStorageProvider(IStorageProviderFactory factory)
    {
        var type = typeof(AvaloniaObject).Assembly.GetType("Avalonia.AvaloniaLocator")!;
        var locator = type.GetProperty("CurrentMutable")!.GetValue(null)!;
        var scope = (IDisposable)locator.GetType().GetMethod("EnterScope")!.Invoke(locator, null)!;
        locator = type.GetProperty("CurrentMutable")!.GetValue(null)!;
        var registration = locator.GetType().GetMethods().Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(IStorageProviderFactory)).Invoke(locator, null)!;
        registration.GetType().GetMethods().Single(m => m.Name == "ToConstant" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(factory.GetType()).Invoke(registration, new object[] { factory });
        return scope;
    }

    public class PickerProxy : DispatchProxy
    {
        internal IStorageFolder Folder = null!;
        internal IStorageFile Result = null!;
        internal Uri? RequestedFolder;
        internal FilePickerOpenOptions? Options;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "TryGetFolderFromPathAsync")
            {
                RequestedFolder = (Uri)args![0]!;
                return Task.FromResult<IStorageFolder?>(Folder);
            }
            if (method?.Name == "OpenFilePickerAsync")
            {
                Options = (FilePickerOpenOptions)args![0]!;
                return Task.FromResult<IReadOnlyList<IStorageFile>>(new[] { Result });
            }
            return method?.Name switch
            {
                "get_CanOpen" => true, "get_CanSave" or "get_CanPickFolder" => false, "Dispose" => null,
                _ => throw new NotSupportedException(method?.Name),
            };
        }
    }

    public class ItemProxy : DispatchProxy
    {
        internal Uri PathValue = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Path" => PathValue, "get_Name" => Path.GetFileName(PathValue.LocalPath),
            "get_CanBookmark" => false, "Dispose" => null,
            _ => throw new NotSupportedException(method?.Name),
        };
    }
}

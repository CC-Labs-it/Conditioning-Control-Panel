using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Lab.GazeMinigame;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class GazeFolderPickerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AddFolderUsesAssetsAndFallsBackWhenLookupFails(bool failLookup) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var previous = CorePaths.EffectiveAssetsProvider;
        var assets = Path.Combine(TestUserDataProfile.Root, "gaze-picker-assets");
        CorePaths.EffectiveAssetsProvider = () => assets;
        var provider = DispatchProxy.Create<IStorageProvider, PickerProxy>();
        var picker = (PickerProxy)(object)provider;
        picker.FailLookup = failLookup;
        using var scope = NewEnhancementSourceTests.BindStorageProvider(new NewEnhancementSourceTests.Factory(provider));
        GazeMinigameWindow? window = null;
        try
        {
            window = new GazeMinigameWindow();
            window.Show();
            window.FindControl<Button>("BtnAddFolder")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
            Assert.Equal(assets, picker.Requested!.LocalPath.TrimEnd(Path.DirectorySeparatorChar));
            Assert.NotNull(picker.Options);
            Assert.Same(failLookup ? null : picker.Folder, picker.Options!.SuggestedStartLocation);
        }
        finally { window?.Close(); CorePaths.EffectiveAssetsProvider = previous; }
    });

    [Fact]
    public Task PanicWhileLookingUpFolderDoesNotOpenPicker() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var provider = DispatchProxy.Create<IStorageProvider, PickerProxy>();
        var picker = (PickerProxy)(object)provider;
        picker.Pending = new TaskCompletionSource<IStorageFolder?>();
        using var scope = NewEnhancementSourceTests.BindStorageProvider(new NewEnhancementSourceTests.Factory(provider));
        var window = new GazeMinigameWindow();
        try
        {
            window.Show();
            window.FindControl<Button>("BtnAddFolder")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(picker.Requested);
            GazeMinigameWindow.CloseAllForPanic();
            picker.Pending.SetResult(picker.Folder);
            await Task.Yield();
            Assert.Null(picker.Options);
        }
        finally { window.Close(); }
    });

    private static void EnsureApp()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    public class PickerProxy : DispatchProxy
    {
        internal readonly IStorageFolder Folder = DispatchProxy.Create<IStorageFolder, NewEnhancementSourceTests.ItemProxy>();
        internal Uri? Requested;
        internal FolderPickerOpenOptions? Options;
        internal bool FailLookup;
        internal TaskCompletionSource<IStorageFolder?>? Pending;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "TryGetFolderFromPathAsync" => Lookup((Uri)args![0]!),
            "OpenFolderPickerAsync" => Open((FolderPickerOpenOptions)args![0]!),
            "get_CanPickFolder" => true,
            _ => throw new NotSupportedException(method?.Name),
        };
        private Task<IStorageFolder?> Lookup(Uri path)
        {
            Requested = path;
            if (FailLookup) throw new IOException("lookup failed");
            return Pending?.Task ?? Task.FromResult<IStorageFolder?>(Folder);
        }
        private Task<IReadOnlyList<IStorageFolder>> Open(FolderPickerOpenOptions options)
        {
            Options = options;
            return Task.FromResult<IReadOnlyList<IStorageFolder>>(Array.Empty<IStorageFolder>());
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class DeeperEditorPreviewTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CoreSettings.Current.HasSeenDeeperEditorIntro = true;
    }

    private static void Invoke(object target, string name) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);

    [Fact]
    public void ZoomButtonsWheelNavigationAndDisposalUseTheSameBridge() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        var scripts = new List<string>();
        editor.PreviewBridge.Invoke = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); };
        try
        {
            editor.Show(); Dispatcher.UIThread.RunJobs();
            editor.BrowserPreview.IsVisible = true;
            Assert.True(editor.PreviewZoomCluster.IsVisible);
            Assert.False(editor.PreviewZoomCluster.IsEnabled); // fallback panel has no page to zoom
            editor.BtnPreviewZoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1.1, editor.PreviewBridge.Zoom, 9);
            editor.BrowserPreview.OnWebMessage("\"ccp_zoom_in\"");
            editor.BrowserPreview.OnWebMessage("ccp_zoom_out");
            Assert.Equal(1.1, editor.PreviewBridge.Zoom, 9);
            int before = scripts.Count;
            editor.BrowserPreview.OnNavigationCompleted(new Uri("https://example.com/video"));
            Assert.Equal(before + 1, scripts.Count);
            Assert.Contains("style.zoom='1.1'", scripts[^1]);
            editor.BrowserPreview.IsVisible = false;
            Assert.False(editor.PreviewZoomCluster.IsVisible);
            editor.ForceClose();
            before = scripts.Count;
            editor.BrowserPreview.OnWebMessage("ccp_zoom_in");
            editor.BrowserPreview.OnNavigationCompleted(new Uri("https://example.com/next"));
            Assert.Equal(before, scripts.Count);
            Assert.Equal(1.1, editor.PreviewBridge.Zoom, 9);
        }
        finally { editor.ForceClose(); }
    });

    [Fact]
    public void FullscreenReparentsOnlyPreviewAndRestoresOnKeyCloseNavigationSwapPanicAndEditorClose() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show(); Dispatcher.UIThread.RunJobs();
            editor.BrowserPreview.IsVisible = true;
            var originalState = editor.WindowState;
            foreach (var exit in new[] { "Escape", "F11", "close", "navigation", "swap", "panic", "editor" })
            {
                editor.BrowserPreview.OnWebMessage("ccp_fullscreen_on");
                editor.BrowserPreview.OnWebMessage("ccp_fullscreen_on");
                var fullscreen = Assert.Single(editor.OwnedWindows);
                Assert.Same(editor.BrowserPreview, fullscreen.Content);
                Assert.DoesNotContain(editor.BrowserPreview, editor.PreviewHost.Children);
                Assert.Equal(WindowState.FullScreen, fullscreen.WindowState);
                Assert.Equal(WindowDecorations.None, fullscreen.WindowDecorations);
                Assert.True(fullscreen.Topmost);
                Assert.False(fullscreen.ShowInTaskbar);
                Assert.Equal(originalState, editor.WindowState);
                switch (exit)
                {
                    case "Escape": case "F11":
                        fullscreen.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Enum.Parse<Key>(exit) });
                        break;
                    case "close": fullscreen.Close(); break;
                    case "navigation": editor.BrowserPreview.OnNavigationCompleted(new Uri("https://example.com/next")); break;
                    case "swap": Invoke(editor, "StopBrowserPreview"); break;
                    case "panic":
                        var surface = PanicSurfaces.All.Single(s => s.Id == "deeper-editor-fullscreen");
                        Assert.True(surface.OwnsTheScreen!()); surface.Stop(null); Assert.False(surface.OwnsTheScreen!());
                        break;
                    case "editor": editor.ForceClose(); break;
                }
                Assert.False(fullscreen.IsVisible);
                Assert.False(editor.PreviewBridge.PageFullscreen);
                Assert.Same(editor.PreviewHost, editor.BrowserPreview.Parent);
                Assert.Single(editor.PreviewHost.Children.Where(c => ReferenceEquals(c, editor.BrowserPreview)));
            }
            editor.BrowserPreview.OnWebMessage("ccp_fullscreen_on");
            Assert.Empty(editor.OwnedWindows);
        }
        finally { editor.ForceClose(); }
    });

    [Fact]
    public void EditorUsesSeparateWindowsProfileBeforeNativeAttachment() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            Assert.Equal(Path.Combine(CorePaths.UserData, "browser_data_deeper_editor"), editor.BrowserPreview.WindowsUserDataFolder);
            // The adapter constructs these args; no engine, desktop or profile is opened here.
            var args = (WindowsWebView2EnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(WindowsWebView2EnvironmentRequestedEventArgs));
            editor.BrowserPreview.ConfigureEnvironment(args);
            Assert.Equal(editor.BrowserPreview.WindowsUserDataFolder, args.UserDataFolder);
            Assert.Null(new WebHost().WindowsUserDataFolder);
        }
        finally { editor.ForceClose(); }
    });
}

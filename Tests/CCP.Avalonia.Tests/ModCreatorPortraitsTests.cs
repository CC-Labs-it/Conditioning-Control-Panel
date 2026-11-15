using System;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ModCreatorPortraitsTests
{
    private static void Hook(ModCreatorWindow window, string method, params object[] args) =>
        typeof(ModCreatorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    [Fact]
    public void LoadedPortraitsSurviveExportAndResetClearsThem() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily("fonts:Inter#Inter"), FontStyle.Normal, FontWeight.Bold), out _))
            FontManager.Current.AddFontCollection(new global::Avalonia.Fonts.Inter.InterFontCollection());
        var root = Path.Combine(Path.GetTempPath(), "ccp-portraits-" + Guid.NewGuid());
        var source = Path.Combine(root, "source", "sounds", "companion_audio");
        var skin = Path.Combine(source, "portraits", "base");
        Directory.CreateDirectory(skin);
        const string json = "{\"skins\":[{\"id\":\"base\",\"dir\":\"portraits/base\"}],\"emotions\":{\"happy\":{\"portraits\":[{\"file\":\"one.png\"},{\"file\":\"missing.png\"}]}},\"lines\":{\"hello\":{\"emotion\":\"happy\"}},\"fxRules\":{\"blushEmotions\":[\"happy\"]}}";
        File.WriteAllText(Path.Combine(source, "avatar_manifest.json"), json);
        File.WriteAllText(Path.Combine(skin, "one.png"), "image");
        var window = new ModCreatorWindow();
        try
        {
            Hook(window, "LoadSideFilesFrom", Path.Combine(root, "source"));
            var output = Path.Combine(root, "out");
            Hook(window, "WriteSideFilesTo", output);
            var exported = Path.Combine(output, "sounds", "companion_audio");
            Assert.Equal(json, File.ReadAllText(Path.Combine(exported, "avatar_manifest.json")));
            Assert.Equal("image", File.ReadAllText(Path.Combine(exported, "portraits", "base", "one.png")));
            Hook(window, "ClearSideFileState");
            var cleared = Path.Combine(root, "cleared");
            Hook(window, "WriteSideFilesTo", cleared);
            Assert.False(File.Exists(Path.Combine(cleared, "sounds", "companion_audio", "avatar_manifest.json")));
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(root, true); }
    });
}

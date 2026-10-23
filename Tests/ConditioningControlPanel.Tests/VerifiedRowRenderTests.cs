using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Strict WPF companions to CCP.Avalonia.Snapshot.Tests/VerifiedRowSnapshotTests.
/// No windows are shown; PNGs are keyed by ledger row, not by CLR type.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public sealed class VerifiedRowRenderTests
{
    private sealed record Row(int Width, int Height, Func<Window> Create);

    // Keep dimensions/state aligned with the Avalonia snapshot's default constructors.
    // Add a constructible verified row here in one line. Currently no rows are excluded.
    private static readonly Dictionary<string, Row> Rows = new()
    {
        ["win-layered-audio"] = new(480, 660, () => new LayeredAudioWindow()),
        ["release-content-mod-manager-rows"] = new(700, 500, () => new ModManagerDialog()),
    };

    public static TheoryData<string> RowIds => new(Rows.Keys);

    [Theory]
    [MemberData(nameof(RowIds))]
    [Trait("Category", "WpfRender")]
    public void VerifiedRowWritesPng(string rowId)
    {
        Assert.Null(PackUriBootstrap.Failure);
        // The existing module initializer owns this disposable profile before CorePaths is read.
        Assert.NotNull(TestLocalizationBootstrap.SandboxDir);
        Assert.Equal(TestLocalizationBootstrap.SandboxDir, CorePaths.UserData);
        var output = Environment.GetEnvironmentVariable("WPF_RENDER_OUT");
        if (string.IsNullOrWhiteSpace(output))
            output = Path.Combine("TestResults", "wpf-render");
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, rowId + ".png");
        if (File.Exists(path)) File.Delete(path); // a failed render must not leave stale evidence

        WpfRenderHarness.OnStaThread(() => WithSnapshotState(() =>
        {
            var row = Rows[rowId];
            var window = row.Create();
            try
            {
                AssertDefaultState(window);
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                window.Content = null;
                // Same offscreen content-host pattern as WpfReferenceRender; preserve inherited
                // window typography too. Native title bars are outside the Avalonia snapshots.
                var host = new Border
                {
                    Child = content, Background = window.Background, Resources = window.Resources,
                    FlowDirection = window.FlowDirection,
                };
                TextElement.SetForeground(host, window.Foreground);
                TextElement.SetFontFamily(host, window.FontFamily);
                TextElement.SetFontSize(host, window.FontSize);
                TextElement.SetFontWeight(host, window.FontWeight);
                host.Measure(new Size(row.Width, row.Height));
                host.Arrange(new Rect(0, 0, row.Width, row.Height));
                host.UpdateLayout();

                var bitmap = new RenderTargetBitmap(row.Width, row.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                // A blank/solid background must fail even when a valid PNG could be encoded.
                var pixels = new int[row.Width * row.Height];
                bitmap.CopyPixels(pixels, row.Width * 4, 0);
                Assert.True(pixels.Distinct().Take(33).Count() > 32, "Expected painted controls and text");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(path)) encoder.Save(file);

                // Check the actual artifact, not just the bitmap we intended to save.
                using var saved = File.OpenRead(path);
                var frame = BitmapFrame.Create(saved, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Assert.Equal(row.Width, frame.PixelWidth);
                Assert.Equal(row.Height, frame.PixelHeight);
            }
            finally { window.Close(); }
        }));
    }

    private static void AssertDefaultState(Window window)
    {
        if (window is LayeredAudioWindow)
        {
            Assert.Equal("70%", Assert.IsType<TextBlock>(window.FindName("TxtMaster")).Text);
            Assert.False(Assert.IsType<CheckBox>(window.FindName("ChkMasterEnable")).IsChecked == true);
            Assert.Single(Assert.IsType<StackPanel>(window.FindName("SlotsPanel")).Children);
        }
        else if (window is ModManagerDialog)
        {
            var list = Assert.IsType<ListBox>(window.FindName("ModList"));
            Assert.Single(list.Items);
            Assert.Equal(BuiltInMods.CCPDefaultId, Assert.IsType<ListBoxItem>(list.SelectedItem).Tag);
            Assert.Equal("CCP Default", Assert.IsType<TextBlock>(window.FindName("TxtModName")).Text);
            Assert.Equal(Visibility.Visible, Assert.IsType<StackPanel>(window.FindName("DetailsPanel")).Visibility);
        }
    }

    private static void WithSnapshotState(Action body)
    {
        var settingsProperty = typeof(App).GetProperty(nameof(App.Settings))!;
        var modsProperty = typeof(App).GetProperty(nameof(App.Mods))!;
        var releaseProperty = typeof(App).GetProperty(nameof(App.ReleaseContent))!;
        var previous = (settingsProperty.GetValue(null), modsProperty.GetValue(null), releaseProperty.GetValue(null));
        var previousProvider = CoreSettings.ServiceProvider;
        // ponytail: like LayeredAudioWindowTests, seed only the read state. Running service
        // constructors would migrate settings/extract shipped packs and change the snapshot state.
        var settings = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        typeof(SettingsService).GetProperty(nameof(SettingsService.Current))!
            .SetValue(settings, new AppSettings { MotionLevel = MotionLevel.Off });
        var vanilla = new ModPackage(BuiltInMods.CCPDefault, null, isBuiltIn: true);
        var mods = (ModService)RuntimeHelpers.GetUninitializedObject(typeof(ModService));
        foreach (var (name, value) in new (string, object)[]
        {
            ("_activeMod", vanilla), ("_baseMod", vanilla),
            ("_installedMods", new Dictionary<string, ModPackage> { [vanilla.Id] = vanilla }),
        })
            typeof(ModService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mods, value);
        try
        {
            settingsProperty.SetValue(null, settings);
            modsProperty.SetValue(null, mods);
            releaseProperty.SetValue(null, null);
            CoreSettings.ServiceProvider = () => settings;
            body();
        }
        finally
        {
            CoreSettings.ServiceProvider = previousProvider;
            settingsProperty.SetValue(null, previous.Item1);
            modsProperty.SetValue(null, previous.Item2);
            releaseProperty.SetValue(null, previous.Item3);
        }
    }
}

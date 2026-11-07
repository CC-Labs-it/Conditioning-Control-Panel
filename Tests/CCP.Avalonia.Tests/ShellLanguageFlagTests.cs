using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ShellLanguageFlagTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.RenderProof.EnsureSetUp();
    }

    [Fact]
    public void EveryLanguageHasADistinctRoundedFlagAndRegionalCodesShareTheirPainter() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        Assert.Null(LanguageFlag.For(null));
        Assert.Null(LanguageFlag.For("xx"));
        Assert.Same(LanguageFlag.For("pt-BR"), LanguageFlag.For(" PT "));
        var hashes = new HashSet<string>();
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var flag = LanguageFlag.For(code);
            Assert.NotNull(flag);
            Assert.Equal(new Size(20, 14), flag.Size);
            var image = new Image { Source = flag, Width = 80, Height = 56 };
            image.Measure(new Size(80, 56));
            image.Arrange(new Rect(0, 0, 80, 56));
            using var bitmap = new RenderTargetBitmap(new PixelSize(80, 56));
            bitmap.Render(image);
            var pixels = Pixels(bitmap);
            Assert.True(Enumerable.Range(0, 80 * 56).Count(i => pixels[4 * i + 3] > 200) > 80 * 56 * 0.8, code);
            Assert.True(pixels[3] < 40, code + " has a square corner");
            Assert.True(hashes.Add(Convert.ToBase64String(SHA256.HashData(pixels))), code + " repeats another flag");
        }
    });

    [Fact]
    public void BothLanguagePickersShowFlagsIncludingTheClosedSelectionBox() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var settings = global::ConditioningControlPanel.CoreSettings.Current;
        var motionBefore = settings.MotionLevel;
        settings.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Off;
        var languageBefore = settings.Language;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("appsettings");
            shell.AppSettingsPage!.FocusSection("general");
            Dispatcher.UIThread.RunJobs();
            var pill = shell.Named<ComboBox>("CmbLanguagePill")!;
            var general = shell.AppSettingsPage!.FindControl<GeneralSettingsSection>("SectionGeneral")!.LanguageSelector;
            foreach (var combo in new[] { pill, general })
            {
                foreach (var item in combo.Items.OfType<ComboBoxItem>())
                {
                    var option = Assert.IsType<LanguageOption>(item.Content);
                    Assert.Equal(item.Tag, option.Code);
                    Assert.NotNull(option.Flag);
                    Assert.NotNull(item.ContentTemplate);
                }
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "it");
                combo.ApplyTemplate();
                Dispatcher.UIThread.RunJobs();
                Assert.IsType<LanguageOption>(combo.SelectionBoxItem);
                Assert.NotNull(combo.SelectionBoxItemTemplate);
            }
            // Render the actual closed picker: the Italian green must survive selection-box templating.
            general.Measure(new Size(250, 40));
            general.Arrange(new Rect(0, 0, 250, 40));
            using var bitmap = new RenderTargetBitmap(new PixelSize(250, 40));
            bitmap.Render(general);
            var pixels = Pixels(bitmap);
            Assert.Contains(Enumerable.Range(0, 250 * 40), i => pixels[4 * i + 1] is > 125 and < 165 &&
                pixels[4 * i + (bitmap.Format == global::Avalonia.Platform.PixelFormats.Rgba8888 ? 0 : 2)] < 30);
        }
        finally
        {
            shell.ApplyLanguageSelection(languageBefore);
            shell.Close();
            settings.MotionLevel = motionBefore;
        }
    });

    private static byte[] Pixels(Bitmap bitmap)
    {
        var pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var memory = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), memory, pixels.Length, bitmap.PixelSize.Width * 4);
            Marshal.Copy(memory, pixels, 0, pixels.Length);
            return pixels;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

}

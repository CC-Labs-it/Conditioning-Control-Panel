using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LanguageFlagTests
{
    private static void Ensure()
    {
        if (Application.Current is null)
        {
            // The shell draws text during construction. Use the shipped embedded fonts,
            // including aliases for the Windows faces, even on runners without those fonts.
            var fonts = ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] =
                new FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        }
    }

    [Fact]
    public void EveryLanguageDrawsADistinctOpaqueTwentyByFourteenFlag() => AvaloniaTestDispatcher.Run(() =>
    {
        Ensure();
        var seen = new HashSet<string>();
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            Assert.True(LanguageFlag.Has(code), code);
            var flag = Assert.IsAssignableFrom<IImage>(LanguageFlag.For(code));
            Assert.Equal(new Size(20, 14), flag.Size);
            var image = new Image { Source = flag, Width = 20, Height = 14 };
            image.Measure(new Size(20, 14));
            image.Arrange(new Rect(0, 0, 20, 14));
            using var bitmap = new RenderTargetBitmap(new PixelSize(20, 14));
            bitmap.Render(image);
            var pixels = new byte[20 * 14 * 4];
            var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, 80); }
            finally { pin.Free(); }
            Assert.True(Enumerable.Range(0, 280).Count(i => pixels[i * 4 + 3] > 200) > 224, code);
            Assert.True(seen.Add(Convert.ToBase64String(pixels)), code);
        }
        Assert.Null(LanguageFlag.For("xx"));
        Assert.Null(LanguageFlag.For(null));
        Assert.Null(LanguageFlag.For(""));
        Assert.Same(LanguageFlag.For("pt-BR"), LanguageFlag.For(" PT "));
        Assert.Same(LanguageFlag.For("zh-CN"), LanguageFlag.For("zh"));
    });

    [Fact]
    public void RowTemplatePreservesTheFlagNameAndKeyboardSearch() => AvaloniaTestDispatcher.Run(() =>
    {
        Ensure();
        foreach (var (code, name, _) in LocalizationManager.AvailableLanguages)
        {
            var item = new ComboBoxItem { Tag = code };
            var combo = new ComboBox();
            LanguageFlag.Dress(combo, item, code, name);
            var option = Assert.IsType<LanguageOption>(item.Content);
            Assert.Equal(code, option.Code);
            Assert.Equal(name, option.ToString());
            Assert.Equal(name, global::Avalonia.Controls.Primitives.TextSearch.GetText(item));
            Assert.Equal(name, global::Avalonia.Automation.AutomationProperties.GetName(item));
            var row = Assert.IsType<StackPanel>(item.ContentTemplate!.Build(option));
            Assert.Same(LanguageFlag.For(code), Assert.IsType<Image>(row.Children[0]).Source);
            Assert.Equal(name, Assert.IsType<TextBlock>(row.Children[1]).Text);
            combo.Items.Add(item);
            combo.SelectedIndex = 0;
            Assert.Same(option, combo.SelectionBoxItem);
            Assert.Same(item.ContentTemplate, combo.SelectionBoxItemTemplate);
        }
    });

    [Fact]
    public void BothPickersKeepCodesNamesAndFlagsInRowsAndSelection() => AvaloniaTestDispatcher.Run(() =>
    {
        Ensure();
        var shell = new MainShellWindow();
        try
        {
            var pill = shell.FindControl<ComboBox>("CmbLanguagePill")!;
            var general = new GeneralSettingsSection().FindControl<ComboBox>("CmbLanguageSetting")!;
            foreach (var combo in new[] { pill, general })
            {
                Assert.Equal(LocalizationManager.AvailableLanguages.Length, combo.Items.Count);
                foreach (var item in combo.Items.Cast<ComboBoxItem>())
                {
                    var option = Assert.IsType<LanguageOption>(item.Content);
                    Assert.Equal(item.Tag, option.Code);
                    Assert.Same(LanguageFlag.For(option.Code), option.Flag);
                    Assert.NotNull(ToolTip.GetTip(item));
                    var row = Assert.IsType<StackPanel>(item.ContentTemplate!.Build(option));
                    Assert.Same(option.Flag, Assert.IsType<Image>(row.Children[0]).Source);
                    Assert.Equal(option.Name, Assert.IsType<TextBlock>(row.Children[1]).Text);
                }
                combo.SelectedIndex = 1;
                Assert.IsType<LanguageOption>(combo.SelectionBoxItem);
            }
        }
        finally { shell.Close(); }
    });
}

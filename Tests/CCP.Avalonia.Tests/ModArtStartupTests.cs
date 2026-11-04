using System;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ModArtStartupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StartupEvictsCachedArtOnSwitchOrPackInstall(bool packInstall, bool existingArt) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
            if (Application.Current is null)
                builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            else builder.AfterSetupCallback(builder);

            using var mods = new CoreModsSnapshot();
            var settings = new SettingsService();
            var oldSettings = CoreSettings.ServiceProvider;
            var oldOverride = CoreModArt.OverridePathProvider;
            var oldPost = CoreDispatch.PostProvider;
            var packEvent = typeof(CoreReleaseContent).GetField("PackInstalled", BindingFlags.NonPublic | BindingFlags.Static)!;
            var oldSubscribers = packEvent.GetValue(null);
            var dir = Directory.CreateTempSubdirectory("ccp-mod-art-");
            var file = Path.Combine(dir.FullName, "art.png");
            var key = "test/" + Guid.NewGuid().ToString("N") + ".png";
            try
            {
                CoreSettings.ServiceProvider = () => settings;
                CoreDispatch.PostProvider = action => action();
                CoreModsHooks.ClearModResourceCache = null;
                AvApp.StartMods();
                CoreModArt.OverridePathProvider = name => name == key && File.Exists(file) ? file : null;
                if (existingArt) WriteArt(file, 6, 3);
                var full = ModArt.FirstOf(new[] { key });
                var scaled = ModArt.FirstOf(new[] { key }, 8);
                if (existingArt) { Assert.NotNull(full); Assert.NotNull(scaled); }
                else { Assert.Null(full); Assert.Null(scaled); }

                WriteArt(file, 12, 6);
                Assert.Same(full, ModArt.FirstOf(new[] { key }));
                Assert.Same(scaled, ModArt.FirstOf(new[] { key }, 8));
                PixelSize? notified = null;
                if (packInstall)
                {
                    AvApp.Mods!.ModAvailabilityChanged += (_, _) => notified = ModArt.FirstOf(new[] { key })?.PixelSize;
                    AvApp.Mods.AttachReleaseContent();
                    CoreReleaseContent.RaisePackInstalled(null, "test-loose-art");
                }
                else
                {
                    var original = AvApp.Mods!.ActiveModId;
                    var other = original == BuiltInMods.SissyHypnoId ? BuiltInMods.BambiSleepId : BuiltInMods.SissyHypnoId;
                    AvApp.Mods.ModChanged += (_, mod) =>
                    {
                        if (mod.Id == original) notified = ModArt.FirstOf(new[] { key })?.PixelSize;
                    };
                    AvApp.Mods.ActivateMod(other);
                    AvApp.Mods.ActivateMod(original);
                }

                Assert.Equal(new PixelSize(12, 6), notified);
                Assert.Equal(new PixelSize(12, 6), Assert.IsType<Bitmap>(ModArt.FirstOf(new[] { key })).PixelSize);
                Assert.Equal(new PixelSize(8, 4), Assert.IsType<Bitmap>(ModArt.FirstOf(new[] { key }, 8)).PixelSize);
            }
            finally
            {
                ModArt.ClearModCache();
                packEvent.SetValue(null, oldSubscribers);
                CoreModArt.OverridePathProvider = oldOverride;
                CoreDispatch.PostProvider = oldPost;
                settings.SealForReset();
                CoreSettings.ServiceProvider = oldSettings;
                AvApp.ResetReleaseContent();
                dir.Delete(true);
            }
        });

    private static void WriteArt(string file, int width, int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Save(file);
    }
}

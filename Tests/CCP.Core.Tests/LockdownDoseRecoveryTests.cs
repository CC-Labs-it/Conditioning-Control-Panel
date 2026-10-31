using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class LockdownDoseRecoveryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
    public void Dispose() => File.Delete(_path);

    [Fact]
    public void No_settings_service_leaves_the_record_for_a_loaded_profile()
    {
        var path = Path.Combine(ConditioningControlPanel.CorePaths.UserData, "lockdown-dose.json");
        Assert.StartsWith(Path.GetTempPath(), path);
        var provider = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"Flipped\":[\"flash\"]}");
            ConditioningControlPanel.CoreSettings.ServiceProvider = null;
            LockdownDoseRecovery.RecoverIfNeeded();
            Assert.True(File.Exists(path));
        }
        finally
        {
            ConditioningControlPanel.CoreSettings.ServiceProvider = provider;
            if (previous == null) File.Delete(path); else File.WriteAllBytes(path, previous);
        }
    }

    [Fact]
    public void Desktop_startup_recovers_dose_after_lockdown_settings_and_before_service_install()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "../.."));
        var source = File.ReadAllText(Path.Combine(root, "CCP.Avalonia/App.axaml.cs"));
        int recovery = source.IndexOf("Services.Haptics.LockdownDoseRecovery.RecoverIfNeeded();", StringComparison.Ordinal);
        Assert.True(recovery > source.IndexOf("LockdownService.RecoverIfNeeded();", StringComparison.Ordinal));
        Assert.True(recovery < source.IndexOf("LockdownService.Current = new LockdownService();", StringComparison.Ordinal));
    }
    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Theory]
    [InlineData("flash")]
    [InlineData("subliminal")]
    [InlineData("spiral")]
    [InlineData("pinkfilter")]
    [InlineData("bouncingtext")]
    [InlineData("bubbles")]
    [InlineData("video")]
    public void Recovery_clears_only_the_recorded_feature_and_saves_before_consuming(string key)
    {
        var s = new AppSettings { FlashEnabled = true, SubliminalEnabled = true, SpiralEnabled = true,
            PinkFilterEnabled = true, BouncingTextEnabled = true, BubblesEnabled = true,
            MandatoryVideosEnabled = true, LockCardEnabled = true };
        File.WriteAllText(_path, "{\"Flipped\":[\"" + key + "\",\"unknown\"]}");
        int saved = 0;
        LockdownDoseRecovery.Recover(_path, s, () => { Assert.True(File.Exists(_path)); saved++; });
        Assert.Equal(key != "flash", s.FlashEnabled);
        Assert.Equal(key != "subliminal", s.SubliminalEnabled);
        Assert.Equal(key != "spiral", s.SpiralEnabled);
        Assert.Equal(key != "pinkfilter", s.PinkFilterEnabled);
        Assert.Equal(key != "bouncingtext", s.BouncingTextEnabled);
        Assert.Equal(key != "bubbles", s.BubblesEnabled);
        Assert.Equal(key != "video", s.MandatoryVideosEnabled);
        Assert.True(s.LockCardEnabled);
        Assert.Equal(1, saved);
        Assert.False(File.Exists(_path));
        LockdownDoseRecovery.Recover(_path, s, () => saved++);
        Assert.Equal(1, saved);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Invalid_record_is_retained_without_changing_settings(string json)
    {
        File.WriteAllText(_path, json);
        var s = new AppSettings { FlashEnabled = true };
        int saved = 0;
        LockdownDoseRecovery.Recover(_path, s, () => saved++);
        Assert.True(s.FlashEnabled);
        Assert.Equal(0, saved);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Throwing_save_callback_retains_record_for_next_launch()
    {
        File.WriteAllText(_path, "{\"Flipped\":[\"flash\"]}");
        var s = new AppSettings { FlashEnabled = true };
        LockdownDoseRecovery.Recover(_path, s, () => throw new IOException("test save failure"));
        Assert.True(File.Exists(_path));
        s.FlashEnabled = true;
        LockdownDoseRecovery.Recover(_path, s, () => { });
        Assert.False(s.FlashEnabled);
        Assert.False(File.Exists(_path));
    }
}

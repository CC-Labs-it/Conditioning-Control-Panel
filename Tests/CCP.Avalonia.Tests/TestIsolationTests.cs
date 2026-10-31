using System.IO;
using System.Linq;
using ConditioningControlPanel;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Proves the assembly-wide <see cref="IsolateProcessStateAttribute"/> hook covers the
/// seams that leaked before (PLAYBOOK P02) and really puts each one back.</summary>
public class TestIsolationTests
{
    [Fact]
    public void AssemblyBootstrapProvidesPortableFontsBeforeAnyFixtureSetup() =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
        {
            Assert.Contains("Inter", global::Avalonia.Media.FontManager.Current.DefaultFontFamily.Name);
            foreach (var family in new[] { "Segoe UI Black, Segoe UI", "Fredoka, Segoe UI" })
            {
                var text = new global::Avalonia.Controls.TextBlock
                {
                    Text = "Lobby 123",
                    FontFamily = new global::Avalonia.Media.FontFamily(family),
                    FontWeight = global::Avalonia.Media.FontWeight.Black,
                };
                text.Measure(global::Avalonia.Size.Infinity);
                Assert.True(text.DesiredSize.Width > 0, family);
                Assert.True(text.DesiredSize.Height > 0, family);
            }
            Assert.True(global::Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(
                new global::Avalonia.Media.Typeface("Fredoka"), out var display));
            Assert.Equal("Fredoka Light", display!.FamilyName);
        });

    /// <summary>The first test in a testhost decides the Avalonia platform for every later one. A setup
    /// with headless drawing (no Skia) made later text measure differently and bitmaps read garbage, so
    /// ChasterRaffleCardLayout, BlinkTrainerSession and EmiBookDemo failed only after it (flake-layout).</summary>
    [Fact]
    public void EveryPlatformSetupUsesSkiaDrawing()
    {
        var bad = Directory.EnumerateFiles(Path.GetDirectoryName(Here())!, "*.cs", SearchOption.AllDirectories) // every test project
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f =>
            {
                var code = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(f), @"@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])+'|//[^\n]*|/\*.*?\*/", "",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                return System.Text.RegularExpressions.Regex.Matches(code, @"\.UseHeadless\(")
                    .Where(m => !code.Substring(m.Index, System.Math.Min(160, code.Length - m.Index)).Contains("UseHeadlessDrawing = false")
                             || !code.Substring(System.Math.Max(0, m.Index - 160), System.Math.Min(160, m.Index)).Contains("UseSkia()"))
                    .Select(_ => Path.GetFileName(f));
            }).ToList();
        Assert.Empty(bad);
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string p = "") => Path.GetDirectoryName(p)!;

    [Fact]
    public void EveryTestRunsInsideTheHook() => Assert.NotNull(IsolateProcessStateAttribute.Current);

    [Theory]
    [InlineData(typeof(CoreSettings), nameof(CoreSettings.ServiceProvider))]
    [InlineData(typeof(CoreSecrets), nameof(CoreSecrets.RetrieveProvider))]
    [InlineData(typeof(CoreAccount), nameof(CoreAccount.UnifiedUserId))]
    [InlineData(typeof(CoreMods), nameof(CoreMods.MakeModAwareProvider))]
    [InlineData(typeof(AvApp), nameof(AvApp.Ai))]
    [InlineData(typeof(AvApp), nameof(AvApp.Brain))]
    [InlineData(typeof(ConditioningControlPanel.Services.LockdownService), nameof(ConditioningControlPanel.Services.LockdownService.Current))]
    [InlineData(typeof(ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay), "PanicListenerLive")]
    public void TheHookCoversEverySeamThatLeakedBefore(System.Type type, string member) =>
        Assert.Contains(ProcessStateSnapshot.Members.Value, m => m.DeclaringType == type && m.Name == member);

    [Fact]
    public void RestorePutsStaticsSettingsAndProfileFilesBack()
    {
        var settingsFile = Path.Combine(TestUserDataProfile.Root, "settings.json");
        var stray = Path.Combine(TestUserDataProfile.Root, "isolation-probe.json");
        var hadFile = File.Exists(settingsFile);
        var before = hadFile ? File.ReadAllText(settingsFile) : null;
        var user = CoreAccount.UnifiedUserId;
        var provider = CoreSettings.ServiceProvider;
        var chat = CoreSettings.Current.AiChatEnabled;

        var snapshot = ProcessStateSnapshot.Take();
        CoreAccount.UnifiedUserId = "leaked-user";
        CoreSettings.Current.AiChatEnabled = !chat;
        CoreSettings.ServiceProvider = () => null;
        File.WriteAllText(settingsFile, "{\"leaked\":true}");
        File.WriteAllText(stray, "{}");
        var leaked = new ConditioningControlPanel.Services.SettingsService();
        leaked.Save();   // armed, as a test that ends inside the debounce leaves it
        snapshot.Restore();

        Assert.Equal(user, CoreAccount.UnifiedUserId);
        Assert.Same(provider, CoreSettings.ServiceProvider);
        Assert.Equal(chat, CoreSettings.Current.AiChatEnabled);
        Assert.Equal(hadFile, File.Exists(settingsFile));
        if (hadFile) Assert.Equal(before, File.ReadAllText(settingsFile));
        Assert.False(File.Exists(stray));
        Assert.False(leaked.HasPendingSave);
    }

    /// <summary>A debounce that already FIRED is no longer armed. Its write used to land after Restore
    /// and became every later test's baseline (Welcomed, no age acceptance: an extra Age Verification
    /// dialog in JumpRightInTests, flake-stackcapture). Restore must wait for that write.</summary>
    [Fact]
    public void ASaveAlreadyWritingWhenTheTestEndsLandsBeforeTheFilesGoBack()
    {
        var settingsFile = Path.Combine(TestUserDataProfile.Root, "settings.json");
        var before = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        var snapshot = ProcessStateSnapshot.Take();
        var service = new ConditioningControlPanel.Services.SettingsService();
        service.Current.Welcomed = true;
        var flags = System.Reflection.BindingFlags.NonPublic;
        var writeLock = typeof(ConditioningControlPanel.Services.SettingsService)
            .GetField("_saveLock", flags | System.Reflection.BindingFlags.Instance)!.GetValue(service)!;
        var inFlight = typeof(ConditioningControlPanel.Services.SettingsService)
            .GetField("_debouncedSavesInFlight", flags | System.Reflection.BindingFlags.Static)!;
        System.Threading.Tasks.Task? restore = null;
        var held = new System.Threading.ManualResetEventSlim();
        var release = new System.Threading.ManualResetEventSlim();
        var holder = new System.Threading.Thread(() =>
        {
            lock (writeLock) { held.Set(); release.Wait(); }
        }) { IsBackground = true };
        holder.Start();
        held.Wait();
        try
        {
            service.Save();
            // The debounce fired and is blocked inside its write: a signal, not a guess.
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while ((int)inFlight.GetValue(null)! == 0)
            {
                Assert.True(deadline.Elapsed < System.TimeSpan.FromSeconds(10), "the debounce never fired");
                System.Threading.Thread.Yield();
            }
            restore = System.Threading.Tasks.Task.Run(snapshot.Restore);
            // Without the wait Restore is done well inside this; with it, it is still waiting (5 s bound).
            restore.Wait(System.TimeSpan.FromMilliseconds(500));
        }
        finally { release.Set(); }
        restore!.Wait();
        var drained = System.Diagnostics.Stopwatch.StartNew();   // the write is over before the files are judged
        while ((int)inFlight.GetValue(null)! != 0)
        {
            Assert.True(drained.Elapsed < System.TimeSpan.FromSeconds(10), "the write never finished");
            System.Threading.Thread.Yield();
        }
        Assert.Equal(before, File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null);
    }
}

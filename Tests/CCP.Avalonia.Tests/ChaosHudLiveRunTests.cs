using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The HUD binds the real Core ChaosRunState: stepping the run engine across a loop
/// boundary and drafting a boon must show up on screen with no HUD-side code.</summary>
public sealed class ChaosHudLiveRunTests
{
    [Fact]
    public async Task Portrait_uses_user_art_then_bundled_and_hides_when_missing()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var user = Path.Combine(TestUserDataProfile.Root, "portrait-user");
            var bundle = Path.Combine(TestUserDataProfile.Root, "portrait-bundle");
            var userFile = Path.Combine(user, "assets", "Chaos", "portraits", "neutral.png");
            var bundleFile = Path.Combine(bundle, "assets", "Chaos", "portraits", "neutral.png");
            Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(bundleFile)!);
            File.Copy(Path.Combine(System.AppContext.BaseDirectory, "assets", "Chaos", "portraits", "neutral.png"), bundleFile, true);
            File.WriteAllText(userFile, "invalid image");
            using (var fallback = ChaosHudWindow.ResolvePortrait(user, bundle)) Assert.NotNull(fallback);
            File.Copy(bundleFile, userFile, true);
            File.Delete(bundleFile);
            using (var portrait = ChaosHudWindow.ResolvePortrait(user, bundle)) Assert.NotNull(portrait);
            File.Delete(userFile);
            Assert.Null(ChaosHudWindow.ResolvePortrait(user, bundle));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Hud_follows_a_stepped_run()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var run = new ChaosRunState(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            var hud = new ChaosHudWindow(run);
            try
            {
                hud.Show();
                hud.SetPreRunExpanded(true);   // the panel that lists the run picks
                string Texts() { hud.UpdateLayout(); return string.Join("|", hud.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)); }
                Assert.Contains("LOOP 1/3", Texts());

                // 21 s of RunTick: the clock crosses into loop 2 the way WPF's RunTick moves it.
                for (int i = 0; i < 84; i++)
                {
                    double t = ChaosRunEngine.Advance(run);
                    var (wave, _) = ChaosRunEngine.WaveAt(run, t);
                    if (wave > run.WaveIndex) { run.WaveIndex = wave; run.ActIndex = ChaosRunEngine.ActFor(wave); }
                }
                run.ApplyBoon(ChaosBoonPool.All.First(b => b.Id == "golden_touch"));

                var after = Texts();
                Assert.Contains("LOOP 2/3", after);
                Assert.Contains("00:21", after);
                // the drafted mantra is realised as a ribbon tile bound to the Core tile model
                Assert.Contains(hud.GetVisualDescendants().OfType<Control>(),
                    c => c.DataContext is ChaosSidebarBoon { Id: "golden_touch" });
            }
            finally { hud.Close(); }
            return Task.CompletedTask;
        });
    }
}

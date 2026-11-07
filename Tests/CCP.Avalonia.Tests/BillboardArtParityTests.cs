using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The drawn billboard art ported from WPF (BillboardVectorArt and its scenes): every scene
/// paints and moves on its clock, holds still at Motion Off, freezes when held, and (with
/// CCP_BILLBOARD_PARITY_SHOTS set) writes port-{name}-{t}.png beside the WPF suite's wpf-*.png
/// (ConditioningControlPanel.Tests BillboardArtParityShotsTests) for a side-by-side look.</summary>
public sealed class BillboardArtParityTests
{
    internal static readonly double[] Times = { 0.6, 1.6, 3.1, 5.3 };

    public static readonly TheoryData<string> Names = new(Scenes().Select(s => s.Name));

    private static IEnumerable<(string Name, Func<BillboardVectorArt> Make, string Hue)> Scenes()
    {
        yield return ("discord", () => new DiscordHouseArt(), "#7b86ff");
        yield return ("webapp", () => new WebAppHouseArt(), "#5fe3ff");
        yield return ("remix", () => new RemixHouseArt(), "#ff4fa8");
        yield return ("loom", () => new LoomHouseArt(), "#9b7bff");
        yield return ("support", () => new SupportHouseArt(), "#ff4fa8");
        yield return ("wheel", () => new WheelArtView(), "#ffc94a");
        yield return ("tip-quests", () => new QuestsTipArt(), "#3ce67d");
        yield return ("tip-awareness", () => new AwarenessTipArt(), "#5fe3ff");
        yield return ("tip-programs", () => new ProgramsTipArt(), "#ffc94a");
        yield return ("tip-deeper", () => new DeeperTipArt(), "#9b7bff");
        yield return ("tip-blink", () => new BlinkTipArt(), "#ff4fa8");
        yield return ("tip-haptics", () => new HapticsTipArt(), "#ff4fa8");
        yield return ("tip-folders", () => new FoldersTipArt(), "#ffc94a");
        yield return ("tip-lockdown", () => new LockdownTipArt(), "#ff4fa8");
        yield return ("tip-remote", () => new RemoteTipArt(), "#5fe3ff");
        yield return ("tip", () => new TipArtView(), "#9b7bff");
        yield return ("invite", () => new InviteArtView(), "#ffc94a");
        yield return ("spiral", () => new SpiralArtView(null), "#ff4fa8");
        yield return ("calendar", () => new CalendarArtView(BuiltInArt.CalendarData(
            new Dictionary<string, int> { ["counted"] = 6, ["need"] = 25, ["days"] = 31, ["today"] = 7 }, new DateTime(2026, 10, 7))), "#ff4fa8");
        yield return ("tables", () => new TablesArtView(new[] { "Velvet", "mort5366", "Pika" }, 3), "#5fe3ff");
    }

    private static (string Name, Func<BillboardVectorArt> Make, string Hue) Scene(string name) => Scenes().Single(s => s.Name == name);

    [Theory]
    [MemberData(nameof(Names))]
    public Task SceneMovesOnItsClockHoldsWhenHeldAndStandsStillAtMotionOff(string name) => WithArt(name, MotionLevel.Full, (art, time, render) =>
    {
        // A still card (never played) shows the landed frame.
        Assert.False(art.Playing);
        Assert.Equal(BillboardVectorArt.StillSeconds, art.ShownSeconds, 5);
        var still = render();

        // Played: the clock starts at the top and runs on the shared beat.
        art.Play();
        Assert.True(art.Playing);
        Assert.Equal(0, art.ShownSeconds, 2);
        time.Advance(TimeSpan.FromSeconds(3.1));
        BeatLoop.StepAll();
        Assert.Equal(3.1, art.ShownSeconds, 2);
        var moving = render();
        Assert.False(moving.AsSpan().SequenceEqual(still), $"{name} drew the same at 1.6 s and 3.1 s");

        // Held (another tab, a fold): the frame stays where it was, then runs on from there.
        art.Pause();
        time.Advance(TimeSpan.FromSeconds(5));
        BeatLoop.StepAll();
        Assert.Equal(3.1, art.ShownSeconds, 2);
        Assert.True(render().AsSpan().SequenceEqual(moving));
        art.Play();
        time.Advance(TimeSpan.FromSeconds(1));
        BeatLoop.StepAll();
        Assert.Equal(4.1, art.ShownSeconds, 2);

        // Motion Off (ambient loops off): Play holds a still frame (WPF BillboardVectorArt.Play).
        art.Pause();
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        art.Play();
        Assert.False(art.Playing);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        art.Play();
        Assert.False(art.Playing); // Reduced has no ambient loops either
    });

    [Fact]
    public void HouseScenesAreDispatchedByPosterName()
    {
        Assert.IsType<DiscordHouseArt>(HouseScenes.Create("billboard/discord.png"));
        Assert.IsType<WebAppHouseArt>(HouseScenes.Create("billboard/webapp.png"));
        Assert.IsType<RemixHouseArt>(HouseScenes.Create("billboard/remix.png"));
        Assert.IsType<LoomHouseArt>(HouseScenes.Create("billboard/loom.png"));
        Assert.IsType<SupportHouseArt>(HouseScenes.Create("billboard/support.png"));
        Assert.IsType<WheelArtView>(HouseScenes.Create("billboard/backroom.png"));
    }

    [Fact]
    public void TipScenesAreDispatchedByTipId()
    {
        // WPF TipScenes.Create: one picture per tip id, the generic tip art otherwise.
        Assert.IsType<QuestsTipArt>(TipScenes.Create("quests"));
        Assert.IsType<AwarenessTipArt>(TipScenes.Create("awareness"));
        Assert.IsType<ProgramsTipArt>(TipScenes.Create("programs"));
        Assert.IsType<DeeperTipArt>(TipScenes.Create("deeper"));
        Assert.IsType<BlinkTipArt>(TipScenes.Create("blink"));
        Assert.IsType<HapticsTipArt>(TipScenes.Create("haptics"));
        Assert.IsType<FoldersTipArt>(TipScenes.Create("folders"));
        Assert.IsType<LockdownTipArt>(TipScenes.Create("lockdown"));
        Assert.IsType<RemoteTipArt>(TipScenes.Create("remote"));
        Assert.IsType<TipArtView>(TipScenes.Create("nope"));
        Assert.IsType<TipArtView>(TipScenes.Create(null));
    }

    [Fact]
    public Task ShotsWhenAsked() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_BILLBOARD_PARITY_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return Task.CompletedTask;
        Directory.CreateDirectory(dir);
        Setup();
        var timings = new List<string>();
        foreach (var s in Scenes())
        {
            var art = s.Make();
            art.Accent = BillboardVectorArt.ParseHue(s.Hue, Colors.HotPink);
            var host = Host(art, out var window);
            try
            {
                foreach (var t in Times)
                {
                    art.SecondsForTests = t;
                    art.Redraw();
                    using var bmp = Render(host);
                    bmp.Save(Path.Combine(dir, $"port-{s.Name}-{t.ToString("0.0", CultureInfo.InvariantCulture)}.png"));
                }
                // CPU (software Skia) cost of a frame, the no-GPU path: 60 frames across the loop.
                using var frame = new RenderTargetBitmap(new PixelSize(1000, 420));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 60; i++)
                {
                    art.SecondsForTests = i / 30.0 * 3;
                    frame.Render(art);
                }
                timings.Add($"{s.Name}: {sw.Elapsed.TotalMilliseconds / 60:0.00} ms/frame");
            }
            finally { window.Close(); art.Release(); }
        }
        File.WriteAllLines(Path.Combine(dir, "port-timings.txt"), timings);
        return Task.CompletedTask;
    });

    // ---- harness -----------------------------------------------------------------------------

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Panel Host(Control art, out Window window)
    {
        var host = new Panel { Width = 1000, Height = 420, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), Children = { art } };
        window = new Window { Width = 1000, Height = 420, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return host;
    }

    private static RenderTargetBitmap Render(Control c)
    {
        var bmp = new RenderTargetBitmap(new PixelSize(1000, 420));
        bmp.Render(c);
        return bmp;
    }

    private static byte[] Pixels(Control c)
    {
        using var bmp = Render(c);
        var px = new byte[1000 * 420 * 4];
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(px.Length);
        try
        {
            bmp.CopyPixels(new PixelRect(0, 0, 1000, 420), buffer, px.Length, 1000 * 4);
            System.Runtime.InteropServices.Marshal.Copy(buffer, px, 0, px.Length);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
        return px;
    }

    private static Task WithArt(string name, MotionLevel level, Action<BillboardVectorArt, SteppedTime, Func<byte[]>> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MotionLevel = level;
        CoreSettings.Current.PerformanceMode = CoreSettings.Current.AutoPerformanceMode = false;
        var oldTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var s = Scene(name);
        var art = s.Make();
        art.Accent = BillboardVectorArt.ParseHue(s.Hue, Colors.HotPink);
        var host = Host(art, out var window);
        try { check(art, time, () => Pixels(host)); }
        finally
        {
            window.Close();
            art.Release();
            BeatLoop.Time = oldTime;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}

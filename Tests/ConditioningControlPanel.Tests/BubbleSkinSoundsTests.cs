using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ConditioningControlPanel.BubbleCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Bubble skin pop sounds: which clip for which skin (soap = today's list), every skin's three
/// rendered clips on disk and in the build output, never louder than Pop.mp3, the PlayPopSound seam,
/// and a rerun of Scripts/render-bubble-pops.mjs giving the same bytes.</summary>
public class BubbleSkinSoundsTests
{
    /// <summary>Pop.mp3's peak, -3.33 dBFS (POP_REF in the render script): no skin clip may reach it.</summary>
    private const double PopPeak = 0.6814;

    [Fact]
    public void Soap_and_unknown_skins_get_todays_pops()
    {
        Assert.Equal(new[] { "bubbles/Pop.mp3", "bubbles/Pop2.mp3", "bubbles/Pop3.mp3" }, BubbleSkinSounds.SoapPops);
        foreach (var s in new[] { null, "", BubbleSkinIds.Soap, "glitter", "Champagne" })
        {
            Assert.Same(BubbleSkinSounds.SoapPops, BubbleSkinSounds.PopClips(s));
            Assert.False(BubbleSkinSounds.HasOwnPop(s));
        }
    }

    [Fact]
    public void Every_other_skin_gets_its_own_three_clips()
    {
        Assert.Equal(BubbleSkinIds.All.Where(id => id != BubbleSkinIds.Soap), BubbleSkinSounds.Skins);
        foreach (var id in BubbleSkinSounds.Skins)
        {
            Assert.True(BubbleSkinSounds.HasOwnPop(id));
            Assert.Equal(Enumerable.Range(1, BubbleSkinSounds.Variants).Select(v => $"bubbles/{id}_{v}.wav"),
                BubbleSkinSounds.PopClips(id));
        }
    }

    [Fact]
    public void Every_clip_is_a_quiet_wav_on_disk_and_in_the_build_output()
    {
        var src = Repo("ConditioningControlPanel", "Resources", "sounds");
        var bin = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds");
        foreach (var clip in BubbleSkinSounds.Skins.SelectMany(BubbleSkinSounds.PopClips))
        {
            var file = Path.Combine(src, clip.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"missing {file} - run Scripts/render-bubble-pops.mjs");
            Assert.True(File.Exists(Path.Combine(bin, clip.Replace('/', Path.DirectorySeparatorChar))),
                $"{clip} is not copied to the build output (Resources\\sounds content glob)");

            var b = File.ReadAllBytes(file);
            Assert.True(b.Length > 44 && b[0] == 'R' && b[1] == 'I' && b[8] == 'W', $"{clip} is not a WAV (an LFS pointer?)");
            Assert.Equal(1, BitConverter.ToInt16(b, 20));       // PCM
            Assert.Equal(1, BitConverter.ToInt16(b, 22));       // mono
            Assert.Equal(44100, BitConverter.ToInt32(b, 24));
            Assert.Equal(16, BitConverter.ToInt16(b, 34));
            double peak = 0;
            for (int i = 44; i + 1 < b.Length; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(b, i) / 32768.0));
            Assert.InRange(peak, 0.05, PopPeak);                 // audible, and never louder than Pop.mp3
            Assert.InRange((b.Length - 44) / 2 / 44100.0, 0.1, 2.0);
        }
    }

    [Fact]
    public void The_render_script_names_the_same_skins_and_variants()
    {
        var script = File.ReadAllText(Repo("ConditioningControlPanel", "Scripts", "render-bubble-pops.mjs"));
        Assert.Contains($"const VARIANTS = {BubbleSkinSounds.Variants};", script);
        foreach (var id in BubbleSkinSounds.Skins)
            Assert.Contains($"  {id}(a, n, t) {{", script);
    }

    [Fact]
    public void PlayPopSound_takes_the_skin_and_the_lucky_chime_still_comes_first()
    {
        var src = File.ReadAllText(Repo("ConditioningControlPanel", "Services", "BubbleService.cs"));
        var at = src.IndexOf("private void PlayPopSound(bool isLucky = false, float volumeMult = 1f, string? skin = null)", StringComparison.Ordinal);
        Assert.True(at > 0, "PlayPopSound signature moved");
        var body = src.Substring(at, src.IndexOf("public static void DrainAudioDevicePool", at, StringComparison.Ordinal) - at);
        var chime = body.IndexOf("chime1.mp3", StringComparison.Ordinal);
        var clips = body.IndexOf("BubbleSkinSounds.PopClips(skin)", StringComparison.Ordinal);
        Assert.True(chime > 0 && clips > chime, "the lucky chime must be tried before the skin's pop");
        Assert.Contains("PlaySoundAsync(popPath, Math.Min(volume, 1f));", body);
        Assert.Contains("PlayPopSound(paidLucky, bubble.AvatarPopVolumeMult, bubble.Skin);", src);
    }

    [Fact]
    public void A_rerun_of_the_render_script_gives_the_same_bytes()
    {
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d.Trim('"'), OperatingSystem.IsWindows() ? "node.exe" : "node"))
            .FirstOrDefault(File.Exists);
        if (node == null) Assert.Skip("node is not on PATH");

        var psi = new ProcessStartInfo(node, "Scripts/render-bubble-pops.mjs --check")
        {
            WorkingDirectory = Repo("ConditioningControlPanel"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(60_000), "render script timed out");
        Assert.True(p.ExitCode == 0, $"rerun differs from the committed clips:\n{stdout.Result}\n{stderr.Result}");
    }

    [Fact]
    public void Classic_pop_option_puts_the_original_pop_on_every_skin()
    {
        var src = File.ReadAllText(Repo("ConditioningControlPanel", "Services", "BubbleService.cs"));
        int optOut = src.IndexOf("BubbleClassicPop == true) skin = null;", StringComparison.Ordinal);
        int pick = src.IndexOf("BubbleSkinSounds.PopClips(skin)", StringComparison.Ordinal);
        Assert.True(optOut > 0 && optOut < pick, "the opt-out must clear the skin before the clip is picked");
        Assert.False(new ConditioningControlPanel.Models.AppSettings().BubbleClassicPop);   // off by default
        var xaml = File.ReadAllText(Repo("ConditioningControlPanel", "Features", "BubblePopFeatureControl.xaml"));
        Assert.Contains("setting_bubble_classic_pop", xaml);
    }

    private static string Repo(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "ConditioningControlPanel.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir! }.Concat(parts).ToArray());
    }
}

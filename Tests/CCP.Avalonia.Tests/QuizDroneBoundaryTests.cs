using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class QuizDroneBoundaryTests
{
    [Fact]
    public void ProductionQuizPlayerRepeatsDecodedSamplesSequentiallyAcrossThreeBoundaries()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "quiz-drone-boundary.mp3");
        using var source = new NLayer.MpegFile(path);
        var pass = new List<float>();
        var buffer = new float[1024];
        int read;
        while ((read = source.ReadSamples(buffer, 0, buffer.Length)) > 0)
            pass.AddRange(buffer.AsSpan(0, read).ToArray());
        Assert.NotEmpty(pass);
        Assert.Contains(pass, sample => Math.Abs(sample) > 0.01f);
        var captured = new List<short>();
        var target = pass.Count * 4;
        using var ready = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        var audio = new LibVlcAudio("--aout=dummy");
        var oldAudio = LibVlcAudio.Instance;
        var oldConfigure = LibVlcAudio.ConfigureQuizDrone;
        LibVlcAudio.Instance = audio;
        LibVlcAudio.ConfigureQuizDrone = player =>
        {
            player.SetAudioFormat("S16N", (uint)source.SampleRate, (uint)source.Channels);
            player.SetAudioCallbacks((_, samples, count, _) =>
            {
                lock (captured)
                {
                    if (captured.Count >= target) return;
                    var chunk = new short[checked((int)count * source.Channels)];
                    Marshal.Copy(samples, chunk, 0, chunk.Length);
                    captured.AddRange(chunk);
                    if (captured.Count >= target) ready.Set();
                }
            }, null, null, null, null);
        };
        MindWipePlayer.IVoice? voice = null;
        try
        {
            voice = QuizWindow.PlayDrone(path, 1, true, () => stopped.Set());
            Assert.NotNull(voice);
            Assert.True(ready.Wait(10_000), "four complete decoded passes must reach the same player");
            // VLC 3's amem output is S16N. Allow at most two quantization/dither units.
            lock (captured)
                for (var i = 0; i < target; i++)
                    Assert.True(Math.Abs(pass[i % pass.Count] * 32768 - captured[i]) <= 2,
                        $"sample {i}: expected {pass[i % pass.Count] * 32768}, actual {captured[i]}");
        }
        finally
        {
            voice?.Dispose();
            var disposed = voice == null || stopped.Wait(5000);
            LibVlcAudio.Instance = oldAudio;
            LibVlcAudio.ConfigureQuizDrone = oldConfigure;
            Assert.True(disposed);
        }
    }

    [Theory]
    [InlineData("en", "(unnamed)")]
    [InlineData("de", "(unbenannt)")]
    [InlineData("es", "(sin nombre)")]
    [InlineData("fr", "(sans nom)")]
    [InlineData("it", "(senza nome)")]
    [InlineData("ja", "(名前なし)")]
    [InlineData("ko", "(이름 없음)")]
    [InlineData("pt-BR", "(sem nome)")]
    [InlineData("ru", "(без имени)")]
    [InlineData("zh-CN", "(未命名)")]
    public void UnnamedEndpointUsesTheSelectedLanguage(string language, string expected)
    {
        var loc = LocalizationManager.Instance;
        var old = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(language);
            Assert.Equal(expected, WindowsAudioDevices.DisplayName(null));
            Assert.Equal("USB", WindowsAudioDevices.DisplayName("USB"));
        }
        finally { loc.SetLanguage(old); }
    }
}

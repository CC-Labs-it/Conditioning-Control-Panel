using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Quiz;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class IntakeSubliminalsTests
{
    [Fact]
    public void EffectSampleKeepsFortyEnabledPhrasesAndOriginalText()
    {
        var pool = Enumerable.Range(0, 45).ToDictionary(i => " text " + i + " ", _ => true);
        pool["disabled"] = false;
        pool[" "] = true;
        var sample = IntakeSubliminals.Sample(pool)!;
        Assert.Equal(40, sample.Length);
        Assert.All(sample, text => Assert.Contains(text, pool.Keys.Where(k => pool[k] && !string.IsNullOrWhiteSpace(k))));
        Assert.Null(IntakeSubliminals.Sample(null));
    }

    [Fact]
    public void OnlyEnabledNonblankPhrasesAreTrimmedAndCapped()
    {
        Assert.Null(IntakeSubliminals.Build(null, true, null, null));
        Assert.Null(IntakeSubliminals.Build(new Dictionary<string, bool>() { ["off"] = false, [" "] = true }, true, null, null));
        var pool = Enumerable.Range(0, 410).ToDictionary(i => " text " + i + " ", _ => true);
        pool["disabled"] = false;
        var payload = IntakeSubliminals.Build(pool, false, null, null)!;
        Assert.Equal(400, payload.Length);
        Assert.All(payload, entry =>
        {
            var item = JObject.FromObject(entry);
            Assert.StartsWith("text ", (string?)item["text"]);
            Assert.Equal(((string)item["text"]!).Trim(), (string)item["text"]!);
            Assert.Null(item["audio"]);
        });
    }

    [Fact]
    public void ModWhispersWinAndApostropheAndCaseVariantsMatch()
    {
        var root = Directory.CreateTempSubdirectory("intake-subs-").FullName;
        var mod = Directory.CreateDirectory(Path.Combine(root, "mod")).FullName;
        var fallback = Directory.CreateDirectory(Path.Combine(root, "default")).FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(mod, "you're ready.WAV"), new byte[] { 1, 2 });
            File.WriteAllBytes(Path.Combine(fallback, "YOU'RE READY.mp3"), new byte[] { 3 });
            File.WriteAllBytes(Path.Combine(fallback, "DEFAULT.ogg"), new byte[] { 4 });
            var pool = new Dictionary<string, bool> { [" You’re Ready "] = true, ["default"] = true };
            var items = IntakeSubliminals.Build(pool, true, mod, fallback)!
                .Select(JObject.FromObject).ToDictionary(o => (string)o["text"]!);
            Assert.Equal("data:audio/wav;base64,AQI=", (string?)items["You’re Ready"]["audio"]);
            Assert.Equal("data:audio/ogg;base64,BA==", (string?)items["default"]["audio"]);
            Assert.All(IntakeSubliminals.Build(pool, false, mod, fallback)!,
                item => Assert.Null(JObject.FromObject(item)["audio"]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingOversizedAndPathLikeClipsStayTextOnly()
    {
        var root = Directory.CreateTempSubdirectory("intake-subs-").FullName;
        var audio = Directory.CreateDirectory(Path.Combine(root, "audio")).FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(root, "outside.mp3"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(audio, "big.mp3"), new byte[512 * 1024 + 1]);
            var pool = new Dictionary<string, bool> { ["../outside"] = true, ["big"] = true, ["missing"] = true };
            var payload = IntakeSubliminals.Build(pool, true, audio, null)!;
            Assert.Equal(3, payload.Length);
            Assert.All(payload, item => Assert.Null(JObject.FromObject(item)["audio"]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TotalRawWhisperPayloadDoesNotExceedSixMegabytes()
    {
        var root = Directory.CreateTempSubdirectory("intake-subs-").FullName;
        try
        {
            var pool = new Dictionary<string, bool>();
            for (int i = 0; i < 14; i++)
            {
                pool["clip" + i] = true;
                File.WriteAllBytes(Path.Combine(root, "clip" + i + ".mp3"), new byte[512 * 1024]);
            }
            var payload = IntakeSubliminals.Build(pool, true, root, null)!;
            Assert.Equal(14, payload.Length);
            var audio = payload.Select(JObject.FromObject).Select(o => (string?)o["audio"]).Where(a => a != null).ToArray();
            Assert.Equal(12, audio.Length);
            Assert.Equal(6 * 1024 * 1024, audio.Sum(a => Convert.FromBase64String(a![(a.IndexOf(',') + 1)..]).Length));
        }
        finally { Directory.Delete(root, true); }
    }
}

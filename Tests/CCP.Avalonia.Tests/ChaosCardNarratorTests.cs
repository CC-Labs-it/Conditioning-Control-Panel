using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosCardNarratorTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task RealCardPlaysResolvedVoiceHoldsBarksAcrossAdvancesAndReleasesOnClose()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            // The first test owns the process-wide font manager. This setup must also
            // support the Windows font names used by later shell tests on Linux.
            Assert.NotNull(new Typeface(new FontFamily("Segoe UI Black, Segoe UI"),
                weight: FontWeight.Black).GlyphTypeface);
            var saved = (CoreAudio.PlayOneShotProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider,
                CoreAudio.DuckGenerationProvider, ChaosCardNarrator.Roots, ChaosCardNarrator.ClipMilliseconds,
                ChaosCardNarrator.Clock, CoreSettings.Current.AvatarEnabled, CoreSettings.Current.MasterVolume);
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var user = Path.Combine(dir, "user");
            var bundle = Path.Combine(dir, "bundle");
            var events = new List<string>();
            var clock = new Clock();
            ChaosOverlayWindow? window = null;
            try
            {
                foreach (var root in new[] { user, bundle })
                {
                    Directory.CreateDirectory(Path.Combine(root, "assets", "Chaos", "narrator"));
                    File.WriteAllText(Path.Combine(root, "assets", "Chaos", "narrator", "line.wav"), "test clip");
                }
                var userWav = Path.Combine(user, "assets", "Chaos", "narrator", "line.wav");
                ChaosCardNarrator.Roots = () => new[] { user, bundle };
                ChaosCardNarrator.ClipMilliseconds = _ => 3000;
                ChaosCardNarrator.Clock = clock;
                CoreSettings.Current.AvatarEnabled = true;
                CoreSettings.Current.MasterVolume = 50;
                CoreAudio.DuckProvider = strength => events.Add("duck " + strength);
                CoreAudio.DuckGenerationProvider = () => 17;
                CoreAudio.UnduckProvider = gen => events.Add("unduck " + gen);
                CoreAudio.PlayOneShotProvider = (path, volume, tag, _, _) =>
                {
                    Assert.Equal(userWav, path);
                    Assert.Equal(0.5f, volume);
                    Assert.Equal("chaos-narrator", tag);
                    events.Add("play");
                };
                Assert.Equal(userWav, ChaosCardNarrator.ResolveAudio("line"));
                var engine = new BarkEngine(() => new BarkRuleSet(new[] {
                    new BarkRule { Id = "card", Trigger = "AttentionCheckFail", VariantPool = new() { new("look") } }
                }));
                BarkHead.Seed(engine);
                engine.Speak = _ => events.Add("bark");
                engine.Start();
                window = ChaosOverlayWindow.ForRun();
                window.Show();
                window.ShowConversation(new ChaosConversation { Lines = new() {
                    new() { AudioKey = "line", Text = "first" }, new() { Text = "second" }
                } }, null, null);
                Assert.Equal(new[] { "duck 72", "play" }, events);
                Assert.False(engine.Raise("AttentionCheckFail"));
                clock.Now = clock.Now.AddMilliseconds(3449); // clip 3000 + 250 + 200
                Assert.True(ChaosCardNarrator.IsPlaying);
                clock.Now = clock.Now.AddMilliseconds(1);
                Assert.False(ChaosCardNarrator.IsPlaying);
                // No automatic unduck on a manual card, even after the line's dwell.
                Assert.Equal(2, events.Count);
                typeof(ChaosOverlayWindow).GetMethod("AdvanceStory", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                Assert.True(ChaosCardNarrator.IsPlaying); // missing clip uses the text dwell
                Assert.Equal(2, events.Count); // one duck for the whole card
                typeof(ChaosOverlayWindow).GetMethod("AdvanceStory", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                Assert.False(ChaosCardNarrator.IsPlaying); // final press releases before the fade
                window.ShowConversation(new ChaosConversation { Lines = new() { new() { Text = "again" } } }, null, null);
                Assert.True(ChaosCardNarrator.IsPlaying);
                window.Close();
                Assert.False(ChaosCardNarrator.IsPlaying);
                Assert.Equal("unduck 17", events[^1]);
                Assert.True(engine.Raise("AttentionCheckFail"));
                ChaosCardNarrator.EndCard();
                Assert.Equal(2, events.FindAll(x => x == "unduck 17").Count);
                Assert.Equal(2400, ChaosCardNarrator.PlayCardLine(null, ""));
                Assert.Equal(7000, ChaosCardNarrator.PlayCardLine(null, new string('x', 200)));
                ChaosCardNarrator.EndCard();
                ChaosCardNarrator.ClipMilliseconds = _ => 100;
                Assert.Equal(2400, ChaosCardNarrator.PlayCardLine("line", ""));
                ChaosCardNarrator.EndCard();
                ChaosCardNarrator.ClipMilliseconds = _ => 9000;
                Assert.Equal(7000, ChaosCardNarrator.PlayCardLine("line", ""));
                ChaosCardNarrator.EndCard();
                var mp3 = Path.ChangeExtension(userWav, ".mp3");
                File.WriteAllText(mp3, "test clip");
                Assert.Equal(mp3, ChaosCardNarrator.ResolveAudio("line"));
                File.Delete(mp3);
                File.Delete(userWav);
                Assert.Equal(Path.Combine(bundle, "assets", "Chaos", "narrator", "line.wav"), ChaosCardNarrator.ResolveAudio("line"));
            }
            finally
            {
                window?.Close();
                ChaosCardNarrator.EndCard();
                (CoreAudio.PlayOneShotProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider,
                    CoreAudio.DuckGenerationProvider, ChaosCardNarrator.Roots, ChaosCardNarrator.ClipMilliseconds,
                    ChaosCardNarrator.Clock, CoreSettings.Current.AvatarEnabled, CoreSettings.Current.MasterVolume) = saved;
                Directory.Delete(dir, true);
            }
            return Task.CompletedTask;
        });
    }
}

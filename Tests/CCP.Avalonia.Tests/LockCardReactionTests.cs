using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LockCardReactionTests
{
    [Fact]
    public Task AiAndFallbackAndPanic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var oldAi = settings.AiChatEnabled;
        var oldEnabled = settings.AvatarEnabled;
        var oldProvider = BarkHead.TubeProvider;
        var tube = new AvatarTubeWindow(null);
        var oldLive = AvatarTubeWindow.TestLive;
        AvatarTubeWindow.TestLive = tube;
        var lines = new List<string>();
        var e = new BarkEngine(() => new BarkRuleSet(new[] { new BarkRule {
            Id = "lock-test", Trigger = "LockCardCompleted", VariantPool = new() { new BarkVariant("pool") }
        }}));
        e.Speak = speech => lines.Add(speech.Text);
        try
        {
            settings.AiChatEnabled = true;
            settings.AvatarEnabled = true;
            tube.Show();
            BarkHead.TubeProvider = () => tube;
            tube.LockReactionSpeech = lines.Add;
            e.Start();
            tube.LockReactionRequest = (phrase, mistakes, repeats) =>
            {
                Assert.Equal("phrase", phrase); Assert.Equal(2, mistakes); Assert.Equal(3, repeats);
                return Task.FromResult<string?>("AI line");
            };
            await BarkHead.LockCardReactionAsync(e, "phrase", 2, 3, true);
            Assert.Equal(new[] { "AI line" }, lines);
            lines.Clear();
            tube.LockReactionRequest = (_, _, _) => Task.FromResult<string?>(null);
            await BarkHead.LockCardReactionAsync(e, "phrase", 2, 3, true);
            Assert.Equal(new[] { "pool" }, lines);
            lines.Clear();
            var pending = new TaskCompletionSource<string?>();
            tube.LockReactionRequest = (_, _, _) => pending.Task;
            var reaction = BarkHead.LockCardReactionAsync(e, "phrase", 2, 3, true);
            PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(null);
            pending.SetResult("too late");
            await reaction;
            Assert.Empty(lines);
        }
        finally
        {
            tube.Close(); AvatarTubeWindow.TestLive = oldLive; BarkHead.TubeProvider = oldProvider;
            settings.AiChatEnabled = oldAi; settings.AvatarEnabled = oldEnabled;
        }
    });
}

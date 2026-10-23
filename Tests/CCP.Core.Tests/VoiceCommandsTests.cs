using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core VoiceCommands (moved from WPF AutonomyService.VoiceCommands): what a head cannot run
/// is not in the grammar, a guarded command is refused with the tripwire, and a cancelled prompt
/// (panic) stops listening at once.</summary>
public sealed class VoiceCommandsTests
{
    private static (VoiceCommands Vc, List<string> Ran, List<string> Said, Queue<string> Heard) Make(Func<VoiceGuard, bool>? blocked = null)
    {
        var ran = new List<string>(); var said = new List<string>(); var heard = new Queue<string>();
        var vc = new VoiceCommands(new VoiceCommandHost
        {
            ActionFor = n => n is "bubbles_on" or "takeover_off" ? () => ran.Add(n) : null,
            IsBlocked = blocked ?? (_ => false),
            OnRefused = () => ran.Add("refused"),
            Say = (t, _) => said.Add(t),
            Recognize = (_, _) => Task.FromResult(heard.Count > 0
                ? new PhraseResult { Transcript = heard.Dequeue(), LoudEnough = true } : new PhraseResult()),
            Delay = _ => Task.CompletedTask,
        });
        return (vc, ran, said, heard);
    }

    [Fact]
    public async Task OnlyRunnableIntentsAreHeard_AndAGuardRefuses()
    {
        var (vc, ran, said, heard) = Make(g => g == VoiceGuard.Lockdown);
        Assert.Contains("show me the bubbles", vc.Grammar());
        Assert.DoesNotContain("turn on the spiral", vc.Grammar());   // no action on this head

        heard.Enqueue("show me the bubbles");
        heard.Enqueue("stop taking over");
        Assert.True(await vc.TryHandleVoiceCommandAsync());
        Assert.Equal(new[] { "bubbles_on", "refused" }, ran);   // takeover_off refused under Lockdown
        Assert.Contains(said, l => l.Contains("lockdown"));
    }

    [Fact]
    public async Task ACancelledPromptEndsWithoutAnotherListen()
    {
        var (vc, ran, said, heard) = Make();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.True(await vc.TryHandleVoiceCommandAsync(cts.Token));
        Assert.Empty(said);   // no spoken "you called?" re-prompt after a panic
    }

    /// <summary>WPF 769373c28 (hard rule 6): the voice panic reaches as far as the panic key. A head's
    /// RunFor that refuses (false) leaves the "everything's off" line unsaid; one that ran says it.
    /// A RunFor intent is in the grammar even with no ActionFor.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARefusedPanicSaysNothing_ARunPanicConfirms(bool panicRuns)
    {
        var ran = new List<string>(); var said = new List<string>(); var heard = new Queue<string>();
        var vc = new VoiceCommands(new VoiceCommandHost
        {
            RunFor = n => n == "panic" ? () => { ran.Add(n); return panicRuns; } : null,
            Say = (t, _) => said.Add(t),
            Recognize = (_, _) => Task.FromResult(heard.Count > 0
                ? new PhraseResult { Transcript = heard.Dequeue(), LoudEnough = true } : new PhraseResult()),
            Delay = _ => Task.CompletedTask,
        });
        Assert.Contains("safe word", vc.Grammar());
        heard.Enqueue("safe word");
        Assert.True(await vc.TryHandleVoiceCommandAsync());
        Assert.Equal(new[] { "panic" }, ran);
        Assert.Equal(panicRuns, said.Any(l => l.Contains("safe")));
        if (!panicRuns) Assert.Empty(said);
    }

    [Fact]
    public void EveryConfirmTableLeadsWithNeutral()
        => Assert.All(VoiceCommands.ConfirmTables(), t => Assert.Equal("neutral", t.Lines.Keys.First()));
}

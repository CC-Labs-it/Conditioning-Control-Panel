using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class TakeoverCommentTests
{
    [Fact]
    public void DropsBlankAndOnlyTheLastCommentIgnoringCaseAndOuterSpace()
    {
        var comment = new TakeoverComment();
        Assert.False(comment.TryAccept(null));
        Assert.True(comment.TryAccept("Hello there"));
        Assert.False(comment.TryAccept("  HELLO THERE  "));
        Assert.False(comment.TryAccept(" "));
        Assert.False(comment.TryAccept("Hello there"));
        Assert.True(comment.TryAccept("Something else"));
        Assert.True(comment.TryAccept("Hello there"));
        Assert.True(new TakeoverComment().TryAccept("Hello there"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RoutesThroughAmbientBrainOrTheRewordedStatelessPrompt(bool enabled, bool hasBrain)
    {
        var old = CoreSettings.Current.UseCompanionBrain;
        var ai = DispatchProxy.Create<IAiService, FakeAi>();
        var fake = (FakeAi)(object)ai;
        using var brain = new CompanionBrain(ai, store: new EmptyStore(), preview: () => false);
        try
        {
            CoreSettings.Current.UseCompanionBrain = enabled;
            var result = await TakeoverComment.GenerateAsync(ai, hasBrain ? brain : null);
            Assert.Equal("Hello there", result.Text);
            Assert.True(result.IsAiGenerated);
            if (enabled && hasBrain)
            {
                Assert.Null(fake.LegacyPrompt);
                Assert.NotNull(fake.Options);
                Assert.False(fake.Options!.Interactive);
                Assert.Contains(brain.Session.Turns, t => t.Kind == TurnKind.AmbientEvent && t.Text.Contains("asked for nothing"));
                Assert.Contains(brain.Session.Turns, t => t.Kind == TurnKind.AmbientReply);
            }
            else
            {
                Assert.Null(fake.Options);
                Assert.Equal("Tease the user in one short line, out of nowhere. They are busy in the app and did nothing to ask for it. Be playful.", fake.LegacyPrompt);
            }
        }
        finally { CoreSettings.Current.UseCompanionBrain = old; }
    }

    public class FakeAi : DispatchProxy
    {
        public string? LegacyPrompt;
        public AiCallOptions? Options;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_IsAvailable") return true;
            if (method.Name == "GetBambiReplyExAsync") LegacyPrompt = (string)args![0]!;
            else if (method.Name == "SendAsync") Options = (AiCallOptions)args![1]!;
            else throw new InvalidOperationException(method.Name);
            return Task.FromResult(new AiReplyResult("Hello there", true, null));
        }
    }

    private sealed class EmptyStore : ICompanionSessionStore
    {
        public CompanionSessionSnapshot Load() => CompanionSessionSnapshot.Empty;
        public void Save(IReadOnlyList<CompanionTurn> turns) { }
        public void Wipe() { }
    }
}

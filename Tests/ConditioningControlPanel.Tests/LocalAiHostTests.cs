using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #5197: a local AI host saved without http:// made new Uri(host) throw in the LocalAiService
/// constructor, and the exception escaped through App.Ai.IsAvailable into the MainWindow
/// constructor, so the panel never opened.
/// </summary>
public class LocalAiHostTests
{
    [Theory]
    [InlineData("127.0.0.1:11434", "http://127.0.0.1:11434/")]
    [InlineData("localhost:11434", "http://localhost:11434/")]
    [InlineData("  box:11434/  ", "http://box:11434/")]
    [InlineData("http://192.168.1.9:11434", "http://192.168.1.9:11434/")]
    [InlineData("http://localhost:11434/", "http://localhost:11434/")]
    [InlineData("https://ollama.example.net/", "https://ollama.example.net/")]
    public void Host_without_scheme_gets_http_and_one_slash(string saved, string expected)
    {
        Assert.Equal(expected, LocalAiHost.Normalize(saved, out var fellBack));
        Assert.False(fellBack);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_host_is_the_default_without_a_warning(string? saved)
    {
        Assert.Equal(LocalAiHost.Default, LocalAiHost.Normalize(saved, out var fellBack));
        Assert.False(fellBack);
        Assert.False(LocalAiHost.TryNormalize(saved, out _));
    }

    [Theory]
    [InlineData("http://")]
    [InlineData("ftp://box:11434")]
    [InlineData("http://box:99999")]
    [InlineData("::::")]
    [InlineData("http:// bad host")]
    [InlineData("file:///C:/ollama")]
    public void Garbage_host_falls_back_to_the_default_and_never_throws(string saved)
    {
        Assert.Equal(LocalAiHost.Default, LocalAiHost.Normalize(saved, out var fellBack));
        Assert.True(fellBack);
        Assert.False(LocalAiHost.TryNormalize(saved, out _));
    }

    [Theory]
    [InlineData("127.0.0.1:11434")]
    [InlineData("")]
    [InlineData("::::")]
    [InlineData("http://")]
    public void Every_normalized_host_builds_a_base_address(string saved)
    {
        var host = LocalAiHost.Normalize(saved);
        var uri = new Uri(host); // the call that threw in #5197
        Assert.True(uri.IsAbsoluteUri);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("127.0.0.1:11434", true)]
    [InlineData("http://localhost:11434/", true)]
    [InlineData("http://", false)]
    [InlineData("::::", false)]
    public void Engine_room_saves_only_a_usable_or_blank_host(string text, bool saves)
    {
        Assert.Equal(saves, EngineRoomRuntimeVm.IsSavableHost(text));
    }

    [Fact]
    public void A_local_provider_that_fails_to_build_reads_as_unavailable_and_is_not_retried_at_once()
    {
        var strategy = new AiServiceStrategy();
        int builds = 0;
        strategy.LocalFactory = () => { builds++; throw new UriFormatException("Invalid URI"); };

        Assert.Null(strategy.TryBuildLocal());
        Assert.Null(strategy.TryBuildLocal());
        Assert.Equal(1, builds);
    }

    [Fact]
    public async Task The_unavailable_stand_in_never_throws()
    {
        var stand = AiServiceStrategy.UnavailableAiService.Instance;
        Assert.False(stand.IsAvailable);
        var reply = await stand.SendAsync(Array.Empty<ChatMessage>(), new AiCallOptions());
        Assert.Equal(AiFailureKind.Unavailable, reply.Failure);
#pragma warning disable CS0618
        Assert.Null(await stand.GetKeywordCommentAsync("x"));
#pragma warning restore CS0618
    }
}

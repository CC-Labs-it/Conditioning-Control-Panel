using ConditioningControlPanel.Services.Companion.Rain;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RainLookHashTests
{
    [Fact]
    public void DefaultHashMatchesWpfSeventeenActionClipSet()
    {
        const string look = """
            {"rev":1,"items":{"hair":"h1","dress":"d1"},"colors":{"fabric":"#ff00aa"}}
            """;
        // WPF RainLookHash: layout rev 1, clip set 2 (17-action GLB), ordinal-sorted JSON.
        // Fixed SHA-256 calculated independently from that UTF-8 payload.
        Assert.Equal("50812c7276eb0b76a875cb1192ca63e905e405152cd3207e57e9a457d0045388",
            RainLookHash.Compute(look));
    }
}

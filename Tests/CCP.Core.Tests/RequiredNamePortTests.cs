using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RequiredNamePortTests
{
    [Theory]
    [InlineData(true, null, false, false)]
    [InlineData(true, "Old", true, true)]
    [InlineData(true, null, null, true)]
    [InlineData(false, null, true, false)]
    public void ServerFlagWins(bool signedIn, string? name, bool? flag, bool ask) =>
        Assert.Equal(ask, NeedsDisplayNameRule.ShouldAsk(signedIn, name, flag));

    [Theory]
    [InlineData("ab", DisplayNameShape.TooShort)]
    [InlineData("Own Name", DisplayNameShape.Ok)]
    [InlineData("a@b", DisplayNameShape.BadChars)]
    public void ShapeMatchesRequiredMode(string name, DisplayNameShape shape) =>
        Assert.Equal(shape, NeedsDisplayNameRule.Check(name));

    private sealed class Wire : HttpMessageHandler
    {
        public string? Query;
        public bool Throw;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Query = request.RequestUri!.Query;
            if (Throw) throw new HttpRequestException();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"available":false}""") });
        }
    }

    [Fact]
    public async Task AvailabilityEscapesNameAndTreatsNetworkFailureAsUnknown()
    {
        var wire = new Wire();
        var api = new V2AuthService(() => null, wire);
        Assert.False(await api.IsDisplayNameAvailableAsync("Own Name&"));
        Assert.Equal("?name=Own%20Name%26", wire.Query);
        wire.Throw = true;
        Assert.Null(await api.IsDisplayNameAvailableAsync("Own Name"));
    }
}

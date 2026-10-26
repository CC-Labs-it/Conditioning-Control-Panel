using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1499: a stale app token made the sign-in handoff answer 401 and the Just Drop window
/// showed the raw "invalid_ccp_token" JSON. A refused handoff now falls back to the signed-out page.
/// Only an error status landing ON the handoff url counts; the site's own pages never trip it.
/// </summary>
public class JustDropHandoffFallbackTests
{
    [Theory]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?unified_id=u&next=%2Fdashboard%2Fexpress", 401, true)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?unified_id=u&next=%2Fdashboard%2Fexpress", 400, true)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?unified_id=u&next=%2Fdashboard%2Fexpress", 429, true)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?unified_id=u&next=%2Fdashboard%2Fexpress", 502, true)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?unified_id=u&next=%2Fdashboard%2Fexpress", 200, false)]
    [InlineData("https://app.cclabs.app/dashboard/express", 401, false)]
    [InlineData("https://app.cclabs.app/dashboard/express", 200, false)]
    [InlineData("https://evil.example/api/auth/desktop-session", 401, false)]
    [InlineData(null, 401, false)]
    public void Only_a_refused_handoff_falls_back(string? source, int status, bool expected)
        => Assert.Equal(expected, JustDropHostService.IsRefusedHandoff(source, status));
}

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class JackpotRemixHostTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source #1.gif");
        public string Output => Path.Combine(Root, "out");
        public Fixture() { Directory.CreateDirectory(Root); File.WriteAllBytes(Source, "GIF89a"u8.ToArray()); }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static string Reply(string request)
    {
        using var json = JsonDocument.Parse(request);
        return JsonSerializer.Serialize(new { type = "built", id = json.RootElement.GetProperty("id").GetString(), ok = true,
            code = "fixture", base64 = Convert.ToBase64String("GIF89aoutput"u8.ToArray()) });
    }

    [Fact]
    public async Task BuildCyclesLocalFixturesAndWritesMatchingReply()
    {
        using var f = new Fixture();
        using var builder = new JackpotRemixBuilder(f.Root, () => f.Root, f.Output, exchange: (request, _) =>
        {
            using var json = JsonDocument.Parse(request);
            var media = json.RootElement.GetProperty("media").EnumerateArray().ToArray();
            Assert.Equal(8, media.Length);
            Assert.All(media, m => Assert.EndsWith("/ccp.assets/source%20%231.gif", m.GetProperty("url").GetString()));
            Assert.Equal(17, json.RootElement.GetProperty("seed").GetInt32());
            return Task.FromResult<string?>(Reply(request));
        });
        var output = await builder.BuildAsync(new[] { f.Source }, 17, CancellationToken.None);
        Assert.NotNull(output);
        Assert.Equal("GIF89aoutput"u8.ToArray(), File.ReadAllBytes(output));
    }

    [Fact]
    public async Task BuildResolvesCurrentLibraryForEveryRequest()
    {
        using var f = new Fixture();
        var current = f.Root;
        var other = Path.Combine(f.Root, "other");
        Directory.CreateDirectory(other);
        var next = Path.Combine(other, "next.gif");
        File.WriteAllBytes(next, "GIF89a"u8.ToArray());
        using var builder = new JackpotRemixBuilder(f.Root, () => current, f.Output,
            exchange: (request, _) => Task.FromResult<string?>(Reply(request)));
        Assert.NotNull(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        current = other;
        Assert.Null(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.NotNull(await builder.BuildAsync(new[] { next }, 1, CancellationToken.None));
    }

    [Fact]
    public async Task ReplyWithWrongIdIsIgnored()
    {
        using var f = new Fixture();
        using var builder = new JackpotRemixBuilder(f.Root, () => f.Root, f.Output,
            exchange: (_, _) => Task.FromResult<string?>("{\"type\":\"built\",\"id\":\"wrong\",\"ok\":true,\"base64\":\"R0lGODlh\"}"));
        Assert.Null(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.False(Directory.Exists(f.Output));
    }

    [Fact]
    public async Task FailureRecoversOnceAndSecondFailureDisablesHost()
    {
        using var f = new Fixture();
        var calls = 0;
        using var builder = new JackpotRemixBuilder(f.Root, () => f.Root, f.Output, exchange: (request, _) =>
            Task.FromResult(++calls == 2 ? Reply(request) : null));
        Assert.Null(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.NotNull(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.Null(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.True(builder.Disabled);
        Assert.Null(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task FailuresSurviveBuilderReplacement()
    {
        using var f = new Fixture();
        var state = new JackpotRemixBuilder.FailureState();
        var calls = 0;
        using (var first = new JackpotRemixBuilder(f.Root, () => f.Root, f.Output,
            exchange: (_, _) => { calls++; return Task.FromResult<string?>(null); }, failures: state))
        {
            await first.BuildAsync(new[] { f.Source }, 1, CancellationToken.None);
            await first.BuildAsync(new[] { f.Source }, 1, CancellationToken.None);
        }
        using var next = new JackpotRemixBuilder(f.Root, () => f.Root, f.Output,
            exchange: (_, _) => { calls++; return Task.FromResult<string?>(null); }, failures: state);
        Assert.True(next.Disabled);
        Assert.Null(await next.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TimeoutDoesNotDisableAndLaterBuildSucceeds()
    {
        using var f = new Fixture();
        var calls = 0;
        using var builder = new JackpotRemixBuilder(f.Root, () => f.Root, f.Output, TimeSpan.FromMilliseconds(50),
            (request, _) => ++calls <= 2 ? new TaskCompletionSource<string?>().Task : Task.FromResult<string?>(Reply(request)));
        Assert.Null(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.False(builder.Disabled);
        Assert.Null(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.False(builder.Disabled);
        Assert.NotNull(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
    }

    [Fact]
    public async Task CancelAndDisposeStopPendingBuildWithoutDisabling()
    {
        using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var builder = new JackpotRemixBuilder(f.Root, () => f.Root, f.Output, exchange: (_, _) =>
        { entered.TrySetResult(); return new TaskCompletionSource<string?>().Task; });
        using var cancel = new CancellationTokenSource();
        var first = builder.BuildAsync(new[] { f.Source }, 1, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        Assert.Null(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(builder.Disabled);
        var second = builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None);
        builder.Dispose();
        Assert.Null(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(await builder.BuildAsync(new[] { f.Source }, 1, CancellationToken.None));
        Assert.False(Directory.Exists(f.Output));
    }
}

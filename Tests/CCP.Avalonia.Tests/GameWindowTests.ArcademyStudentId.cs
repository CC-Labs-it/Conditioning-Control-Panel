using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    /// <summary>A ready Arcademy window with the Discord seams stubbed and put back afterwards.</summary>
    private static void RunStudentId(bool linked, Func<Action<string>, CancellationToken, Task> signIn,
        Action<GameWindow, List<JObject>, Func<bool>, Action<bool>> body) => Run((_, launcher) =>
    {
        var s = CoreSettings.Current;
        var old = (GameWindow.ArcademyDiscordLinked, GameWindow.ArcademyDiscordSignIn, CoreAccount.DisplayNameProvider, s.ArcademyPresenceShare);
        var oldCache = (ArcademyAvatarCache.Account, ArcademyAvatarCache.Fetch);
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        var frames = new List<JObject>(); w.Posted += json => frames.Add(JObject.Parse(json));
        try
        {
            ArcademyAvatarCache.Account = () => null;
            ClearAvatarCache();
            GameWindow.ArcademyDiscordLinked = () => linked;
            GameWindow.ArcademyDiscordSignIn = signIn;
            CoreAccount.DisplayNameProvider = () => "  Student  ";
            s.ArcademyPresenceShare = "username";
            launcher.Hide(); w.Show(); Dispatcher.UIThread.RunJobs();
            w.HandleMessage("{\"type\":\"ready\"}");
            body(w, frames, () => linked, v => linked = v);
        }
        finally
        {
            w.Close();
            (GameWindow.ArcademyDiscordLinked, GameWindow.ArcademyDiscordSignIn, CoreAccount.DisplayNameProvider, s.ArcademyPresenceShare) = old;
            (ArcademyAvatarCache.Account, ArcademyAvatarCache.Fetch) = oldCache;
            ClearAvatarCache();
        }
    });

    private static void ClearAvatarCache()
    {
        if (!Directory.Exists(ArcademyAvatarCache.Dir)) return;
        foreach (var f in new[] { "avatar.jpg", "avatar.hash" }) File.Delete(Path.Combine(ArcademyAvatarCache.Dir, f));
    }

    private static byte[] Png(SKColor color)
    {
        using var bmp = new SKBitmap(256, 256);
        bmp.Erase(color);
        return bmp.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    // WPF ArcademyHostService.cs:529-532, 1057, 4387-4401, 4430-4436; ArcademyAvatarCache.cs:225-323: the window open
    // caches the photo, the discord rung shows it, a new avatar hash re-fetches it, offline fetches nothing.
    [Fact]
    public void Arcademy_StudentPhotoAppearsAndUpdates() => RunStudentId(true, (_, _) => Task.CompletedTask, (w, frames, _, _) =>
    {
        var fetched = new List<string>();
        var avatar = ("u:a1", Png(SKColors.Red));
        ArcademyAvatarCache.Account = () => (avatar.Item1, "https://cdn.discordapp.com/avatars/1/" + avatar.Item1[2..] + ".gif?size=128");
        ArcademyAvatarCache.Fetch = url => { fetched.Add(url); return Task.FromResult<byte[]?>(avatar.Item2); };
        string? Photo() => (string?)frames.Last(f => (string?)f["type"] == "profile")["profile"]!["avatarUrl"];
        // The kick from ready (sent before the stub) found nothing to do; ask again the way a re-open would.
        CoreSettings.Current.ArcademyPresenceShare = "discord";
        Dispatcher.UIThread.RunJobs();
        Settle(w.ArcademyLastAvatar);
        Assert.Equal(new[] { "https://cdn.discordapp.com/avatars/1/a1.png?size=128" }, fetched);
        var first = Photo();
        Assert.StartsWith("data:image/jpeg;base64,", first);
        Assert.True(first!.Length <= ArcademyAvatarCache.MaxDataUriChars);

        // Same hash: no second fetch. New hash: the link short path waits for the new photo before answering.
        CoreSettings.Current.ArcademyPresenceShare = "username";
        CoreSettings.Current.ArcademyPresenceShare = "discord";
        Dispatcher.UIThread.RunJobs();
        Settle(w.ArcademyLastAvatar);
        Assert.Single(fetched);
        avatar = ("u:a2", Png(SKColors.Blue));
        w.HandleMessage("{\"type\":\"link-discord\"}");
        Settle(w.ArcademyLastLink);
        Assert.Equal(2, fetched.Count);
        var linked = frames.Last(f => (string?)f["result"] == "linked");
        Assert.StartsWith("data:image/jpeg;base64,", (string?)linked["profile"]!["avatarUrl"]);
        Assert.NotEqual(first, (string?)linked["profile"]!["avatarUrl"]);

        // Off the discord rung the card wears no photo; offline never leaves the machine.
        CoreSettings.Current.ArcademyPresenceShare = "anon";
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Photo());
        var offline = CoreSettings.Current.OfflineMode;
        try
        {
            CoreSettings.Current.OfflineMode = true;
            avatar = ("u:a3", Png(SKColors.Green));
            CoreSettings.Current.ArcademyPresenceShare = "discord";
            Dispatcher.UIThread.RunJobs();
            Settle(w.ArcademyLastAvatar);
            Assert.Equal(2, fetched.Count);
            Assert.StartsWith("data:image/jpeg;base64,", Photo());   // the photo on disk stays readable
        }
        finally { CoreSettings.Current.OfflineMode = offline; }
    });

    private static string?[] LinkResults(List<JObject> frames) =>
        frames.Where(f => (string?)f["type"] == "profile" && f["result"]?.Type == JTokenType.String).Select(f => (string?)f["result"]).ToArray();

    // WPF ArcademyHostService.cs:800, 1047-1070: who the card is made out to, no photo without the cache.
    [Fact]
    public void Arcademy_InitCarriesTheStudentIdProfile() => RunStudentId(false, (_, _) => Task.CompletedTask, (w, frames, _, _) =>
    {
        var profile = (JObject)frames.Single(f => (string?)f["type"] == "init")["profile"]!;
        Assert.Equal(("Student", false, "username"), ((string?)profile["name"], (bool)profile["discordLinked"]!, (string?)profile["presenceShare"]));
        Assert.Equal(JTokenType.Null, profile["avatarUrl"]!.Type);
        // WPF :5857: a rung moved anywhere repaints the card.
        CoreSettings.Current.ArcademyPresenceShare = "anon";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("anon", (string?)frames.Last(f => (string?)f["type"] == "profile")["profile"]!["presenceShare"]);
    });

    // WPF ArcademyHostService.cs:4430-4436: an already-linked account applies the rung and answers linked, no OAuth.
    [Fact]
    public void Arcademy_LinkDiscordLinkedShortPath()
    {
        int calls = 0;
        RunStudentId(true, (_, _) => { calls++; return Task.CompletedTask; }, (w, frames, _, _) =>
        {
            w.HandleMessage("{\"type\":\"link-discord\"}");
            Settle(w.ArcademyLastLink);
            Assert.Equal(new string?[] { "linked" }, LinkResults(frames));
            Assert.Equal("discord", CoreSettings.Current.ArcademyPresenceShare);
            Assert.Equal("discord", (string?)frames.Last(f => (string?)f["result"] == "linked")["profile"]!["presenceShare"]);
            Assert.Contains(frames, f => (string?)f["type"] == "setting" && (string?)f["key"] == "presenceShare");
        });
        Assert.Equal(0, calls);
    }

    // WPF ArcademyHostService.cs:4516-4525: panic closes the open link-up and the page hears cancelled exactly once.
    [Fact]
    public void Arcademy_LinkDiscordCancelOnPanicRepliesOnce()
    {
        var tokens = new List<CancellationToken>();
        RunStudentId(false, (_, ct) => { tokens.Add(ct); return Task.Delay(Timeout.Infinite, ct); }, (w, frames, _, _) =>
        {
            w.HandleMessage("{\"type\":\"link-discord\"}");
            var flow = w.ArcademyLastLink;
            w.HandleMessage("{\"type\":\"link-discord\"}");   // one link in flight
            Assert.Single(tokens);
            Assert.True(GameWindow.HandleArcademyPanic(DateTime.Now));
            Assert.True(tokens[0].IsCancellationRequested);
            Settle(flow);
            Assert.Equal(new string?[] { "cancelled" }, LinkResults(frames));
            Assert.Equal("username", CoreSettings.Current.ArcademyPresenceShare);
            // Teardown cancels too, and nobody is left to answer.
            w.HandleMessage("{\"type\":\"link-discord\"}");
            var second = w.ArcademyLastLink;
            w.Close();
            Assert.True(tokens[1].IsCancellationRequested);
            Settle(second);
            Assert.Equal(new string?[] { "cancelled" }, LinkResults(frames));
        });
    }

    // WPF ArcademyHostService.cs:4460-4491: a thrown flow is failed, a flow that links nothing is cancelled, success is linked.
    [Fact]
    public void Arcademy_LinkDiscordFailureAndSuccessReplies()
    {
        var mode = "throw";
        RunStudentId(false, (_, _) => mode == "throw" ? Task.FromException(new InvalidOperationException("proxy down")) : Task.CompletedTask, (w, frames, _, setLinked) =>
        {
            w.HandleMessage("{\"type\":\"link-discord\"}");
            Settle(w.ArcademyLastLink);
            mode = "noop";
            w.HandleMessage("{\"type\":\"link-discord\"}");
            Settle(w.ArcademyLastLink);
            Assert.Equal("username", CoreSettings.Current.ArcademyPresenceShare);
            setLinked(true);
            w.HandleMessage("{\"type\":\"link-discord\"}");
            Settle(w.ArcademyLastLink);
            Assert.Equal(new string?[] { "failed", "cancelled", "linked" }, LinkResults(frames));
            Assert.Equal("discord", CoreSettings.Current.ArcademyPresenceShare);
        });
    }
}

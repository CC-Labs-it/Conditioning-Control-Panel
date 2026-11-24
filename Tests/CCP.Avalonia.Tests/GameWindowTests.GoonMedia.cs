using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF GoonHostService.cs:503,1851-2006: init carries the media block; the pick is a session opt-in.
    [Fact]
    public void GoonMedia_PickIsStoredAndOnlineOffDeclinesPeerAndNoise() => Run((_, launcher) =>
    {
        var s = CoreSettings.Current;
        var old = (s.GoonMediaFlavour, s.GoonMediaSubs, s.GoonMediaCustom, s.GoonMediaOnline);
        var oldMaterialize = GoonOnlineMedia.Materialize;
        var oldRelease = GoonOnlineMedia.ReleaseTempFile;
        try
        {
            (s.GoonMediaFlavour, s.GoonMediaSubs, s.GoonMediaOnline) = ("pink", "", true);
            launcher.Hide();
            var w = GameWindow.Launch("goon")!;
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            var media = Assert.Single(frames, x => (string?)x["type"] == "init")["media"]!;
            Assert.Equal(("", "pink", true), ((string?)media["flavour"], (string?)media["last"], (bool?)media["online"]));
            w.HandleMessage("{\"type\":\"media-flavour\",\"flavour\":\"shiny\",\"subs\":[],\"online\":true}");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("shiny", s.GoonMediaFlavour);
            Assert.Equal("empty", (string?)Assert.Single(frames, x => (string?)x["type"] == "online-media")["state"]);
            w.HandleMessage("{\"type\":\"media-flavour\",\"flavour\":\"shiny\",\"subs\":[],\"online\":false}");
            Assert.False(s.GoonMediaOnline);
            w.HandleMessage("{\"type\":\"peer-niches\",\"subs\":[\"cats\"]}");
            w.HandleMessage("{\"type\":\"noise-want\",\"set\":\"cats\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(frames, x => (string?)x["type"] == "peer-media" && (string?)x["state"] == "declined");
            Assert.Contains(frames, x => (string?)x["type"] == "noise-media" && (string?)x["state"] == "declined" && (string?)x["set"] == "cats");
            w.Close();
        }
        finally
        {
            (s.GoonMediaFlavour, s.GoonMediaSubs, s.GoonMediaCustom, s.GoonMediaOnline) = old;
            GoonOnlineMedia.Materialize = oldMaterialize;
            GoonOnlineMedia.ReleaseTempFile = oldRelease;
        }
    });

    // WPF GoonShareCardHandler.cs:16-101: every request answers once; save writes the PNG bytes.
    [Fact]
    public void GoonShareCard_RefusesBadFramesAndSavesThePng() => Run((_, launcher) =>
    {
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        var old = GameWindow.GoonSavePicker;
        var dir = Directory.CreateTempSubdirectory("goon-share-").FullName;
        var target = Path.Combine(dir, "card.png");
        string? asked = null;
        try
        {
            launcher.Hide();
            var w = GameWindow.Launch("goon")!;
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            JObject Send(string frame)
            {
                frames.Clear();
                w.HandleMessage(frame);
                var deadline = DateTime.UtcNow.AddSeconds(10); // the save resumes on the UI thread
                while (!w.GoonSharePending.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(1); }
                Dispatcher.UIThread.RunJobs();
                return Assert.Single(frames, x => (string?)x["type"] == "share-card-result");
            }
            Assert.Equal("bad-format", (string?)Send("{\"type\":\"share-card\",\"id\":\"a1\",\"action\":\"save\",\"png\":\"AAAA\"}")["error"]);
            Assert.Equal("bad-format", (string?)Send("{\"type\":\"share-card\",\"id\":{},\"action\":\"save\"}")["error"]);
            GameWindow.GoonSavePicker = (_, name) => { asked = name; return System.Threading.Tasks.Task.FromResult<string?>(null); };
            Assert.Equal("cancelled", (string?)Send("{\"type\":\"share-card\",\"id\":\"a2\",\"action\":\"save\",\"name\":\"x/y\",\"png\":\"" + png + "\"}")["error"]);
            Assert.Equal("x-y.png", asked);
            GameWindow.GoonSavePicker = (_, _) => System.Threading.Tasks.Task.FromResult<string?>(target);
            var ok = Send("{\"type\":\"share-card\",\"id\":\"a3\",\"action\":\"save\",\"png\":\"" + png + "\"}");
            Assert.Equal(("a3", true), ((string?)ok["id"], (bool)ok["ok"]!));
            Assert.Equal(Convert.FromBase64String(png), File.ReadAllBytes(target));
            w.Close();
        }
        finally { GameWindow.GoonSavePicker = old; Directory.Delete(dir, true); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoonShareCard_RetriesBusyClipboardAndRepliesOnce(bool eventuallySucceeds) => Run((_, launcher) =>
    {
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        var old = GameWindow.GoonCopyBitmap;
        var oldMaterialize = GoonOnlineMedia.Materialize;
        var oldRelease = GoonOnlineMedia.ReleaseTempFile;
        GameWindow? w = null;
        var attempts = 0;
        try
        {
            GameWindow.GoonCopyBitmap = (_, _) =>
            {
                attempts++;
                return !eventuallySucceeds || attempts < 3
                    ? System.Threading.Tasks.Task.FromException(new InvalidOperationException("busy"))
                    : System.Threading.Tasks.Task.CompletedTask;
            };
            launcher.Hide();
            w = GameWindow.Launch("goon")!;
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"share-card\",\"id\":\"copy1\",\"action\":\"copy\",\"png\":\"" + png + "\"}");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!w.GoonSharePending.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(1); }
            Dispatcher.UIThread.RunJobs();
            var reply = Assert.Single(frames, x => (string?)x["type"] == "share-card-result");
            Assert.Equal(eventuallySucceeds ? 3 : 4, attempts);
            Assert.Equal(eventuallySucceeds, (bool)reply["ok"]!);
            Assert.Equal(eventuallySucceeds ? "" : "clipboard-busy", (string?)reply["error"]);
            Assert.Equal("copy1", (string?)reply["id"]);
        }
        finally
        {
            w?.Close();
            GameWindow.GoonCopyBitmap = old;
            GoonOnlineMedia.Materialize = oldMaterialize;
            GoonOnlineMedia.ReleaseTempFile = oldRelease;
        }
    });

}

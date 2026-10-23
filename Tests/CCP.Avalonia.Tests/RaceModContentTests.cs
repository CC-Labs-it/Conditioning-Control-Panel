using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RaceModContentTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("race-mod-").FullName;
    private readonly Func<ModPackage?>? _old = CoreMods.ActiveModPackageProvider;
    private string Root => Path.Combine(_dir, "resources", "dtrh");
    public RaceModContentTests() => CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "race-mod", Name = "Race" }, _dir, false);
    private string Write(string rel, string data = "x")
    {
        var path = Path.GetFullPath(Path.Combine(Root, rel));   // Windows normalises "/" the way the server does
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, data);
        return path;
    }
    public void Dispose() { CoreMods.ActiveModPackageProvider = _old; Directory.Delete(_dir, true); }

    [Fact]
    public void InitCollectsTintPoolsPortraitDroneAndServesOnlyTheModFolder()
    {
        Assert.Null(DtrhModContent.BuildInitPayload());
        var drone = Write("audio/drone.ogg");
        Write("barks/fall_sensory_01.ogg"); Write("barks/unknown.wav"); Write("barks/ignored.txt");
        Write("portrait/default.png"); Write("portrait/happy.png");
        Write("dtrh.json", "{\"tintA\":\"#123456\",\"tintB\":\"bad\",\"drone\":\"audio/drone.ogg\",\"driftMode\":\"mix\"}");
        var payload = JObject.FromObject(DtrhModContent.BuildInitPayload()!);
        Assert.Equal("race-mod", (string?)payload["id"]);
        Assert.Equal("18,52,86", (string?)payload["tint"]!["a"]);
        Assert.Equal((string?)payload["tint"]!["a"], (string?)payload["tint"]!["b"]);
        Assert.Equal("mix", (string?)payload["drift"]!["mode"]);
        Assert.Single(payload["drift"]!["pools"]!["sensory"]!);
        Assert.Single(payload["drift"]!["pools"]!["drift"]!);
        Assert.Equal("/ccp.mod/portrait/default.png", (string?)payload["portrait"]!["defaultUrl"]);
        Assert.Equal("/ccp.mod/portrait/happy.png", (string?)payload["portrait"]!["emotes"]!["happy"]);
        Assert.DoesNotContain(_dir, payload.ToString());
        using var server = new WebAssetServer(_dir);
        CoreMods.ActiveModPackageProvider = () => null; // mappings survive a later mod switch, as WPF does
        Assert.Equal(drone, server.ResolveFile((string)payload["droneUrl"]!));
        Assert.Null(server.ResolveFile("/ccp.mod/../../private.txt"));
    }

    [Fact]
    public void MediaModesBadMetadataAndClipCapMatchWpf()
    {
        Write("images/own image.webp");
        Write("dtrh.json", "{\"imageMode\":\"replace\",\"drone\":\"../private.mp3\"}");
        var m = new GameMediaManifest.Manifest();
        m.Images.Add(new("local", "/local")); m.Videos.Add(new("local-video", "/video"));
        DtrhModContent.MergeMedia(m);
        Assert.Equal("mod:own image.webp", Assert.Single(m.Images).Name);
        Assert.Equal("/ccp.mod/images/own%20image.webp", m.Images[0].Url);
        Assert.Equal("local-video", Assert.Single(m.Videos).Name);
        Write("portrait/happy.png");
        var payload = JObject.FromObject(DtrhModContent.BuildInitPayload()!);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, payload["droneUrl"]!.Type);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, payload["portrait"]!.Type);
        Write("dtrh.json", "broken");
        DtrhModContent.MergeMedia(m);
        Assert.Equal(2, m.Images.Count); // malformed metadata defaults to mix
        for (int i = 0; i < 201; i++) Write($"barks/fall_drift_{i:000}.mp3");
        payload = JObject.FromObject(DtrhModContent.BuildInitPayload()!);
        Assert.Equal(200, payload["drift"]!["pools"]!["drift"]!.Count());
        Assert.Equal("replace", (string?)payload["drift"]!["mode"]);
    }
}

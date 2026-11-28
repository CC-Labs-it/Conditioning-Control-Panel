using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WebAssetServerPortTests
{
    [Fact]
    public void Remembered_port_is_reused_after_disposal()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var preference = Path.Combine(dir, "port");
            int port;
            using (var first = new WebAssetServer(dir, preference)) { Assert.True(first.HasStableOrigin); port = first.Port; }
            using var second = new WebAssetServer(dir, preference);
            Assert.Equal(port, second.Port);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Occupied_preference_falls_back_then_returns_to_saved_origin()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var preference = Path.Combine(dir, "port");
            var held = new TcpListener(IPAddress.Loopback, 0);
            held.Start();
            try
            {
                int port = ((IPEndPoint)held.LocalEndpoint).Port;
                File.WriteAllText(preference, port.ToString());
                using (var server = new WebAssetServer(dir, preference))
                {
                    Assert.NotEqual(port, server.Port);
                    Assert.False(server.HasStableOrigin);
                }
                Assert.Equal(port.ToString(), File.ReadAllText(preference));
            }
            finally { held.Stop(); }
            using var restarted = new WebAssetServer(dir, preference);
            Assert.Equal(int.Parse(File.ReadAllText(preference)), restarted.Port);
            Assert.True(restarted.HasStableOrigin);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Saved_port_freed_during_the_retries_is_reused()
    {
        var dir = Directory.CreateTempSubdirectory("race-port-").FullName;
        var held = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            var preference = Path.Combine(dir, "port");
            held.Start();
            int port = ((IPEndPoint)held.LocalEndpoint).Port;
            File.WriteAllText(preference, port.ToString());
            var release = Task.Delay(300).ContinueWith(_ => held.Stop());
            using var server = new WebAssetServer(dir, preference);
            release.Wait();
            Assert.Equal(port, server.Port);
            Assert.True(server.HasStableOrigin);
            Assert.False(server.SavedPortTaken);
        }
        finally { held.Stop(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Adopting_a_fallback_port_saves_it_as_the_origin()
    {
        var dir = Directory.CreateTempSubdirectory("race-port-").FullName;
        var held = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            var preference = Path.Combine(dir, "port");
            held.Start();
            File.WriteAllText(preference, ((IPEndPoint)held.LocalEndpoint).Port.ToString());
            using var server = new WebAssetServer(dir, preference);
            Assert.True(server.SavedPortTaken);
            Assert.False(server.HasStableOrigin);
            Assert.True(server.AdoptOrigin());
            Assert.True(server.HasStableOrigin);
            Assert.Equal(server.Port.ToString(), File.ReadAllText(preference));
        }
        finally { held.Stop(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Cannot_save_preference_makes_origin_unstable()
    {
        var dir = Directory.CreateTempSubdirectory("race-port-").FullName;
        try
        {
            var file = Path.Combine(dir, "not-a-directory");
            File.WriteAllText(file, "x");
            using var server = new WebAssetServer(dir, Path.Combine(file, "port"));
            Assert.False(server.HasStableOrigin);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Unreadable_preference_is_not_replaced()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Directory.CreateTempSubdirectory("race-port-").FullName;
        var preference = Path.Combine(dir, "port");
        try
        {
            File.WriteAllText(preference, "12345");
            File.SetUnixFileMode(preference, UnixFileMode.UserWrite);
            using (var server = new WebAssetServer(dir, preference))
                Assert.False(server.HasStableOrigin);
            File.SetUnixFileMode(preference, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Equal("12345", File.ReadAllText(preference));
        }
        finally
        {
            if (File.Exists(preference)) File.SetUnixFileMode(preference, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Request_without_token_is_refused()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "index.html"), "page");
            using var server = new WebAssetServer(dir, Path.Combine(dir, "port"));
            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/index.html");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally { Directory.Delete(dir, true); }
    }
}

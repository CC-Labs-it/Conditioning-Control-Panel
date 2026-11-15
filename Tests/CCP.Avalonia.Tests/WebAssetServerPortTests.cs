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
            using (var first = new WebAssetServer(dir, preference)) port = first.Port;
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
                    Assert.NotEqual(port, server.Port);
                Assert.Equal(port.ToString(), File.ReadAllText(preference));
            }
            finally { held.Stop(); }
            using var restarted = new WebAssetServer(dir, preference);
            Assert.Equal(int.Parse(File.ReadAllText(preference)), restarted.Port);
        }
        finally { Directory.Delete(dir, true); }
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

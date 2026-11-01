using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public sealed class CloudDataExportTests : IDisposable
{
    private sealed class Server : HttpMessageHandler
    {
        public readonly Queue<(HttpStatusCode Status, string Body)> Replies = new();
        public readonly List<(string Path, string? Token, string Body)> Calls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("X-Auth-Token", out var token) ? token.Single() : null,
                await request.Content!.ReadAsStringAsync(ct)));
            var (status, body) = Replies.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private readonly Func<string, string?>? _get = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _set = CoreSecrets.StoreProvider;
    private readonly Func<Task<bool>>? _revalidate = AuthRecovery.ProviderRevalidate;
    private readonly Action? _recovered = AuthRecovery.Recovered;
    private readonly Func<HttpResponseMessage, string?, Task<bool>>? _merged = V2AuthService.MergedRecovery;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly Server _server = new();
    private readonly AppSettings _settings;

    public CloudDataExportTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
        _settings = new AppSettings { UnifiedId = "u-test", AuthToken = "old" };
        AuthRecovery.ResetForTest();
        AuthRecovery.SettingsForTest = () => _settings;
        AuthRecovery.HandlerForTest = _server;
        AuthRecovery.ProviderRevalidate = null;
        AuthRecovery.Recovered = null;
        V2AuthService.MergedRecovery = null;
    }

    public void Dispose()
    {
        AuthRecovery.ResetForTest();
        AuthRecovery.ProviderRevalidate = _revalidate;
        AuthRecovery.Recovered = _recovered;
        V2AuthService.MergedRecovery = _merged;
        CoreSecrets.RetrieveProvider = _get;
        CoreSecrets.StoreProvider = _set;
    }

    [Fact]
    public async Task ExportPostsIdentityAndTokenAndFormatsTheWholeResponse()
    {
        _server.Replies.Enqueue((HttpStatusCode.OK, "{\"profile\":{\"name\":\"Ada\"},\"items\":[1,2]}"));
        var (success, error, json) = await new CloudSettingsBackup(_server).ExportDataAsync(_settings);
        Assert.True(success, error);
        var call = Assert.Single(_server.Calls);
        Assert.Equal("/v2/user/export-data", call.Path);
        Assert.Equal("old", call.Token);
        Assert.Equal("u-test", (string?)JObject.Parse(call.Body)["unified_id"]);
        Assert.Equal(JToken.Parse("{\"profile\":{\"name\":\"Ada\"},\"items\":[1,2]}").ToString(), json);
    }

    [Fact]
    public async Task ExportRequiresIdentity()
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("en");
            _settings.UnifiedId = null;
            var result = await new CloudSettingsBackup(_server).ExportDataAsync(_settings);
            Assert.False(result.Success);
            Assert.Equal("You must be logged in to export your data", result.Error);
            Assert.Empty(_server.Calls);
        }
        finally { loc.SetLanguage(previous); }
    }

    [Theory]
    [InlineData("identity", "Debes iniciar sesión para exportar tus datos")]
    [InlineData("server", "Error del servidor: Forbidden")]
    [InlineData("connection", "La exportación de datos requiere conexión a Internet")]
    public async Task ExportErrorsUseTheSelectedLanguage(string scenario, string expected)
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("es");
            if (scenario == "identity") _settings.UnifiedId = null;
            if (scenario == "server") _server.Replies.Enqueue((HttpStatusCode.Forbidden, "{}"));
            // An empty reply queue throws, like a failed connection.
            var result = await new CloudSettingsBackup(_server).ExportDataAsync(_settings);
            Assert.False(result.Success);
            Assert.Equal(expected, result.Error);
        }
        finally { loc.SetLanguage(previous); }
    }

    [Fact]
    public void EveryLanguageDefinesAllExportStrings()
    {
        var keys = new[] { "cloud_export_sign_in_required", "cloud_export_server_error",
            "cloud_export_connection_required", "cloud_export_json_files" };
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
            var strings = JObject.Parse(File.ReadAllText(path));
            foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace((string?)strings[key]), $"{code}: {key}");
            Assert.Contains("{0}", (string)strings["cloud_export_server_error"]!);
        }
    }

    // WPF ProfileSyncService.cs:4296-4301 falls back to the status when deserialization returns null.
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("null")]
    public async Task ExportWithEmptyErrorBodyReportsServerStatus(string body)
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("en");
            _server.Replies.Enqueue((HttpStatusCode.InternalServerError, body));
            var result = await new CloudSettingsBackup(_server).ExportDataAsync(_settings);
            Assert.False(result.Success);
            Assert.Equal("Server error: InternalServerError", result.Error);
            Assert.Null(result.JsonData);
            Assert.Single(_server.Calls);
        }
        finally { loc.SetLanguage(previous); }
    }

    [Fact]
    public async Task ExportReportsServerError()
    {
        _server.Replies.Enqueue((HttpStatusCode.Forbidden, "{\"error\":\"denied\"}"));
        var result = await new CloudSettingsBackup(_server).ExportDataAsync(_settings);
        Assert.False(result.Success);
        Assert.Equal("denied", result.Error);
        Assert.Null(result.JsonData);
        Assert.Single(_server.Calls);
    }

    [Fact]
    public async Task ExportReportsMergedAccountWithoutRetry()
    {
        V2AuthService.MergedRecovery = (_, _) => Task.FromResult(true);
        _server.Replies.Enqueue((HttpStatusCode.Conflict, "{}"));
        var result = await new CloudSettingsBackup(_server).ExportDataAsync(_settings);
        Assert.False(result.Success);
        Assert.Equal(Loc.Get("account_merged_retry_hint"), result.Error);
        Assert.Single(_server.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExportRetries401OnlyAfterRecovery(bool recover)
    {
        _server.Replies.Enqueue((HttpStatusCode.Unauthorized, "{\"error\":\"bad token\"}"));
        _server.Replies.Enqueue(recover ? (HttpStatusCode.OK, "{\"auth_token\":\"fresh\"}") : (HttpStatusCode.Unauthorized, "{}"));
        if (recover) _server.Replies.Enqueue((HttpStatusCode.OK, "{\"data\":1}"));
        var result = await new CloudSettingsBackup(_server).ExportDataAsync(_settings);
        Assert.Equal(recover, result.Success);
        Assert.Equal(recover ? null : "bad token", result.Error);
        Assert.Equal(recover ? 3 : 2, _server.Calls.Count);
        Assert.Equal("/v2/auth/restore-session", _server.Calls[1].Path);
        if (recover) Assert.Equal(("/v2/user/export-data", "fresh"), (_server.Calls[2].Path, _server.Calls[2].Token));
        else Assert.Equal("old", _settings.AuthToken);
    }
}

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

    // WPF ProfileSyncService.cs:4257-4301; an empty error body falls back to the status (4296-4301).
    // Status -1: no identity, nothing sent. Null body: no reply queued, which throws like a failed connection.
    [Theory]
    [InlineData("en", -1, null, "You must be logged in to export your data")]
    [InlineData("es", -1, null, "Debes iniciar sesión para exportar tus datos")]
    [InlineData("es", 403, "{}", "Error del servidor: Forbidden")]
    [InlineData("es", 0, null, "La exportación de datos requiere conexión a Internet")]
    [InlineData("en", 500, "", "Server error: InternalServerError")]
    [InlineData("en", 500, "  ", "Server error: InternalServerError")]
    [InlineData("en", 500, "null", "Server error: InternalServerError")]
    [InlineData("en", 403, "{\"error\":\"denied\"}", "denied")]
    public async Task ExportErrorsAreReportedInTheSelectedLanguage(string language, int status, string? body, string expected)
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(language);
            if (status < 0) _settings.UnifiedId = null;
            if (body != null) _server.Replies.Enqueue(((HttpStatusCode)status, body));
            var result = await new CloudSettingsBackup(_server).ExportDataAsync(_settings);
            Assert.False(result.Success);
            Assert.Equal(expected, result.Error);
            Assert.Null(result.JsonData);
            Assert.Equal(status < 0 ? 0 : 1, _server.Calls.Count);
        }
        finally { loc.SetLanguage(previous); }
    }

    [Fact]
    public void EveryLanguageDefinesAllExportStrings()
    {
        var keys = new[] { "cloud_export_sign_in_required", "cloud_export_server_error",
            "cloud_export_connection_required", "cloud_export_json_files", "account_merged_signin_title", "account_merged_signin_summary", "account_merged_signin_body", "account_merged_retry_hint" };
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
            var strings = JObject.Parse(File.ReadAllText(path));
            foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace((string?)strings[key]), $"{code}: {key}");
            Assert.Contains("{0}", (string)strings["cloud_export_server_error"]!);
        }
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

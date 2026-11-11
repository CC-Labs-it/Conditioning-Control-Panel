using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class AutomaticCloudBackupHeadTests
{
    private sealed class Server : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<bool> Uploaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/backup-settings")) Uploaded.TrySetResult(true);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"backup\":null}") });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAndExitScheduleBackup(bool exit)
    {
        var oldClient = AutomaticCloudBackup.Client;
        var oldHook = CoreSettingsHooks.CloudBackup;
        var oldProvider = CoreSettings.ServiceProvider;
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        var secrets = new Dictionary<string, string?>();
        var server = new Server();
        try
        {
            CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
            CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.UnifiedId = "u_test";
            service.Current.AuthToken = "token";
            AutomaticCloudBackup.Client = new CloudSettingsBackup(server);
            AutomaticCloudBackup.Seed();
            if (exit) App.SaveSettingsOnExit(null, () => service.SaveImmediate());
            else service.Save();
            await server.Uploaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            AutomaticCloudBackup.Client = oldClient;
            CoreSettingsHooks.CloudBackup = oldHook;
            CoreSettings.ServiceProvider = oldProvider;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
        }
    }
}

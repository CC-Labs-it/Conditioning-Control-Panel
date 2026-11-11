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
            if (exit) App.SaveSettingsOnExit(null, () => service.SaveImmediate(suppressCloudBackup: true));
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
    private sealed class DelayedServer : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<bool> FetchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> UploadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> ReleaseFetch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> ReleaseUpload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Fetches;
        internal int Uploads;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/backup-settings"))
            {
                Interlocked.Increment(ref Uploads);
                UploadStarted.TrySetResult(true);
                await ReleaseUpload.Task;
            }
            else
            {
                Interlocked.Increment(ref Fetches);
                FetchStarted.TrySetResult(true);
                await ReleaseFetch.Task;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"backup\":null}") };
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitWaitsForDelayedFetchAndUploadIncludingAnExistingBackup(bool alreadyRunning)
    {
        var oldClient = AutomaticCloudBackup.Client;
        var oldHook = CoreSettingsHooks.CloudBackup;
        var oldProvider = CoreSettings.ServiceProvider;
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        var secrets = new Dictionary<string, string?>();
        var server = new DelayedServer();
        Task? pending = null;
        Task? exit = null;
        var saved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
            if (alreadyRunning)
            {
                pending = CoreSettingsHooks.CloudBackup!();
                await server.FetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            exit = Task.Run(() => App.SaveSettingsOnExit(null, () =>
            {
                service.SaveImmediate(suppressCloudBackup: true);
                saved.TrySetResult(true);
            }));
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await server.FetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(exit.IsCompleted);
            server.ReleaseFetch.TrySetResult(true);
            await server.UploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(exit.IsCompleted);
            server.ReleaseUpload.TrySetResult(true);
            await exit.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, server.Fetches);
            Assert.Equal(1, server.Uploads);
        }
        finally
        {
            server.ReleaseFetch.TrySetResult(true);
            server.ReleaseUpload.TrySetResult(true);
            if (exit != null) await exit.WaitAsync(TimeSpan.FromSeconds(10));
            if (pending != null) await pending.WaitAsync(TimeSpan.FromSeconds(10));
            AutomaticCloudBackup.Client = oldClient;
            CoreSettingsHooks.CloudBackup = oldHook;
            CoreSettings.ServiceProvider = oldProvider;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
        }
    }

    [Fact]
    public async Task ExitWaitHasABoundAndDoesNotRestartAnInFlightBackup()
    {
        var oldClient = AutomaticCloudBackup.Client;
        var oldHook = CoreSettingsHooks.CloudBackup;
        var oldProvider = CoreSettings.ServiceProvider;
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        var secrets = new Dictionary<string, string?>();
        var server = new DelayedServer();
        Task? pending = null;
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
            pending = CoreSettingsHooks.CloudBackup!();
            await server.FetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var started = System.Diagnostics.Stopwatch.StartNew();
            Assert.False(AutomaticCloudBackup.WaitForExit(TimeSpan.FromMilliseconds(150)));
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(3));
            Assert.Equal(1, server.Fetches);
        }
        finally
        {
            server.ReleaseFetch.TrySetResult(true);
            server.ReleaseUpload.TrySetResult(true);
            if (pending != null) await pending.WaitAsync(TimeSpan.FromSeconds(10));
            AutomaticCloudBackup.Client = oldClient;
            CoreSettingsHooks.CloudBackup = oldHook;
            CoreSettings.ServiceProvider = oldProvider;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
        }
    }

}

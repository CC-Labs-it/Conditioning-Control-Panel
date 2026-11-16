using System;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RemoteSessionProviderIsolationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public Task Remote_session_test_restores_both_session_providers(int test) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldRunning = CoreSession.IsSessionRunningProvider;
        var oldPaused = CoreSession.IsSessionPausedProvider;
        Func<bool> running = () => true;
        Func<bool> paused = () => false;
        try
        {
            CoreSession.IsSessionRunningProvider = running;
            CoreSession.IsSessionPausedProvider = paused;
            var cases = new RemoteControlShellTests();
            switch (test)
            {
                case 0: cases.Start_session_refuses_a_local_panic_without_replacing_the_paused_run(); break;
                case 1: await cases.Remote_start_reports_panic_and_keeps_the_paused_session(); break;
                case 2: await cases.Remote_start_uses_sessions_or_wearer_settings("known"); break;
                case 3: await cases.Queued_remote_start_does_not_replace_a_run_after_leave_or_end(true); break;
                case 4: await cases.Remote_end_stops_only_remote_owned_runs(true, false, false); break;
            }
            Assert.Same(running, CoreSession.IsSessionRunningProvider);
            Assert.Same(paused, CoreSession.IsSessionPausedProvider);
        }
        finally
        {
            CoreSession.IsSessionRunningProvider = oldRunning;
            CoreSession.IsSessionPausedProvider = oldPaused;
        }
    });
}

using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests
{
    /// <summary>WPF App.xaml.cs:1819-1899: a second launch asks the primary to show, waits for the ack, exits.</summary>
    public class SingleInstanceTests
    {
        [Fact]
        public void SecondLaunchRaisesThePrimaryAndExits()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            int shown = 0;
            using var primary = SingleInstance.Claim(suffix, _ => { Interlocked.Increment(ref shown); return Task.CompletedTask; });
            Assert.NotNull(primary);
            primary!.EndStartupPhase();

            // A mutex is thread-owned, so the second launch claims from another thread, as another process would.
            SingleInstance? second = null;
            var t = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));

            Assert.Null(second);
            Assert.Equal(1, Volatile.Read(ref shown));
        }

        /// <summary>WPF App.xaml.cs:2138-2154: a primary that takes the request but never acks is wedged; the
        /// second launch kills it (KillStaleInstances) and runs. A healthy primary is never killed.</summary>
        [Fact]
        public void AWedgedPrimaryIsKilledAndAHealthyOneIsNot()
        {
            var savedKill = SingleInstance.KillStale;
            var savedAck = SingleInstance.ShowAckTimeoutMs;
            var savedWait = SingleInstance.StaleTakeoverWait;
            int kills = 0;
            SingleInstance.KillStale = () => { Interlocked.Increment(ref kills); return 1; };
            SingleInstance.ShowAckTimeoutMs = 500;
            SingleInstance.StaleTakeoverWait = TimeSpan.FromMilliseconds(100);
            using var wedge = new ManualResetEventSlim();
            try
            {
                // Healthy: acked, no kill.
                var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
                using (var healthy = SingleInstance.Claim(suffix, _ => Task.CompletedTask))
                {
                    healthy!.EndStartupPhase();
                    var h = new Thread(() => SingleInstance.Claim(suffix, _ => Task.CompletedTask));
                    h.Start();
                    Assert.True(h.Join(TimeSpan.FromSeconds(20)));
                }
                Assert.Equal(0, Volatile.Read(ref kills));

                // Wedged: the show never returns, so no ack; the second launch kills and runs (unowned).
                suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
                using var primary = SingleInstance.Claim(suffix, _ => Task.Run(() => wedge.Wait()));
                primary!.EndStartupPhase();
                SingleInstance? second = null;
                var t = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask));
                t.Start();
                Assert.True(t.Join(TimeSpan.FromSeconds(20)));
                Assert.Equal(1, Volatile.Read(ref kills));
                Assert.NotNull(second);
                second!.Dispose();

                // A throwing kill still lets the launch run (WPF falls through on any takeover failure).
                // A fresh wedged primary: the first one's listener is still stuck in its show.
                SingleInstance.KillStale = () => { Interlocked.Increment(ref kills); throw new InvalidOperationException("denied"); };
                suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
                using var primary2 = SingleInstance.Claim(suffix, _ => Task.Run(() => wedge.Wait()));
                primary2!.EndStartupPhase();
                t = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask));
                t.Start();
                Assert.True(t.Join(TimeSpan.FromSeconds(20)));
                Assert.Equal(2, Volatile.Read(ref kills));
                Assert.NotNull(second);
                second!.Dispose();


            }
            finally
            {
                wedge.Set();
                SingleInstance.KillStale = savedKill;
                SingleInstance.ShowAckTimeoutMs = savedAck;
                SingleInstance.StaleTakeoverWait = savedWait;
            }
        }

        /// <summary>WPF App.xaml.cs:2177-2185: a cold start acks before its UI can show the request.</summary>
        [Fact]
        public void ColdStartupAcksBeforeShowingWithoutKillingThePrimary()
        {
            var savedKill = SingleInstance.KillStale;
            var savedAck = SingleInstance.ShowAckTimeoutMs;
            var savedWait = SingleInstance.StaleTakeoverWait;
            var show = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int kills = 0;
            try
            {
                SingleInstance.KillStale = () => { Interlocked.Increment(ref kills); return 0; };
                SingleInstance.ShowAckTimeoutMs = 500;
                SingleInstance.StaleTakeoverWait = TimeSpan.FromMilliseconds(100);
                var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
                using var primary = SingleInstance.Claim(suffix, _ => show.Task);
                SingleInstance? second = null;
                var launch = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask)) { IsBackground = true };
                launch.Start();
                Assert.True(launch.Join(TimeSpan.FromSeconds(10)));
                Assert.Null(second);
                Assert.Equal(0, Volatile.Read(ref kills));
                Assert.False(show.Task.IsCompleted);
            }
            finally
            {
                show.TrySetResult();
                SingleInstance.KillStale = savedKill;
                SingleInstance.ShowAckTimeoutMs = savedAck;
                SingleInstance.StaleTakeoverWait = savedWait;
            }
        }

        /// <summary>WPF App.xaml.cs:2147-2154: after termination, the new primary owns the mutex and answers relaunches.</summary>
        [Fact]
        public void TakeoverAcquiresTheReleasedMutexAndAnswersRelaunches()
        {
            var savedKill = SingleInstance.KillStale;
            var savedAck = SingleInstance.ShowAckTimeoutMs;
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            using var ready = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var show = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var owner = new Thread(() =>
            {
                using var primary = SingleInstance.Claim(suffix, _ => show.Task);
                primary!.EndStartupPhase();
                ready.Set();
                release.Wait(); // Dispose releases the mutex on its owning thread.
            }) { IsBackground = true };
            try
            {
                SingleInstance.ShowAckTimeoutMs = 500;
                SingleInstance.KillStale = () => { show.TrySetResult(); release.Set(); return 1; };
                owner.Start();
                Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
                using var takeover = SingleInstance.Claim(suffix, _ => Task.CompletedTask);
                Assert.NotNull(takeover);
                takeover!.EndStartupPhase();
                SingleInstance? next = null;
                var launch = new Thread(() => next = SingleInstance.Claim(suffix, _ => Task.CompletedTask)) { IsBackground = true };
                launch.Start();
                Assert.True(launch.Join(TimeSpan.FromSeconds(10)));
                Assert.Null(next); // Fails if takeover runs without owning/listening on the mutex.
            }
            finally
            {
                release.Set();
                show.TrySetResult();
                owner.Join(TimeSpan.FromSeconds(10));
                SingleInstance.KillStale = savedKill;
                SingleInstance.ShowAckTimeoutMs = savedAck;
            }
        }

        /// <summary>The kill is per profile like the mutex: a sandbox (kc, tests) never kills the user's app or
        /// another sandbox running the same binary, and an unreadable profile is never a target.</summary>
        [Fact]
        public void TheStaleKillNeverCrossesAProfile()
        {
            static byte[] Env(params string[] vars) => System.Text.Encoding.UTF8.GetBytes(string.Join('\0', vars) + "\0");
            Assert.True(SingleInstance.SameProfile(null, Env("HOME=/home/u"), true));                         // real app, real app
            Assert.True(SingleInstance.SameProfile("/s/a", Env("HOME=/h", "CCP_USERDATA_DIR=/s/a"), true));   // same sandbox
            Assert.False(SingleInstance.SameProfile("/s/a", Env("HOME=/home/u"), true));                      // sandbox -> real app
            Assert.False(SingleInstance.SameProfile(null, Env("CCP_USERDATA_DIR=/s/a"), true));               // real app -> sandbox
            Assert.False(SingleInstance.SameProfile("/s/a", Env("CCP_USERDATA_DIR=/s/b"), true));             // other sandbox
            Assert.False(SingleInstance.SameProfile("/s/a", Env("XCCP_USERDATA_DIR=/s/a"), true));            // not the variable
            Assert.False(SingleInstance.SameProfile(null, null, true));                                       // unreadable: skip
            Assert.False(SingleInstance.SameProfile("/s/a", null, false));                                    // no environ, sandboxed: never
            Assert.False(SingleInstance.SameProfile(null, null, false));                                       // cannot prove the target profile: skip
        }

        /// <summary>WPF's kill fails closed (App.xaml.cs:4141-4155): only our exact, readable executable path is a
        /// target, and a host shared by unrelated processes (dotnet, testhost) never kills anything.</summary>
        [Fact]
        public void TheStaleKillOnlyTargetsThisExactBuild()
        {
            Assert.True(SingleInstance.IsAppExecutable("/opt/ccp/CCP.Avalonia"));
            Assert.True(SingleInstance.IsAppExecutable("/opt/ccp/CCP.Avalonia.exe"));
            Assert.False(SingleInstance.IsAppExecutable("/usr/share/dotnet/dotnet"));
            Assert.False(SingleInstance.IsAppExecutable("/x/testhost"));
            Assert.False(SingleInstance.IsAppExecutable(null));
            Assert.False(SingleInstance.IsAppExecutable(Environment.ProcessPath));   // this testhost

            Assert.True(SingleInstance.IsSameBuild("/opt/ccp/CCP.Avalonia", "/opt/ccp/./CCP.Avalonia"));
            Assert.False(SingleInstance.IsSameBuild("/opt/ccp/CCP.Avalonia", "/home/u/wt2/CCP.Avalonia"));
            Assert.False(SingleInstance.IsSameBuild("/opt/ccp/CCP.Avalonia", null));      // unreadable: skip
            Assert.False(SingleInstance.IsSameBuild(null, null));
            if (!OperatingSystem.IsWindows())
                Assert.False(SingleInstance.IsSameBuild("/opt/ccp/CCP.Avalonia", "/opt/CCP/CCP.Avalonia"));
        }

        [Fact]
        public void AFailedShowStillAcksSoTheSecondLaunchExits()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            using var primary = SingleInstance.Claim(suffix, _ => throw new InvalidOperationException("show failed"));
            primary!.EndStartupPhase();
            SingleInstance? second = null;
            var t = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));
            Assert.Null(second);
        }
    
        [Fact]
        public void SecondLaunchHandsItsSurfaceToThePrimary()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            string? got = "unset";
            using var primary = SingleInstance.Claim(suffix, p => { got = p; return Task.CompletedTask; });
            primary!.EndStartupPhase();
            var t = new Thread(() => SingleInstance.Claim(suffix, _ => Task.CompletedTask, "game:race"));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));
            Assert.Equal("game:race", got);

            t = new Thread(() => SingleInstance.Claim(suffix, _ => Task.CompletedTask));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));
            Assert.Null(got);                                   // a bare relaunch
        }

        /// <summary>A sandbox profile (CCP_USERDATA_DIR, as kc and the tests use) never wakes or blocks the
        /// user's real instance: its mutex/pipe names carry a per-directory suffix; a shipped build has none.</summary>
        [Fact]
        public void ASandboxProfileIsItsOwnInstance()
        {
            var saved = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
            try
            {
                Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", null);
                Assert.Equal("", SingleInstance.SandboxSuffix());   // WPF's exact names
                Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", "/tmp/ccp-sandbox-a-" + Guid.NewGuid().ToString("N"));
                var a = SingleInstance.SandboxSuffix();
                Assert.Equal(a, SingleInstance.SandboxSuffix());     // stable for one directory
                Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", "/tmp/ccp-sandbox-b-" + Guid.NewGuid().ToString("N"));
                var b = SingleInstance.SandboxSuffix();
                Assert.NotEqual("", a);
                Assert.NotEqual(a, b);

                // Two profiles both own their instance: neither launch asks the other to show and exits.
                int shown = 0;
                using var first = SingleInstance.Claim(a, _ => { Interlocked.Increment(ref shown); return Task.CompletedTask; });
                SingleInstance? second = null;
                var t = new Thread(() => second = SingleInstance.Claim(b, _ => Task.CompletedTask));
                t.Start();
                Assert.True(t.Join(TimeSpan.FromSeconds(20)));
                Assert.NotNull(first);
                Assert.NotNull(second);
                Assert.Equal(0, Volatile.Read(ref shown));
                second!.Dispose();
            }
            finally { Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", saved); }
        }

        /// <summary>Review R4: on Unix a plain mutex name is per Unix session, so an autostart launch and a
        /// menu launch ran as two instances on one profile. Unix claims one mutex per user for every session;
        /// Windows keeps WPF's plain name for the installer's AppMutex.</summary>
        [Fact]
        public void UnixClaimsOneInstancePerUserAcrossSessions()
        {
            var options = SingleInstance.UnixMutexOptions();
            if (OperatingSystem.IsWindows())
            {
                Assert.Null(options);
                return;
            }
            Assert.NotNull(options);
            Assert.False(options!.Value.CurrentSessionOnly);
            Assert.True(options.Value.CurrentUserOnly);
        }
    }
}

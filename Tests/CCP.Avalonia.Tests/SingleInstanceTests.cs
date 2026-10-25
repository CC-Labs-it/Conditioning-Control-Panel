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

        /// <summary>WPF App.xaml.cs:2094-2104: a primary that takes the request but never acks is wedged; the
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

                // Cold start (WPF 2133-2142): a primary whose UI thread is not pumping yet still acks, so the
                // relaunch exits and nothing is killed; the show runs once the dispatcher pumps.
                suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
                int coldShown = 0;
                using var starting = SingleInstance.Claim(suffix, _ => Task.Run(() => { wedge.Wait(); Interlocked.Increment(ref coldShown); }));
                second = null;
                t = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask));
                t.Start();
                Assert.True(t.Join(TimeSpan.FromSeconds(20)));
                Assert.Null(second);
                Assert.Equal(2, Volatile.Read(ref kills));
                Assert.Equal(0, Volatile.Read(ref coldShown));
            }
            finally
            {
                wedge.Set();
                SingleInstance.KillStale = savedKill;
                SingleInstance.ShowAckTimeoutMs = savedAck;
                SingleInstance.StaleTakeoverWait = savedWait;
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
            Assert.True(SingleInstance.SameProfile(null, null, false));                                       // no environ, real app: WPF
        }

        /// <summary>WPF's kill fails closed (App.xaml.cs:4041-4057): only our exact, readable executable path is a
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
    }
}

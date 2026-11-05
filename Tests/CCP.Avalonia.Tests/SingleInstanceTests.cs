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

            // A mutex is thread-owned, so the second launch claims from another thread, as another process would.
            SingleInstance? second = null;
            var t = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));

            Assert.Null(second);
            Assert.Equal(1, Volatile.Read(ref shown));
        }

        [Fact]
        public void AFailedShowStillAcksSoTheSecondLaunchExits()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            using var primary = SingleInstance.Claim(suffix, _ => throw new InvalidOperationException("show failed"));
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
            var t = new Thread(() => SingleInstance.Claim(suffix, _ => Task.CompletedTask, "game:race"));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));
            Assert.Equal("game:race", got);

            t = new Thread(() => SingleInstance.Claim(suffix, _ => Task.CompletedTask));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));
            Assert.Null(got);                                   // a bare relaunch
        }

        [Theory]
        [InlineData(242)]
        [InlineData(243)]
        [InlineData(1024)]
        public void MediaPayloadsSurviveThePipeLengthBoundary(int length)
        {
            var payload = "deeper:edit\n" + new string('x', length);
            var suffix = "_test_" + Guid.NewGuid().ToString("N");
            string? got = null;
            using var primary = SingleInstance.Claim(suffix, p => { got = p; return Task.CompletedTask; });
            var t = new Thread(() => SingleInstance.Claim(suffix, _ => Task.CompletedTask, payload));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));
            Assert.Equal(payload, got);
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

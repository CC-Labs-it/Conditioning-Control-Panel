using System;
using System.IO;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO.Pipes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests
{
    public class FileOpenRequestTests
    {
        [Theory]
        [InlineData("mp4")][InlineData("webm")][InlineData("mkv")][InlineData("mov")]
        [InlineData("avi")][InlineData("m4v")][InlineData("mp3")][InlineData("wav")]
        [InlineData("m4a")][InlineData("aac")][InlineData("flac")][InlineData("ogg")]
        public void AcceptsExistingMediaAndNormalizes(string extension)
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var file = Path.Combine(dir, "音楽." + extension.ToUpperInvariant());
                File.WriteAllText(file, "");
                Assert.Equal(("play", file), FileOpenRequest.Parse(new[] { "--play", Path.Combine(dir, ".", Path.GetFileName(file)) }));
                Assert.Equal(("edit", file), FileOpenRequest.Parse(new[] { "--edit", file }));
                File.Delete(file);
                Assert.Null(FileOpenRequest.Validate(file));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void RejectsInvalidPathsAndExtensions()
        {
            var file = Path.GetTempFileName();
            try
            {
                Assert.Null(FileOpenRequest.Validate(file));
                Assert.Null(FileOpenRequest.Validate("\\\\server\\share\\song.mp3"));
                Assert.Null(FileOpenRequest.Validate("bad\0.mp3"));
                Assert.Null(FileOpenRequest.Validate(" "));
                Assert.Equal((null, null), FileOpenRequest.Parse(new[] { "--play", file, "--edit", "other.mp3" }));
            }
            finally { File.Delete(file); }
        }
        [Fact]
        public void RejectsUncEvenWhenThatNameExistsLocallyOnLinux()
        {
            if (OperatingSystem.IsWindows()) return;
            var previous = Environment.CurrentDirectory;
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Environment.CurrentDirectory = dir;
                const string unc = @"\\server\share\clip.mp3";
                File.WriteAllText(unc, "");
                Assert.True(File.Exists(unc));
                Assert.Null(FileOpenRequest.Validate(unc));
            }
            finally
            {
                Environment.CurrentDirectory = previous;
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ExistingLongUnicodeFileSurvivesEncodingAndReceipt()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var dir = root;
            try
            {
                for (int i = 0; i < 12; i++) dir = Path.Combine(dir, new string('音', 20));
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "曲.mp3");
                File.WriteAllText(path, "");
                Assert.True(Encoding.UTF8.GetByteCount(path) > 255);
                var payload = FileOpenRequest.Encode(new[] { "--play", path });
                Assert.True(FileOpenRequest.Decode(payload, out var action, out var received));
                Assert.Equal("play", action);
                Assert.Equal(path, received);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void ReceiptValidatesAndNeverRoutesMalformedFileAsLauncher()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
            File.WriteAllText(path, "");
            try
            {
                var payload = FileOpenRequest.Encode(new[] { "--edit", path });
                Assert.True(FileOpenRequest.Decode(payload, out var action, out var received));
                Assert.Equal("edit", action);
                Assert.Equal(path, received);
                File.Delete(path);
                Assert.True(FileOpenRequest.Decode(payload, out action, out received));
                Assert.Null(action);
                Assert.Null(received);
                Assert.True(FileOpenRequest.Decode("file:bad:/tmp/test.mp3", out action, out _));
                Assert.Null(action);
                Assert.False(FileOpenRequest.Decode("game:race", out _, out _));
                Assert.False(FileOpenRequest.Decode(null, out _, out _));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void StartupQueuePreservesBareLauncherAndFileRequests()
        {
            var queue = new FileOpenRequest();
            var received = new List<string?>();
            queue.Enqueue(null);
            queue.Enqueue("game:race");
            queue.Enqueue("file:play:/example.mp3");
            queue.Ready(p => { received.Add(p); return Task.CompletedTask; });
            queue.Enqueue("panel");
            Assert.Equal(new string?[] { null, "game:race", "file:play:/example.mp3", "panel" }, received);
        }

        [Fact]
        public async Task AReadyQueueWaitsForDeliveryWhileStartupQueueAcksImmediately()
        {
            var queue = new FileOpenRequest();
            Assert.True(queue.Enqueue("panel").IsCompleted);
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Ready(_ => delivered.Task);
            var waiting = queue.Enqueue("game:race");
            Assert.False(waiting.IsCompleted);
            delivered.SetResult();
            await waiting;
        }

        [Fact]
        public async Task FramesRejectOversizedAndTruncatedMessages()
        {
            var header = new byte[5];
            header[0] = 255;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), SingleInstance.MaxPayloadBytes + 1);
            await Assert.ThrowsAsync<InvalidDataException>(() => SingleInstance.ReadPayloadAsync(new MemoryStream(header), CancellationToken.None));
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), 255);
            await Assert.ThrowsAsync<EndOfStreamException>(() => SingleInstance.ReadPayloadAsync(new MemoryStream(header), CancellationToken.None));
        }

        [Fact]
        public void LongUnicodeAndConcurrentRequestsReachThePrimaryIntact()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            var received = new List<string?>();
            using var primary = SingleInstance.Claim(suffix, p => { lock (received) received.Add(p); return Task.CompletedTask; });
            var longPath = "file:play:/" + string.Concat(Enumerable.Repeat("音楽/", 120)) + "clip.mp4";
            Assert.True(Encoding.UTF8.GetByteCount(longPath) > 255);
            var payloads = new string?[] { longPath, "game:race", null, "panel", "file:edit:/test.wav" };
            var seconds = new SingleInstance?[payloads.Length];
            var threads = payloads.Select((payload, i) => new Thread(() =>
                seconds[i] = SingleInstance.Claim(suffix, _ => Task.CompletedTask, payload))).ToArray();
            foreach (var thread in threads) thread.Start();
            foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            Assert.All(seconds, Assert.Null);
            lock (received)
            {
                Assert.Equal(payloads.Length, received.Count);
                foreach (var payload in payloads) Assert.Contains(payload, received);
            }
        }
    
        // An older primary listens only on the original pipe and routes any payload as a surface.
        [Fact]
        public void AnOlderPrimaryGetsTheSurfaceFallbackInItsOneByteFrame()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            using var mutex = SingleInstance.CreateMutex(SingleInstance.MutexName + suffix, out var owned);
            Assert.True(owned);
            try
            {
                using var old = new NamedPipeServerStream(SingleInstance.PipeName(suffix), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                SingleInstance? second = null;
                var t = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask, "file:play:/clip.mp3", "game:race"));
                t.Start();
                Assert.True(old.WaitForConnectionAsync().Wait(TimeSpan.FromSeconds(15)));
                var body = new byte[old.ReadByte()];
                old.ReadExactly(body);
                old.WriteByte(1); old.Flush();
                Assert.True(t.Join(TimeSpan.FromSeconds(15)));
                Assert.Null(second);
                Assert.Equal("game:race", Encoding.UTF8.GetString(body));
            }
            finally { mutex.ReleaseMutex(); }
        }

        [Fact]
        public void AnOlderSendersFullOneBytePayloadReachesTheOriginalPipeIntact()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            string? got = null;
            using var primary = SingleInstance.Claim(suffix, p => { got = p; return Task.CompletedTask; });
            using var c = new NamedPipeClientStream(".", SingleInstance.PipeName(suffix), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            c.Connect(5000);
            var sent = "game:" + new string('r', 250);
            c.WriteByte(255);
            c.Write(Encoding.UTF8.GetBytes(sent));
            c.Flush();
            Assert.Equal(1, c.ReadByte());
            Assert.Equal(sent, got);
        }

        [Fact]
        public Task StartupLeavesTheHiddenPanelAloneASecondLaunchRestoresItAndFailuresAreShown() => AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
            File.WriteAllText(file, "");
            var avatar = CoreSettings.Current.AvatarEnabled;
            var shell = new MainShellWindow();
            try
            {
                CoreSettings.Current.AvatarEnabled = false;
                shell.Show();
                shell.Hide();                                    // start minimized to the tray
                var editor = shell.OpenMediaRequest("edit", file, restore: false);
                Assert.IsType<DeeperEditorWindow>(editor);
                Assert.False(shell.IsVisible);
                editor!.Close();

                var player = shell.OpenMediaRequest("play", file, restore: true);
                Assert.IsType<EnhancementPlayerWindow>(player);
                Assert.True(shell.IsVisible);
                player!.Close();

                MainShellWindow.MediaRequestFault = _ => throw new InvalidOperationException("boom");
                Assert.Null(shell.OpenMediaRequest("edit", file, restore: true));
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal("Open Editor failed", dialog.Title);
                Assert.Equal("Couldn't open Deeper Editor:\n\nInvalidOperationException: boom", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            }
            finally
            {
                MainShellWindow.MediaRequestFault = null;
                CoreSettings.Current.AvatarEnabled = avatar;
                foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
                shell.Close();
                File.Delete(file);
            }
            return Task.CompletedTask;
        });
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests
{
    /// <summary>Second launches that arrive before the shell opens are routed once it does, in arrival order.</summary>
    public class HandoffQueueTests
    {
        [Fact]
        public void RequestsBeforeTheShellOpensAreRoutedInOrderOnceItDoes()
        {
            var queue = new HandoffQueue();
            var received = new List<string?>();
            Assert.True(queue.Enqueue(null).IsCompleted);
            queue.Enqueue("game:race");
            queue.Enqueue("deeper:play\n/clip.mp4");
            Assert.Empty(received);
            queue.Ready(p => { received.Add(p); return Task.CompletedTask; });
            queue.Enqueue("panel");
            Assert.Equal(new string?[] { null, "game:race", "deeper:play\n/clip.mp4", "panel" }, received);
        }

        [Fact]
        public async Task AfterTheShellOpensTheAckWaitsForTheRoute()
        {
            var queue = new HandoffQueue();
            var routed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Ready(_ => routed.Task);
            var waiting = queue.Enqueue("game:race");
            Assert.False(waiting.IsCompleted);
            routed.SetResult();
            await waiting;
        }
    }
}

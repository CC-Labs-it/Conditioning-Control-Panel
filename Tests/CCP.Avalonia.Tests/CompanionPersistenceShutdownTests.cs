using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionPersistenceShutdownTests
{
    private sealed class CountingStore : ICompanionSessionStore
    {
        public int Saves;
        public CompanionSessionSnapshot Load() => CompanionSessionSnapshot.Empty;
        public void Save(IReadOnlyList<CompanionTurn> turns) => Saves++;
        public void Wipe() { }
    }

    [Fact]
    public async Task QueuedPersistenceCannotWriteAfterBrainDisposal()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-shutdown-").FullName;
        var memory = new MemoryStore(Path.Combine(dir, "memory.json"));
        var store = new CountingStore();
        var transport = new AiServiceStrategy();
        var brain = new CompanionBrain(transport, memory: memory, store: store);
        try
        {
            var gate = typeof(CompanionBrain).GetField("_conversationMutation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(brain)!;
            Task queued;
            lock (gate)
            {
                queued = brain.PersistAsync(); // the worker cannot enter until Dispose has flushed
                brain.Dispose();
                Assert.Equal(1, store.Saves);
            }
            await queued;
            Assert.Equal(1, store.Saves); // no write after the final flush
        }
        finally
        {
            brain.Dispose();
            memory.Dispose();
            transport.Dispose();
            Directory.Delete(dir, true);
        }
    }
}

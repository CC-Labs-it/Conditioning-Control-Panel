using System;
using System.IO;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace CCP.Core.Tests;

public sealed class MemoryInjectionBudgetTests
{
    [Fact]
    public void DefaultAccountingPreservesWpfFactSelection()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-memory-budget-").FullName;
        try
        {
            using var store = new MemoryStore(Path.Combine(dir, "memory.json"), sessionSeed: 1234);
            store.AddFact("abc", MemoryFactKind.Event, 0.5);
            store.AddFact("def", MemoryFactKind.Event, 0.5);

            var block = store.GetInjectionBlock(3)!;
            Assert.Single(block.Split('\n'));
            Assert.True(block == "- abc" || block == "- def");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void OrdinaryFactsUseTheRenderedBlockLengthIncludingPlatformNewlines()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-memory-budget-").FullName;
        try
        {
            using var store = new MemoryStore(Path.Combine(dir, "memory.json"), sessionSeed: 1234) { UseRenderedInjectionBudget = true };
            store.AddFact("abc", MemoryFactKind.Event, 0.5);
            store.AddFact("def", MemoryFactKind.Event, 0.5);
            var block = store.GetInjectionBlock(3)!;

            Assert.Contains("- abc", block);
            Assert.Contains("- def", block);
            Assert.True(ChatSession.ApproxTokens(block) <= 3);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ManyOrdinaryFactsNeverExceedTheBudget()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-memory-budget-").FullName;
        try
        {
            using var store = new MemoryStore(Path.Combine(dir, "memory.json"), sessionSeed: 1234) { UseRenderedInjectionBudget = true };
            for (int i = 0; i < 200; i++)
                store.AddFact($"fact number {i} with some padding text", MemoryFactKind.Event, 0.5);
            var block = store.GetInjectionBlock(120)!;

            Assert.True(ChatSession.ApproxTokens(block) <= 120);
            Assert.True(block.Split('\n').Length > 1);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

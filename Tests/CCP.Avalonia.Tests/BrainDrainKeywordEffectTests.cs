using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Controls;
using IconKind = FluentIcons.Common.Symbol;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>main-sync 0fc2d4faa (ccp-bugs #1214): Brain Drain is a keyword visual effect in the
/// Awareness preset editor, with its own chip like WPF AwarenessPresetDetailDialog's 🫠 (a Fluent icon since fluent-icons 2026-10-10).</summary>
public sealed class BrainDrainKeywordEffectTests
{
    [Fact]
    public void BrainDrainEffectHasItsOwnChip()
    {
        var trigger = new KeywordTrigger();
        trigger.Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
        Assert.Equal(new[] { IconKind.EmojiMeh }, AwarenessPresetDetailDialog.BuildActionChips(trigger));
        // "Its own": no other visual effect shares the Brain Drain icon.
        var others = Enum.GetValues<KeywordVisualEffect>().Where(e => e != KeywordVisualEffect.BrainDrain)
            .Select(AwarenessPresetDetailDialog.IconForVisualEffect);
        Assert.DoesNotContain(IconKind.EmojiMeh, others);
    }
}

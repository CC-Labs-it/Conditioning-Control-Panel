namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>shortcut: stand-in until trunk's KeywordTriggerHead + Core KeywordTriggerEngine are imported.
    /// The tour's keyword demo step calls Engine.FireDemo; here it does nothing.</summary>
    internal static class KeywordTriggerHead
    {
        internal static Stub Engine { get; } = new();

        internal sealed class Stub
        {
            internal void FireDemo(string keyword, string source) { }
        }
    }
}

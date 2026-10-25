namespace ConditioningControlPanel.Services;

/// <summary>Exact root refusal shared by heads that expand one selected file into a media pool.
/// One rule: forwards to <see cref="AssetFolderSafety"/> (WPF SecurityHelper #1053).</summary>
public static class PersonalFolderRoots
{
    public static bool IsPersonalFolderRoot(string? directory)
        => AssetFolderSafety.IsPersonalFolderRoot(directory);
}

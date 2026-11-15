using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private AvatarPortraitLoader? _portrait;
    private Bitmap? _portraitImage;
    internal bool IsPortraitMode => _portraitImage != null;
    internal bool IsPoseRotating => _poseTimer.IsEnabled;

    // WPF Avatar.cs:1018-1119: enter on mod change, hold idle still, restore legacy on exit.
    private void LoadPortrait()
    {
        _portrait = SingleEmoteSet() == null ? AvatarPortraitLoader.Load() : null;
    }

    private bool PaintPortrait()
    {
        if (_portrait == null || ResolveV1EmoteFolder() != null) return false;
        var image = _portrait.Idle(_currentAvatarSet - 1);
        if (image == null) return false;
        StopRainPlayback();
        _poseTimer.Stop();
        var old = _portraitImage;
        _portraitImage = image;
        _imgAvatar.Source = image;
        old?.Dispose();
        _imgAvatar.MaxHeight = 306 * 0.88;
        _imgAvatar.MaxWidth = 198 * 0.88;
        _avatarBorder.RenderTransform = new TranslateTransform(10, -30);
        var skin = _portrait.Manifest.Skins[System.Math.Clamp(_currentAvatarSet - 1, 0, _portrait.Manifest.Skins.Count - 1)];
        _txtAvatarTitle.Text = string.IsNullOrWhiteSpace(skin.Title) ? skin.Id : skin.Title;
        _txtAvatarLevel.IsVisible = false;
        return true;
    }

    private void ClearPortraitImage()
    {
        _portraitImage?.Dispose();
        _portraitImage = null;
        _imgAvatar.MaxHeight = 306;
        _imgAvatar.MaxWidth = 198;
    }
}

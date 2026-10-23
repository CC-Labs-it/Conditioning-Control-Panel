// Adapted from WPF AvatarTubeWindow.Avatar.cs:395,819 and CirceEmotes.cs:203,235.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private CancellationTokenSource? _avatarSwitchCancellation;
    private bool _avatarSwitchClosed;

    /// <summary>WPF's roster path: preserve a sole emote avatar; otherwise persist her set and
    /// fade out/in for 200 ms each. All sets are unlocked in WPF, even at player level one.</summary>
    public async Task SwitchToCompanionAvatarAsync(CompanionId id)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => SwitchToCompanionAvatarAsync(id));
            return;
        }
        if (_avatarSwitchClosed || IsSingleEmoteAvatarMod()) return;

        var cancellation = new CancellationTokenSource();
        // Swap the field before Cancel: a canceled fade can resume inline inside Cancel().
        var superseded = _avatarSwitchCancellation;
        _avatarSwitchCancellation = cancellation;
        superseded?.Cancel();
        _currentAvatarSet = id switch
        {
            CompanionId.OGBambiSprite => 3,
            CompanionId.CultBunny => 4,
            CompanionId.BrainParasite => 5,
            CompanionId.BambiTrainer => 6,
            CompanionId.BimboCow => 7,
            _ => 1,
        };
        var settings = CoreSettings.Current;
        settings.SelectedAvatarSet = _currentAvatarSet;
        ModAvatarLooks.Store(settings.ModAvatarSet, CoreMods.ActiveModId, _currentAvatarSet);
        CoreSettings.Save();
        _poseTimer.Stop();
        var swapped = false;
        try
        {
            await FadeAvatar(1, 0, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested(); // rapid clicks keep only the latest paint
            // Close detaches the border, which ends the fade without a cancel; never repaint then.
            if (_avatarSwitchClosed || !IsVisible) return;
            _avatarBorder.Opacity = 0;
            // ponytail: GIF decoding and emotive-portrait skins remain unported. Reuse this head's
            // static pose loader/fallback; do not add a second renderer in the roster.
            swapped = true;
            ApplyAvatarSet();
            _avatarBorder.Opacity = 1;
            await FadeAvatar(0, 1, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Panic cut the fade-out (not a newer click, not close): the saved set is already the
            // new one, so jump straight to it rather than leave old art and a stopped rotation.
            if (!swapped && !_avatarSwitchClosed && _avatarSwitchCancellation is null)
                ApplyAvatarSet();
        }
        catch (Exception ex) { Log.Warning(ex, "Roster: avatar switch failed"); }
        finally
        {
            if (ReferenceEquals(_avatarSwitchCancellation, cancellation))
            {
                _avatarSwitchCancellation = null;
                _avatarBorder.Opacity = 1;
            }
            cancellation.Dispose();
        }
    }

    private Task FadeAvatar(double from, double to, CancellationToken cancellation) => new Animation
    {
        Duration = TimeSpan.FromMilliseconds(200),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, to) } },
        },
    }.RunAsync(_avatarBorder, cancellation);

    // Reached by the existing tube-speech panic registration and by close. A canceled fade must
    // not repaint a closed/hidden tube or leave it transparent when the user next shows it.
    private void CancelAvatarSwitch()
    {
        var pending = _avatarSwitchCancellation;
        _avatarSwitchCancellation = null;
        pending?.Cancel();
        _avatarBorder.Opacity = 1;
    }

    /// <summary>WPF's registry union mod-local set folders, with the declared-other-set exception.
    /// The pin protects the selected look even while animated rendering itself remains unported.</summary>
    internal static bool IsSingleEmoteAvatarMod()
    {
        var package = CoreMods.ActiveModPackage;
        var sets = new HashSet<int>();
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar_emotes_registry.json"));
            using var reader = new StreamReader(stream);
            foreach (var entry in (JArray)JObject.Parse(reader.ReadToEnd())["sets"]!)
                if (string.Equals((string?)entry["modId"], CoreMods.ActiveModId, StringComparison.OrdinalIgnoreCase))
                    sets.Add((int)entry["avatarSet"]!);
        }
        catch (Exception ex) { Log.Debug(ex, "Roster: emote registry unavailable"); }
        try
        {
            if (!string.IsNullOrEmpty(package?.InstalledPath))
            {
                var root = Path.Combine(package.InstalledPath, "resources", "emotes");
                if (Directory.Exists(root))
                    foreach (var directory in Directory.EnumerateDirectories(root, "set*"))
                        if (int.TryParse(Path.GetFileName(directory).AsSpan(3), out int set) && set > 0 &&
                            File.Exists(Path.Combine(directory, "emotes.json"))) sets.Add(set);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "Roster: mod-local emote scan failed"); }
        if (sets.Count != 1) return false;
        var only = sets.Single();
        var manifest = package?.Manifest;
        return !(manifest?.SupportedAvatarSets?.Any(s => s != only) ?? false) &&
               !(manifest?.CustomAvatarSets?.Any(s => s.SetNumber != only) ?? false);
    }
}

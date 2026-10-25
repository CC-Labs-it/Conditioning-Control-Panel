// Adapted from parity/trunk a4ee52d40cd347d288dcf7fdaa8445b79f8e5038, AvatarSets.cs;
// WPF AvatarTubeWindow.Avatar.cs:214,395,850-995 and CirceEmotes.cs:193-256.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private int _avatarSwitchGeneration;
    private bool _avatarSetsClosed;
    private Action _cancelAvatarSwitch = static () => { };

    // Timer seam, like SpeechDelayScheduler: tests advance the fade boundary without sleeping.
    internal Func<TimeSpan, Action, Action> AvatarSwitchScheduler = static (delay, fire) =>
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); fire(); };
        timer.Start();
        return timer.Stop;
    };

    private static bool SupportsAvatarSet(int set)
    {
        var supported = CoreMods.ActiveModPackage?.Manifest.SupportedAvatarSets;
        return supported == null || supported.Count == 0 || supported.Contains(set);
    }

    // 7.1.5 removed level gates. Keep the original unlock ORDER, including custom sets.
    private static int[] SupportedAvatarSets()
    {
        var sets = new[] { 1, 2, 3, 4, 7, 5, 6 }.Where(SupportsAvatarSet);
        var custom = CoreMods.ActiveModPackage?.Manifest.CustomAvatarSets;
        if (custom != null)
            sets = sets.Concat(custom.OrderBy(c => c.UnlockLevel).Select(c => c.SetNumber).Where(SupportsAvatarSet));
        return sets.ToArray();
    }

    // Parsed once and never fatal, like WPF CirceEmotes.cs:279-309.
    private static (string Mod, int Set)[]? _registeredEmoteSets;
    private static IEnumerable<(string Mod, int Set)> RegisteredEmoteSets()
    {
        if (_registeredEmoteSets != null) return _registeredEmoteSets;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar_emotes_registry.json"));
            using var reader = new StreamReader(stream);
            var registry = JObject.Parse(reader.ReadToEnd());
            return _registeredEmoteSets = registry["sets"]!.Select(e => ((string)e["modId"]!, (int)e["avatarSet"]!)).ToArray();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Avatar emote registry unreadable; no emote sets");
            return _registeredEmoteSets = Array.Empty<(string, int)>();
        }
    }

    private static int? SingleEmoteSet()
    {
        var sets = new SortedSet<int>(RegisteredEmoteSets()
            .Where(e => string.Equals(e.Mod, CoreMods.ActiveModId, StringComparison.OrdinalIgnoreCase)).Select(e => e.Set));
        var installed = CoreMods.ActiveModPackage?.InstalledPath;
        try
        {
            if (!string.IsNullOrEmpty(installed))
            {
                var root = Path.Combine(installed, "resources", "emotes");
                if (Directory.Exists(root))
                    foreach (var folder in Directory.EnumerateDirectories(root, "set*"))
                        if (int.TryParse(Path.GetFileName(folder).AsSpan(3), out int n) && n > 0
                            && File.Exists(Path.Combine(folder, "emotes.json"))) sets.Add(n);
            }
        }
        catch (IOException) { } // a removed/unreadable user mod keeps the registry fallback
        catch (UnauthorizedAccessException) { }
        if (sets.Count != 1) return null;
        int only = sets.Single();
        var manifest = CoreMods.ActiveModPackage?.Manifest;
        bool others = (manifest?.SupportedAvatarSets?.Any(n => n != only) ?? false)
            || (manifest?.CustomAvatarSets?.Any(c => c.SetNumber != only) ?? false);
        return others ? null : only;
    }

    internal int[] PickableAvatarSets() => SingleEmoteSet() is int only ? new[] { only } : SupportedAvatarSets();

    private int ResolveStartAvatarSet()
    {
        var s = CoreSettings.Current;
        var sets = PickableAvatarSets();
        int set = ModAvatarLooks.StoredFor(s.ModAvatarSet, CoreMods.ActiveModId, sets) ?? s.SelectedAvatarSet;
        if (CoreMods.ActiveModPackage?.Manifest.CustomAvatarSets?.Any(c => c.SetNumber == set) != true)
            set = Math.Clamp(set, 1, 7);
        return sets.Contains(set) || sets.Length == 0 ? set : sets[^1];
    }

    private void UpdateNavigationArrows()
    {
        var sets = PickableAvatarSets();
        int i = Array.IndexOf(sets, _currentAvatarSet);
        this.FindControl<Border>("BtnPrevAvatar")!.IsVisible = sets.Length > 1 && i > 0;
        this.FindControl<Border>("BtnNextAvatar")!.IsVisible = sets.Length > 1 && i >= 0 && i < sets.Length - 1;
    }

    private void StepAvatarSet(int delta)
    {
        var sets = PickableAvatarSets();
        int i = Array.IndexOf(sets, _currentAvatarSet), next = i + delta;
        if (i >= 0 && next >= 0 && next < sets.Length) SelectAvatarSet(sets[next]);
    }

    internal bool SelectAvatarSet(int setNumber)
    {
        if (_avatarSetsClosed || setNumber == _currentAvatarSet || !PickableAvatarSets().Contains(setNumber)) return false;
        CancelAvatarSwitch();
        _currentAvatarSet = setNumber;
        var s = CoreSettings.Current;
        s.SelectedAvatarSet = setNumber;
        ModAvatarLooks.Store(s.ModAvatarSet, CoreMods.ActiveModId, setNumber);
        CoreSettings.Save();
        // WPF notifies as soon as the user picks; coupling waits until the fade settles.
        BarkHead.Engine?.Raise("AvatarChanged");
        int generation = _avatarSwitchGeneration;
        _avatarBorder.Transitions ??= new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) },
        };
        _avatarBorder.Opacity = 0;
        _cancelAvatarSwitch = AvatarSwitchScheduler(TimeSpan.FromMilliseconds(200), () =>
        {
            if (_avatarSetsClosed || generation != _avatarSwitchGeneration) return;
            _cancelAvatarSwitch = static () => { };
            if (CompanionForAvatarSet(setNumber) is { } companion) CoreModsHooks.SwitchCompanion?.Invoke(companion);
            ApplyAvatarSet();
            UpdateNavigationArrows();
            _avatarBorder.Opacity = 1;
        });
        return true;
    }

    private void CancelAvatarSwitch()
    {
        ++_avatarSwitchGeneration;
        _cancelAvatarSwitch();
        _cancelAvatarSwitch = static () => { };
        _avatarBorder.Opacity = 1;
    }

    private void OnTubeModChanged(object? sender, ModPackage mod) => RunOnAvatar(() =>
    {
        if (_avatarSetsClosed) return;
        CancelAvatarSwitch();
        var s = CoreSettings.Current;
        var sets = PickableAvatarSets();
        var only = SingleEmoteSet();
        int next = only ?? ModAvatarLooks.ForModSwitch(s.ModAvatarSet, CoreMods.ActiveModId,
            s.SelectedAvatarSet, _currentAvatarSet, sets);
        // A single-emote mod pins its own look without replacing the last global pick.
        if (only == null && next != _currentAvatarSet) s.SelectedAvatarSet = next;
        _currentAvatarSet = next;
        ApplyAvatarSet();
        RefreshTubeGlass();
        RefreshTubeLayout();
        UpdateNavigationArrows();
        UpdateQuickMenuState();
        UpdatePosition();
    });
}

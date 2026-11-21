using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a saved preset configuration
    /// </summary>
    public class Preset : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _id = Guid.NewGuid().ToString();
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        private string _name = "New Preset";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        private bool _isDefault = false;
        public bool IsDefault
        {
            get => _isDefault;
            set { _isDefault = value; OnPropertyChanged(); }
        }

        private DateTime _createdAt = DateTime.Now;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        // Flash Settings
        public bool FlashEnabled { get; set; } = true;
        public int FlashFrequency { get; set; } = 2;
        public int SimultaneousImages { get; set; } = 5;
        public int HydraLimit { get; set; } = 20;
        public int ImageScale { get; set; } = 100;
        public int FlashOpacity { get; set; } = 100;
        public int FadeDuration { get; set; } = 40;
        public bool FlashClickable { get; set; } = true;
        public bool CorruptionMode { get; set; } = false;
        public bool HydraLinkedTiming { get; set; } = true;

        // Video Settings
        public bool MandatoryVideosEnabled { get; set; } = false;
        public int VideosPerHour { get; set; } = 6;
        public bool StrictLockEnabled { get; set; } = false;
        public int VideoMinDurationSeconds { get; set; } = 0;
        public int VideoMaxDurationSeconds { get; set; } = 0;
        public bool VideoCutRandomStart { get; set; } = true;
        public bool MandatoryVideoLoopShortClips { get; set; } = false;
        public bool MandatoryVideoFillShortWithOthers { get; set; } = false;

        // Attention Check (Mini-Game) Settings
        public bool AttentionChecksEnabled { get; set; } = false;
        public int AttentionDensity { get; set; } = 3;
        public int AttentionLifespan { get; set; } = 5;
        public int AttentionSize { get; set; } = 70;

        // Subliminal Settings
        public bool SubliminalEnabled { get; set; } = false;
        public int SubliminalFrequency { get; set; } = 5;
        public int SubliminalDuration { get; set; } = 2;
        public int SubliminalOpacity { get; set; } = 80;
        public Dictionary<string, bool>? SubliminalPool { get; set; }
        public Dictionary<string, bool>? AttentionPool { get; set; }
        public Dictionary<string, bool>? LockCardPhrases { get; set; }

        // Audio Settings
        public bool SubAudioEnabled { get; set; } = false;
        public int SubAudioVolume { get; set; } = 50;
        public int MasterVolume { get; set; } = 32;
        public bool AudioDuckingEnabled { get; set; } = true;
        public int DuckingLevel { get; set; } = 100;

        // Overlay Settings (Level 10+)
        public bool SpiralEnabled { get; set; } = false;
        public int SpiralOpacity { get; set; } = 15;
        public bool PinkFilterEnabled { get; set; } = false;
        public int PinkFilterOpacity { get; set; } = 10;

        // Bubbles Settings (Level 20+)
        public bool BubblesEnabled { get; set; } = false;
        public int BubblesFrequency { get; set; } = 5;
        // Nullable for back-compat: presets saved before these fields existed (or shared
        // from older versions) deserialize them as null, and ApplyTo skips null — so
        // applying an old preset can't clobber the live values (e.g. a deliberate
        // BubbleSharedHost=false opt-out for the mixed-DPI hit-test workaround).
        public int? BubblesVolume { get; set; }
        public bool? BubbleSharedHost { get; set; }
        public bool? BubblesLinkRamp { get; set; }
        public bool? BubblesClickable { get; set; }

        // Trigger Bubbles (ambient bubbles that fire a Chaos effect on pop)
        public bool BubbleTriggersEnabled { get; set; } = false;
        public int BubbleTriggerChance { get; set; } = 10;
        public int BubbleSpeedBoost { get; set; } = 0;
        public List<string> BubbleTriggerVariants { get; set; } = new() { "flash", "subliminal", "pink", "spiral", "glitch", "htlink", "video" };
        public bool BubbleAvatarEggEnabled { get; set; } = true;

        // Lock Card Settings (Level 35+)
        public bool LockCardEnabled { get; set; } = false;
        public int LockCardFrequency { get; set; } = 2;
        public int LockCardRepeats { get; set; } = 3;
        public bool LockCardStrict { get; set; } = false;
        // The 6.9.4 dials. Defaults mirror AppSettings so a preset saved before they existed
        // applies as "off, fixed count", exactly what it meant when it was written.
        public bool LockCardRandomRepeats { get; set; } = false;
        public int LockCardRepeatsMin { get; set; } = 1;
        public bool LockCardTargetLengthEnabled { get; set; } = false;
        public int LockCardTargetLength { get; set; } = 120;
        public int LockCardTargetLengthVariance { get; set; } = 20;

        // Bubble Count Settings (Level 50+)
        public bool BubbleCountEnabled { get; set; } = false;
        public int BubbleCountFrequency { get; set; } = 3;
        public int BubbleCountDifficulty { get; set; } = 50;
        public bool BubbleCountStrictLock { get; set; } = false;

        // Bouncing Text Settings (Level 65+)
        public bool BouncingTextEnabled { get; set; } = false;
        public int BouncingTextSpeed { get; set; } = 50;
        public int BouncingTextSize { get; set; } = 48;
        public int BouncingTextOpacity { get; set; } = 100;

        // Mind Wipe Settings (Level 80+)
        public bool MindWipeEnabled { get; set; } = false;
        public int MindWipeFrequency { get; set; } = 2;
        public int MindWipeVolume { get; set; } = 50;
        public bool MindWipeLoop { get; set; } = false;

        // Brain Drain Settings (Level 95+)
        public bool BrainDrainEnabled { get; set; } = false;
        public int BrainDrainIntensity { get; set; } = 50;
        public bool BrainDrainHighRefresh { get; set; } = false;

        // System Settings
        public bool DualMonitorEnabled { get; set; } = true;
        public bool PanicKeyEnabled { get; set; } = true;

        /// <summary>
        /// Each effect's V2 switch, keyed by <c>SuperEffect</c> name, stored like
        /// <see cref="AppSettings.SuperModes"/> (0 classic, 1 and 2 V2 on). A save writes all eight
        /// explicitly. Null on presets saved before 7.3.1 and on the built-in ones: applying such a
        /// preset turns every V2 switch OFF (classic), the look it had when it was made; an effect
        /// missing from the map turns off too (ccp-bugs #1696: V2 defaults on, so an old preset
        /// came back with the Whirlpool and Steam it never had).
        /// </summary>
        public Dictionary<string, int>? SuperModes { get; set; }

        /// <summary>
        /// The newer settings (Steam, floating videos, flash motion, V2 options, monitor targets) as
        /// AppSettings name to value; see <see cref="PresetCarry"/>. Null on presets saved before 7.3.7
        /// and on the built-ins: applying one puts the looks back to their defaults.
        /// </summary>
        public Dictionary<string, string>? Carried { get; set; }

        /// <summary>
        /// Creates the 5 default presets
        /// </summary>
        public static List<Preset> GetDefaultPresets()
        {
            return new List<Preset>
            {
                new Preset
                {
                    Id = "default-gentle",
                    Name = "Gentle Introduction",
                    Description = "Very mild settings for beginners. Relaxed pace with minimal intensity.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 5, // 5 per hour = every 12 minutes
                    SimultaneousImages = 3,
                    FlashOpacity = 60,
                    FadeDuration = 60,
                    FlashClickable = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 1,
                    StrictLockEnabled = false,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 2,
                    SubliminalOpacity = 50,
                    SubAudioEnabled = false,
                    MasterVolume = 30,
                    SpiralEnabled = false,
                    PinkFilterEnabled = false,
                    PanicKeyEnabled = true
                },
                new Preset
                {
                    Id = "default-basics",
                    Name = "Bimbo Basics",
                    Description = "Light conditioning with occasional flashes. Good for daily use.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 10, // 10 per hour = every 6 minutes
                    SimultaneousImages = 5,
                    FlashOpacity = 80,
                    FadeDuration = 40,
                    FlashClickable = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 2,
                    StrictLockEnabled = false,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 3,
                    SubliminalOpacity = 60,
                    SubAudioEnabled = false,
                    MasterVolume = 35,
                    SpiralEnabled = false,
                    PinkFilterEnabled = false,
                    PanicKeyEnabled = true
                },
                new Preset
                {
                    Id = "default-pink-cloud",
                    Name = "Pink Cloud",
                    Description = "Medium intensity with visual focus. Dreamy pink aesthetic.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 15, // 15 per hour = every 4 minutes
                    SimultaneousImages = 6,
                    FlashOpacity = 90,
                    FadeDuration = 30,
                    FlashClickable = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 3,
                    StrictLockEnabled = false,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 5,
                    SubliminalOpacity = 70,
                    SubAudioEnabled = true,
                    SubAudioVolume = 40,
                    MasterVolume = 40,
                    SpiralEnabled = false,
                    PinkFilterEnabled = false,
                    BubblesEnabled = true,
                    BubblesFrequency = 3,
                    PanicKeyEnabled = true
                },
                new Preset
                {
                    Id = "default-deep",
                    Name = "Deep Conditioning",
                    Description = "Higher intensity training. More frequent triggers and reinforcement.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 20, // 20 per hour = every 3 minutes
                    SimultaneousImages = 8,
                    FlashOpacity = 100,
                    FadeDuration = 25,
                    FlashClickable = true,
                    CorruptionMode = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 5,
                    StrictLockEnabled = false,
                    AttentionChecksEnabled = true,
                    AttentionDensity = 3,
                    AttentionLifespan = 5,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 7,
                    SubliminalOpacity = 80,
                    SubAudioEnabled = true,
                    SubAudioVolume = 50,
                    MasterVolume = 45,
                    AudioDuckingEnabled = true,
                    DuckingLevel = 70,
                    SpiralEnabled = true,
                    SpiralOpacity = 5,
                    PinkFilterEnabled = true,
                    PinkFilterOpacity = 5,
                    BubblesEnabled = true,
                    BubblesFrequency = 6,
                    LockCardEnabled = true,
                    LockCardFrequency = 2,
                    LockCardRepeats = 3,
                    PanicKeyEnabled = true
                },
                new Preset
                {
                    Id = "default-surrender",
                    Name = "Total Surrender",
                    Description = "Maximum intensity preset. For experienced users seeking deeper conditioning.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 30, // 30 per hour = every 2 minutes
                    SimultaneousImages = 10,
                    HydraLimit = 15,
                    FlashOpacity = 100,
                    FadeDuration = 20,
                    FlashClickable = true,
                    CorruptionMode = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 8,
                    StrictLockEnabled = false,
                    AttentionChecksEnabled = true,
                    AttentionDensity = 4,
                    AttentionLifespan = 6,
                    AttentionSize = 60,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 10,
                    SubliminalDuration = 3,
                    SubliminalOpacity = 90,
                    SubAudioEnabled = true,
                    SubAudioVolume = 60,
                    MasterVolume = 50,
                    AudioDuckingEnabled = true,
                    DuckingLevel = 80,
                    SpiralEnabled = true,
                    SpiralOpacity = 10,
                    PinkFilterEnabled = true,
                    PinkFilterOpacity = 10,
                    BubblesEnabled = true,
                    BubblesFrequency = 10,
                    LockCardEnabled = true,
                    LockCardFrequency = 4,
                    LockCardRepeats = 5,
                    LockCardStrict = false,
                    PanicKeyEnabled = true
                }
            };
        }

        /// <summary>
        /// Apply this preset to the given settings
        /// </summary>
        public void ApplyTo(AppSettings settings)
        {
            // Flash
            settings.FlashEnabled = FlashEnabled;
            settings.FlashFrequency = FlashFrequency;
            settings.SimultaneousImages = SimultaneousImages;
            settings.HydraLimit = HydraLimit;
            settings.ImageScale = ImageScale;
            settings.FlashOpacity = FlashOpacity;
            settings.FadeDuration = FadeDuration;
            settings.FlashClickable = FlashClickable;
            settings.CorruptionMode = CorruptionMode;
            settings.HydraLinkedTiming = HydraLinkedTiming;

            // Video
            settings.MandatoryVideosEnabled = MandatoryVideosEnabled;
            settings.VideosPerHour = VideosPerHour;
            settings.StrictLockEnabled = StrictLockEnabled;
            settings.VideoMinDurationSeconds = VideoMinDurationSeconds;
            settings.VideoMaxDurationSeconds = VideoMaxDurationSeconds;
            settings.VideoCutRandomStart = VideoCutRandomStart;
            settings.MandatoryVideoLoopShortClips = MandatoryVideoLoopShortClips;
            settings.MandatoryVideoFillShortWithOthers = MandatoryVideoFillShortWithOthers;

            // Attention Checks
            settings.AttentionChecksEnabled = AttentionChecksEnabled;
            settings.AttentionDensity = AttentionDensity;
            settings.AttentionLifespan = AttentionLifespan;
            settings.AttentionSize = AttentionSize;

            // Subliminal
            settings.SubliminalEnabled = SubliminalEnabled;
            settings.SubliminalFrequency = SubliminalFrequency;
            settings.SubliminalDuration = SubliminalDuration;
            settings.SubliminalOpacity = SubliminalOpacity;
            if (SubliminalPool != null)
                settings.SubliminalPool = new Dictionary<string, bool>(SubliminalPool);

            // Attention
            if (AttentionPool != null)
                settings.AttentionPool = new Dictionary<string, bool>(AttentionPool);

            // Lock Card Phrases
            if (LockCardPhrases != null)
                settings.LockCardPhrases = new Dictionary<string, bool>(LockCardPhrases);

            // Audio
            settings.SubAudioEnabled = SubAudioEnabled;
            settings.SubAudioVolume = SubAudioVolume;
            settings.MasterVolume = MasterVolume;
            settings.AudioDuckingEnabled = AudioDuckingEnabled;
            settings.DuckingLevel = DuckingLevel;

            // Overlays
            settings.SpiralEnabled = SpiralEnabled;
            settings.SpiralOpacity = SpiralOpacity;
            settings.PinkFilterEnabled = PinkFilterEnabled;
            settings.PinkFilterOpacity = PinkFilterOpacity;

            // Bubbles
            settings.BubblesEnabled = BubblesEnabled;
            settings.BubblesFrequency = BubblesFrequency;
            if (BubblesVolume.HasValue) settings.BubblesVolume = BubblesVolume.Value;
            if (BubbleSharedHost.HasValue) settings.BubbleSharedHost = BubbleSharedHost.Value;
            if (BubblesLinkRamp.HasValue) settings.BubblesLinkRamp = BubblesLinkRamp.Value;
            if (BubblesClickable.HasValue) settings.BubblesClickable = BubblesClickable.Value;
            settings.BubbleTriggersEnabled = BubbleTriggersEnabled;
            settings.BubbleTriggerChance = BubbleTriggerChance;
            settings.BubbleSpeedBoost = BubbleSpeedBoost;
            settings.BubbleTriggerVariants = BubbleTriggerVariants != null ? new List<string>(BubbleTriggerVariants) : new List<string>();
            settings.BubbleAvatarEggEnabled = BubbleAvatarEggEnabled;

            // Lock Card
            settings.LockCardEnabled = LockCardEnabled;
            settings.LockCardFrequency = LockCardFrequency;
            settings.LockCardRepeats = LockCardRepeats;
            settings.LockCardStrict = LockCardStrict;
            settings.LockCardRandomRepeats = LockCardRandomRepeats;
            settings.LockCardRepeatsMin = LockCardRepeatsMin;
            settings.LockCardTargetLengthEnabled = LockCardTargetLengthEnabled;
            settings.LockCardTargetLength = LockCardTargetLength;
            settings.LockCardTargetLengthVariance = LockCardTargetLengthVariance;

            // Bubble Count
            settings.BubbleCountEnabled = BubbleCountEnabled;
            settings.BubbleCountFrequency = BubbleCountFrequency;
            settings.BubbleCountDifficulty = BubbleCountDifficulty;
            settings.BubbleCountStrictLock = BubbleCountStrictLock;

            // Bouncing Text
            settings.BouncingTextEnabled = BouncingTextEnabled;
            settings.BouncingTextSpeed = BouncingTextSpeed;
            settings.BouncingTextSize = BouncingTextSize;
            settings.BouncingTextOpacity = BouncingTextOpacity;

            // Mind Wipe
            settings.MindWipeEnabled = MindWipeEnabled;
            settings.MindWipeFrequency = MindWipeFrequency;
            settings.MindWipeVolume = MindWipeVolume;
            settings.MindWipeLoop = MindWipeLoop;

            // Brain Drain
            settings.BrainDrainEnabled = BrainDrainEnabled;
            settings.BrainDrainIntensity = BrainDrainIntensity;
            settings.BrainDrainHighRefresh = BrainDrainHighRefresh;

            // System
            settings.DualMonitorEnabled = DualMonitorEnabled;
            settings.PanicKeyEnabled = PanicKeyEnabled;

            // V2 switches: every effect, classic where the preset names none (ccp-bugs #1696);
            // assigned only on a real change so a preset that matches the live state raises nothing.
            var superModes = MergeSuperModes(settings.SuperModes, SuperModesOrClassic(SuperModes));
            if (superModes != null) settings.SuperModes = superModes;

            // Steam, floating videos, flash motion, V2 options, monitors (ccp-bugs #1695 #1696).
            PresetCarry.Apply(settings, Carried);

            // Update current preset name
            settings.CurrentPresetName = Name;
        }

        /// <summary>
        /// Create a preset from current settings
        /// </summary>
        public static Preset FromSettings(AppSettings settings, string name, string description = "")
        {
            return new Preset
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Description = description,
                IsDefault = false,
                CreatedAt = DateTime.Now,

                // Flash
                FlashEnabled = settings.FlashEnabled,
                FlashFrequency = settings.FlashFrequency,
                SimultaneousImages = settings.SimultaneousImages,
                HydraLimit = settings.HydraLimit,
                ImageScale = settings.ImageScale,
                FlashOpacity = settings.FlashOpacity,
                FadeDuration = settings.FadeDuration,
                FlashClickable = settings.FlashClickable,
                CorruptionMode = settings.CorruptionMode,
                HydraLinkedTiming = settings.HydraLinkedTiming,

                // Video
                MandatoryVideosEnabled = settings.MandatoryVideosEnabled,
                VideosPerHour = settings.VideosPerHour,
                StrictLockEnabled = settings.StrictLockEnabled,
                VideoMinDurationSeconds = settings.VideoMinDurationSeconds,
                VideoMaxDurationSeconds = settings.VideoMaxDurationSeconds,
                VideoCutRandomStart = settings.VideoCutRandomStart,
                MandatoryVideoLoopShortClips = settings.MandatoryVideoLoopShortClips,
                MandatoryVideoFillShortWithOthers = settings.MandatoryVideoFillShortWithOthers,

                // Attention Checks
                AttentionChecksEnabled = settings.AttentionChecksEnabled,
                AttentionDensity = settings.AttentionDensity,
                AttentionLifespan = settings.AttentionLifespan,
                AttentionSize = settings.AttentionSize,

                // Subliminal
                SubliminalEnabled = settings.SubliminalEnabled,
                SubliminalFrequency = settings.SubliminalFrequency,
                SubliminalDuration = settings.SubliminalDuration,
                SubliminalOpacity = settings.SubliminalOpacity,
                SubliminalPool = new Dictionary<string, bool>(settings.SubliminalPool),
                AttentionPool = new Dictionary<string, bool>(settings.AttentionPool),
                LockCardPhrases = new Dictionary<string, bool>(settings.LockCardPhrases),

                // Audio
                SubAudioEnabled = settings.SubAudioEnabled,
                SubAudioVolume = settings.SubAudioVolume,
                MasterVolume = settings.MasterVolume,
                AudioDuckingEnabled = settings.AudioDuckingEnabled,
                DuckingLevel = settings.DuckingLevel,

                // Overlays
                SpiralEnabled = settings.SpiralEnabled,
                SpiralOpacity = settings.SpiralOpacity,
                PinkFilterEnabled = settings.PinkFilterEnabled,
                PinkFilterOpacity = settings.PinkFilterOpacity,

                // Bubbles
                BubblesEnabled = settings.BubblesEnabled,
                BubblesFrequency = settings.BubblesFrequency,
                BubblesVolume = settings.BubblesVolume,
                BubbleSharedHost = settings.BubbleSharedHost,
                BubblesLinkRamp = settings.BubblesLinkRamp,
                BubblesClickable = settings.BubblesClickable,
                BubbleTriggersEnabled = settings.BubbleTriggersEnabled,
                BubbleTriggerChance = settings.BubbleTriggerChance,
                BubbleSpeedBoost = settings.BubbleSpeedBoost,
                BubbleTriggerVariants = settings.BubbleTriggerVariants != null ? new List<string>(settings.BubbleTriggerVariants) : new List<string>(),
                BubbleAvatarEggEnabled = settings.BubbleAvatarEggEnabled,

                // Lock Card
                LockCardEnabled = settings.LockCardEnabled,
                LockCardFrequency = settings.LockCardFrequency,
                LockCardRepeats = settings.LockCardRepeats,
                LockCardStrict = settings.LockCardStrict,
                LockCardRandomRepeats = settings.LockCardRandomRepeats,
                LockCardRepeatsMin = settings.LockCardRepeatsMin,
                LockCardTargetLengthEnabled = settings.LockCardTargetLengthEnabled,
                LockCardTargetLength = settings.LockCardTargetLength,
                LockCardTargetLengthVariance = settings.LockCardTargetLengthVariance,

                // Bubble Count
                BubbleCountEnabled = settings.BubbleCountEnabled,
                BubbleCountFrequency = settings.BubbleCountFrequency,
                BubbleCountDifficulty = settings.BubbleCountDifficulty,
                BubbleCountStrictLock = settings.BubbleCountStrictLock,

                // Bouncing Text
                BouncingTextEnabled = settings.BouncingTextEnabled,
                BouncingTextSpeed = settings.BouncingTextSpeed,
                BouncingTextSize = settings.BouncingTextSize,
                BouncingTextOpacity = settings.BouncingTextOpacity,

                // Mind Wipe
                MindWipeEnabled = settings.MindWipeEnabled,
                MindWipeFrequency = settings.MindWipeFrequency,
                MindWipeVolume = settings.MindWipeVolume,
                MindWipeLoop = settings.MindWipeLoop,

                // Brain Drain
                BrainDrainEnabled = settings.BrainDrainEnabled,
                BrainDrainIntensity = settings.BrainDrainIntensity,
                BrainDrainHighRefresh = settings.BrainDrainHighRefresh,

                // System
                DualMonitorEnabled = settings.DualMonitorEnabled,
                PanicKeyEnabled = settings.PanicKeyEnabled,

                // V2 switches
                SuperModes = SnapshotSuperModes(settings.SuperModes),
                Carried = PresetCarry.Snapshot(settings)
            };
        }

        /// <summary>Every effect's V2 switch as the player has it now (absent = the default, written out).</summary>
        public static Dictionary<string, int> SnapshotSuperModes(IReadOnlyDictionary<string, int>? live)
        {
            var map = new Dictionary<string, int>();
            foreach (SuperEffect e in Enum.GetValues(typeof(SuperEffect)))
                map[e.ToString()] = (int)SuperModeRule.Resolve(e, live);
            return map;
        }

        /// <summary>
        /// The preset's V2 switches with every effect present: what it names (exact names), classic for
        /// the rest. A preset with no map (saved before 7.3.1, or a built-in) is all classic, the look
        /// it had when it was made (ccp-bugs #1696).
        /// </summary>
        public static Dictionary<string, int> SuperModesOrClassic(IReadOnlyDictionary<string, int>? preset)
        {
            var map = new Dictionary<string, int>();
            foreach (SuperEffect e in Enum.GetValues(typeof(SuperEffect)))
            {
                var name = e.ToString();
                map[name] = preset != null && preset.TryGetValue(name, out var v) ? v : (int)SuperMode.Classic;
            }
            return map;
        }

        /// <summary>
        /// The live map after applying a preset's V2 switches, or null when nothing changes (no map
        /// in the preset, or every named effect already as the preset has it). Unknown names are
        /// ignored; values are clamped to what the effect offers.
        /// </summary>
        public static Dictionary<string, int>? MergeSuperModes(IReadOnlyDictionary<string, int>? live, IReadOnlyDictionary<string, int>? preset)
        {
            if (preset == null || preset.Count == 0) return null;
            Dictionary<string, int>? next = null;
            foreach (var kv in preset)
            {
                // Exact names only: Enum.TryParse would also take "3" or "Vortex, Creep".
                if (!Enum.TryParse<SuperEffect>(kv.Key, ignoreCase: false, out var effect)
                    || effect.ToString() != kv.Key) continue;
                var want = SuperModeRule.Clamp(effect, kv.Value);
                if (SuperModeRule.Resolve(effect, next ?? live) == SuperModeRule.Resolve(effect, new Dictionary<string, int> { [kv.Key] = (int)want }))
                    continue;
                next = SuperModeRule.With(next ?? live, effect, want);
            }
            return next;
        }
    }
}

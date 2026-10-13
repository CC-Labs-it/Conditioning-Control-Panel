using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Finding the offline speech model on disk, and the folder it goes in.
///
/// <para>Four people spent forty-minute support threads on this in one week. Two facts came out of
/// them: the folder does not exist on a fresh install, and the ~128 MB model the README recommended
/// "did not work" while the 40 MB one did. The second is not about size - it is about DEPTH.
/// Windows' "Extract All" defaults to a subfolder named after the zip, so unpacking the bigger
/// model into this folder lands it TWO levels down, and the resolver only looked one. It reported
/// that as no model at all, and the hint said "no speech model installed yet" to someone looking
/// straight at the model they had just put there.</para>
/// </summary>
public class SpeechModelFolderTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ccp-vosk-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Makes a directory look like a Vosk model: am/ next to conf/.</summary>
    private static string Model(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "am"));
        Directory.CreateDirectory(Path.Combine(dir, "conf"));
        return dir;
    }

    private string Under(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = _root;
        parts.CopyTo(all, 1);
        return Path.Combine(all);
    }

    // ---- the three layouts --------------------------------------------------------------

    [Fact]
    public void FilesDroppedStraightIn()
    {
        Model(_root);
        Assert.Equal(_root, SpeechEngine.ResolveModelDir(_root));
    }

    [Fact]
    public void OneNestedFolderTheWayTheOfficialZipUnpacks()
    {
        var dir = Model(Under("vosk-model-small-en-us-0.15"));
        Assert.Equal(dir, SpeechEngine.ResolveModelDir(_root));
    }

    /// <summary>The regression this exists for. Extract All, twice the folder, no model found.</summary>
    [Fact]
    public void TheSameFolderTwiceWhichIsWhatExtractAllProduces()
    {
        var dir = Model(Under("vosk-model-en-us-0.22-lgraph", "vosk-model-en-us-0.22-lgraph"));
        Assert.Equal(dir, SpeechEngine.ResolveModelDir(_root));
    }

    [Fact]
    public void ThreeLevelsDownIsStillNotFound()
    {
        // Deliberate: a bounded search, not a tree walk. The README says to move it up.
        Model(Under("a", "b", "c"));
        Assert.Null(SpeechEngine.ResolveModelDir(_root));
    }

    [Fact]
    public void AnEmptyFolderIsTheSameAsNoFolder()
    {
        Directory.CreateDirectory(_root);
        Assert.Null(SpeechEngine.ResolveModelDir(_root));
        Assert.Null(SpeechEngine.ResolveModelDir(Path.Combine(_root, "not-there")));
    }

    [Fact]
    public void HalfAModelIsNotAModel()
    {
        // am/ with no conf/ is a half-finished unpack, which Vosk would refuse anyway.
        Directory.CreateDirectory(Under("vosk-model-small-en-us-0.15", "am"));
        Assert.Null(SpeechEngine.ResolveModelDir(_root));
    }

    // ---- ranking ------------------------------------------------------------------------

    /// <summary>
    /// The grammar-capable ranking has to survive the deeper scan: a full model found nested must
    /// not outrank a small one found at the top, or dropping a model in makes speech worse.
    /// </summary>
    [Fact]
    public void GrammarCapableModelsStillWinFromAnyDepth()
    {
        Model(Under("vosk-model-en-us-0.42-gigaspeech"));
        var lgraph = Model(Under("outer", "vosk-model-en-us-0.22-lgraph"));
        Assert.Equal(lgraph, SpeechEngine.ResolveModelDir(_root));
    }

    [Fact]
    public void SmallBeatsAFullModelToo()
    {
        Model(Under("vosk-model-en-us-0.42-gigaspeech"));
        var small = Model(Under("vosk-model-small-en-us-0.15"));
        Assert.Equal(small, SpeechEngine.ResolveModelDir(_root));
    }

    // ---- the folder itself ----------------------------------------------------------------

    /// <summary>
    /// The button must never be a dead end: it creates the folder, because on a fresh install
    /// there is nothing there and "put it in Resources\Models\vosk" is the instruction that has
    /// been costing the support threads.
    /// </summary>
    [Fact]
    public void EnsureCreatesTheFolderAndIsHappyToFindIt()
    {
        Assert.False(Directory.Exists(_root));
        Assert.Equal(_root, SpeechModelFolder.Ensure(_root));
        Assert.True(Directory.Exists(_root));

        // Idempotent, and it does not disturb a model already sitting there.
        Model(_root);
        Assert.Equal(_root, SpeechModelFolder.Ensure(_root));
        Assert.Equal(_root, SpeechEngine.ResolveModelDir(_root));
    }

    [Fact]
    public void EnsureRefusesNonsenseInsteadOfThrowing()
    {
        Assert.Null(SpeechModelFolder.Ensure(null));
        Assert.Null(SpeechModelFolder.Ensure("   "));
    }

    /// <summary>The button opens the install-side folder WPF always used, and the app searches it.</summary>
    [Fact]
    public void TheFolderTheButtonOpensIsTheFolderTheAppSearches()
    {
        var expected = Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "vosk");
        Assert.Equal(expected, SpeechModelFolder.Root);
        Assert.Contains(expected, SpeechEngine.DefaultModelRoots);
    }

    // ---- which microphone ----------------------------------------------------------------

    private static readonly SpeechInputDevice[] Mics =
    {
        new(-1, "System default"), new(0, "Headset"), new(1, "Webcam Mic"), new(2, "Blue Yeti"),
    };

    /// <summary>#441b: a virtual device appearing reshuffles ordinals, so the name wins.</summary>
    [Fact]
    public void TheSavedNameBeatsAStaleIndex()
        => Assert.Equal(2, SpeechEngine.ResolveInputDevice(Mics, savedIndex: 0, savedName: "blue yeti")?.Index);

    [Fact]
    public void AGoneNameFallsBackToTheSavedIndex()
        => Assert.Equal(1, SpeechEngine.ResolveInputDevice(Mics, savedIndex: 1, savedName: "Unplugged")?.Index);

    [Fact]
    public void NothingSavedOrNothingLeftMeansTheOsDefault()
    {
        Assert.Null(SpeechEngine.ResolveInputDevice(Mics, savedIndex: -1, savedName: null));
        Assert.Null(SpeechEngine.ResolveInputDevice(Mics, savedIndex: 7, savedName: "Unplugged"));
        // The "System default" row is a picker entry, not a device to match by name.
        Assert.Null(SpeechEngine.ResolveInputDevice(Mics, savedIndex: -1, savedName: "System default"));
    }
}

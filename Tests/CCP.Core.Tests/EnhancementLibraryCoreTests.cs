using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// EnhancementLibrary, moved from the WPF head (deeper-core-a). Pins the seams that replaced
/// App.*: the folder comes from CorePaths, the recent list and last directory go through
/// CoreSettings, and the watcher's debounced LibraryChanged reaches subscribers through
/// CoreDispatch instead of a WPF DispatcherTimer.
/// </summary>
[Collection(SessionStatics.Name)] // CoreSettings.ServiceProvider and CoreDispatch.PostProvider are process-global
public sealed class EnhancementLibraryCoreTests
{
    private static Enhancement Named(string name) => new()
    {
        MediaType = "video",
        MediaSource = "https://hypnotube.com/video/abc",
        Metadata = new EnhancementMetadata { Name = name },
    };

    private static T WithSettings<T>(Func<T> body)
    {
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try { return body(); }
        finally { CoreSettings.ServiceProvider = old; }
    }

    [Fact]
    public void LibraryFolder_is_enhancements_under_the_core_user_data_root_and_exists()
    {
        using var library = new EnhancementLibrary();

        Assert.Equal(Path.Combine(CorePaths.UserData, "enhancements"), library.LibraryFolder);
        Assert.True(Directory.Exists(library.LibraryFolder));
    }

    [Fact]
    public void Save_records_the_recent_file_and_last_directory_in_core_settings()
    {
        WithSettings(() =>
        {
            using var library = new EnhancementLibrary();
            var dir = Directory.CreateTempSubdirectory("ccp-enhlib-").FullName;
            try
            {
                var path = Path.Combine(dir, "spiral" + EnhancementLibrary.FileSuffix);

                library.Save(Named("Spiral"), path);

                Assert.Equal(Path.GetFullPath(path), CoreSettings.Current.DeeperRecentFiles[0]);
                Assert.Equal(dir, CoreSettings.Current.DeeperLastDirectory);
                Assert.Equal(dir, library.LastDirectory);
                Assert.Contains(Path.GetFullPath(path), library.RecentFiles);
            }
            finally { Directory.Delete(dir, recursive: true); }
            return 0;
        });
    }

    [Fact]
    public void PromoteToLibrary_dedupes_a_clashing_name_and_the_scan_finds_both()
    {
        WithSettings(() =>
        {
            using var library = new EnhancementLibrary();
            var name = "promote-" + Guid.NewGuid().ToString("N");

            var first = library.PromoteToLibrary(Named(name), "test");
            var second = library.PromoteToLibrary(Named(name), "test");
            try
            {
                Assert.Equal(Path.Combine(library.LibraryFolder, name + EnhancementLibrary.FileSuffix), first);
                Assert.Equal(Path.Combine(library.LibraryFolder, name + " (2)" + EnhancementLibrary.FileSuffix), second);
                Assert.Equal(2, library.ScanLibrary().FindAll(e => e.Name == name).Count);
                Assert.Equal(first, library.FindByJsonPath(first!)?.FilePath);
            }
            finally
            {
                File.Delete(first!);
                File.Delete(second!);
            }
            return 0;
        });
    }

    [Fact]
    public void A_burst_of_file_events_raises_LibraryChanged_once_through_CoreDispatch()
    {
        var oldPost = CoreDispatch.PostProvider;
        var posted = 0;
        CoreDispatch.PostProvider = action => { Interlocked.Increment(ref posted); action(); };
        try
        {
            var clock = new PopQuizSchedulerTests.FakeClock();
            using var library = new EnhancementLibrary(clock);
            var raised = 0;
            library.LibraryChanged += (_, _) => raised++;

            // An atomic-save style burst: three watcher events 100 ms apart, inside the 300 ms debounce.
            for (var i = 0; i < 3; i++)
            {
                library.ScheduleChangeNotification();
                clock.Advance(TimeSpan.FromMilliseconds(100));
            }

            clock.Advance(TimeSpan.FromMilliseconds(199));
            Assert.Equal(0, raised); // 299 ms after the last event: still settling
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(1, raised);
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(1, raised);
            Assert.Equal(1, posted);
        }
        finally
        {
            CoreDispatch.PostProvider = oldPost;
        }
    }
}

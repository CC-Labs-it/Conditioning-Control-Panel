using System;
using System.IO;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>BANK ON RETURN: gains made with no wallet window in front fly later, as one flight.</summary>
public sealed class SparkleOwedBookTests
{
    private static SparkleChange C(int amount, int before, SparkleSource source = SparkleSource.BackRoom)
        => new(amount, before, before + amount, source);

    [Fact]
    public void UnshownGainsAddUpAndFlyAsOneFromTheOldValue()
    {
        var book = new SparkleOwedBook();
        var me = new object();
        book.Note(me, C(5, 100), shown: false);
        book.Note(me, C(12, 105, SparkleSource.Place), shown: false);
        book.Note(me, C(3, 117, SparkleSource.LevelUp), shown: true);   // flew: not owed
        Assert.Equal(17, book.Owed);
        Assert.Equal((103, 120), book.Take(me, 120));
        Assert.Equal(0, book.Owed);
        Assert.Null(book.Take(me, 120));
    }

    [Theory]
    [InlineData(SparkleSource.Synced, 100)]   // a sync adoption
    [InlineData(SparkleSource.Reset, 100)]
    [InlineData(SparkleSource.BackRoom, 0)]   // the first fill of an empty wallet (sign-in, profile load)
    public void SignInSyncAndResetAreNeverOwed(SparkleSource source, int before)
    {
        var book = new SparkleOwedBook();
        var me = new object();
        book.Note(me, C(40, before, source), shown: false);
        Assert.Equal(0, book.Owed);
        Assert.False(SparkleOwedBook.CanOwe(C(40, before, source)));
    }

    [Fact]
    public void ASpendMeanwhileTakesOffWhatIsOwedNeverBelowZero()
    {
        var book = new SparkleOwedBook();
        var me = new object();
        book.Note(me, C(30, 100), shown: false);
        book.Note(me, C(-10, 130, SparkleSource.CounterBuy), shown: false);
        Assert.Equal(20, book.Owed);
        book.Note(me, C(-90, 120, SparkleSource.Stake), shown: true);
        Assert.Equal(0, book.Owed);
        book.Note(me, C(8, 30), shown: false);
        Assert.Equal(8, book.Owed);
    }

    [Fact]
    public void NeverOwedBeyondTheBalanceAndNeverForAnotherProfile()
    {
        var book = new SparkleOwedBook();
        var me = new object();
        book.Note(me, C(50, 10), shown: false);
        Assert.Equal(50, book.OwedTo(me, 60));
        Assert.Equal(20, book.OwedTo(me, 20));          // the balance fell under it some other way
        Assert.Equal(0, book.OwedTo(new object(), 60)); // another profile is never owed my stars
        book.Note(new object(), C(5, 60), shown: true);  // a switch drops it
        Assert.Equal(0, book.OwedTo(me, 60));
        book.Note(me, C(5, 60), shown: false);
        book.Note(me, new SparkleChange(-65, 65, 0, SparkleSource.Reset), shown: false);
        Assert.Equal(0, book.Owed);
        book.Note(me, C(5, 60), shown: false);
        book.Clear();
        Assert.Equal(0, book.Owed);
    }

    [Fact]
    public void PanicStopsTheFlightAndTheBadgeGivesWay()
    {
        var main = Read("ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs");
        int panic = main.IndexOf("private void HandlePanicKeyPress()", StringComparison.Ordinal);
        int stop = main.IndexOf("SparkleFlightDirector.Stop()", panic, StringComparison.Ordinal);
        int lockCard = main.IndexOf("LockCardWindow.IsAnyOpen()", panic, StringComparison.Ordinal);
        Assert.True(stop > panic && stop < lockCard, "panic stops the flight before any rung can return");
        Assert.Contains("SparkleFlightDirector.Stop()", Read("ConditioningControlPanel", "MainWindow", "MainWindow.RemoteControl.cs"));

        var director = Read("ConditioningControlPanel", "Services", "Prizes", "SparkleFlightDirector.cs");
        Assert.Contains("SparkleSfx.StopAll();", director);
        Assert.Contains("Abort(snap: true);", director);
        Assert.Contains("!Services.Prizes.SparkleFlightDirector.Covers(award.Balance)",
            Read("ConditioningControlPanel", "MainWindow", "MainWindow.SparkleWallet.cs"));
        Assert.Contains("WalletChip.IsPressed", Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.SparkleFly.cs"));
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}

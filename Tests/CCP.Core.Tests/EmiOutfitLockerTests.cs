using System;
using System.IO;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public class EmiOutfitLockerTests
{
    [Fact]
    public void OwnedPickAndChangesAreReadButInvalidDataIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var locker = new EmiOutfitLocker();
        try
        {
            File.WriteAllText(path, "{\"lockerOutfit\":\"cheer\",\"wallet\":{\"inv\":{\"emi_cheer\":{\"n\":1}}}}");
            Assert.Equal("cheer", locker.Read(path));
            File.WriteAllText(path, "{\"lockerOutfit\":\"cheer\"}");
            Assert.Null(locker.Read(path));
            File.WriteAllText(path, "{\"lockerOutfit\":\"unknown\",\"wallet\":{\"inv\":{\"emi_cheer\":{\"n\":1}}}}");
            Assert.Null(locker.Read(path));
            File.WriteAllText(path, "broken");
            Assert.Null(locker.Read(path));
        }
        finally { File.Delete(path); }
    }
}

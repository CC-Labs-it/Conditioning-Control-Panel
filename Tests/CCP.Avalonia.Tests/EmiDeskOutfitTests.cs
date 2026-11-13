using System;
using System.IO;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiDeskOutfitTests
{
    [Fact]
    public Task SummonReadsWalletAndRepaintsBody() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var path = Path.Combine(CorePaths.UserData, "arcademy_meta.json");
        var old = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var svc = EmiDeskService.Instance;
        var settings = CoreSettings.Current;
        var enabled = settings.EmiDeskEnabled;
        var mute = settings.EmiDeskMuteAvatar;
        EmiDeskWindow? desk = null;
        try
        {
            File.WriteAllText(path, "{\"lockerOutfit\":\"cheer\",\"wallet\":{\"inv\":{\"emi_cheer\":{\"n\":1}}}}");
            settings.EmiDeskEnabled = true; settings.EmiDeskMuteAvatar = false;
            await svc.Summon();
            desk = svc.Window!;
            Assert.NotNull(desk);
            Assert.Equal("cheer", desk.Outfit);
            var body = (Image)typeof(EmiDeskWindow).GetField("_bodyImage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!;
            var dressed = body.Source;
            Assert.NotNull(dressed);
            var cache = (Dictionary<string, Bitmap>)typeof(EmiDeskWindow).GetField("_bodyCache", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!;
            Assert.True(cache.ContainsKey(Path.Combine(AppContext.BaseDirectory, "Resources", "web", "arcademy", "art", "emi", "cheer", "body-idle.png")));
            File.WriteAllText(path, "{\"lockerOutfit\":\"cheer\"}");
            desk.StartDeskEquipment();
            Assert.Null(desk.Outfit);
            Assert.NotNull(body.Source);
            Assert.NotSame(dressed, body.Source);
        }
        finally
        {
            desk?.ShutDown();
            settings.EmiDeskEnabled = enabled; settings.EmiDeskMuteAvatar = mute;
            if (old == null) File.Delete(path); else File.WriteAllBytes(path, old);
        }
    });
}

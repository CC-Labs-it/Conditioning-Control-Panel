using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LockdownConsentGambleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WarningPlacesGambleBetweenSafetyValveAndPossession(bool windows)
    {
        var cfg = new AppSettings { LockdownPossessionEnabled = true };
        var warning = LockdownTabView.LockdownWarning(20, cfg, windows);
        var valve = windows
            ? "  (or Ctrl+Alt+Del → Task Manager as a safety valve)"
            : "  (or ending the app from a system monitor / terminal as a safety valve)";
        const string gamble = "\n- The Emergency Exit button is a gamble: win its little game and you may leave early,\n"
            + "  lose it and the timer restarts at its FULL length";
        Assert.Contains(valve + gamble + "\n- Possession is ON", warning);
    }
    [Theory]
    [InlineData("de", "Der Notausgang ist ein Glücksspiel: Gewinne das kleine Spiel und du kannst früher gehen,\n  verlierst du, beginnt der Timer wieder mit seiner VOLLEN Dauer")]
    [InlineData("es", "La salida de emergencia es una apuesta: gana el juego y podrás salir antes,\n  si pierdes, el temporizador vuelve a empezar con su duración COMPLETA")]
    [InlineData("fr", "La sortie de secours est un pari : gagne son petit jeu et tu pourras partir plus tôt,\n  si tu perds, le minuteur repart pour sa durée COMPLÈTE")]
    [InlineData("it", "L'uscita di emergenza è una scommessa: vinci il gioco e potrai uscire prima,\n  se perdi, il timer riparte dalla sua durata COMPLETA")]
    [InlineData("ja", "緊急脱出は賭けです。ゲームに勝てば早く出られるかもしれません。\n  負けるとタイマーは全時間で再開します")]
    [InlineData("ko", "비상 탈출은 도박입니다. 게임에서 이기면 일찍 나갈 수 있지만,\n  지면 타이머가 전체 시간으로 다시 시작됩니다")]
    [InlineData("pt-BR", "A saída de emergência é uma aposta: vença o jogo e poderá sair mais cedo,\n  se perder, o temporizador recomeça com a duração COMPLETA")]
    [InlineData("ru", "Аварийный выход это риск: выиграйте игру и сможете уйти раньше,\n  проиграете, и таймер запустится заново на ПОЛНОЕ время")]
    [InlineData("zh-CN", "紧急出口是一场赌博：赢得小游戏就可能提前离开，\n  输了计时器会按完整时长重新开始")]
    public async Task WarningUsesSelectedLanguage(string language, string gamble)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var loc = LocalizationManager.Instance;
            var previous = loc.CurrentLanguage;
            try
            {
                loc.SetLanguage(language);
                var warning = LockdownTabView.LockdownWarning(20, new AppSettings { LockdownPossessionEnabled = true }, false);
                Assert.Contains("safety valve)\n- " + gamble + "\n- Possession is ON", warning);
            }
            finally { loc.SetLanguage(previous); }
            return Task.CompletedTask;
        });
    }
}

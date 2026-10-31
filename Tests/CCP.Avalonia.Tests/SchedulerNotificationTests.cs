using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SchedulerNotificationTests
{
    [Theory]
    [InlineData("en", "Scheduler active", "Session started on schedule.", "Scheduler", "Scheduled session ended.")]
    [InlineData("de", "Zeitplan aktiv", "Sitzung nach Zeitplan gestartet.", "Zeitplan", "Geplante Sitzung beendet.")]
    [InlineData("es", "Programación activa", "Sesión iniciada según el horario.", "Programación", "La sesión programada terminó.")]
    [InlineData("fr", "Planification active", "Séance démarrée selon le planning.", "Planification", "La séance planifiée est terminée.")]
    [InlineData("it", "Pianificazione attiva", "Sessione avviata secondo il programma.", "Pianificazione", "La sessione programmata è terminata.")]
    [InlineData("pt-BR", "Agendamento ativo", "Sessão iniciada conforme o agendamento.", "Agendamento", "A sessão agendada terminou.")]
    [InlineData("ru", "Расписание активно", "Сеанс запущен по расписанию.", "Расписание", "Запланированный сеанс завершён.")]
    [InlineData("ja", "スケジュール有効", "スケジュールに従ってセッションを開始しました。", "スケジュール", "予定されたセッションが終了しました。")]
    [InlineData("ko", "일정 활성화", "일정에 따라 세션이 시작되었습니다.", "일정", "예정된 세션이 종료되었습니다.")]
    [InlineData("zh-CN", "计划已启用", "会话已按计划开始。", "计划", "计划会话已结束。")]
    public void BothNotificationsUseTheSelectedLanguage(string language, string startTitle,
        string startBody, string endTitle, string endBody) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var loc = LocalizationManager.Instance;
        var oldLanguage = loc.CurrentLanguage;
        var oldSink = OsNotifications.Sink;
        var shell = new MainShellWindow();
        try
        {
            loc.SetLanguage(language);
            var notices = new List<(string Title, string Body)>();
            OsNotifications.Sink = (title, body) => notices.Add((title, body));
            var s = CoreSettings.Current;
            s.FlashEnabled = s.SubliminalEnabled = s.BubblesEnabled = s.BouncingTextEnabled = false;
            s.LockCardEnabled = s.PopQuizEnabled = s.MindWipeEnabled = s.MandatoryVideosEnabled = s.BubbleCountEnabled = false;
            PanicAftermath.EndQuiet();
            // Already running: exercise the real start notification without a portal bind or effects.
            CoreEngine.Start();
            var apply = typeof(MainShellWindow).GetMethod("ApplySchedulerAction", BindingFlags.Instance | BindingFlags.NonPublic)!;
            apply.Invoke(shell, new object[] { SchedulerRun.Action.Start });
            apply.Invoke(shell, new object[] { SchedulerRun.Action.Stop });
            Assert.Equal(new[] { (startTitle, startBody), (endTitle, endBody) }, notices);
        }
        finally
        {
            CoreEngine.Stop();
            shell.Close();
            OsNotifications.Sink = oldSink;
            loc.SetLanguage(oldLanguage);
        }
    });
}

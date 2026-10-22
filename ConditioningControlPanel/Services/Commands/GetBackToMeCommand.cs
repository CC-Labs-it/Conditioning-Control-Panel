using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Schedules a delayed AI follow-up: after <c>Delay</c> seconds, queries the AI again
    /// and optionally executes nested commands. Recursion is bounded by
    /// <see cref="CommandFactory.MaxGetBackToMeDepth"/> and per-command delay is capped.
    /// </summary>
    public class GetBackToMeCommand : ICommand
    {
        // Cap delay so AI can't schedule things hours from now.
        public const int MaxDelaySec = 600;

        private readonly GetBackToMe _data;
        private readonly CancellationToken _cancellationToken;
        private readonly int _depth;

        public GetBackToMeCommand(GetBackToMe data, CancellationToken cancellationToken, int depth = 0)
        {
            _data = data;
            _cancellationToken = cancellationToken;
            _depth = depth;
        }

        public async Task<bool> ExecuteAsync()
        {
            if (_depth >= CommandFactory.MaxGetBackToMeDepth)
            {
                App.Logger?.Information("GetBackToMeCommand: depth cap reached ({Depth}) — refusing further nesting", _depth);
                return false;
            }

            var delay = Math.Clamp(_data.Delay, 1, MaxDelaySec);
            // A panic while this waits cancels it: nothing AI-driven comes back after a panic.
            var panicsAtSchedule = Safety.PanicAftermath.PanicCount;

            try
            {
                App.Logger?.Debug("GetBackToMeCommand: scheduled in {Delay}s (depth={Depth}, token={Token})",
                    delay, _depth, _data.Token);
                await Task.Delay(delay * 1000, _cancellationToken);

                if (!AiCommandAdmission.FollowUpStands(panicsAtSchedule, Safety.PanicAftermath.PanicCount))
                {
                    App.Logger?.Information("GetBackToMeCommand: a panic came first, follow-up dropped");
                    return false;
                }

                await SendTokenMessage(_data.Token, _data.JsonOnly, _data.Text);

                if (_data.Commands != null)
                {
                    // Nested commands meet the same gate as a top-level one (switches, panic quiet,
                    // output moderation) and the same per-reply cap.
                    var ran = 0;
                    foreach (var subCommand in _data.Commands)
                    {
                        if (_cancellationToken.IsCancellationRequested) break;
                        if (subCommand == null || !AiCommandService.Admits(subCommand)) continue;
                        if (++ran > AiCommandService.MaxCommandsPerResponse) break;
                        var cmd = CommandFactory.CreateCommand(subCommand, _cancellationToken, _depth + 1);
                        if (cmd != null)
                        {
                            await cmd.ExecuteAsync();
                        }
                    }
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GetBackToMeCommand failed");
                return false;
            }
        }

        private async Task SendTokenMessage(string token, bool jsonOnly, string? text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                // The model wrote this line inside its getbacktome command, so it wears the AI
                // badge (it passed output moderation with the command).
                ShowAvatarMessage(text, aiGenerated: true);
            }
            // R2-NEW-H-1: migrate to typed AI API. Refusals are silently dropped on
            // this remote-control surface (the local user didn't directly prompt — a
            // POLICY bubble would surprise them). Downstream guard already logged via
            // ModerationLog. IsAiGenerated propagates so canned fallbacks don't wear
            // the AI badge.
            // The reply's own commands dispatch one level deeper, so a getbacktome in it counts
            // against the depth cap instead of starting a fresh chain.
            AiCommandAdmission.EnterFollowUp(_depth + 1);
            var result = await App.Ai.GetBambiReplyExAsync($"[Token={token}, JsonOnly={jsonOnly}]");
            if (!jsonOnly && result.Refusal == null && !string.IsNullOrEmpty(result.Text))
            {
                ShowAvatarMessage(result.Text, aiGenerated: result.IsAiGenerated);
            }
        }

        private static void ShowAvatarMessage(string text, bool aiGenerated = false)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { App.AvatarWindow?.GigglePriority(text, playSound: true, aiGenerated: aiGenerated); }
                catch (Exception ex) { App.Logger?.Debug("GetBackToMeCommand: avatar speak failed: {Error}", ex.Message); }
            }));
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Asks;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Mode = ConditioningControlPanel.Views.Controls.Companion.CompanionProviderMode;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;

internal sealed record ConversationAction(string TurnId, string Id, string Label);
internal sealed record ConversationAskChoice(string CardId, string ChoiceId, string Label, bool Chosen, bool Enabled,
    IBrush Background, IBrush BorderColor, IBrush Foreground);
internal sealed record ConversationLine(string Id, string Speaker, string Text, string Time, bool IsUser)
{
    public ConversationAction[] Actions { get; init; } = Array.Empty<ConversationAction>();
    public ConversationAskChoice[] AskChoices { get; init; } = Array.Empty<ConversationAskChoice>();
    public string? AskCardId { get; init; }
    public string? LinkTitle { get; init; }
    public string? LinkUrl { get; init; }
    public bool HasLink => !string.IsNullOrEmpty(LinkTitle) && !string.IsNullOrEmpty(LinkUrl);
    public bool HasAsk => AskChoices.Length > 0;
    /// <summary>WPF ConversationLine.WithAsk: the live ask card; one the service forgot draws as plain text.</summary>
    internal ConversationLine WithAsk(string? cardId)
    {
        if (CompanionAskService.Instance.Find(cardId) is not { } card) return this;
        var open = card.IsOpen(DateTime.UtcNow);
        return this with
        {
            AskCardId = card.Id,
            Text = card.Question,
            AskChoices = card.Choices.Select(c =>
            {
                var (bg, border, fg) = AskTones.Colours(c.Tone);
                return new ConversationAskChoice(card.Id, c.Id, c.Label, card.ChosenId == c.Id, open, Brush.Parse(bg), Brush.Parse(border), Brush.Parse(fg));
            }).ToArray()
        };
    }
}

/// <summary>
/// PORTED from WPF V2/ConversationPageVm.cs: one projection of the brain's shared conversation, no
/// transcript of its own. The room is this head's CompanionRoomView (hero + engine zones) where WPF
/// passes CompanionRoomRuntimeVm; the pure rules are Core <see cref="ConversationRules"/>.
/// </summary>
internal sealed class ConversationPageVm : CompanionObservable
{
    private readonly Func<CompanionHeroCardViewModel?> _hero;
    private readonly Func<EngineRoomVm?> _engine;
    private ChatSession? _session;
    private CancellationTokenSource? _send;
    private string _draft = string.Empty, _notice = string.Empty;
    private bool _busy, _canRetry, _observing;
    private IMemoryStore? _memoryOwner;
    private EngineRoomVm? _watchedEngine;
    private CompanionHeroCardViewModel? _watchedHero;
    public ConversationPageVm(Func<CompanionHeroCardViewModel?> hero, Func<EngineRoomVm?> engine) { _hero = hero; _engine = engine; }
    public CompanionHeroCardViewModel? Hero => _hero();
    public ObservableCollection<ConversationLine> Turns { get; } = new();
    public string Draft { get => _draft; set { Set(ref _draft, value ?? string.Empty); Raise(nameof(CanSend)); Raise(nameof(DraftEmpty)); } }
    public bool DraftEmpty => Draft.Length == 0;
    public string Notice { get => _notice; private set { Set(ref _notice, value); Raise(nameof(HasNotice)); Raise(nameof(Status)); Raise(nameof(StatusColor)); } }
    public bool HasNotice => Notice.Length > 0;
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(CanCompose)); Raise(nameof(CanSend)); Raise(nameof(Status)); Raise(nameof(StatusColor)); } }
    public bool CanCompose => !Busy;
    public bool CanSend => !Busy && !string.IsNullOrWhiteSpace(Draft);
    public bool CanRetry { get => _canRetry; private set => Set(ref _canRetry, value); }
    // WPF: the active mod's own companion name (CCP Default's manifest says "Companion"), and the
    // active personality preset's description, falling back to the hero's.
    public string Name => CoreMods.Service?.GetCompanionName() ?? Hero?.Name ?? string.Empty;
    public string Flavor => PersonalityService.Shared.GetActivePreset()?.Description ?? Hero?.Flavor ?? string.Empty;
    public string PerkCost => CompanionCore.ActivePerk == CompanionBonusType.XPDrain ? "Leech: -3 XP/s" : string.Empty;
    public bool HasPerkCost => PerkCost.Length > 0;
    private Mode Provider => _engine()?.Provider ?? Mode.Off;
    private bool LoggedIn => _engine()?.IsLoggedIn == true;
    public bool DailyExhausted => Provider == Mode.Cloud && App.Ai?.DailyRequestsRemaining == 0;
    public bool NeedsStart => Provider == Mode.Off;
    public bool NeedsSignIn => Provider == Mode.Cloud && !LoggedIn;
    public bool ShowStart => NeedsStart || NeedsSignIn;
    public bool HasNoTurns => Turns.Count == 0;
    public bool HasTurns => !HasNoTurns;
    public bool CompanionShown => Hero?.IsCompanionShown == true;
    public bool CompanionHidden => !CompanionShown;
    public string VoiceLabel => Loc.Get(Hero?.IsMuted == true ? "companion_v2_unmute" : "companion_v2_mute");
    public string StartLabel => Loc.Get(NeedsSignIn ? "companion_v2_signin" : "companion_v2_start");
    public IBrush StatusColor => Brush.Parse(HasNotice || NeedsSignIn || DailyExhausted ? "#FFCD83" : NeedsStart ? "#BFAEC9" : "#F2A8D4");
    public string Status => Busy ? Loc.Get("companion_v2_replying") : HasNotice ? Loc.Get("companion_v2_reply_failed") : NeedsStart ? Loc.Get("companion_v2_off")
        : NeedsSignIn ? Loc.Get("companion_v2_signin_status") : DailyExhausted ? Loc.Get("companion_v2_error_limit")
        : _engine() is { IsHealthy: false, StatusLine: { Length: > 0 } line } ? line : Loc.Get("companion_v2_ready");
    public string Privacy => Loc.Get(CoreSettings.Current?.CompanionPrompt?.AiProvider is AiProviderType.Local or AiProviderType.OpenAiCompatible
        ? "companion_v2_privacy_endpoint" : "companion_v2_privacy_cloud");
    private static readonly string[] Derived = { nameof(NeedsStart), nameof(NeedsSignIn), nameof(ShowStart), nameof(StartLabel), nameof(Status),
        nameof(Privacy), nameof(Name), nameof(Flavor), nameof(StatusColor), nameof(PerkCost), nameof(HasPerkCost), nameof(VoiceLabel),
        nameof(CompanionShown), nameof(CompanionHidden), nameof(Hero) };
    /// <summary>WPF Refresh: an account switch drops the send in flight, the draft and the notice.</summary>
    public void Refresh()
    {
        App.Brain?.EnsureCurrentAccount();
        var owner = App.Brain?.Memory;
        if (!ReferenceEquals(owner, _memoryOwner))
        {
            _memoryOwner = owner;
            Stop();
            Draft = string.Empty;
            Notice = string.Empty;
            CanRetry = false;
        }
        _engine()?.Sync();   // WPF _room.SyncBrain(): provider and sign-in re-read before the page decides
        if (_observing) { Attach(App.Brain?.Session); Watch(); }
        Reconcile();
        foreach (var name in Derived) Raise(name);
    }
    public void Resume()
    {
        if (!_observing)
        {
            _observing = true;
            CoreAccount.UnifiedIdentityChanged += IdentityChanged;   // WPF App.UnifiedIdentityChanged
            CompanionAskService.Instance.CardChanged += AskChanged;
        }
        Refresh();
    }
    public void Detach()
    {
        _observing = false;
        CoreAccount.UnifiedIdentityChanged -= IdentityChanged;
        CompanionAskService.Instance.CardChanged -= AskChanged;
        Attach(null);
        Watch(stop: true);
    }
    private void IdentityChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Normal);
    private void Watch(bool stop = false)
    {
        var engine = stop ? null : _engine();
        var hero = stop ? null : Hero;
        if (!ReferenceEquals(engine, _watchedEngine))
        {
            if (_watchedEngine != null) _watchedEngine.PropertyChanged -= RoomChanged;
            if ((_watchedEngine = engine) != null) engine!.PropertyChanged += RoomChanged;
        }
        if (!ReferenceEquals(hero, _watchedHero))
        {
            if (_watchedHero != null) _watchedHero.PropertyChanged -= RoomChanged;
            if ((_watchedHero = hero) != null) hero!.PropertyChanged += RoomChanged;
        }
    }
    private void RoomChanged(object? sender, PropertyChangedEventArgs e) { foreach (var name in Derived) Raise(name); }
    private void Attach(ChatSession? session)
    {
        if (ReferenceEquals(session, _session)) return;
        if (_session != null) _session.TurnsChanged -= Changed;
        _session = session;
        if (_session != null) _session.TurnsChanged += Changed;
    }
    private void Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Reconcile, DispatcherPriority.Normal);
    /// <summary>Updates only the changed suffix, keeping scroll position and selected text (WPF Reconcile).</summary>
    internal void Reconcile()
    {
        var next = (_session?.Turns ?? Array.Empty<CompanionTurn>()).Where(t => ConversationRules.Shows(t.Kind)).ToArray();
        var common = 0;
        while (common < Turns.Count && common < next.Length && Turns[common].Id == next[common].Id) common++;
        while (Turns.Count > common) Turns.RemoveAt(Turns.Count - 1);
        var activities = CompanionEffects.Activities();
        foreach (var t in next.Skip(common))
            Turns.Add((Line(t, Name, CompanionLinkIndex.FindMentionedTitle) with
            {
                Actions = t.ActivityIds.Select(id => activities.FirstOrDefault(a => a.Id == id)).Where(a => a?.Allowed == true)
                    .Select(a => new ConversationAction(t.Id, a!.Id, Loc.GetF("companion_v2_open_activity", a.Label))).ToArray()
            }).WithAsk(t.AskCardId));
        Raise(nameof(HasNoTurns));
        Raise(nameof(HasTurns));
    }
    private void AskChanged(AskCard card) => Dispatcher.UIThread.Post(() =>
    {
        for (int i = 0; i < Turns.Count; i++)
            if (Turns[i].AskCardId == card.Id) Turns[i] = Turns[i].WithAsk(card.Id);
    }, DispatcherPriority.Normal);
    public void AnswerAsk(ConversationAskChoice choice) => CompanionAskService.Instance.Answer(choice.CardId, choice.ChoiceId);
    /// <summary>WPF Line: only a model reply gets a watch link, and its text loses the dead "[Title]" brackets.</summary>
    internal static ConversationLine Line(CompanionTurn t, string name, Func<string?, CompanionLinkIndex.Entry?> findTitle)
    {
        var isUser = t.Kind == TurnKind.UserChat;
        var model = t.Kind == TurnKind.AssistantChat && !t.IsApplicationReply;
        var link = model ? findTitle(t.Text) : null;
        var text = model ? ConversationLinks.Tidy(t.Text, link?.Title) : t.Text;
        return new(t.Id, isUser ? Loc.Get("companion_v2_you") : name, text, t.Utc.ToLocalTime().ToString("t"), isUser)
        { LinkTitle = link?.Title, LinkUrl = link?.Url };
    }
    public void OpenLink(ConversationLine line)
    {
        if (line.HasLink && CompanionLinkIndex.IsSanctioned(line.LinkUrl)) Platform.ExternalOpener.Open(line.LinkUrl);
    }
    public void OpenActivity(ConversationAction action)
    {
        App.Brain?.EnsureCurrentAccount();
        if (!ReferenceEquals(_memoryOwner, App.Brain?.Memory)
            || _session?.Turns.Any(t => t.Id == action.TurnId && t.ActivityIds.Contains(action.Id)) != true) return;
        try { if (CompanionEffects.Activities().FirstOrDefault(a => a.Id == action.Id)?.TryOpen() == true) return; }
        catch (Exception ex) { Serilog.Log.Warning("Companion activity failed: {Type}", ex.GetType().Name); }
        Notice = Loc.Get("companion_v2_activity_unavailable");
        CanRetry = false;
    }
    /// <summary>WPF Start: Off picks the configured provider; Cloud without an account asks to sign in.</summary>
    public void Start()
    {
        if (NeedsStart && _engine() is { } engine)
            engine.Provider = (CoreSettings.Current?.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud) switch
            {
                AiProviderType.Local => Mode.LocalOllama,
                AiProviderType.OpenAiCompatible => Mode.Custom,
                _ => Mode.Cloud
            };
        Refresh();
        // EngineRoomVm.LoginCommand is null on this head; account sign-in is ShowTab("patreon") like the room's other sign-in doors.
        if (NeedsSignIn) { if (_engine()?.LoginCommand is { } login) login.Execute(null); else Hero?.Shell?.ShowTab("patreon"); }
    }
    public void Stop() => _send?.Cancel();
    public async Task SendAsync()
    {
        Refresh();
        var text = Draft.Trim();
        if (Busy || text.Length == 0) return;
        if (ShowStart) { Start(); return; }
        var brain = App.Brain;
        if (!CompanionBrain.ShouldRoute(brain)) { Notice = Loc.Get("companion_v2_unavailable"); return; }
        using var cancellation = new CancellationTokenSource();
        _send = cancellation;
        Busy = true;
        CanRetry = false;
        Notice = string.Empty;
        try
        {
            var owner = brain!.Memory;
            var result = await brain.ChatAsync(text, cancellation.Token);
            brain.EnsureCurrentAccount();
            if (!ReferenceEquals(owner, brain.Memory)) return;
            if (result.Refusal != null)
            {
                // A policy refusal must not leave the prohibited draft available for resending.
                Draft = string.Empty;
                Notice = Loc.Get("companion_v2_refused");
                AvatarTube.AvatarTubeWindow.Live?.ShowModerationRefusalBubble(result.Refusal.Source);
            }
            else if (result.IsAiGenerated || result.IsApplicationReply)
            {
                Draft = string.Empty;
                // The page's send never passes the tube's input box, so the reply is spoken exactly once.
                var tube = AvatarTube.AvatarTubeWindow.Live;
                if (tube != null && tube.IsVisible && ConversationRules.ShouldSpeak(result.Text, CoreSettings.Current?.AvatarEnabled == true))
                    tube.GigglePriority(result.Text!, aiGenerated: result.IsAiGenerated);
                CompanionAskService.Instance.OfferForRequest(text);
            }
            else
            {
                Notice = Loc.Get(ConversationRules.FailureNoticeKey(result.Failure));
                CanRetry = result.Retryable || result.Failure == AiFailureKind.Cancelled;
                if (result.Failure == AiFailureKind.DailyLimit) Windows.MainShellWindow.Current?.NoteCompanionCap();
            }
        }
        catch (OperationCanceledException) { Notice = Loc.Get("companion_v2_cancelled"); CanRetry = true; }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Companion conversation failed: {Type}", ex.GetType().Name);
            Notice = Loc.Get("companion_v2_unavailable");
            CanRetry = true;
        }
        finally { _send = null; Busy = false; Refresh(); }
    }
    public void NewChat()
    {
        if (Busy) return;
        App.Brain?.ForgetThread();
        Notice = string.Empty;
        CanRetry = false;
        Refresh();
    }
}

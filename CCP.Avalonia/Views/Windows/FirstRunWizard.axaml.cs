using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// One mod row on the wizard's second step. Deliberately a separate view-model from
    /// <c>ModPickerCard</c> even though the two look alike: the picker's card is a multi-select
    /// download queue (its checkbox hides for anything without a pack), while this one is a
    /// single-choice "which flavour do you want to run", so CCP Default and already-installed mods
    /// must be pickable too. Same discipline though - every visual decision is a plain INPC
    /// property, including the visibility ones, so the DataTemplate needs no value converters.
    ///
    /// <para>PORTED from the WPF code-behind's nested-in-the-same-file class. Deviations, all
    /// forced by Avalonia: <c>Brush</c> -> <c>IBrush</c>, and the five <c>Visibility</c> properties
    /// become <c>bool</c> ones (<c>NoteVisible</c>, ...) because Avalonia's <c>IsVisible</c> binds a
    /// bool directly - see CLAUDE.md. The names keep their shape so the two files still diff.</para>
    /// </summary>
    public sealed class FirstRunModCard : INotifyPropertyChanged
    {
        public string ModId { get; init; } = "";
        public string? PackId { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public IBrush AccentBrush { get; init; } = Brushes.HotPink;
        public string Note { get; init; } = "";

        /// <summary>
        /// The catalogue's shipped art, never the active mod's: WPF binds the compiled ArtUri for
        /// the same reason (five candidates, one active mod).
        /// </summary>
        public IImage? Art { get; init; }

        public bool HasPack => !string.IsNullOrEmpty(PackId);

        public bool NoteVisible => !string.IsNullOrEmpty(Note);

        // ---- selection ----

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectMark));
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardBorderThickness));
            }
        }

        public string SelectMark => IsSelected ? "◉" : "○";

        public IBrush CardBorderBrush => IsSelected ? AccentBrush : Brushes.Transparent;

        public Thickness CardBorderThickness => new Thickness(IsSelected ? 2 : 0);

        // ---- download state (mirrors ModPickerCard's, minus the queue affordances) ----

        public enum CardState { Available, Queued, Downloading, Installing, Installed, Failed }

        private CardState _state = CardState.Available;
        public CardState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressVisible));
                OnPropertyChanged(nameof(StatusOnlyVisible));
                OnPropertyChanged(nameof(InstalledVisible));
                OnPropertyChanged(nameof(SizeVisible));
            }
        }

        private string _sizeText = "";
        public string SizeText
        {
            get => _sizeText;
            set
            {
                if (_sizeText == value) return;
                _sizeText = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeVisible));
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set { if (_statusText == value) return; _statusText = value ?? ""; OnPropertyChanged(); }
        }

        private double _percent;
        public double Percent
        {
            get => _percent;
            set { if (Math.Abs(_percent - value) < 0.01) return; _percent = value; OnPropertyChanged(); }
        }

        public string InstalledText { get; init; } = "";

        public bool IsInstalled => State == CardState.Installed;

        public bool InstalledVisible => IsInstalled && HasPack;

        public bool SizeVisible => !IsInstalled && !string.IsNullOrEmpty(SizeText);

        public bool ProgressVisible => State == CardState.Downloading || State == CardState.Installing;

        public bool StatusOnlyVisible => State == CardState.Queued || State == CardState.Failed;

        public void MarkQueued()
        {
            State = CardState.Queued;
            StatusText = Loc.Get("modpicker_status_queued");
        }

        public void MarkProgress(double percent)
        {
            Percent = Math.Max(0, Math.Min(100, percent));
            if (Percent >= 100)
            {
                State = CardState.Installing;
                StatusText = Loc.Get("modpicker_status_installing");
            }
            else
            {
                State = CardState.Downloading;
                StatusText = Loc.GetF("modpicker_status_downloading", (int)Math.Round(Percent));
            }
        }

        public void MarkInstalled()
        {
            Percent = 100;
            State = CardState.Installed;
            StatusText = Loc.Get("modpicker_status_done");
        }

        public void MarkFailed()
        {
            State = CardState.Failed;
            StatusText = Loc.Get("modpicker_status_failed");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// The first run, in two steps: Welcome (language, your own content folder, the 18+ gate) and
    /// Flavour (which mod to run), in place of the popup gauntlet a fresh install used to walk
    /// through.
    ///
    /// <para>PORTED from ConditioningControlPanel/Windows/FirstRunWizard.xaml.cs. What survives is
    /// everything the VIEW owns - the two steps and their copy, the language row, the step counter
    /// and footer captions, card selection, the chrome (drag, Esc, X, Enter/Keep the default) -
    /// plus everything the Core seams can now answer: the <c>Welcomed</c> gate
    /// (<see cref="ShouldRunAndClaim"/> / <see cref="HandBackFirstRun"/>, on
    /// <c>CoreSettings</c> and <c>CoreReleaseContent.AppVersion</c>), the active mod the cards
    /// pre-select against, the real pack ids and sizes (see <see cref="ModPickerCatalog"/>), and
    /// the pack-installed signal.</para>
    ///
    /// <para><b>One tour.</b> As in WPF there is no doors step and no "Take the tour" button: EMI
    /// offers the single walk there is from her chip, after this window is gone.</para>
    ///
    /// <para><see cref="Run"/> is real, and <c>MainShellWindow.FirstRun.cs</c> calls it: the
    /// shell claims the gate in its constructor and opens this window once it is on screen. The
    /// content-folder picker is <c>MainShellWindow.PickAssetsFolder</c>, opened at once from the
    /// button (docs/avalonia-decisions.html).</para>
    ///
    /// <para>Step 2 commits for real: <see cref="PrepareModStep"/> spends (or, offline, latches)
    /// the picker's one-shot offer and <see cref="CommitModChoice"/> switches through the shell's
    /// one path (<c>MainShellWindow.ActivateChosenMod</c>) or records a <c>PendingModChoice</c> and
    /// starts the pack download (WPF FirstRunWizard.xaml.cs:913-969, 1048-1103).</para>
    ///
    /// <para>The hardening lessons the original documents are preserved as comments where the code
    /// they guard still exists, and dropped with the code where it does not - a comment about
    /// spending a one-shot flag on a method that no longer spends one is worse than no comment.</para>
    /// </summary>
    public partial class FirstRunWizard : Window
    {
        private const int StepCount = 2;

        private readonly ObservableCollection<FirstRunModCard> _cards = new();

        private int _step = 1;
        private FirstRunModCard? _selected;

        // Named controls. FindControl rather than the generated fields, matching the other ports.
        private readonly TextBlock _txtWizardTitle;
        private readonly TextBlock _txtStepCounter;
        private readonly Image _imgWelcomeLogo;
        private readonly TextBlock _txtAppTitle;
        private readonly TextBlock _txtWelcomeHeading;
        private readonly TextBlock _txtWelcomeBody;
        private readonly TextBlock _txtLanguageLabel;
        private readonly TextBlock _txtLanguageHint;
        private readonly ComboBox _cmbWizardLanguage;
        private readonly TextBlock _txtFolderLabel;
        private readonly TextBlock _txtFolderHint;
        private readonly TextBlock _txtModHeading;
        private readonly TextBlock _txtModSub;
        private readonly TextBlock _txtModHint;
        private readonly TextBlock _txtPickFolder;
        private readonly TextBlock _txtSkip;
        private readonly TextBlock _txtNext;
        private readonly Grid _step1;
        private readonly Grid _step2;
        private readonly Button _btnPickFolder;
        private readonly ItemsControl _modCardsList;
        private readonly CheckBox _chkAgeConfirm;
        private readonly TextBlock _txtAgeConfirm;
        private readonly TextBlock _txtContentPolicy;
        private readonly TextBlock _txtCloseHint;
        private readonly Button _btnSkip;
        private readonly Button _btnNext;

        /// <summary>True once Enter was pressed on the Welcome step with the 18+ box ticked.</summary>
        public bool AgeAccepted { get; private set; }

        private bool _populatingLanguage;

        /// <summary>The folder the Welcome step's button applied, shown on that button.</summary>
        private string? _pickedFolder;

        /// <summary>
        /// The only constructor. WPF's took the owning MainWindow so the mod commit could call
        /// back into it; nothing here can, so it takes nothing - which is also what
        /// <c>--render-all</c> needs to discover the view. The owner is passed to
        /// <c>ShowDialog</c> by <see cref="Run"/> instead, which is all Avalonia needs it for.
        /// </summary>
        internal FirstRunWizard()
        {
            AvaloniaXamlLoader.Load(this);

            _txtWizardTitle = this.FindControl<TextBlock>("TxtWizardTitle")!;
            _txtStepCounter = this.FindControl<TextBlock>("TxtStepCounter")!;
            _imgWelcomeLogo = this.FindControl<Image>("ImgWelcomeLogo")!;
            _txtAppTitle = this.FindControl<TextBlock>("TxtAppTitle")!;
            _txtWelcomeHeading = this.FindControl<TextBlock>("TxtWelcomeHeading")!;
            _txtWelcomeBody = this.FindControl<TextBlock>("TxtWelcomeBody")!;
            _txtLanguageLabel = this.FindControl<TextBlock>("TxtLanguageLabel")!;
            _txtLanguageHint = this.FindControl<TextBlock>("TxtLanguageHint")!;
            _cmbWizardLanguage = this.FindControl<ComboBox>("CmbWizardLanguage")!;
            _txtFolderLabel = this.FindControl<TextBlock>("TxtFolderLabel")!;
            _txtFolderHint = this.FindControl<TextBlock>("TxtFolderHint")!;
            _txtModHeading = this.FindControl<TextBlock>("TxtModHeading")!;
            _txtModSub = this.FindControl<TextBlock>("TxtModSub")!;
            _txtModHint = this.FindControl<TextBlock>("TxtModHint")!;
            _txtPickFolder = this.FindControl<TextBlock>("TxtPickFolder")!;
            _txtSkip = this.FindControl<TextBlock>("TxtSkip")!;
            _txtNext = this.FindControl<TextBlock>("TxtNext")!;
            _step1 = this.FindControl<Grid>("Step1")!;
            _step2 = this.FindControl<Grid>("Step2")!;
            _btnPickFolder = this.FindControl<Button>("BtnPickFolder")!;
            _modCardsList = this.FindControl<ItemsControl>("ModCardsList")!;
            _chkAgeConfirm = this.FindControl<CheckBox>("ChkAgeConfirm")!;
            _txtAgeConfirm = this.FindControl<TextBlock>("TxtAgeConfirm")!;
            _txtContentPolicy = this.FindControl<TextBlock>("TxtContentPolicy")!;
            _txtCloseHint = this.FindControl<TextBlock>("TxtCloseHint")!;
            _btnSkip = this.FindControl<Button>("BtnSkip")!;
            _btnNext = this.FindControl<Button>("BtnNext")!;

            DetectSystemLanguage();
            ApplyStaticText();
            PopulateLanguages();
            BuildModCards();
            _modCardsList.ItemsSource = _cards;

            // A pack finishing while this window is open flips its card to Installed. Raised on the
            // head's download thread, hence the hop. CoreReleaseContent.PackInstalled is a STATIC
            // event, so the Closed handler below must detach or the window outlives its own close.
            //
            // ponytail: still needs App.ReleaseContent's PackProgressChanged (the per-percent
            // MarkProgress) and App.Mods' ModAvailabilityChanged (extracted, not merely downloaded);
            // neither has a Core seam yet, so a download shows nothing until it lands.
            CoreReleaseContent.PackInstalled += OnPackInstalled;

            // Handlers live here rather than in markup, per the porting convention.
            this.FindControl<Border>("TitleBar")!.PointerPressed += TitleBar_PointerPressed;
            this.FindControl<Button>("BtnCloseX")!.Click += (_, _) => CloseSafely();
            _btnPickFolder.Click += BtnPickFolder_Click;
            _btnSkip.Click += BtnSkip_Click;
            _btnNext.Click += BtnNext_Click;
            _cmbWizardLanguage.SelectionChanged += CmbWizardLanguage_SelectionChanged;
            _chkAgeConfirm.IsCheckedChanged += (_, _) => ApplyStepChrome();
            // WPF AgeConfirmLabel_Click: the sentence is the target too.
            _txtAgeConfirm.PointerReleased += (_, _) => _chkAgeConfirm.IsChecked = _chkAgeConfirm.IsChecked != true;

            // One handler on the list instead of one inside the DataTemplate: template content has
            // no name scope to FindControl through, and PointerReleased bubbles the same way WPF's
            // MouseLeftButtonUp did.
            _modCardsList.AddHandler(InputElement.PointerReleasedEvent, ModCard_PointerReleased);

            // WPF's PreviewKeyDown. Tunnel, so Esc closes the window before any focused child eats it.
            AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);

            Closed += OnWizardClosed;
            ShowStep(1);
        }

        // ------------------------------------------------------------------ entry points

        /// <summary>
        /// First launch? Reads <c>Welcomed</c> and latches it (plus the assets-prompt one-shot),
        /// exactly where <c>WelcomeDialog.ShowIfNeeded</c> used to. That dialog carries a SECOND
        /// copy of the same latch, and only one of the two may ever be called or the second sees
        /// an already-claimed flag and shows nothing. SETTLED, and it is WPF's answer: the wizard
        /// owns the latch. <c>MainShellWindow.FirstRun.cs</c> calls this and nothing else;
        /// <c>WelcomeDialog.ShowIfNeeded</c> stays deliberately uncalled on this head.
        ///
        /// <para>Three flags are spent here rather than when the window opens, on purpose:</para>
        /// <list type="bullet">
        /// <item><c>Welcomed</c> - a crash inside the wizard must not make it an every-launch
        /// screen (the ModPickerShown lesson).</item>
        /// <item><c>FirstRunAssetsPromptShown</c> - the "choose a content folder" prompt fires
        /// before this wizard can open, so spending it in the gate is the only way a first-run user
        /// never gets that modal on top of the wizard; the Welcome step carries the affordance.</item>
        /// <item><c>LastSeenVersion</c> - the first-run branch is the ONE path that never reaches
        /// the What's New check, which is where every other launch stamps it. Left blank, this
        /// install's first upgrade reads as a fresh install and has its first ever patch notes
        /// stamped away unshown. Correct on both wizard outcomes, so
        /// <see cref="HandBackFirstRun"/> deliberately does not undo it.</item>
        /// </list>
        ///
        /// <para>WPF returned false when <c>App.Settings</c> was null. The Core seam's
        /// <c>Current</c> is never null - with no head attached it is a throwaway default - so
        /// <see cref="CoreSettings.Service"/> is what stands in for that check. Without it the
        /// headless render and the smoke runner would claim a first run against an object nobody
        /// ever saves.</para>
        /// </summary>
        public static bool ShouldRunAndClaim()
        {
            try
            {
                // WPF's `App.Settings?.Current` null check. The seam's Current is never null - with
                // no head attached it is a throwaway default nobody saves - so the service itself is
                // what "is there settings to claim against" has to ask.
                if (CoreSettings.Service == null) return false;

                var settings = CoreSettings.Current;
                if (settings.Welcomed) return false;

                settings.Welcomed = true;
                settings.FirstRunAssetsPromptShown = true;
                settings.LastSeenVersion = CoreReleaseContent.AppVersion;
                CoreSettings.Save();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Gate check failed - skipping the first-run wizard");
                return false;
            }
        }

        /// <summary>
        /// Undoes <see cref="ShouldRunAndClaim"/> when the caller decided NOT to open the wizard
        /// after all (an update dialog outlasted its wait, the window never loaded). Without this
        /// the flags are spent on a screen nobody ever saw and the install silently never gets a
        /// first run. A crash INSIDE the wizard is deliberately NOT covered - that is what spending
        /// up front buys. <c>LastSeenVersion</c> is not handed back: it IS a fresh install of this
        /// version either way, and the next launch re-stamps whatever version it is.
        /// </summary>
        public static void HandBackFirstRun(string reason)
        {
            try
            {
                if (CoreSettings.Service == null) return;

                var settings = CoreSettings.Current;
                settings.Welcomed = false;
                settings.FirstRunAssetsPromptShown = false;
                CoreSettings.Save();
                Log.Information("[FirstRun] Not shown ({Reason}) - handing the first run back to the next launch", reason);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Could not hand the first run back");
            }
        }

        /// <summary>
        /// Shrinks the fixed 900x680 card to the screen's working area (DIPs) so the footer - Enter -
        /// stays on screen; the step bodies already scroll, so the 18+ checkbox stays reachable.
        /// At 200% on 1920x1080 (960x540 DIP) the fixed size put Enter off-screen and the 18+ gate
        /// could not be accepted (distro-check F5). WPF has the same fixed size (FirstRunWizard.xaml:6,
        /// ResizeMode=NoResize); this head improves on it, a normal-scale screen is unchanged.
        /// </summary>
        internal void FitToWorkingArea(Screen? screen)
        {
            if (screen is not null) FitToWorkingArea(screen.WorkingArea, screen.Scaling);
        }

        // Working area is in physical pixels; the window size is in DIPs.
        internal void FitToWorkingArea(PixelRect workingArea, double scaling)
        {
            if (scaling <= 0) return;
            FitTo(workingArea.Width / scaling, workingArea.Height / scaling);
        }

        internal void FitTo(double workWidth, double workHeight)
        {
            Width = Math.Min(Width, workWidth);
            Height = Math.Min(Height, workHeight);
        }

        /// <summary>
        /// Opens the wizard modally on <paramref name="owner"/> and opens the quiet window on the way
        /// out (the folder picker already ran from its button). Never throws - a first-run screen
        /// must never be the reason a fresh install fails to start.
        ///
        /// <para>WPF's <c>Run(MainWindow)</c> is synchronous because <c>ShowDialog</c> is;
        /// Avalonia's is awaitable, so this returns a Task and the caller awaits it. The tail runs
        /// straight after the await rather than from a second dispatcher post: WPF needed the post
        /// only to get off <c>ShowDialog</c>'s nested message loop, and <c>await</c> already is
        /// that.</para>
        /// </summary>
        public static async Task Run(MainShellWindow owner)
        {
            FirstRunWizard? wizard = null;
            try
            {
                MainShellWindow.IsStartupDialogShowing = true;
                wizard = new FirstRunWizard { ShellOwner = owner };
                // The constructor may have switched to the OS language before it had an owner
                // (WPF passes the owner in and calls ApplyLanguageSelection there): re-select the
                // shell's own two language surfaces on that code.
                owner.ApplyLanguageSelection(CoreSettings.Current.Language);
                wizard.FitToWorkingArea(wizard.Screens.ScreenFromWindow(owner) ?? wizard.Screens.Primary);
                await wizard.ShowDialogSafe(owner);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] The first-run wizard failed to show");
            }
            finally
            {
                MainShellWindow.IsStartupDialogShowing = false;
            }

            // WPF Run's safety net (FirstRunWizard.xaml.cs:482-486): every exit that is not
            // "Enter, with the box ticked" stops the launch rather than running ungated.
            if (wizard == null || !wizard.AgeAccepted)
            {
                AbortUngatedLaunch(owner, "age gate not accepted");
                return;
            }

            // The far side of the first run (WPF FirstRunWizard.xaml.cs:502): ten minutes in which
            // passive surfaces go to the Inbox instead of popping.
            Platform.StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            Platform.RemoteMediaOffer.FlushPending();
        }

        /// <summary>
        /// WPF AbortUngatedLaunch (FirstRunWizard.xaml.cs:428): an acceptance already on file
        /// (FirstRunGate.MustShutDown) lets the launch continue; otherwise hand the first run back
        /// and shut down, which is what WPF's decline does.
        /// </summary>
        internal static void AbortUngatedLaunch(MainShellWindow owner, string reason)
        {
            try
            {
                if (CoreSettings.Current.HasAcceptedAgeVerification) return;
                HandBackFirstRun(reason);
                Log.Information("[FirstRun] The 18+ gate was never accepted ({Reason}) - shutting down rather than running ungated", reason);
                owner.ExitWithoutBill();   // WPF FirstRunWizard.xaml.cs:443 Shutdown, no bill
            }
            catch (Exception ex) { Log.Warning(ex, "[FirstRun] Could not stop an ungated launch"); }
        }

        /// <summary>WPF RecordAgeAcceptance: written the moment Enter is pressed.</summary>
        private void RecordAgeAcceptance()
        {
            AgeAccepted = true;
            try
            {
                CoreSettings.Current.HasAcceptedAgeVerification = true;
                CoreSettings.Save();
                Log.Information("[FirstRun] Age verification accepted on the Welcome step");
            }
            catch (Exception ex) { Log.Warning(ex, "[FirstRun] Could not record the age acceptance"); }
        }

        // ------------------------------------------------------------------ copy

        /// <summary>
        /// Localized string with an English fallback. New <c>fr8_</c> keys land in the language
        /// files on their own schedule; until then (and for any language that has not caught up)
        /// this renders the English draft rather than the raw key. Core's
        /// <c>LocalizationManager.Get</c> returns the key itself on a miss, which is what the
        /// equality check below detects.
        /// </summary>
        private static string Str(string key, string english)
        {
            try
            {
                var value = Loc.Get(key);
                return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal)
                    ? english
                    : value;
            }
            catch { return english; }
        }

        private static string StrF(string key, string english, params object[] args)
        {
            var template = Str(key, english);
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }

        /// <summary>
        /// The one art load on this screen that CAN be mod-aware: logo.png is a real
        /// resource-relative path, so a mod shipping its own brand mark shows it here.
        /// <para>
        /// Which wordmark is WPF's branch verbatim: logo2.png is the neutral "Conditioning Control
        /// Panel" mark used by CCP Default and Sissy, logo.png the Bambi-branded one. A fresh
        /// install IS CCP Default, so the wizard must not hardcode logo.png.
        /// </para>
        /// <para>
        /// WPF's <c>ModResourceResolver.ResolveUri</c> is <see cref="Helpers.ModArt.TryLoad"/> here
        /// - the mod override through <see cref="CoreModArt"/>, then this head's own avares:// copy,
        /// which Assets/logo.png and logo2.png are linked into. Null means neither exists, and the
        /// frame is hidden rather than left as an empty 46px gap. WPF's DecodePixelWidth=92 has no
        /// TryLoad equivalent; the PNGs are small and the Image scales, so full-res is the cost.
        /// </para>
        /// </summary>
        private void RefreshWelcomeLogo()
        {
            try
            {
                var useNeutralLogo = CoreMods.IsCCPDefault || CoreSettings.Current.IsSissyMode;
                var logo = Helpers.ModArt.TryLoad(useNeutralLogo ? "logo2.png" : "logo.png");
                _imgWelcomeLogo.Source = logo;
                _imgWelcomeLogo.IsVisible = logo != null;
            }
            catch (Exception ex) { Log.Debug("[FirstRun] logo resolve failed: {E}", ex.Message); }
        }

        /// <summary>
        /// Every string on the screen, re-runnable: the language row switches the app's language in
        /// place, and this window's own copy is code-behind rather than <c>{loc:Str}</c> bindings,
        /// so it has to be repainted by hand when that happens (WPF FirstRunWizard.xaml.cs:572-612).
        /// </summary>
        private void ApplyStaticText()
        {
            Title = Str("fr8_wizard_title", "First run");
            _txtWizardTitle.Text = Title;

            // --- step 1: welcome ---
            RefreshWelcomeLogo();

            _txtAppTitle.Text = Loc.Get("app_title");
            _txtWelcomeHeading.Text = Str("fr8_welcome_title", "Welcome.");
            _txtWelcomeBody.Text = Str("fr8_welcome_body",
                "Two choices and you are in. Everything else waits until you go looking for it.");

            _txtLanguageLabel.Text = Str("fr8_welcome_language", "Language");
            _txtLanguageHint.Text = Str("fr8_welcome_language_hint", "You can change this later in Settings.");

            _txtFolderLabel.Text = Str("fr8_welcome_folder", "Your own content");
            _txtFolderHint.Text = Str("fr8_welcome_folder_hint",
                "Optional. Point the app at a folder of your own images and videos.");

            // Keep the chosen folder if one was already picked: this method also runs on a
            // language switch, and repainting the button would quietly un-say it.
            _txtPickFolder.Text = _pickedFolder ?? Str("fr8_welcome_pick_folder", "Choose a content folder");

            _txtAgeConfirm.Text = Str("fr8_age_confirm", "I am 18 or older and I have read the content policy.");
            _txtContentPolicy.Text = Str("fr8_age_policy_link", "Read the content policy");

            // --- step 2: flavour ---
            _txtModHeading.Text = Str("fr8_modpick_heading", "Pick your flavour");
            _txtModSub.Text = Str("fr8_modpick_sub",
                "A mod re-skins the whole app: your companion's name and voice, the art, the phrases, " +
                "the programs. Pick one to start with. You can switch any time from the title bar.");
            if (!_modStepOffline)
            {
                _txtModHint.Text = Str("fr8_modpick_offline_hint",
                    "Offline? The download waits. There is no second ask.");
            }
        }

        // ------------------------------------------------------------------ steps

        private void ShowStep(int step)
        {
            _step = Math.Max(1, Math.Min(StepCount, step));

            _step1.IsVisible = _step == 1;
            _step2.IsVisible = _step == 2;

            if (_step == 1) RefreshWelcomeLogo();
            if (_step == 2) PrepareModStep();

            ApplyStepChrome();
            FadeInCurrentStep();
        }

        /// <summary>
        /// WPF ApplyStepChrome (FirstRunWizard.xaml.cs:635-660): the title-bar counter and the two
        /// footer buttons. Split out of <see cref="ShowStep"/> because a language switch and a card
        /// selection both change this copy without changing which step is on screen.
        /// </summary>
        private void ApplyStepChrome()
        {
            _txtStepCounter.Text = StrF("fr8_wizard_step_of", "Step {0} of {1}", _step, StepCount);

            if (_step == 1)
            {
                // No skip on the gate: not accepting is closing the window, and the muted line
                // under the button says so rather than dressing it up as a third choice.
                _btnSkip.IsVisible = false;
                _txtNext.Text = Str("fr8_welcome_enter", "Enter");
                _btnNext.IsEnabled = _chkAgeConfirm.IsChecked == true;

                _txtCloseHint.Text = Str("fr8_welcome_close_hint", "Not for you? Close this window.");
                _txtCloseHint.IsVisible = true;
            }
            else
            {
                _btnSkip.IsVisible = true;
                _txtSkip.Text = Str("fr8_modpick_keep_default", "Keep the default");
                _btnNext.IsEnabled = true;
                UpdateFlavourButton();

                _txtCloseHint.Text = "";
                _txtCloseHint.IsVisible = false;
            }
        }

        /// <summary>"Enter with Circe", or plain "Enter" for the in-box default.</summary>
        private void UpdateFlavourButton()
        {
            if (_step != 2) return;

            var card = _selected;
            var isDefault = card == null
                            || string.Equals(card.ModId, BuiltInMods.CCPDefaultId, StringComparison.OrdinalIgnoreCase);

            _txtNext.Text = isDefault
                ? Str("fr8_welcome_enter", "Enter")
                : StrF("fr8_modpick_enter_with", "Enter with {0}", card!.Name);
        }

        // ------------------------------------------------------------------ step 1: language

        /// <summary>
        /// The same language list Settings · General offers, from the same helper -
        /// <see cref="MainShellWindow.FillLanguageCombo"/> - so this screen can never drift from it.
        /// The list opens on whatever <see cref="DetectSystemLanguage"/> settled on.
        /// </summary>
        private void PopulateLanguages()
        {
            _populatingLanguage = true;
            try { MainShellWindow.FillLanguageCombo(_cmbWizardLanguage, shortLabels: false); }
            catch (Exception ex) { Log.Warning(ex, "[FirstRun] Could not fill the language list"); }
            finally { _populatingLanguage = false; }
        }

        /// <summary>The OS display language, read once. Tests pin it so a run on a non-English box
        /// does not switch the whole test process's language.</summary>
        internal static Func<CultureInfo> SystemUiCulture { get; set; } = () => CultureInfo.InstalledUICulture;

        /// <summary>
        /// WPF DetectSystemLanguage (FirstRunWizard.xaml.cs:726-748): a fresh box starts in the OS
        /// display language when the app ships it. Runs once, from the constructor, before the
        /// static text is applied: the setting is still at its "en" default on a first run, so
        /// nothing a person chose can be overwritten. Exact match first (pt-BR, zh-CN), then the
        /// two-letter language, then an xx- prefix, then leave English alone. Never throws.
        ///
        /// <para>Skipped with no settings service (a render or a headless view check), which is
        /// <see cref="ShouldRunAndClaim"/>'s own "is there settings to claim against" test: those
        /// runs must not switch the process language. The owner is not known yet here (it is set
        /// after construction), so the static writer is used and <see cref="Run"/> re-syncs the
        /// shell's selectors.</para>
        /// </summary>
        private static void DetectSystemLanguage()
        {
            try
            {
                if (CoreSettings.Service == null) return;
                var current = CoreSettings.Current.Language;
                if (current != null && current != "en") return;

                var ui = SystemUiCulture();
                var codes = LocalizationManager.AvailableLanguages.Select(l => l.Code).ToArray();
                var match = codes.FirstOrDefault(c => string.Equals(c, ui.Name, StringComparison.OrdinalIgnoreCase))
                         ?? codes.FirstOrDefault(c => string.Equals(c, ui.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                         ?? codes.FirstOrDefault(c => c.StartsWith(ui.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase));
                if (match == null || match == "en") return;

                MainShellWindow.SetApplicationLanguage(match);
                Log.Information("[FirstRun] Language defaulted to {Code} from the OS display language {Culture}", match, ui.Name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Could not read the OS display language");
            }
        }

        private void CmbWizardLanguage_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_populatingLanguage) return;
            if (_cmbWizardLanguage.SelectedItem is not ComboBoxItem item) return;

            try
            {
                // The one writer of the saved language, either through the shell (which also
                // re-selects its own two surfaces and raises the restart banner) or, when there is
                // no owner, through the same static core it calls.
                if (ShellOwner != null) ShellOwner.ApplyLanguageSelection(item.Tag as string);
                else MainShellWindow.SetApplicationLanguage(item.Tag as string);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Language switch failed");
            }

            // This window's copy is assigned in code-behind, so it does not follow the live
            // LocalizationManager change on its own.
            ApplyStaticText();
            ApplyStepChrome();
        }

        /// <summary>
        /// The only motion on this screen, and it is gated, exactly as WPF has it: at MotionLevel
        /// Off the page simply swaps. No loop, so there is nothing for the motion kill-switch to
        /// stop.
        ///
        /// <para><c>MotionFx</c> itself is WPF Storyboard code and is NOT coming to Core, but its
        /// DECISION is <c>CoreSettings.Current.MotionLevel</c>, which this head already reads
        /// through <see cref="AmbientFxCanvas.Env"/> - so the gate is the real one.
        /// <c>MotionFx.AllowTransitions</c> is <c>Level != Off</c>.</para>
        ///
        /// <para>The animation ends at 1 and stays there (<c>FillMode.Forward</c>), and a throw
        /// lands on the swap rather than leaving a step parked at Opacity 0 - the wizard is the
        /// first thing a new install sees, so an invisible page here is unrecoverable.</para>
        /// </summary>
        private void FadeInCurrentStep()
        {
            var host = _step == 1 ? (Control)_step1 : _step2;

            if (AmbientFxCanvas.Env.Level == MotionLevel.Off)
            {
                host.Opacity = 1;
                return;
            }

            try
            {
                host.Opacity = 0;
                _ = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(140),
                    Easing = new QuadraticEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1d) } },
                    },
                }.RunAsync(host);
            }
            catch (Exception ex)
            {
                Log.Debug("[FirstRun] Step fade failed, swapping instead: {Error}", ex.Message);
                host.Opacity = 1;
            }
        }

        // ------------------------------------------------------------------ step 2: mod pick

        private void BuildModCards()
        {
            var installedBadge = Loc.Get("modpicker_installed_badge");
            var activeModId = CoreMods.ActiveModId;

            foreach (var entry in ModPickerCatalog.All)
            {
                IBrush accent;
                try { accent = new SolidColorBrush(Color.Parse(entry.AccentHex)); }
                catch { accent = Brushes.HotPink; }

                var note = entry.PremiumProgramNote ? Loc.Get("modpicker_note_premium_programs") : "";
                if (entry.NoVoiceNote)
                {
                    var voice = Loc.Get("modpicker_note_no_voice");
                    note = string.IsNullOrEmpty(note) ? voice : note + "  " + voice;
                }

                var card = new FirstRunModCard
                {
                    ModId = entry.ModId,
                    PackId = entry.PackId,
                    Name = Loc.Get(entry.NameLocKey),
                    Description = Loc.Get(entry.DescriptionLocKey),
                    Art = Helpers.ModArt.TryLoadBuiltIn(entry.ArtName),
                    AccentBrush = accent,
                    Note = note,
                    InstalledText = installedBadge
                };

                if (!card.HasPack)
                {
                    // CCP Default ships in the box - always a legitimate choice, never a download.
                    card.State = FirstRunModCard.CardState.Installed;
                }
                else
                {
                    card.SizeText = ModPickerCatalog.FormatSize(ModPickerCatalog.SizeBytesFor(entry));
                    if (ModPickerCatalog.IsInstalled(entry.PackId)) card.State = FirstRunModCard.CardState.Installed;
                }

                _cards.Add(card);
            }

            // Pre-select what this install is already running (CCP Default on a fresh box), so the
            // screen reads as "here is what you have, here is what you can have" and pressing Next
            // without touching anything is a no-op rather than an accidental switch.
            Select(_cards.FirstOrDefault(c => string.Equals(c.ModId, activeModId, StringComparison.OrdinalIgnoreCase))
                   ?? _cards.FirstOrDefault());
        }

        internal void Select(FirstRunModCard? card)
        {
            _selected = card;
            foreach (var c in _cards) c.IsSelected = ReferenceEquals(c, card);
            UpdateFlavourButton();
        }

        private void ModCard_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            if ((e.Source as Control)?.DataContext is FirstRunModCard card) Select(card);
        }

        /// <summary>The shell that owns the one switching path (WPF's constructor argument); set by <see cref="Run"/>.</summary>
        internal MainShellWindow? ShellOwner { get; set; }

        private bool _modStepPrepared;
        private bool _modStepOffline;
        private bool _offlineOfferCounted;
        private string? _committedModId;

        /// <summary>
        /// PORTED from WPF FirstRunWizard.xaml.cs:913-969: the picker's one-shot bookkeeping, applied
        /// the moment this step is first shown. Reuses <see cref="Dialogs.ModPickerDialog.ShouldDeferForOffline"/>
        /// rather than restating it.
        /// </summary>
        private void PrepareModStep()
        {
            if (_modStepPrepared) return;
            _modStepPrepared = true;

            try
            {
                var svc = App.ReleaseContent;
                if (!CoreSettings.HasProvider || svc == null)
                {
                    // No pack service this session: the cards still render, nothing to download.
                    SetModStepOffline(countOffer: false);
                    return;
                }
                var settings = CoreSettings.Current;

                if (svc.IsFullInstall)
                {
                    foreach (var card in _cards) card.MarkInstalled();
                    _txtModHint.Text = Str("fr8_modpick_installed_hint",
                        "Every mod is already on disk in this build - pick one and it switches straight away.");
                    return;
                }

                if (settings.ModPickerShown) return;   // already offered: picking still works

                if (Dialogs.ModPickerDialog.ShouldDeferForOffline(settings.OfflineMode, svc.ManifestUnavailable,
                                                                  settings.ModPickerOfflineOffers))
                {
                    Log.Information("[FirstRun] {Reason} - the flavour step opens read-only",
                        settings.OfflineMode ? "Offline mode is on" : "Manifest unreachable this session");
                    SetModStepOffline(countOffer: true);
                    return;
                }

                // Spend the offer BEFORE anything can go wrong on this page.
                settings.ModPickerShown = true;
                CoreSettings.Save();
                _ = RefreshManifestAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Flavour step preparation failed - falling back to the offline copy");
                SetModStepOffline(countOffer: false);
            }
        }

        private async Task RefreshManifestAsync()
        {
            try
            {
                var svc = App.ReleaseContent;
                if (svc == null) { SetModStepOffline(countOffer: false); return; }

                var packed = ModPickerCatalog.All.Where(en => en.PackId != null).ToList();
                if (packed.Any(en => svc.GetPackInfo(en.PackId!) == null)
                    && await svc.FetchManifestAsync().ConfigureAwait(true) == null)
                {
                    SetModStepOffline(countOffer: true);
                    return;
                }

                foreach (var entry in packed)
                {
                    var card = FindCard(entry.PackId);
                    if (card == null || card.IsInstalled) continue;
                    card.SizeText = ModPickerCatalog.FormatSize(ModPickerCatalog.SizeBytesFor(entry));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Manifest refresh failed - falling back to the offline copy");
                SetModStepOffline(countOffer: true);
            }
        }

        /// <summary>WPF SetModStepOffline: read-only step; an offline showing latches the offer
        /// (FirstRunGate.ModPickerShownAfterOfflineFlavourStep is constant true) and is counted once.</summary>
        private void SetModStepOffline(bool countOffer)
        {
            _modStepOffline = true;
            try { _txtModHint.Text = Loc.Get("modpicker_hint_offline"); } catch { }

            if (!countOffer || _offlineOfferCounted || !CoreSettings.HasProvider) return;
            _offlineOfferCounted = true;
            try
            {
                var settings = CoreSettings.Current;
                settings.ModPickerOfflineOffers++;
                settings.ModPickerShown = true;
                CoreSettings.Save();
                Log.Information("[FirstRun] Flavour step ended offline (offer {Count}, diagnostics only) - latched; " +
                                "the Mod Manager owns downloads from here", settings.ModPickerOfflineOffers);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Could not record the offline flavour offer");
            }
        }

        /// <summary>
        /// PORTED from WPF FirstRunWizard.xaml.cs:1048-1103: content on disk goes straight through
        /// <see cref="MainShellWindow.ActivateChosenMod"/>; content that must be fetched records the
        /// intent (<see cref="PendingModChoice"/>) and starts the download, so the switch happens the
        /// moment the pack lands - even after this window is gone.
        /// </summary>
        private void CommitModChoice()
        {
            try
            {
                var card = _selected;
                var chosen = card?.ModId;
                if (card == null || string.IsNullOrWhiteSpace(chosen)) return;
                if (string.Equals(_committedModId, chosen, StringComparison.OrdinalIgnoreCase)) return;
                _committedModId = chosen;

                if (string.Equals(chosen, App.Mods?.ActiveModId, StringComparison.OrdinalIgnoreCase)) return;

                if (PendingModChoice.IsContentAvailable(chosen, App.ReleaseContent))
                {
                    ShellOwner?.ActivateChosenMod(chosen, MainShellWindow.ModChoiceTrigger.Immediate);
                    return;
                }

                if (_modStepOffline || !card.HasPack) return;

                PendingModChoice.Record(chosen, App.Mods?.ActiveModId);
                ChosenPackDownload = DownloadChosenPackAsync(card);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Could not apply the chosen mod");
            }
        }

        /// <summary>The download task, for tests to await.</summary>
        internal Task? ChosenPackDownload { get; private set; }

        private async Task DownloadChosenPackAsync(FirstRunModCard card)
        {
            var svc = App.ReleaseContent;
            if (svc == null || string.IsNullOrEmpty(card.PackId)) return;

            card.MarkQueued();
            var progress = new Progress<double>(p => card.MarkProgress(p));
            var ok = false;
            try
            {
                // CancellationToken.None on purpose: closing this window must not kill the download.
                ok = await svc.RequestPackAsync(card.PackId!, progress, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Pack {Pack} download threw", card.PackId);
            }

            if (ok) card.MarkInstalled();
            else card.MarkFailed();
        }

        // ------------------------------------------------------------------ pack events

        private void OnPackInstalled(object? sender, string packId) =>
            Dispatcher.UIThread.Post(() => FindCard(packId)?.MarkInstalled());

        private FirstRunModCard? FindCard(string? packId) =>
            string.IsNullOrEmpty(packId)
                ? null
                : _cards.FirstOrDefault(c => string.Equals(c.PackId, packId, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ chrome

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            try { BeginMoveDrag(e); } catch { /* as WPF's DragMove, throws if the button already went up */ }
        }

        private void BtnNext_Click(object? sender, RoutedEventArgs e)
        {
            if (!_btnPickFolder.IsEnabled) return;   // folder picker still open: finish after it answers
            if (_step == 1)
            {
                // Belt and braces, as WPF: the button is disabled until the box is ticked, but the
                // gate is the one thing on this screen that must not be reachable by accident.
                if (_chkAgeConfirm.IsChecked != true) return;

                RecordAgeAcceptance();
                ShowStep(2);
                return;
            }

            CommitModChoice();
            CloseSafely();
        }

        /// <summary>
        /// WPF BtnSkip_Click (FirstRunWizard.xaml.cs:1165-1171), "Keep the default": puts the
        /// selection back on whatever this install is already running before closing, so a card
        /// the user clicked and then changed their mind about is not committed on the way out.
        /// </summary>
        private void BtnSkip_Click(object? sender, RoutedEventArgs e)
        {
            var activeModId = CoreMods.ActiveModId;
            Select(_cards.FirstOrDefault(c => string.Equals(c.ModId, activeModId, StringComparison.OrdinalIgnoreCase))
                   ?? _cards.FirstOrDefault());
            CloseSafely();
        }

        private async void BtnPickFolder_Click(object? sender, RoutedEventArgs e)
        {
            // Opened here, owned by this window: WPF deferred it (FirstRunWizard.xaml.cs:1175) only
            // to dodge a modal-on-modal Win32 folder browser, and the user saw nothing happen
            // (docs/avalonia-decisions.html). Same guard, write and follow-ups as the shell's picker.
            _btnPickFolder.IsEnabled = false;
            if (await MainShellWindow.PickAssetsFolder(this) is { } chosen)
            {
                _pickedFolder = chosen;
                _txtPickFolder.Text = chosen;
            }
            _btnPickFolder.IsEnabled = true;
        }

        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (!_btnPickFolder.IsEnabled) { e.Handled = true; return; }   // same: don't close under the picker
            e.Handled = true;
            CloseSafely();
        }

        private void CloseSafely()
        {
            try { Close(); } catch { }
        }

        private bool _closed;

        private void OnWizardClosed(object? sender, EventArgs e)
        {
            // Detached first and unconditionally: a static event holding a closed window is a leak
            // whatever the rest of this handler decides to do.
            CoreReleaseContent.PackInstalled -= OnPackInstalled;

            if (_closed) return;
            _closed = true;
            if (!AgeAccepted) return;   // declined: Run hands the first run back and shuts down

            // A choice made but never entered (Esc, the X on the flavour step) still counts - the
            // user ticked a mod, and honouring it is what the picker's own contract promises.
            CommitModChoice();
        }
    }
}

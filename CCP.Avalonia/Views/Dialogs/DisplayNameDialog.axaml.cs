using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Dialogs/DisplayNameDialog.xaml.cs. Deviations:
    ///  - DialogResult becomes Close(bool).
    ///  - Delete mode addresses the warning label/text and prompt by x:Name instead of walking
    ///    Children[n]; the ordering it relied on is unchanged in the markup.
    ///  - The MessageBox in Accept() guarded a path the UI already blocks (Confirm disabled and
    ///    Enter ignored outside 2-20 characters), so it is a plain return here.
    /// The parameterless constructor is the real "welcome" mode and doubles as the render ctor.
    ///
    /// <para>Callers: the Trainer Card's rename and delete buttons (MainShellWindow.Browser.cs
    /// ChangeDisplayNameAsync / DeleteProfileAsync, through CoreAccount), and the welcome mode's
    /// pending registration (MainShellWindow.PendingRegistration.cs, WPF MainWindow CheckPendingRegistrationAsync
    /// -> AccountService.HandlePostAuthAsync -> PromptForRegistrationAsync over Core LegacyAccountApi).</para>
    /// </summary>
    public partial class DisplayNameDialog : Window
    {
        private static readonly IBrush DeleteRed = Brush.Parse("#FF4444");

        private readonly TextBox _txtDisplayName;
        private readonly Button _btnConfirm;
        private readonly TextBlock _txtCharCount;
        private bool _isDeleteMode;
        private Func<string, Task<string?>>? _save;
        private bool _closed;
        private bool _saving;

        public DisplayNameDialog(string? suggestion, Func<string, Task<string?>> save) : this()
        {
            _save = save;
            Height = 300; // ponytail: room for the wrapped required-name copy and server refusal.
            this.FindControl<TextBlock>("TxtPrompt")!.TextWrapping = TextWrapping.Wrap;
            Closed += (_, _) => _closed = true;
            this.FindControl<TextBlock>("TxtTitle")!.Text = Loc.Get("label_choose_your_display_name");
            this.FindControl<TextBlock>("TxtPrompt")!.Text = Loc.Get("name_ask_body");
            this.FindControl<Border>("WarningPanel")!.IsVisible = false;
            this.FindControl<Button>("BtnCancel")!.IsVisible = false;
            this.FindControl<Button>("BtnClose")!.IsVisible = true;
            _txtDisplayName.Text = suggestion ?? "";
            _txtDisplayName.SelectAll();
            OnTextChanged();
        }

        private async Task SaveRequiredAsync(string name)
        {
            _saving = true;
            _btnConfirm.IsEnabled = false;
            _txtDisplayName.IsEnabled = false;
            var status = this.FindControl<TextBlock>("TxtStatus")!;
            status.Text = Loc.Get("login_checking");
            status.IsVisible = true;
            string? error;
            try { error = await _save!(name); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Display name save failed"); error = Loc.Get("msg_failed_to_change_display_name"); }
            if (_closed) return;
            _saving = false;
            if (error == null) { DisplayName = name; Close(true); return; }
            status.Text = error;
            _txtDisplayName.IsEnabled = true;
            OnTextChanged();
            _txtDisplayName.Focus();
        }

        public string DisplayName { get; private set; } = "";

        public DisplayNameDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _txtDisplayName = this.FindControl<TextBox>("TxtDisplayName")!;
            _btnConfirm = this.FindControl<Button>("BtnConfirm")!;
            _txtCharCount = this.FindControl<TextBlock>("TxtCharCount")!;

            _txtDisplayName.TextChanged += (_, _) => OnTextChanged();
            _txtDisplayName.KeyDown += TxtDisplayName_KeyDown;
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            _btnConfirm.Click += (_, _) => Accept();

            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close(false);
            Loaded += (_, _) => _txtDisplayName.Focus();
        }

        public DisplayNameDialog(bool isChangeName, string? currentName) : this()
        {
            if (isChangeName)
            {
                this.FindControl<TextBlock>("TxtTitle")!.Text = Loc.Get("label_change_your_display_name");
                this.FindControl<Border>("WarningPanel")!.IsVisible = false;

                if (!string.IsNullOrEmpty(currentName))
                {
                    _txtDisplayName.Text = currentName;
                    _txtDisplayName.SelectAll();
                }
            }
        }

        public DisplayNameDialog(string confirmationMode) : this()
        {
            if (confirmationMode == "delete")
            {
                _isDeleteMode = true;
                var title = this.FindControl<TextBlock>("TxtTitle")!;
                title.Text = Loc.Get("label_delete_your_profile");
                title.Foreground = DeleteRed;

                // Red-tinted warning
                this.FindControl<Border>("WarningPanel")!.IsVisible = true;
                var warningLabel = this.FindControl<TextBlock>("TxtWarningLabel")!;
                var warningText = this.FindControl<TextBlock>("TxtWarningText")!;
                warningLabel.Text = Loc.Get("label_warning_2");
                warningLabel.Foreground = DeleteRed;
                warningText.Text = Loc.Get("label_this_will_permanently_delete_all_your_data_an");
                warningText.Foreground = DeleteRed;

                // Update prompt and button
                this.FindControl<TextBlock>("TxtPrompt")!.Text = Loc.Get("label_type_delete_to_confirm");
                this.FindControl<TextBlock>("TxtConfirm")!.Text = Loc.Get("btn_delete");
                _btnConfirm.Background = DeleteRed;

                _txtDisplayName.MaxLength = 6;
                _txtDisplayName.Text = "";
                _txtCharCount.IsVisible = false;
            }
        }

        private void OnTextChanged()
        {
            var text = (_txtDisplayName.Text ?? "").Trim();
            if (_save != null)
            {
                _btnConfirm.IsEnabled = !_saving && NeedsDisplayNameRule.Check(text) == DisplayNameShape.Ok;
                _txtCharCount.Text = Loc.GetF("label_char_count_of_max", text.Length, 20);
            }
            else if (_isDeleteMode)
            {
                _btnConfirm.IsEnabled = text == "DELETE";
            }
            else
            {
                var length = text.Length;
                _txtCharCount.Text = Loc.GetF("label_char_count_of_max", length, 20);
                _btnConfirm.IsEnabled = length >= 2 && length <= 20;
            }
        }

        private void TxtDisplayName_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _btnConfirm.IsEnabled)
                Accept();
            else if (e.Key == Key.Escape)
                Close(false);
        }

        private void Accept()
        {
            var name = (_txtDisplayName.Text ?? "").Trim();

            if (_isDeleteMode)
            {
                if (name != "DELETE") return;
                DisplayName = name;
                Close(true);
                return;
            }

            if (_save != null)
            {
                if (!_saving && NeedsDisplayNameRule.Check(name) == DisplayNameShape.Ok) _ = SaveRequiredAsync(name);
                return;
            }

            if (name.Length < 2 || name.Length > 20)
                return;

            DisplayName = name;
            Close(true);
        }
    }
}

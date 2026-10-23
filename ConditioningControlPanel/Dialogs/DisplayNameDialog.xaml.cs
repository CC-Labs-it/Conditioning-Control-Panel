using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    public partial class DisplayNameDialog : Window
    {
        public string DisplayName { get; private set; } = "";

        public DisplayNameDialog()
        {
            InitializeComponent();
            SetupTextChanged();

            Loaded += (s, e) =>
            {
                TxtDisplayName.Focus();
            };
        }

        public DisplayNameDialog(bool isChangeName, string? currentName) : this()
        {
            if (isChangeName)
            {
                TxtTitle.Text = Loc.Get("label_change_your_display_name");
                WarningPanel.Visibility = Visibility.Collapsed;

                if (!string.IsNullOrEmpty(currentName))
                {
                    TxtDisplayName.Text = currentName;
                    TxtDisplayName.SelectAll();
                }
            }
        }

        private bool _isDeleteMode;

        // Required mode (onboarding, owner call 2026-10-20): the account has no name yet. No
        // Cancel button; the corner X and Escape just close it and the next launch asks again.
        // The dialog saves for itself so a taken name keeps it open with the reason on screen.
        private Func<string, Task<string?>>? _save;
        private bool _closed;

        /// <param name="save">Sends the name; returns null when saved, else the line to show.</param>
        public DisplayNameDialog(string? suggestion, Func<string, Task<string?>> save) : this()
        {
            _save = save;
            Closed += (_, _) => _closed = true;
            TxtTitle.Text = Loc.Get("label_choose_your_display_name");
            TxtPrompt.Text = Loc.Get("name_ask_body");
            WarningPanel.Visibility = Visibility.Collapsed;
            BtnCancel.Visibility = Visibility.Collapsed;
            BtnClose.Visibility = Visibility.Visible;
            TxtDisplayName.Text = suggestion ?? "";
            TxtDisplayName.SelectAll();
        }

        public DisplayNameDialog(string confirmationMode) : this()
        {
            if (confirmationMode == "delete")
            {
                _isDeleteMode = true;
                TxtTitle.Text = Loc.Get("label_delete_your_profile");
                TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4444"));

                // Red-tinted warning
                WarningPanel.Visibility = Visibility.Visible;
                var warningStack = (System.Windows.Controls.StackPanel)WarningPanel.Child;
                var warningLabel = (System.Windows.Controls.TextBlock)warningStack.Children[0];
                var warningText = (System.Windows.Controls.TextBlock)warningStack.Children[1];
                warningLabel.Text = Loc.Get("label_warning_2");
                warningLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4444"));
                warningText.Text = Loc.Get("label_this_will_permanently_delete_all_your_data_an");
                warningText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4444"));

                // Update prompt and button
                var border = (System.Windows.Controls.Border)Content;
                var grid = (System.Windows.Controls.Grid)border.Child;
                var promptBlock = (System.Windows.Controls.TextBlock)grid.Children[2];
                promptBlock.Text = Loc.Get("label_type_delete_to_confirm");
                BtnConfirm.Content = Loc.Get("btn_delete");
                BtnConfirm.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4444"));

                TxtDisplayName.MaxLength = 6;
                TxtDisplayName.Text = "";
                TxtCharCount.Visibility = Visibility.Collapsed;
            }
        }

        private void SetupTextChanged()
        {
            TxtDisplayName.TextChanged += (s, e) =>
            {
                if (_isDeleteMode)
                {
                    BtnConfirm.IsEnabled = TxtDisplayName.Text.Trim() == "DELETE";
                }
                else if (_save != null)
                {
                    var shape = NeedsDisplayNameRule.Check(TxtDisplayName.Text);
                    TxtCharCount.Text = Loc.GetF("label_char_count_of_max", TxtDisplayName.Text.Trim().Length, NeedsDisplayNameRule.MaxLength);
                    BtnConfirm.IsEnabled = shape == DisplayNameShape.Ok;
                    ShowStatus(shape == DisplayNameShape.BadChars ? Loc.Get("name_ask_bad_chars") : null);
                }
                else
                {
                    var length = TxtDisplayName.Text.Trim().Length;
                    TxtCharCount.Text = Loc.GetF("label_char_count_of_max", length, 20);
                    BtnConfirm.IsEnabled = length >= 2 && length <= 20;
                }
            };
        }

        private void ShowStatus(string? line)
        {
            TxtStatus.Text = line ?? "";
            TxtStatus.Visibility = string.IsNullOrEmpty(line) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnClose_Click(object sender, MouseButtonEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void SaveRequired(string name)
        {
            BtnConfirm.IsEnabled = false;
            TxtDisplayName.IsEnabled = false;
            ShowStatus(Loc.Get("login_checking"));
            string? error;
            try { error = await _save!(name); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Display name save failed");
                error = Loc.Get("msg_failed_to_change_display_name");
            }
            if (_closed) return;
            if (error == null)
            {
                DisplayName = name;
                DialogResult = true;
                Close();
                return;
            }
            TxtDisplayName.IsEnabled = true;
            BtnConfirm.IsEnabled = NeedsDisplayNameRule.Check(TxtDisplayName.Text) == DisplayNameShape.Ok;
            ShowStatus(error);
            TxtDisplayName.Focus();
        }

        private void TxtDisplayName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && BtnConfirm.IsEnabled)
            {
                Accept();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        private void Accept()
        {
            var name = TxtDisplayName.Text.Trim();

            if (_isDeleteMode)
            {
                if (name != "DELETE") return;
                DisplayName = name;
                DialogResult = true;
                Close();
                return;
            }

            if (_save != null)
            {
                if (NeedsDisplayNameRule.Check(name) == DisplayNameShape.Ok) SaveRequired(name);
                return;
            }

            if (name.Length < 2 || name.Length > 20)
            {
                MessageBox.Show(
                    Loc.Get("msg_enter_name_2_20_chars"),
                    Loc.Get("title_invalid_name"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DisplayName = name;
            DialogResult = true;
            Close();
        }
    }
}

using System.IO;
using Avalonia.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

internal enum MediaDropChoice { Cancel, Play, Edit, Library }

/// <summary>WPF MainWindow.Presets.cs:822-925. Closing without a choice cancels.</summary>
public partial class MediaDropDialog : Window
{
    public MediaDropDialog(string path)
    {
        InitializeComponent();
        Title = Loc.Get("dlg_media_drop_title");
        TxtFileName.Text = Path.GetFileName(path);
        BtnPlay.Click += (_, _) => Close(MediaDropChoice.Play);
        BtnEdit.Click += (_, _) => Close(MediaDropChoice.Edit);
        BtnOk.Click += (_, _) => Close(MediaDropChoice.Library);
        BtnCancel.Click += (_, _) => Close(MediaDropChoice.Cancel);
    }
}

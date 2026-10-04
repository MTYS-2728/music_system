using System.Windows;

namespace TingGeRiZhi.App.Dialogs;

public partial class NotesDialog : Window
{
    public string? Result { get; private set; }

    public NotesDialog()
    {
        InitializeComponent();
        NotesBox.TextChanged += (_, _) => CounterText.Text = $"{NotesBox.Text.Length} 字";
    }

    /// <summary>弹出备注编辑框；取消返回 null。</summary>
    public static string? Show(Window? owner, string songDisplay, string initial)
    {
        var dialog = new NotesDialog { Owner = owner is { IsLoaded: true } ? owner : null };
        dialog.SongText.Text = songDisplay;
        dialog.NotesBox.Text = initial ?? "";
        dialog.NotesBox.CaretIndex = dialog.NotesBox.Text.Length;
        dialog.NotesBox.Focus();
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Header_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = NotesBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

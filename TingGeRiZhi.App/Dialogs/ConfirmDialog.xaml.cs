using System.Windows;
using System.Windows.Media;

namespace TingGeRiZhi.App.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
        // 危险操作默认聚焦「取消」，避免误按回车直接删数据。
        Loaded += (_, _) =>
        {
            if (OkButton.IsDefault && _danger) CancelButton.Focus();
            else OkButton.Focus();
        };
    }

    private bool _danger;

    /// <summary>统一样式的确认框；<paramref name="danger"/> 为真时确认按钮显示为危险色。</summary>
    public static bool Show(Window? owner, string title, string message, string okText = "确定", bool danger = false)
    {
        var dialog = new ConfirmDialog { Owner = owner is { IsLoaded: true } ? owner : null };
        dialog._danger = danger;
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.OkButton.Content = okText;
        if (danger)
        {
            dialog.IconBox.Background = (Brush)dialog.FindResource("B.DangerSoft");
            dialog.IconText.Foreground = (Brush)dialog.FindResource("B.Danger");
            dialog.OkButton.Style = (Style)dialog.FindResource("BtnPrimary");
            dialog.OkButton.Background = (Brush)dialog.FindResource("B.Danger");
            dialog.OkButton.BorderBrush = (Brush)dialog.FindResource("B.Danger");
        }
        else
        {
            dialog.IconBox.Background = (Brush)dialog.FindResource("B.AccentSoft");
            dialog.IconText.Foreground = (Brush)dialog.FindResource("B.AccentSoftText");
        }
        return dialog.ShowDialog() == true;
    }

    private void Header_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

using System.Windows;
using TingGeRiZhi.Core;

namespace TingGeRiZhi.App.Dialogs;

public partial class SongEditDialog : Window
{
    public string Title2 { get; private set; } = "";
    public string Artist2 { get; private set; } = "";
    public string Album2 { get; private set; } = "";

    public SongEditDialog()
    {
        InitializeComponent();
        // 打开即聚焦歌曲名，用户不必先点一下输入框。
        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    public static bool Show(Window? owner, Song song, out string title, out string artist, out string album)
    {
        var dialog = new SongEditDialog { Owner = owner is { IsLoaded: true } ? owner : null };
        dialog.TitleBox.Text = song.Title;
        dialog.ArtistBox.Text = song.Artist;
        dialog.AlbumBox.Text = song.Album;
        var ok = dialog.ShowDialog() == true;
        title = dialog.Title2;
        artist = dialog.Artist2;
        album = dialog.Album2;
        return ok;
    }

    private void Header_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            ErrorText.Text = "歌曲名不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            TitleBox.Focus();
            return;
        }
        Title2 = TitleBox.Text.Trim();
        Artist2 = ArtistBox.Text.Trim();
        Album2 = AlbumBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

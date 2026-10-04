using System.Windows;
using System.Windows.Controls;

namespace TingGeRiZhi.App.Dialogs;

public partial class BatchAddDialog : Window
{
    public IReadOnlyList<(string Title, string Artist, string Album)> Entries { get; private set; } =
        Array.Empty<(string, string, string)>();

    public bool RecordNow => RecordNowCheck.IsChecked == true;

    public BatchAddDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => InputBox.Focus();
    }

    /// <summary>弹出批量添加窗口；取消返回 null。</summary>
    public static (IReadOnlyList<(string Title, string Artist, string Album)> Entries, bool RecordNow)? Show(Window? owner)
    {
        var dialog = new BatchAddDialog { Owner = owner is { IsLoaded: true } ? owner : null };
        return dialog.ShowDialog() == true ? (dialog.Entries, dialog.RecordNow) : null;
    }

    /// <summary>解析一行文本：优先 "|" 分隔，其次 " - " 分隔。</summary>
    public static (string Title, string Artist, string Album)? ParseLine(string line)
    {
        var text = line.Trim();
        if (text.Length == 0) return null;

        string[] parts;
        if (text.Contains('|'))
        {
            parts = text.Split('|').Select(p => p.Trim()).ToArray();
        }
        else if (text.Contains(" - "))
        {
            var index = text.IndexOf(" - ", StringComparison.Ordinal);
            parts = new[] { text[..index].Trim(), text[(index + 3)..].Trim() };
        }
        else if (text.Contains('\t'))
        {
            parts = text.Split('\t').Select(p => p.Trim()).ToArray();
        }
        else
        {
            parts = new[] { text };
        }

        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return null;
        var title = parts[0].Trim();
        if (title.Length == 0) return null;
        var artist = parts.Length > 1 ? parts[1].Trim() : "";
        var album = parts.Length > 2 ? parts[2].Trim() : "";
        return (title, artist, album);
    }

    private static List<(string Title, string Artist, string Album)> ParseAll(string text)
    {
        var result = new List<(string, string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
        {
            var parsed = ParseLine(line);
            if (parsed is null) continue;
            var key = $"{parsed.Value.Title}\u001f{parsed.Value.Artist}\u001f{parsed.Value.Album}";
            if (!seen.Add(key)) continue;   // 同一批里重复的行只保留一次
            result.Add(parsed.Value);
        }
        return result;
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var parsed = ParseAll(InputBox.Text);
        PreviewText.Text = parsed.Count == 0 ? "还没有可添加的歌曲" : $"将添加 {parsed.Count} 首";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Header_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var parsed = ParseAll(InputBox.Text);
        if (parsed.Count == 0)
        {
            ErrorText.Text = "至少填写一首歌曲。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Entries = parsed;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

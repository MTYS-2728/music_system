using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TingGeRiZhi.App.Dialogs;
using TingGeRiZhi.App.Services;
using TingGeRiZhi.App.ViewModels;

namespace TingGeRiZhi.App.Views;

public partial class SettingsView : UserControl
{
    private bool _ready;

    public SettingsView()
    {
        InitializeComponent();

        ThresholdBox.ItemsSource = new List<ComboOption<int>>
        {
            new("10 秒", 10), new("15 秒", 15), new("20 秒", 20), new("30 秒（推荐）", 30),
            new("45 秒", 45), new("60 秒", 60), new("90 秒", 90), new("120 秒", 120)
        };
        ThresholdBox.SelectedValuePath = "Value";

        PollBox.ItemsSource = new List<ComboOption<int>>
        {
            new("每 2 秒", 2), new("每 3 秒", 3), new("每 5 秒（推荐）", 5), new("每 10 秒", 10), new("每 15 秒", 15)
        };
        PollBox.SelectedValuePath = "Value";

        PageSizeBox.ItemsSource = new List<ComboOption<int>>
        {
            new("15 首", 15), new("25 首", 25), new("50 首", 50), new("100 首", 100)
        };
        PageSizeBox.SelectedValuePath = "Value";

        VersionText.Text = $"版本 {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"} · .NET {Environment.Version.ToString(2)} · Windows 媒体会话 (GSMTC)";
        _ready = true;
        Refresh();
    }

    /// <summary>重新从设置与数据库读取界面状态。</summary>
    public void Refresh()
    {
        var settings = AppServices.Settings;
        _ready = false;

        AutoRecordSwitch.IsChecked = settings.AutoRecord;
        CoverSwitch.IsChecked = settings.CaptureCoverArt;
        AllowedSwitch.IsChecked = settings.OnlyRecordMusicApps;
        ConfirmSwitch.IsChecked = settings.ConfirmBeforeDelete;
        TraySwitch.IsChecked = settings.MinimizeToTray;
        StartMinSwitch.IsChecked = settings.StartMinimized;
        StartupSwitch.IsChecked = StartupManager.IsEnabled();

        UpdateMusicOnlyHint();

        ThresholdBox.SelectedValue = settings.RecordThresholdSeconds;
        if (ThresholdBox.SelectedIndex < 0) ThresholdBox.SelectedIndex = 3;
        PollBox.SelectedValue = settings.PollSeconds;
        if (PollBox.SelectedIndex < 0) PollBox.SelectedIndex = 2;
        PageSizeBox.SelectedValue = settings.PageSize;
        if (PageSizeBox.SelectedIndex < 0) PageSizeBox.SelectedIndex = 0;

        ThemeLight.IsChecked = settings.Theme != "Dark";
        ThemeDark.IsChecked = settings.Theme == "Dark";

        DbPathText.Text = AppServices.Database.DatabasePath;
        DbInfoText.Text = BuildDatabaseInfo();
        StatusHintText.Text = "";
        _ready = true;

        _ = LoadSessionsAsync();
    }

    private void UpdateMusicOnlyHint()
    {
        MusicOnlyHint.Text = AllowedSwitch.IsChecked == true
            ? $"已开启：只记录来自 {Core.SourceApps.RecordableNames}的媒体会话；浏览器、B 站等其它应用一律跳过。"
            : "已关闭：任何应用的媒体会话都会被记录（浏览器、B 站等也会记进来）。";
    }

    /// <summary>列出当前所有媒体会话，方便确认过滤是否生效。</summary>
    private async Task LoadSessionsAsync()
    {
        try
        {
            var sessions = await new Core.MediaSessionReader().ListSessionsAsync();
            SessionList.ItemsSource = sessions;
            SessionEmptyText.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception)
        {
            SessionList.ItemsSource = null;
            SessionEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void RefreshSessions_Click(object sender, RoutedEventArgs e) => _ = LoadSessionsAsync();

    private static string BuildDatabaseInfo()
    {
        try
        {
            var path = AppServices.Database.DatabasePath;
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;
            var overview = AppServices.Database.GetOverview();
            var coverCount = Directory.Exists(AppServices.CoverCache.Directory)
                ? Directory.GetFiles(AppServices.CoverCache.Directory).Length
                : 0;
            return $"{FormatSize(size)} · {overview.Kpi.TotalSongs} 首歌 · {overview.Kpi.TotalPlays} 条播放记录 · {coverCount} 张缓存封面";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "无法读取数据库信息";
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.##} MB"
    };

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var settings = AppServices.Settings;

        settings.AutoRecord = AutoRecordSwitch.IsChecked == true;
        settings.CaptureCoverArt = CoverSwitch.IsChecked == true;
        settings.OnlyRecordMusicApps = AllowedSwitch.IsChecked == true;
        settings.ConfirmBeforeDelete = ConfirmSwitch.IsChecked == true;
        settings.MinimizeToTray = TraySwitch.IsChecked == true;
        settings.StartMinimized = StartMinSwitch.IsChecked == true;
        if (ThresholdBox.SelectedValue is int threshold) settings.RecordThresholdSeconds = threshold;
        if (PollBox.SelectedValue is int poll) settings.PollSeconds = poll;
        if (PageSizeBox.SelectedValue is int pageSize) settings.PageSize = pageSize;

        UpdateMusicOnlyHint();
        AppServices.Tracker.SetAutoRecording(settings.AutoRecord);
        AppServices.SaveSettings();
        DbInfoText.Text = BuildDatabaseInfo();
        _ = LoadSessionsAsync();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var dark = ReferenceEquals(sender, ThemeDark);
        AppServices.Settings.Theme = dark ? "Dark" : "Light";
        AppServices.SaveSettings();
        ThemeLight.IsChecked = !dark;
        ThemeDark.IsChecked = dark;
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        StartupManager.SetEnabled(StartupSwitch.IsChecked == true);
        StartupSwitch.IsChecked = StartupManager.IsEnabled();
        AppServices.ReportStatus(StartupSwitch.IsChecked == true ? "已开启开机自动启动" : "已关闭开机自动启动");
    }

    // ------------------------------------------------------------------ 数据

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(AppServices.Database.DatabasePath);
            AppServices.ReportStatus("数据库路径已复制到剪贴板");
        }
        catch (Exception)
        {
            AppServices.ReportStatus("复制失败，请手动选择路径");
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = AppServices.Database.DatabaseDirectory;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppServices.ReportStatus($"打开目录失败：{ex.Message}");
        }
    }

    private void ExportAllCsv_Click(object sender, RoutedEventArgs e)
    {
        var rows = AppServices.Database.QueryRecords(new Core.RecordQuery());
        if (rows.Count == 0)
        {
            AppServices.ReportStatus("还没有可导出的记录");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = $"聆迹-全部-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog() != true) return;
        AppServices.Database.ExportCsv(dialog.FileName, rows);
        AppServices.ReportStatus($"已导出 {rows.Count} 条记录");
    }

    private void ExportAllJson_Click(object sender, RoutedEventArgs e)
    {
        var rows = AppServices.Database.QueryRecords(new Core.RecordQuery());
        if (rows.Count == 0)
        {
            AppServices.ReportStatus("还没有可导出的记录");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件|*.json",
            FileName = $"聆迹-全部-{DateTime.Now:yyyyMMdd-HHmm}.json"
        };
        if (dialog.ShowDialog() != true) return;
        AppServices.Database.ExportJson(dialog.FileName, rows);
        AppServices.ReportStatus($"已导出 {rows.Count} 条记录");
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV 或 JSON|*.csv;*.json|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var result = dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? AppServices.Database.ImportJson(dialog.FileName)
                : AppServices.Database.ImportCsv(dialog.FileName);
            AppServices.ReportStatus($"导入完成：成功 {result.Imported} 条，跳过 {result.Skipped} 条");
            AppServices.NotifyRecordsChanged();
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppServices.ReportStatus($"导入失败：{ex.Message}");
        }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SQLite 数据库|*.db",
            FileName = $"聆迹备份-{DateTime.Now:yyyyMMdd-HHmmss}.db"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            AppServices.Database.BackupTo(dialog.FileName);
            AppServices.ReportStatus($"备份完成：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            AppServices.ReportStatus($"备份失败：{ex.Message}");
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "SQLite 数据库|*.db|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        if (!ConfirmDialog.Show(Window.GetWindow(this), "恢复数据库",
                "恢复会覆盖当前的本地数据库。当前数据会先备份为 tingge.db.before-restore。确定继续吗？",
                "覆盖并恢复", danger: true)) return;
        try
        {
            AppServices.Database.RestoreFrom(dialog.FileName);
            AppServices.ReportStatus("恢复完成，界面数据已重新载入");
            AppServices.NotifyRecordsChanged();
            Refresh();
        }
        catch (Exception ex)
        {
            AppServices.ReportStatus($"恢复失败：{ex.Message}");
        }
    }

    private void CleanupCovers_Click(object sender, RoutedEventArgs e)
    {
        var used = AppServices.Database.QuerySongs(new Core.RecordQuery()).Select(s => s.CoverPath);
        var removed = AppServices.CoverCache.Cleanup(used);
        AppServices.ReportStatus(removed == 0 ? "没有需要清理的封面文件" : $"已清理 {removed} 个未使用的封面文件");
        Refresh();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (!ConfirmDialog.Show(owner, "清空播放记录",
                "将删除全部播放记录。歌曲资料、备注与收藏会保留。此操作不可撤销。", "继续", danger: true)) return;

        var alsoSongs = ConfirmDialog.Show(owner, "同时删除歌曲资料？",
            "选择“删除全部”会连同歌曲资料、备注、收藏和封面一起删除；选择“取消”则只清空播放记录。",
            "删除全部", danger: true);

        var removed = AppServices.Database.DeleteAll(alsoSongs);
        AppServices.ReportStatus($"已清空 {removed} 条播放记录" + (alsoSongs ? "，并删除全部歌曲资料" : "，歌曲资料已保留"));
        AppServices.NotifyRecordsChanged();
        Refresh();
    }
}

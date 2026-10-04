using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using TingGeRiZhi.App.Dialogs;
using TingGeRiZhi.App.ViewModels;
using TingGeRiZhi.Core;

namespace TingGeRiZhi.App.Views;

public partial class RecordsView : UserControl
{
    private enum RangeMode { All, Today, Last7, Last30, ThisMonth, Custom }

    private readonly ObservableCollection<RecordRow> _page = new();
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(280) };

    private IReadOnlyList<RecordRow> _all = Array.Empty<RecordRow>();
    private IReadOnlyList<PlayLogRow> _rawRows = Array.Empty<PlayLogRow>();
    private RangeMode _range = RangeMode.All;
    private int _pageIndex;
    private int _pageSize = 15;
    private bool _ready;

    public RecordsView()
    {
        InitializeComponent();
        RecordsGrid.ItemsSource = _page;

        SortBox.ItemsSource = new List<ComboOption<RecordSort>>
        {
            new("播放时间（新→旧）", RecordSort.StartedDescending),
            new("播放时间（旧→新）", RecordSort.StartedAscending),
            new("按歌曲名", RecordSort.Title),
            new("按歌手", RecordSort.Artist),
            new("按播放次数", RecordSort.PlayCount),
            new("按时长", RecordSort.Duration)
        };
        SortBox.SelectedValuePath = "Value";

        PageSizeBox.ItemsSource = new List<ComboOption<int>>
        {
            new("每页 15 首", 15),
            new("每页 25 首", 25),
            new("每页 50 首", 50),
            new("每页 100 首", 100)
        };
        PageSizeBox.SelectedValuePath = "Value";

        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            Refresh();
        };

        var settings = AppServices.Settings;
        _pageSize = settings.PageSize;
        PageSizeBox.SelectedValue = settings.PageSize;
        if (PageSizeBox.SelectedIndex < 0) PageSizeBox.SelectedIndex = 0;

        if (Enum.TryParse<RecordSort>(settings.SortMode, true, out var sort)) SortBox.SelectedValue = sort;
        if (SortBox.SelectedIndex < 0) SortBox.SelectedIndex = 0;

        _range = RangeMode.All;
        _ready = true;
        SyncRangeButtons();
    }

    /// <summary>页面激活 / 数据变化时重新查询。</summary>
    public void Refresh(bool keepPage = false)
    {
        if (!_ready) return;
        var watch = Stopwatch.StartNew();

        var (from, to) = ResolveRange();
        var query = new RecordQuery(
            string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
            from, to,
            FavoriteFilter.IsChecked == true,
            null, null,
            SortBox.SelectedValue is RecordSort sort ? sort : RecordSort.StartedDescending);

        _rawRows = AppServices.Database.QueryRecords(query);
        _all = _rawRows
            .GroupBy(row => $"{row.Title}\u001f{row.Artist}\u001f{row.Album}")
            .Select(group => new RecordRow(group))
            .ToList();

        if (!keepPage) _pageIndex = 0;
        RenderPage();
        watch.Stop();

        TotalBadge.Text = _rawRows.Count.ToString();
        SummaryText.Text = _rawRows.Count == 0
            ? "没有匹配的记录"
            : $"共 {_rawRows.Count} 条播放记录 · 合并为 {_all.Count} 首歌 · {watch.ElapsedMilliseconds} ms";
        ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderPage()
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_all.Count / (double)_pageSize));
        _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);

        _page.Clear();
        foreach (var row in _all.Skip(_pageIndex * _pageSize).Take(_pageSize)) _page.Add(row);

        PageText.Text = $"{_pageIndex + 1} / {pageCount}";
        PageSummary.Text = _all.Count == 0 ? "" : $"显示第 {_pageIndex * _pageSize + 1}–{_pageIndex * _pageSize + _page.Count} 首，共 {_all.Count} 首";
        FirstPageButton.IsEnabled = PrevPageButton.IsEnabled = _pageIndex > 0;
        NextPageButton.IsEnabled = LastPageButton.IsEnabled = _pageIndex < pageCount - 1;

        var empty = _all.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        RecordsGrid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (!empty) return;

        var hasFilter = !string.IsNullOrWhiteSpace(SearchBox.Text) || FavoriteFilter.IsChecked == true || _range != RangeMode.All;
        EmptyTitle.Text = hasFilter ? "没有匹配的记录" : "还没有听歌记录";
        EmptyHint.Text = hasFilter
            ? "换个关键词或时间范围试试，或者清除筛选查看全部记录。"
            : "播放一首歌超过设定时长就会自动记录，也可以点「记录当前歌曲」或「手动添加」。";
        EmptyClearButton.Visibility = hasFilter ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ 筛选

    private (DateTimeOffset? From, DateTimeOffset? To) ResolveRange()
    {
        var today = DateTime.Today;
        DateTime? start = null;
        DateTime? endExclusive = null;

        switch (_range)
        {
            case RangeMode.Today:
                start = today;
                endExclusive = today.AddDays(1);
                break;
            case RangeMode.Last7:
                start = today.AddDays(-6);
                endExclusive = today.AddDays(1);
                break;
            case RangeMode.Last30:
                start = today.AddDays(-29);
                endExclusive = today.AddDays(1);
                break;
            case RangeMode.ThisMonth:
                start = new DateTime(today.Year, today.Month, 1);
                endExclusive = start.Value.AddMonths(1);
                break;
            case RangeMode.Custom:
                if (FromDate.SelectedDate is DateTime from) start = from.Date;
                if (ToDate.SelectedDate is DateTime to) endExclusive = to.Date.AddDays(1);
                break;
        }

        return (ToUtc(start), ToUtc(endExclusive));
    }

    private static DateTimeOffset? ToUtc(DateTime? local) =>
        local is null ? null : new DateTimeOffset(DateTime.SpecifyKind(local.Value, DateTimeKind.Local)).ToUniversalTime();

    private void SyncRangeButtons()
    {
        RangeAll.IsChecked = _range == RangeMode.All;
        RangeToday.IsChecked = _range == RangeMode.Today;
        Range7.IsChecked = _range == RangeMode.Last7;
        Range30.IsChecked = _range == RangeMode.Last30;
        RangeMonth.IsChecked = _range == RangeMode.ThisMonth;
        RangeCustom.IsChecked = _range == RangeMode.Custom;
        CustomRangePanel.Visibility = _range == RangeMode.Custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Range_Click(object sender, RoutedEventArgs e)
    {
        _range = sender switch
        {
            _ when ReferenceEquals(sender, RangeToday) => RangeMode.Today,
            _ when ReferenceEquals(sender, Range7) => RangeMode.Last7,
            _ when ReferenceEquals(sender, Range30) => RangeMode.Last30,
            _ when ReferenceEquals(sender, RangeMonth) => RangeMode.ThisMonth,
            _ when ReferenceEquals(sender, RangeCustom) => RangeMode.Custom,
            _ => RangeMode.All
        };
        SyncRangeButtons();
        Refresh();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (ReferenceEquals(sender, SearchBox))
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
            return;
        }
        Refresh();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        Refresh();
        SearchBox.Focus();
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (SortBox.SelectedValue is RecordSort sort)
        {
            AppServices.Settings.SortMode = sort.ToString();
            AppServices.SaveSettings();
        }
        Refresh();
    }

    private void PageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (PageSizeBox.SelectedValue is int size)
        {
            _pageSize = size;
            AppServices.Settings.PageSize = size;
            AppServices.SaveSettings();
        }
        Refresh();
    }

    private void FirstPage_Click(object sender, RoutedEventArgs e) { _pageIndex = 0; RenderPage(); }
    private void PrevPage_Click(object sender, RoutedEventArgs e) { _pageIndex--; RenderPage(); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { _pageIndex++; RenderPage(); }
    private void LastPage_Click(object sender, RoutedEventArgs e) { _pageIndex = int.MaxValue; RenderPage(); }

    // ------------------------------------------------------------------ 行操作

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RecordRow row) return;
        row.IsFavorite = !row.IsFavorite;
        AppServices.Database.SetFavorite(row.SongId, row.IsFavorite);
        AppServices.ReportStatus(row.IsFavorite ? $"已收藏：{row.Title}" : $"已取消收藏：{row.Title}");
    }

    private void Notes_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RecordRow row) return;
        var dialog = new NotesDialog { Owner = Window.GetWindow(this) };
        dialog.SongText.Text = row.ArtistAlbum.Length > 0 ? $"{row.Title} — {row.ArtistAlbum}" : row.Title;
        dialog.NotesBox.Text = row.Notes;
        dialog.NotesBox.CaretIndex = dialog.NotesBox.Text.Length;
        dialog.NotesBox.Focus();
        if (dialog.ShowDialog() != true) return;

        row.Notes = dialog.Result ?? "";
        AppServices.Database.UpdateNotes(row.SongId, row.Notes);
        AppServices.ReportStatus($"备注已保存：{row.Title}");
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RecordRow row) return;
        var song = AppServices.Database.GetSong(row.SongId);
        if (song is null) return;
        if (!SongEditDialog.Show(Window.GetWindow(this), song, out var title, out var artist, out var album)) return;
        try
        {
            AppServices.Database.UpdateSongInfo(row.SongId, title, artist, album);
            AppServices.ReportStatus($"已更新歌曲信息：{title}");
            Refresh(keepPage: true);
        }
        catch (ArgumentException ex)
        {
            AppServices.ReportStatus(ex.Message);
        }
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RecordRow row) return;
        if (AppServices.Settings.ConfirmBeforeDelete &&
            !ConfirmDialog.Show(Window.GetWindow(this), "删除播放记录",
                $"将删除《{row.Title}》的全部 {row.RecordIds.Count} 条播放记录，歌曲资料与备注会保留。此操作不可撤销。",
                "删除", danger: true)) return;

        AppServices.Database.DeleteRecords(row.RecordIds);
        AppServices.ReportStatus($"已删除《{row.Title}》的 {row.RecordIds.Count} 条记录");
        AppServices.NotifyRecordsChanged();
        Refresh(keepPage: true);
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = RecordsGrid.SelectedItems.OfType<RecordRow>().ToList();
        if (selected.Count == 0)
        {
            AppServices.ReportStatus("请先在列表中选择要删除的记录");
            return;
        }

        var recordIds = selected.SelectMany(r => r.RecordIds).Distinct().ToArray();
        if (AppServices.Settings.ConfirmBeforeDelete &&
            !ConfirmDialog.Show(Window.GetWindow(this), "批量删除",
                $"将删除选中的 {selected.Count} 首歌对应的 {recordIds.Length} 条播放记录。此操作不可撤销。",
                "全部删除", danger: true)) return;

        AppServices.Database.DeleteRecords(recordIds);
        AppServices.ReportStatus($"已删除 {recordIds.Length} 条播放记录");
        AppServices.NotifyRecordsChanged();
        Refresh(keepPage: true);
    }

    private void RecordsGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RecordsGrid.SelectedItem is not RecordRow row) return;
        var song = AppServices.Database.GetSong(row.SongId);
        if (song is null) return;
        if (!SongEditDialog.Show(Window.GetWindow(this), song, out var title, out var artist, out var album)) return;
        AppServices.Database.UpdateSongInfo(row.SongId, title, artist, album);
        AppServices.ReportStatus($"已更新歌曲信息：{title}");
        Refresh(keepPage: true);
    }

    private void RecordsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DeleteSelectedButton.IsEnabled = RecordsGrid.SelectedItems.Count > 0;

    // ------------------------------------------------------------------ 新增

    private async void RecordNow_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.Tracker.TickAsync();
        if (AppServices.Tracker.TryManualRecord(out var message))
        {
            AppServices.ReportStatus(message);
            AppServices.NotifyRecordsChanged();
            Refresh();
        }
        else
        {
            AppServices.ReportStatus(message);
        }
    }

    private void ManualAdd_Click(object sender, RoutedEventArgs e)
    {
        var song = new Song();
        if (!SongEditDialog.Show(Window.GetWindow(this), song, out var title, out var artist, out var album)) return;
        AppServices.Tracker.RecordManualSong(title, artist, album);
        AppServices.ReportStatus($"已手动添加：{title}");
        AppServices.NotifyRecordsChanged();
        Refresh();
    }

    private void BatchAdd_Click(object sender, RoutedEventArgs e)
    {
        var result = BatchAddDialog.Show(Window.GetWindow(this));
        if (result is null) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in result.Value.Entries)
        {
            var id = AppServices.Database.UpsertManualSong(entry.Title, entry.Artist, entry.Album);
            if (result.Value.RecordNow) AppServices.Database.AddRecord(id, now, null, isManual: true);
        }
        AppServices.ReportStatus($"已添加 {result.Value.Entries.Count} 首歌曲" + (result.Value.RecordNow ? "（含播放记录）" : ""));
        AppServices.NotifyRecordsChanged();
        Refresh();
    }

    // ------------------------------------------------------------------ 导入导出

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_rawRows.Count == 0)
        {
            AppServices.ReportStatus("当前筛选结果为空，没有可导出的记录");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = $"聆迹-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            InitialDirectory = AppServices.Settings.LastDirectory ?? ""
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            AppServices.Database.ExportCsv(dialog.FileName, _rawRows);
            RememberDirectory(dialog.FileName);
            AppServices.ReportStatus($"已导出 {_rawRows.Count} 条记录到 CSV");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppServices.ReportStatus($"导出失败：{ex.Message}");
        }
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        if (_rawRows.Count == 0)
        {
            AppServices.ReportStatus("当前筛选结果为空，没有可导出的记录");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件|*.json",
            FileName = $"聆迹-{DateTime.Now:yyyyMMdd-HHmm}.json",
            InitialDirectory = AppServices.Settings.LastDirectory ?? ""
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            AppServices.Database.ExportJson(dialog.FileName, _rawRows);
            RememberDirectory(dialog.FileName);
            AppServices.ReportStatus($"已导出 {_rawRows.Count} 条记录到 JSON");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppServices.ReportStatus($"导出失败：{ex.Message}");
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV 或 JSON|*.csv;*.json|CSV 文件|*.csv|JSON 文件|*.json",
            InitialDirectory = AppServices.Settings.LastDirectory ?? ""
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var result = dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? AppServices.Database.ImportJson(dialog.FileName)
                : AppServices.Database.ImportCsv(dialog.FileName);
            RememberDirectory(dialog.FileName);

            var summary = $"导入完成：成功 {result.Imported} 条，跳过 {result.Skipped} 条";
            AppServices.ReportStatus(result.Warnings.Count > 0 ? $"{summary}（{result.Warnings.Count} 条提示）" : summary);
            if (result.Warnings.Count > 0)
            {
                ConfirmDialog.Show(Window.GetWindow(this), "导入完成",
                    $"{summary}\n\n前几条提示：\n· " + string.Join("\n· ", result.Warnings.Take(8)), "知道了");
            }
            AppServices.NotifyRecordsChanged();
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppServices.ReportStatus($"导入失败：{ex.Message}");
        }
    }

    private static void RememberDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory)) return;
        AppServices.Settings.LastDirectory = directory;
        AppServices.SaveSettings();
    }

    // ------------------------------------------------------------------ 对外

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>设置页改了每页条数 / 排序后同步到本页。</summary>
    public void SyncSettings()
    {
        var settings = AppServices.Settings;
        _ready = false;
        PageSizeBox.SelectedValue = settings.PageSize;
        if (PageSizeBox.SelectedIndex < 0) PageSizeBox.SelectedIndex = 0;
        if (Enum.TryParse<RecordSort>(settings.SortMode, true, out var sort)) SortBox.SelectedValue = sort;
        if (SortBox.SelectedIndex < 0) SortBox.SelectedIndex = 0;
        _pageSize = PageSizeBox.SelectedValue is int size ? size : 15;
        _ready = true;
        if (IsVisible) Refresh();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e) => ClearFilters();

    public void DeleteSelectedRows() => DeleteSelected_Click(this, new RoutedEventArgs());

    public void ClearFilters()
    {
        SearchBox.Clear();
        FavoriteFilter.IsChecked = false;
        _range = RangeMode.All;
        FromDate.SelectedDate = null;
        ToDate.SelectedDate = null;
        SyncRangeButtons();
        Refresh();
    }
}

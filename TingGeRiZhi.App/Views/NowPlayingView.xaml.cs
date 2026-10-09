using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TingGeRiZhi.App.Dialogs;
using TingGeRiZhi.App.ViewModels;
using TingGeRiZhi.Core;

namespace TingGeRiZhi.App.Views;

public partial class NowPlayingView : UserControl
{
    private readonly ObservableCollection<RecordRow> _recent = new();
    private readonly DispatcherTimer _equalizer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Random _random = new();

    private MediaSnapshot? _snapshot;
    private bool _isPlaying;
    private bool _autoRecording = true;
    private double _continuousSeconds;
    private double _untilRecord;
    private string? _renderedKey;
    private string? _renderedCoverPath;

    /// <summary>请求主窗口切换到「听歌记录」页。</summary>
    public event Action? RecordsRequested;

    public NowPlayingView()
    {
        InitializeComponent();
        RecentList.ItemsSource = _recent;
        _equalizer.Tick += (_, _) => AnimateEqualizer();
        Loaded += (_, _) => PageScroll.ScrollToTop();
    }

    /// <summary>页面被激活时把滚动位置回到顶部。</summary>
    public void ScrollToTop() => PageScroll.ScrollToTop();

    /// <summary>页面被激活时调用。</summary>
    public void Refresh()
    {
        LoadRecent();
        LoadSummary();
        Render();
    }

    public void UpdateSnapshot(MediaSnapshot snapshot)
    {
        _snapshot = snapshot;
        Render();
    }

    public void UpdateProgress(double continuousSeconds, double untilRecord, bool autoRecording, string? coverPath)
    {
        _continuousSeconds = continuousSeconds;
        _untilRecord = untilRecord;
        _autoRecording = autoRecording;

        if (coverPath is not null && _snapshot is not null && _snapshot.CoverPath != coverPath)
        {
            _snapshot = _snapshot with { CoverPath = coverPath };
            RenderCover(true);
        }
        RenderCountdown();
    }

    // ------------------------------------------------------------------ 渲染

    private void Render()
    {
        RenderCountdown();

        MediaSnapshot? snapshot = _snapshot;
        if (snapshot is null || !snapshot.IsAvailable)
        {
            RenderUnavailable(snapshot);
            return;
        }

        TitleText.Text = snapshot.Title;
        ArtistText.Text = string.IsNullOrWhiteSpace(snapshot.Artist) ? "未知歌手" : snapshot.Artist;
        AlbumText.Text = string.IsNullOrWhiteSpace(snapshot.Album) ? "" : $"专辑：{snapshot.Album}";

        StateText.Text = snapshot.StateLabel;
        _isPlaying = snapshot.State == PlaybackState.Playing;
        StateDot.Fill = (System.Windows.Media.Brush)FindResource(_isPlaying ? "B.StatusOnline"
            : snapshot.State == PlaybackState.Paused ? "B.StatusPaused" : "B.StatusIdle");

        if (_isPlaying)
        {
            EqualizerBox.Visibility = Visibility.Visible;
            if (!_equalizer.IsEnabled) _equalizer.Start();
        }
        else
        {
            EqualizerBox.Visibility = Visibility.Collapsed;
            _equalizer.Stop();
        }

        if (snapshot.Duration is { TotalSeconds: > 0 } duration && snapshot.Position is { } position)
        {
            Progress.Value = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
            PositionText.Text = Format(position);
            DurationText.Text = Format(duration);
        }
        else
        {
            Progress.Value = 0;
            PositionText.Text = snapshot.Position is { } p ? Format(p) : "--:--";
            DurationText.Text = snapshot.Duration is { } d ? Format(d) : "系统未提供时长";
        }

        // 封面与收藏状态只在换歌或封面变化时重算，避免每次轮询都查库。
        if (snapshot.StableKey != _renderedKey || snapshot.CoverPath != _renderedCoverPath)
        {
            _renderedKey = snapshot.StableKey;
            _renderedCoverPath = snapshot.CoverPath;
            RenderCover(true);
        }

        SetActionEnabled(true);
        SetAutoLabel();
    }

    /// <summary>
    /// 没有可记录的会话时的展示。如果快照带着标题，说明是"正在播放但被规则跳过"的会话
    /// （例如"只记录音乐软件"时正在放 B 站），这时照常显示曲目并说明不会被记录。
    /// </summary>
    private void RenderUnavailable(MediaSnapshot? snapshot)
    {
        _isPlaying = false;
        _renderedKey = null;
        _renderedCoverPath = null;
        EqualizerBox.Visibility = Visibility.Collapsed;
        _equalizer.Stop();

        Progress.Value = 0;
        PositionText.Text = "--:--";
        DurationText.Text = "--:--";
        CoverImage.Source = null;
        CoverImage.Visibility = Visibility.Collapsed;
        FavoriteButton.Content = "收藏";
        SetActionEnabled(false);
        SetAutoLabel();
        AlbumText.Text = snapshot?.Error ?? "";

        if (!string.IsNullOrWhiteSpace(snapshot?.Title))
        {
            StateText.Text = "已跳过";
            StateDot.Fill = (System.Windows.Media.Brush)FindResource("B.StatusPaused");
            TitleText.Text = snapshot!.Title;
            ArtistText.Text = string.IsNullOrWhiteSpace(snapshot.Artist) ? "不在记录范围内" : snapshot.Artist;
            SetActionEnabled(true);
            return;
        }

        StateText.Text = "等待媒体会话";
        StateDot.Fill = (System.Windows.Media.Brush)FindResource("B.StatusIdle");
        TitleText.Text = "尚未读取到正在播放的歌曲";
        ArtistText.Text = AppServices.Settings.OnlyRecordMusicApps
            ? $"当前只记录 {SourceApps.RecordableNames}，请在其中播放；也可以手动记录"
            : $"在 {SourceApps.RecordableNames} 里播放，或使用右侧按钮手动记录";
    }

    /// <summary>卡片上的自动记录状态标签，顺带提示"仅记录音乐软件"。</summary>
    private void SetAutoLabel()
    {
        var musicOnly = AppServices.Settings.OnlyRecordMusicApps;
        AutoText.Text = !_autoRecording ? "自动记录已暂停"
            : musicOnly ? "仅记录音乐软件"
            : "自动记录中";
        AutoText.Foreground = (System.Windows.Media.Brush)FindResource(
            _autoRecording ? "B.AccentSoftText" : "B.Warning");
    }

    private void RenderCover(bool refreshFavorite)
    {
        var path = _snapshot?.CoverPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            CoverImage.Source = new Converters.CoverImageConverter()
                .Convert(path, typeof(object), "288", System.Globalization.CultureInfo.InvariantCulture)
                as System.Windows.Media.ImageSource;
            CoverImage.Visibility = Visibility.Visible;
        }
        else
        {
            CoverImage.Source = null;
            CoverImage.Visibility = Visibility.Collapsed;
        }

        if (refreshFavorite) FavoriteButton.Content = IsCurrentFavorite() ? "已收藏" : "收藏";
    }

    private void RenderCountdown()
    {
        if (_snapshot is not { IsAvailable: true })
        {
            CountdownText.Text = "";
            return;
        }

        if (!_autoRecording)
        {
            CountdownText.Text = "自动记录已暂停，仍可点击「记录当前歌曲」。";
            return;
        }

        CountdownText.Text = _isPlaying
            ? _untilRecord <= 0.5
                ? $"本次连续播放 {Math.Round(_continuousSeconds)} 秒，已满足自动记录条件。"
                : $"本次连续播放 {Math.Round(_continuousSeconds)} 秒，再听 {Math.Ceiling(_untilRecord)} 秒自动记录。"
            : $"本次连续播放 {Math.Round(_continuousSeconds)} 秒（暂停期间不计时）。";
    }

    private void AnimateEqualizer()
    {
        if (!IsVisible || !_isPlaying)
        {
            _equalizer.Stop();
            return;
        }
        EqBar1.Height = 7 + _random.Next(0, 24);
        EqBar2.Height = 7 + _random.Next(0, 24);
        EqBar3.Height = 7 + _random.Next(0, 24);
        EqBar4.Height = 7 + _random.Next(0, 24);
    }

    private void SetActionEnabled(bool enabled)
    {
        RecordNowButton.IsEnabled = enabled;
        FavoriteButton.IsEnabled = enabled;
        NotesButton.IsEnabled = enabled;
        EditButton.IsEnabled = enabled;
    }

    private static string Format(TimeSpan span) =>
        span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");

    // ------------------------------------------------------------------ 数据

    private void LoadRecent()
    {
        _recent.Clear();
        var rows = AppServices.Database.QueryRecords(new RecordQuery());
        foreach (var group in rows.GroupBy(r => $"{r.Title}\u001f{r.Artist}\u001f{r.Album}").Take(6))
            _recent.Add(new RecordRow(group));
        RecentEmpty.Visibility = _recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadSummary()
    {
        var kpi = AppServices.Database.GetOverview().Kpi;

        TodayCount.Text = kpi.PlaysToday.ToString();
        TodayCaption.Text = kpi.PlaysToday == 0
            ? "还没有今天的记录"
            : $"占累计 {(kpi.TotalPlays == 0 ? 0 : Math.Round(kpi.PlaysToday * 100.0 / kpi.TotalPlays))}%";
        WeekCount.Text = kpi.PlaysLast7Days.ToString();
        WeekCaption.Text = kpi.TopArtist.Length > 0 ? $"最爱 {kpi.TopArtist}" : "还没有数据";
        SongCount.Text = kpi.TotalSongs.ToString();
        SongCaption.Text = kpi.FavoriteSongs > 0 ? $"其中 {kpi.FavoriteSongs} 首已收藏" : "首歌曲";
        TotalTime.Text = FormatTotal(kpi.TotalSeconds);
        TotalCaption.Text = $"共 {kpi.TotalPlays} 次播放";
    }

    private static string FormatTotal(long seconds)
    {
        if (seconds <= 0) return "0 分钟";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours} 小时 {span.Minutes} 分"
            : $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} 分钟";
    }

    private bool IsCurrentFavorite()
    {
        if (_snapshot is not { IsAvailable: true } snapshot) return false;
        var id = AppServices.Database.FindSongId(snapshot.Title, snapshot.Artist, snapshot.Album);
        return id is not null && AppServices.Database.GetSong(id.Value)?.IsFavorite == true;
    }

    /// <summary>取当前歌曲在库中的 id；尚未入库时创建歌曲资料（不写播放记录）。</summary>
    private long ResolveCurrentSongId()
    {
        var snapshot = _snapshot!;
        return AppServices.Database.UpsertSong(snapshot, snapshot.CoverPath);
    }

    // ------------------------------------------------------------------ 事件

    private async void RecordNow_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.Tracker.TickAsync();
        if (AppServices.Tracker.TryManualRecord(out var message))
        {
            AppServices.NotifyRecordsChanged();
            Refresh();
        }
        AppServices.ReportStatus(message);
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not { IsAvailable: true }) return;
        var id = ResolveCurrentSongId();
        var next = !IsCurrentFavorite();
        AppServices.Database.SetFavorite(id, next);
        AppServices.NotifyRecordsChanged();
        RenderCover(true);
        AppServices.ReportStatus(next ? "已加入收藏" : "已取消收藏");
    }

    private void Notes_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not { IsAvailable: true } snapshot) return;
        var id = ResolveCurrentSongId();
        var song = AppServices.Database.GetSong(id);
        var result = NotesDialog.Show(Window.GetWindow(this), snapshot.DisplayName, song?.Notes ?? "");
        if (result is null) return;
        AppServices.Database.UpdateNotes(id, result);
        AppServices.NotifyRecordsChanged();
        Refresh();
        AppServices.ReportStatus("备注已保存");
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not { IsAvailable: true }) return;
        var id = ResolveCurrentSongId();
        var song = AppServices.Database.GetSong(id);
        if (song is null) return;
        if (!SongEditDialog.Show(Window.GetWindow(this), song, out var title, out var artist, out var album)) return;
        try
        {
            AppServices.Database.UpdateSongInfo(id, title, artist, album);
            AppServices.NotifyRecordsChanged();
            Refresh();
            AppServices.ReportStatus("歌曲信息已更新");
        }
        catch (ArgumentException ex)
        {
            AppServices.ReportStatus(ex.Message);
        }
    }

    private void GoRecords_Click(object sender, RoutedEventArgs e) => RecordsRequested?.Invoke();
}

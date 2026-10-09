namespace TingGeRiZhi.Core;

/// <summary>播放状态，来自 Windows 媒体会话。</summary>
public enum PlaybackState
{
    Unknown,
    Playing,
    Paused,
    Stopped,
    Closed
}

/// <summary>一次媒体会话读取的结果。</summary>
public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string Album,
    PlaybackState State,
    TimeSpan? Position,
    TimeSpan? Duration,
    string SourceApp,
    DateTimeOffset ReadAtUtc,
    bool IsAvailable,
    string? Error = null,
    string? CoverPath = null)
{
    /// <summary>用于判断"是否换了一首歌"的稳定键（不含封面，避免封面变化被当成换歌）。</summary>
    public string StableKey => $"{SourceApp}|{Title}|{Artist}|{Album}";

    public string DisplayName => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Title} - {Artist}";

    public string StateLabel => State switch
    {
        PlaybackState.Playing => "播放中",
        PlaybackState.Paused => "已暂停",
        PlaybackState.Stopped => "已停止",
        PlaybackState.Closed => "已关闭",
        _ => "未知"
    };
}

/// <summary>一个媒体会话的概要信息（用于"只记录音乐软件"的筛选与排查）。</summary>
public sealed record MediaSessionSummary(
    string SourceApp,
    string Title,
    string Artist,
    PlaybackState State,
    bool IsCurrent)
{
    public string FriendlySource => SourceApps.FriendlyName(SourceApp);
    public MusicApp? App => SourceApps.Match(SourceApp);
    public bool IsRecordable => App is not null;
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Title} - {Artist}";
    public string KindLabel => App?.Name ?? "其它应用";
    public string StateLabel => State switch
    {
        PlaybackState.Playing => "播放中",
        PlaybackState.Paused => "已暂停",
        PlaybackState.Stopped => "已停止",
        PlaybackState.Closed => "已关闭",
        _ => "未知"
    };
}

/// <summary>歌曲资料。</summary>
public sealed class Song
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string? CoverPath { get; set; }
    public string Notes { get; set; } = "";
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>一条播放记录。</summary>
public sealed class PlayRecord
{
    public long Id { get; set; }
    public long SongId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public int? DurationSeconds { get; set; }
    public bool IsManual { get; set; }
    public Song? Song { get; set; }
}

/// <summary>播放记录的一行（已 JOIN 歌曲资料）。</summary>
public sealed record PlayLogRow(
    long RecordId,
    long SongId,
    string Title,
    string Artist,
    string Album,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset RecordedAtUtc,
    int? DurationSeconds,
    bool IsManual,
    string Notes,
    bool IsFavorite,
    string? CoverPath,
    int SongPlayCount);

/// <summary>歌曲维度的一行（用于"歌曲库"与收藏页）。</summary>
public sealed record SongRow(
    long SongId,
    string Title,
    string Artist,
    string Album,
    string? CoverPath,
    string Notes,
    bool IsFavorite,
    int PlayCount,
    int TotalSeconds,
    DateTimeOffset? LastPlayedAtUtc);

/// <summary>列表排序方式。</summary>
public enum RecordSort
{
    StartedDescending,
    StartedAscending,
    Title,
    Artist,
    PlayCount,
    Duration
}

/// <summary>列表查询条件。</summary>
public sealed record RecordQuery(
    string? Search = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    bool FavoritesOnly = false,
    string? Artist = null,
    string? Album = null,
    RecordSort Sort = RecordSort.StartedDescending);

/// <summary>统计图表的一个数据点或一个分类。</summary>
public sealed record StatSlice(string Label, int Count, double Value = 0, string? Detail = null);

/// <summary>统计页顶部指标。</summary>
public sealed record StatsKpi(
    int TotalPlays,
    int TotalSongs,
    int TotalArtists,
    int TotalAlbums,
    int PlaysToday,
    int PlaysLast7Days,
    int PlaysThisMonth,
    int FavoriteSongs,
    long TotalSeconds,
    string TopArtist,
    string TopSong);

/// <summary>数据统计总览。</summary>
public sealed record StatsOverview(
    StatsKpi Kpi,
    IReadOnlyList<StatSlice> TopSongs,
    IReadOnlyList<StatSlice> TopArtists,
    IReadOnlyList<StatSlice> TopAlbums,
    IReadOnlyList<StatSlice> HourDistribution,
    IReadOnlyList<StatSlice> WeekdayDistribution,
    IReadOnlyList<StatSlice> DailyTrend)
{
    public static StatsOverview Empty { get; } = new(
        new StatsKpi(0, 0, 0, 0, 0, 0, 0, 0, 0, "", ""),
        Array.Empty<StatSlice>(), Array.Empty<StatSlice>(), Array.Empty<StatSlice>(),
        Array.Empty<StatSlice>(), Array.Empty<StatSlice>(), Array.Empty<StatSlice>());
}

/// <summary>导入结果。</summary>
public sealed record ImportResult(int Imported, int Skipped, IReadOnlyList<string> Warnings)
{
    public static ImportResult Empty { get; } = new(0, 0, Array.Empty<string>());
}

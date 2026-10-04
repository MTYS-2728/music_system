using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TingGeRiZhi.Core;

/// <summary>
/// 统计聚合：一次 SQL 取回"播放记录 + 歌曲资料"，其余工作（时区换算、分组、排序）全部在 C# 中完成。
/// 注意：SQLite 不认识 IANA/Windows 时区，所以绝不在 SQL 里做时间换算。
/// </summary>
public sealed partial class LogDatabase
{
    /// <summary>统计聚合用的一行原始数据（播放记录 JOIN 歌曲资料；is_favorite 单独查询，故不在此列）。</summary>
    private readonly record struct StatsPlayRow(
        DateTimeOffset StartedAtUtc,
        int DurationSeconds,
        string Title,
        string Artist,
        string Album,
        long SongId);

    /// <summary>图表最多展示的条目数。</summary>
    private const int TopSliceCount = 10;

    /// <summary>每日趋势默认展示的天数（含今天）。</summary>
    private const int DailyTrendDays = 30;

    /// <summary>聚合统计总览。<paramref name="timeZone"/> 为 null 时使用 TimeZoneInfo.Local。</summary>
    public StatsOverview GetOverview(TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? TimeZoneInfo.Local;
        var rows = LoadRows();

        // "今天"取本地日历日：用 tz 把当前时刻换算到目标时区，避免 Local 与自定义时区混用。
        var today = LocalDate(DateTimeOffset.UtcNow, tz);

        // 一次遍历同时累计 KPI 与小时/星期分布。
        var totalSeconds = 0L;
        var playsToday = 0;
        var playsLast7Days = 0;
        var playsThisMonth = 0;
        var hours = new int[24];
        var weekdays = new int[7];
        var songPlayCounts = new Dictionary<long, int>();
        var songsById = new Dictionary<long, (string Title, string Artist, string Album)>();
        // TotalArtists 只统计"非空歌手"的去重个数（不分大小写）。
        var artistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(row.StartedAtUtc.UtcDateTime, tz);
            var day = DateOnly.FromDateTime(local);
            totalSeconds += row.DurationSeconds;
            if (day == today) playsToday++;
            if (day >= today.AddDays(-6) && day <= today) playsLast7Days++;
            if (day.Year == today.Year && day.Month == today.Month) playsThisMonth++;

            hours[local.Hour]++;
            // DayOfWeek 里周日是 0，这里改成"周一 = 0"。
            weekdays[((int)local.DayOfWeek + 6) % 7]++;

            songPlayCounts.TryGetValue(row.SongId, out var songCount);
            songPlayCounts[row.SongId] = songCount + 1;
            songsById[row.SongId] = (row.Title, row.Artist, row.Album);
            if (!string.IsNullOrWhiteSpace(row.Artist)) artistNames.Add(row.Artist.Trim());
        }

        // 收藏数必须单独查：收藏了但一次都没播过的歌不在上面的 JOIN 结果里。
        var favoriteSongs = CountFavorites();

        var topSongs = BuildTopSongs(songsById, songPlayCounts);
        var topArtists = BuildTopArtists(songsById, songPlayCounts);
        var topAlbums = BuildTopAlbums(songsById, songPlayCounts);
        var dailyTrend = BuildDailySlices(rows, tz, today, DailyTrendDays);

        var kpi = new StatsKpi(
            rows.Count,
            songPlayCounts.Count,
            artistNames.Count,
            songsById.Values.Where(s => !string.IsNullOrWhiteSpace(s.Album)).Select(s => s.Album).Distinct(StringComparer.Ordinal).Count(),
            playsToday,
            playsLast7Days,
            playsThisMonth,
            favoriteSongs,
            totalSeconds,
            topArtists.Count > 0 ? topArtists[0].Label : "",
            topSongs.Count > 0 ? topSongs[0].Label : "");

        return new StatsOverview(kpi, topSongs, topArtists, topAlbums, BuildHourSlices(hours), BuildWeekdaySlices(weekdays), dailyTrend);
    }

    /// <summary>按本地日期取"当天/最近 N 天"等常用聚合，供界面卡片使用。</summary>
    public IReadOnlyList<StatSlice> GetDailyCounts(int days, TimeZoneInfo? timeZone = null)
    {
        if (days <= 0) return Array.Empty<StatSlice>();
        var tz = timeZone ?? TimeZoneInfo.Local;
        var today = LocalDate(DateTimeOffset.UtcNow, tz);
        return BuildDailySlices(LoadRows(), tz, today, days);
    }

    // ---------------------------------------------------------------- 数据读取

    /// <summary>把 UTC 时刻换算到目标时区，并取本地日历日。</summary>
    private static DateOnly LocalDate(DateTimeOffset utc, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, timeZone).DateTime);

    /// <summary>用唯一一条查询取回播放记录与歌曲资料（JOIN 在 SQL，时间换算在 C#）。</summary>
    private List<StatsPlayRow> LoadRows()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT r.started_at_utc, r.duration_seconds, s.id, s.title, s.artist, s.album, s.is_favorite
FROM play_records r JOIN songs s ON s.id = r.song_id";
        using var reader = command.ExecuteReader();
        var rows = new List<StatsPlayRow>();
        while (reader.Read())
        {
            rows.Add(new StatsPlayRow(
                FromDb(reader.GetString(0)),
                reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(2)));
        }
        return rows;
    }

    /// <summary>收藏歌曲总数（含没有任何播放记录的收藏）。</summary>
    private int CountFavorites()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM songs WHERE is_favorite=1";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- 图表构造

    private const string UnknownArtist = "未知歌手";
    private const string UnknownAlbum = "未知专辑";

    /// <summary>播放次数最多的前 10 首歌；Label = 歌名，Detail = 歌手。</summary>
    private static List<StatSlice> BuildTopSongs(
        Dictionary<long, (string Title, string Artist, string Album)> songsById,
        Dictionary<long, int> songPlayCounts) =>
        songsById
            .Select(pair => new StatSlice(
                pair.Value.Title,
                songPlayCounts[pair.Key],
                songPlayCounts[pair.Key],
                string.IsNullOrWhiteSpace(pair.Value.Artist) ? UnknownArtist : pair.Value.Artist))
            .OrderByDescending(slice => slice.Count)
            .ThenBy(slice => slice.Label, StringComparer.Ordinal)
            .ThenBy(slice => slice.Detail, StringComparer.Ordinal)
            .Take(TopSliceCount)
            .ToList();

    /// <summary>播放次数最多的前 10 位歌手；Label = 歌手，Detail = "N 首"。</summary>
    private static List<StatSlice> BuildTopArtists(
        Dictionary<long, (string Title, string Artist, string Album)> songsById,
        Dictionary<long, int> songPlayCounts) =>
        songsById
            .GroupBy(
                pair => string.IsNullOrWhiteSpace(pair.Value.Artist) ? UnknownArtist : pair.Value.Artist,
                StringComparer.Ordinal)
            .Select(group => new StatSlice(
                group.Key,
                group.Sum(pair => songPlayCounts[pair.Key]),
                group.Sum(pair => songPlayCounts[pair.Key]),
                $"{group.Select(pair => pair.Key).Distinct().Count()} 首"))
            .OrderByDescending(slice => slice.Count)
            .ThenBy(slice => slice.Label, StringComparer.Ordinal)
            .Take(TopSliceCount)
            .ToList();

    /// <summary>播放次数最多的前 10 张专辑；Label = 专辑，Detail = null。</summary>
    private static List<StatSlice> BuildTopAlbums(
        Dictionary<long, (string Title, string Artist, string Album)> songsById,
        Dictionary<long, int> songPlayCounts) =>
        songsById
            .GroupBy(
                pair => string.IsNullOrWhiteSpace(pair.Value.Album) ? UnknownAlbum : pair.Value.Album,
                StringComparer.Ordinal)
            .Select(group => new StatSlice(
                group.Key,
                group.Sum(pair => songPlayCounts[pair.Key]),
                group.Sum(pair => songPlayCounts[pair.Key])))
            .OrderByDescending(slice => slice.Count)
            .ThenBy(slice => slice.Label, StringComparer.Ordinal)
            .Take(TopSliceCount)
            .ToList();

    /// <summary>固定 24 个小时切片，Label = "00" … "23"。</summary>
    private static List<StatSlice> BuildHourSlices(int[] hours)
    {
        var slices = new List<StatSlice>(24);
        for (var hour = 0; hour < 24; hour++)
        {
            var label = hour.ToString("00", CultureInfo.InvariantCulture);
            slices.Add(new StatSlice(label, hours[hour], hours[hour]));
        }
        return slices;
    }

    /// <summary>固定 7 个星期切片，周一在前。</summary>
    private static List<StatSlice> BuildWeekdaySlices(int[] weekdays)
    {
        string[] labels = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
        var slices = new List<StatSlice>(7);
        for (var index = 0; index < labels.Length; index++)
        {
            slices.Add(new StatSlice(labels[index], weekdays[index], weekdays[index]));
        }
        return slices;
    }

    /// <summary>最近 <paramref name="days"/> 个本地日期（含今天）的播放次数，从旧到新。</summary>
    private static List<StatSlice> BuildDailySlices(
        List<StatsPlayRow> rows, TimeZoneInfo timeZone, DateOnly today, int days)
    {
        var counts = new Dictionary<DateOnly, int>();
        foreach (var row in rows)
        {
            var localDate = LocalDate(row.StartedAtUtc, timeZone);
            counts.TryGetValue(localDate, out var count);
            counts[localDate] = count + 1;
        }

        var slices = new List<StatSlice>(days);
        for (var offset = days - 1; offset >= 0; offset--)
        {
            var date = today.AddDays(-offset);
            counts.TryGetValue(date, out var count);
            slices.Add(new StatSlice(date.ToString("MM-dd", CultureInfo.InvariantCulture), count, count));
        }
        return slices;
    }
}

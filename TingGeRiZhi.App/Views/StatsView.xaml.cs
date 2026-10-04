using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TingGeRiZhi.App.ViewModels;
using TingGeRiZhi.Core;

namespace TingGeRiZhi.App.Views;

public partial class StatsView : UserControl
{
    private const double TrendHeight = 118;
    private const double HourHeight = 100;
    private const double WeekdayHeight = 100;

    public StatsView() => InitializeComponent();

    public void Refresh()
    {
        StatsOverview overview;
        try
        {
            overview = AppServices.Database.GetOverview();
        }
        catch (Exception ex)
        {
            AppServices.ReportStatus($"统计失败：{ex.Message}");
            return;
        }

        var kpi = overview.Kpi;
        var hasData = kpi.TotalPlays > 0;

        SubtitleText.Text = hasData
            ? $"共 {kpi.TotalPlays} 次播放 · {kpi.TotalSongs} 首歌曲 · {kpi.TotalArtists} 位歌手 · 累计 {FormatTotal(kpi.TotalSeconds)}"
            : "还没有可统计的数据";

        BuildKpis(kpi, overview.DailyTrend);

        TrendList.ItemsSource = BarItem.Build(overview.DailyTrend, TrendHeight);
        TrendLabels.ItemsSource = ThinLabels(overview.DailyTrend, 5);
        TrendPeakText.Text = PeakText(overview.DailyTrend, "单日最多");

        HourList.ItemsSource = BarItem.Build(overview.HourDistribution, HourHeight);
        HourLabels.ItemsSource = ThinLabels(overview.HourDistribution, 3);
        HourPeakText.Text = PeakText(overview.HourDistribution, "最常在");

        WeekdayList.ItemsSource = BarItem.Build(overview.WeekdayDistribution, WeekdayHeight);
        WeekdayLabels.ItemsSource = overview.WeekdayDistribution.Select(s => new BarItem { Label = s.Label, Count = 0 }).ToList();
        WeekdayPeakText.Text = PeakText(overview.WeekdayDistribution, "最常在");

        TopSongList.ItemsSource = BarItem.Build(overview.TopSongs, max: 10);
        TopArtistList.ItemsSource = BarItem.Build(overview.TopArtists, max: 10);
        TopAlbumList.ItemsSource = BarItem.Build(overview.TopAlbums, max: 10);

        EmptyCard.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BuildKpis(StatsKpi kpi, IReadOnlyList<StatSlice> trend)
    {
        KpiList.ItemsSource = new List<KpiCard>
        {
            Card("累计播放", kpi.TotalPlays.ToString(), $"平均每天 {AveragePerDay(kpi.TotalPlays, trend)} 次", "G.Play", "B.Accent", "B.AccentSoft"),
            Card("曲库歌曲", kpi.TotalSongs.ToString(), $"{kpi.TotalArtists} 位歌手 · {kpi.TotalAlbums} 张专辑", "G.Music", "B.Indigo", "B.IndigoSoft"),
            Card("收藏歌曲", kpi.FavoriteSongs.ToString(), kpi.TopSong.Length > 0 ? $"最常听《{kpi.TopSong}》" : "还没有收藏", "G.Star", "B.Star", "B.StarSoft"),
            Card("累计收听", FormatTotal(kpi.TotalSeconds), "按曲目时长累计", "G.Chart", "B.Success", "B.SuccessSoft"),
            Card("今日播放", kpi.PlaysToday.ToString(), kpi.PlaysToday > 0 ? "保持记录" : "今天还没听歌", "G.List", "B.Warning", "B.WarningSoft"),
            Card("近 7 天", kpi.PlaysLast7Days.ToString(), $"本月 {kpi.PlaysThisMonth} 次", "G.Refresh", "B.Accent", "B.AccentSoft")
        };
    }

    private KpiCard Card(string title, string value, string caption, string glyphKey, string accentKey, string softKey) => new()
    {
        Title = title,
        Value = value,
        Caption = caption,
        Glyph = FindResource(glyphKey) as Geometry,
        Accent = FindResource(accentKey) as Brush,
        AccentSoft = FindResource(softKey) as Brush
    };

    private static string AveragePerDay(int totalPlays, IReadOnlyList<StatSlice> trend)
    {
        if (totalPlays <= 0) return "0";
        var activeDays = trend.Count(s => s.Count > 0);
        return activeDays <= 0 ? "0" : Math.Round(totalPlays / (double)activeDays, 1).ToString("0.#");
    }

    private static string PeakText(IReadOnlyList<StatSlice> slices, string prefix)
    {
        if (slices.Count == 0) return "";
        var peak = slices.OrderByDescending(s => s.Count).First();
        return peak.Count == 0 ? "" : $"{prefix} {peak.Label}（{peak.Count} 次）";
    }

    /// <summary>30 个标签太密，只显示每第 N 个。</summary>
    private static IReadOnlyList<BarItem> ThinLabels(IReadOnlyList<StatSlice> slices, int step)
    {
        var result = new List<BarItem>(slices.Count);
        for (var index = 0; index < slices.Count; index++)
        {
            var show = index % step == 0 || index == slices.Count - 1;
            result.Add(new BarItem { Label = show ? slices[index].Label : "", Count = 0 });
        }
        return result;
    }

    private static string FormatTotal(long seconds)
    {
        if (seconds <= 0) return "0 分钟";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} 小时 {span.Minutes} 分" : $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} 分钟";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
}

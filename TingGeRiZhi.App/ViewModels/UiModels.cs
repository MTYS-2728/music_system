using System.ComponentModel;
using System.Runtime.CompilerServices;
using TingGeRiZhi.Core;

namespace TingGeRiZhi.App.ViewModels;

/// <summary>列表中的一行：同一首歌的多次播放合并成一行。</summary>
public sealed class RecordRow : INotifyPropertyChanged
{
    private bool _isFavorite;
    private string _notes;

    public IReadOnlyList<long> RecordIds { get; }
    public long SongId { get; }
    public string Title { get; }
    public string Artist { get; }
    public string Album { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public string LocalTime { get; }
    public string LocalDate { get; }
    public string LocalTimeOnly { get; }
    public int? DurationSeconds { get; }
    public bool IsManual { get; }
    public string? CoverPath { get; }

    /// <summary>当前筛选结果里这首歌出现的次数。</summary>
    public int GroupCount { get; }

    /// <summary>这首歌历史上被播放的总次数。</summary>
    public int SongPlayCount { get; }

    public RecordRow(IEnumerable<PlayLogRow> rows)
    {
        var list = rows.OrderByDescending(r => r.StartedAtUtc).ToList();
        var latest = list[0];
        RecordIds = list.Select(r => r.RecordId).Distinct().ToArray();
        SongId = latest.SongId;
        Title = latest.Title;
        Artist = latest.Artist;
        Album = latest.Album;
        StartedAtUtc = latest.StartedAtUtc;
        var local = latest.StartedAtUtc.ToLocalTime();
        LocalTime = local.ToString("yyyy-MM-dd HH:mm:ss");
        LocalDate = local.ToString("yyyy-MM-dd");
        LocalTimeOnly = local.ToString("HH:mm");
        DurationSeconds = latest.DurationSeconds;
        IsManual = latest.IsManual;
        CoverPath = latest.CoverPath;
        GroupCount = RecordIds.Count;
        SongPlayCount = latest.SongPlayCount;
        _notes = latest.Notes ?? "";
        _isFavorite = latest.IsFavorite;
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            OnPropertyChanged();
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (_notes == value) return;
            _notes = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNotes));
        }
    }

    public bool HasNotes => !string.IsNullOrWhiteSpace(_notes);

    public string ArtistAlbum
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Artist)) return string.IsNullOrWhiteSpace(Album) ? "未知歌手" : Album;
            return string.IsNullOrWhiteSpace(Album) ? Artist : $"{Artist} · {Album}";
        }
    }

    public string SourceLabel => IsManual ? "手动" : "自动";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>侧边导航项。</summary>
public sealed class NavItem
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public string Description { get; init; } = "";
    public System.Windows.Media.Geometry? Icon { get; init; }
}

/// <summary>下拉框选项。重写 ToString 让 WPF 在收起状态下也能正确显示文本。</summary>
public sealed class ComboOption<T>
{
    public ComboOption(string text, T value)
    {
        Text = text;
        Value = value;
    }

    public string Text { get; }
    public T Value { get; }

    public override string ToString() => Text;
}

/// <summary>图表中的一根柱 / 一行排行。</summary>
public sealed class BarItem
{
    public required string Label { get; init; }
    public required int Count { get; init; }
    public string? Detail { get; init; }
    public int Rank { get; init; }

    /// <summary>0..1，用于横向条宽度。</summary>
    public double Ratio { get; init; }

    /// <summary>纵向柱的实际高度（像素）。</summary>
    public double Height { get; init; }

    /// <summary>是否是该图表的峰值（用主色强调）。</summary>
    public bool Highlight { get; init; }

    /// <summary>是否有数据（为 0 的柱子用更淡的颜色）。</summary>
    public bool HasValue => Count > 0;

    public string CountText => Count.ToString();
    public System.Windows.GridLength StarLength => new(Math.Max(Ratio, 0.001), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength RestLength => new(Math.Max(1 - Ratio, 0.0001), System.Windows.GridUnitType.Star);

    public static IReadOnlyList<BarItem> Build(IEnumerable<StatSlice> slices, double maxHeight = 0, int max = 0)
    {
        var list = max > 0 ? slices.Take(max).ToList() : slices.ToList();
        if (list.Count == 0) return Array.Empty<BarItem>();
        var peak = Math.Max(1, list.Max(s => s.Count));
        var result = new List<BarItem>(list.Count);
        for (var index = 0; index < list.Count; index++)
        {
            var slice = list[index];
            result.Add(new BarItem
            {
                Label = slice.Label,
                Count = slice.Count,
                Detail = slice.Detail,
                Rank = index + 1,
                Ratio = slice.Count / (double)peak,
                Height = maxHeight > 0 ? Math.Max(2, slice.Count / (double)peak * maxHeight) : 0,
                Highlight = slice.Count == peak && peak > 1
            });
        }
        return result;
    }
}

/// <summary>统计页顶部的一张指标卡。</summary>
public sealed class KpiCard
{
    public required string Title { get; init; }
    public required string Value { get; init; }
    public string Caption { get; init; } = "";
    public System.Windows.Media.Geometry? Glyph { get; init; }
    public System.Windows.Media.Brush? Accent { get; init; }
    public System.Windows.Media.Brush? AccentSoft { get; init; }
}

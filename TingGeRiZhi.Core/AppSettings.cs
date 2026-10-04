using System.Text.Json;
using System.Text.Json.Serialization;

namespace TingGeRiZhi.Core;

/// <summary>用户设置（持久化到 settings.json）。</summary>
public sealed class AppSettings
{
    /// <summary>启动后是否自动记录。</summary>
    public bool AutoRecord { get; set; } = true;

    /// <summary>连续播放多少秒后自动记录一次。</summary>
    public int RecordThresholdSeconds { get; set; } = 30;

    /// <summary>轮询媒体会话的间隔（秒）。</summary>
    public int PollSeconds { get; set; } = 5;

    /// <summary>列表每页条数。</summary>
    public int PageSize { get; set; } = 15;

    /// <summary>主题：Light / Dark。</summary>
    public string Theme { get; set; } = "Light";

    /// <summary>关闭窗口时最小化到托盘。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>启动时直接最小化到托盘。</summary>
    public bool StartMinimized { get; set; }

    /// <summary>自动从 Windows 媒体会话抓取封面。</summary>
    public bool CaptureCoverArt { get; set; } = true;

    /// <summary>只记录来自 QQ 音乐的媒体会话（浏览器、B 站等一律跳过）。</summary>
    public bool OnlyRecordQQMusic { get; set; } = true;

    /// <summary>删除前弹确认框。</summary>
    public bool ConfirmBeforeDelete { get; set; } = true;

    /// <summary>最近一次使用过的目录（导出/导入对话框的起始位置）。</summary>
    public string? LastDirectory { get; set; }

    /// <summary>列表默认排序方式，取值见 <see cref="RecordSort"/>。</summary>
    public string SortMode { get; set; } = nameof(RecordSort.StartedDescending);

    /// <summary>把越界或非法的值收敛到可用范围。</summary>
    public AppSettings Normalized()
    {
        RecordThresholdSeconds = Math.Clamp(RecordThresholdSeconds, 5, 600);
        PollSeconds = Math.Clamp(PollSeconds, 1, 60);
        PageSize = Math.Clamp(PageSize, 5, 500);
        if (Theme is not ("Light" or "Dark")) Theme = "Light";
        if (!Enum.TryParse<RecordSort>(SortMode, true, out _)) SortMode = nameof(RecordSort.StartedDescending);
        return this;
    }

    public AppSettings Clone() => new()
    {
        AutoRecord = AutoRecord,
        RecordThresholdSeconds = RecordThresholdSeconds,
        PollSeconds = PollSeconds,
        PageSize = PageSize,
        Theme = Theme,
        MinimizeToTray = MinimizeToTray,
        StartMinimized = StartMinimized,
        CaptureCoverArt = CaptureCoverArt,
        ConfirmBeforeDelete = ConfirmBeforeDelete,
        OnlyRecordQQMusic = OnlyRecordQQMusic,
        LastDirectory = LastDirectory,
        SortMode = SortMode
    };
}

/// <summary>读写 settings.json，读写失败不抛异常（退回默认值）。</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string FilePath { get; }

    public AppSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TingGeRiZhi", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded is not null) return loaded.Normalized();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 读不到就用默认值，不阻塞启动。
        }
        return new AppSettings().Normalized();
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalized(), Options));
            File.Move(temp, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

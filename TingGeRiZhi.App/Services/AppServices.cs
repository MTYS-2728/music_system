using System.Windows;
using TingGeRiZhi.Core;

namespace TingGeRiZhi.App;

/// <summary>轻量服务容器：让各页面共享数据库、设置和播放跟踪器。</summary>
internal static class AppServices
{
    private static readonly object Gate = new();

    public static LogDatabase Database { get; private set; } = null!;
    public static AppSettingsStore SettingsStore { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;
    public static CoverCache CoverCache { get; private set; } = null!;
    public static PlaybackTracker Tracker { get; private set; } = null!;
    public static bool Initialized { get; private set; }

    /// <summary>播放记录发生变化（新增/删除/编辑）。</summary>
    public static event Action? RecordsChanged;

    /// <summary>设置发生变化（主题、阈值、每页条数…）。</summary>
    public static event Action? SettingsChanged;

    /// <summary>需要在状态栏显示的一句话反馈。</summary>
    public static event Action<string>? StatusReported;

    public static void ReportStatus(string message) => StatusReported?.Invoke(message);

    public static void Initialize()
    {
        lock (Gate)
        {
            if (Initialized) return;
            SettingsStore = new AppSettingsStore();
            Settings = SettingsStore.Load();
            Database = new LogDatabase();
            CoverCache = new CoverCache();
            Tracker = new PlaybackTracker(new MediaSessionReader(), Database, Settings, CoverCache);
            Initialized = true;
        }
        ThemeManager.Apply(Settings.Theme);
    }

    /// <summary>持久化当前设置并广播。</summary>
    public static void SaveSettings()
    {
        SettingsStore.Save(Settings);
        Tracker.ApplySettings(Settings);
        SettingsChanged?.Invoke();
    }

    public static void NotifyRecordsChanged() => RecordsChanged?.Invoke();

    /// <summary>浅拷贝一份设置用于"取消"场景。</summary>
    public static AppSettings SnapshotSettings() => Settings.Clone();
}

/// <summary>运行时切换浅色 / 深色主题。</summary>
internal static class ThemeManager
{
    public static string Current { get; private set; } = "Light";

    public static void Apply(string theme)
    {
        var name = theme == "Dark" ? "Dark" : "Light";
        Current = name;
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var dictionary = new ResourceDictionary { Source = new Uri($"Themes/{name}.xaml", UriKind.Relative) };
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count == 0) merged.Add(dictionary);
        else merged[0] = dictionary;
    }
}

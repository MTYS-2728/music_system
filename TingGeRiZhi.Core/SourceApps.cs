namespace TingGeRiZhi.Core;

/// <summary>一个可以被记录的播放器应用。<paramref name="Keywords"/> 命中任意一个即认为是它。</summary>
public sealed record MusicApp(string Name, string[] Keywords);

/// <summary>
/// 媒体会话来源应用的识别与显示名处理。
/// Windows 媒体会话给出的 <c>SourceAppUserModelId</c> 可能是进程名（如 <c>cloudmusic.exe</c>）、
/// 完整 exe 路径，也可能是形如 <c>Vendor.App_xxx!App</c> 的 AUMID，所以这里统一做宽松的子串匹配。
/// </summary>
public static class SourceApps
{
    /// <summary>
    /// 可以被自动记录的播放器。加新播放器只需在这里追加一条。
    /// 关键字用进程名 / 路径里稳定出现的片段即可，不必区分大小写。
    /// </summary>
    public static IReadOnlyList<MusicApp> Recordable { get; } = new[]
    {
        new MusicApp("QQ 音乐", new[] { "QQMusic", "QQ音乐" }),
        new MusicApp("网易云音乐", new[] { "cloudmusic", "netease", "网易云音乐" }),
    };

    /// <summary>该来源属于哪个可记录的播放器；不属于任何一个是 <c>null</c>。</summary>
    public static MusicApp? Match(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp)) return null;
        var normalized = Normalize(sourceApp);

        foreach (var app in Recordable)
        {
            foreach (var keyword in app.Keywords)
            {
                if (normalized.Contains(Normalize(keyword), StringComparison.OrdinalIgnoreCase)) return app;
            }
        }
        return null;
    }

    /// <summary>该媒体会话是否来自可记录的播放器。</summary>
    public static bool IsRecordable(string? sourceApp) => Match(sourceApp) is not null;

    /// <summary>可记录播放器的名字，用于界面提示，例如"QQ 音乐 / 网易云音乐"。</summary>
    public static string RecordableNames => string.Join(" / ", Recordable.Select(app => app.Name));

    /// <summary>把来源标识整理成适合显示的名字。</summary>
    public static string FriendlyName(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp)) return "未知应用";

        var name = sourceApp.Trim();

        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0 && slash < name.Length - 1) name = name[(slash + 1)..];

        var bang = name.IndexOf('!');
        if (bang > 0) name = name[..bang];

        var underscore = name.IndexOf('_');
        if (underscore > 0) name = name[..underscore];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        name = name.Trim();
        return name.Length == 0 ? "未知应用" : name;
    }

    private static string Normalize(string value) => value.Replace(" ", "").Replace("\t", "");
}

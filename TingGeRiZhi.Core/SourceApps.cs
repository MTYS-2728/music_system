namespace TingGeRiZhi.Core;

/// <summary>
/// 媒体会话来源应用的识别与显示名处理。
/// Windows 媒体会话给出的 <c>SourceAppUserModelId</c> 可能是完整 exe 路径，
/// 也可能是形如 <c>Tencent.QQMusic_xxx!App</c> 的 AUMID，所以这里做宽松匹配。
/// </summary>
public static class SourceApps
{
    /// <summary>QQ 音乐的关键字（忽略大小写与空格）。</summary>
    private const string QQMusicKey = "QQMusic";

    private const string QQMusicCnKey = "QQ音乐";

    /// <summary>该会话是否来自 QQ 音乐。</summary>
    public static bool IsQQMusic(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp)) return false;
        var normalized = sourceApp.Replace(" ", "");
        return normalized.Contains(QQMusicKey, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(QQMusicCnKey, StringComparison.OrdinalIgnoreCase);
    }

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
}

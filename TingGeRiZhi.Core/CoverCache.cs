using System.Security.Cryptography;
using System.Text;

namespace TingGeRiZhi.Core;

/// <summary>
/// 封面缓存：把从 Windows 媒体会话读到的缩略图（或用户手工选择的图片）落到本地磁盘。
/// 只做文件管理，不负责图像解码 —— 解码属于界面层。
/// </summary>
public sealed class CoverCache
{
    private const long MaxBytes = 8 * 1024 * 1024;

    public string Directory { get; }

    public CoverCache(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TingGeRiZhi", "covers");
        try { System.IO.Directory.CreateDirectory(Directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 缓存不可用时静默降级 */ }
    }

    /// <summary>由歌曲身份计算稳定的文件名（同一首歌反复抓取不会堆积文件）。</summary>
    private static string Key(string title, string artist, string album)
    {
        var raw = $"{title.Trim()}\u001f{artist.Trim()}\u001f{album.Trim()}".ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }

    /// <summary>保存媒体会话提供的封面字节，返回缓存文件路径；失败返回 null。</summary>
    public string? SaveFromBytes(byte[]? bytes, string title, string artist, string album)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxBytes) return null;
        if (string.IsNullOrWhiteSpace(title)) return null;
        try
        {
            var path = Path.Combine(Directory, Key(title, artist, album) + DetectExtension(bytes));
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>把用户选择的本地图片复制进缓存，返回缓存文件路径。</summary>
    public string? Import(string sourcePath, string title, string artist, string album)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
        if (string.IsNullOrWhiteSpace(title)) return null;
        try
        {
            var info = new FileInfo(sourcePath);
            if (info.Length == 0 || info.Length > MaxBytes) return null;
            var extension = NormalizeExtension(Path.GetExtension(sourcePath));
            var path = Path.Combine(Directory, Key(title, artist, album) + extension);
            File.Copy(sourcePath, path, true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>删除缓存中的封面（只允许删除缓存目录内的文件）。</summary>
    public void Remove(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath)) return;
        try
        {
            var full = Path.GetFullPath(coverPath);
            if (!full.StartsWith(Path.GetFullPath(Directory), StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 忽略删除失败。
        }
    }

    /// <summary>清理没有被任何歌曲引用的缓存文件。</summary>
    public int Cleanup(IEnumerable<string?> usedPaths)
    {
        var keep = usedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFileName(p!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        try
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory))
            {
                if (keep.Contains(Path.GetFileName(file))) continue;
                try { File.Delete(file); removed++; } catch { /* 占用中则跳过 */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 目录不可读时放弃清理。
        }
        return removed;
    }

    private static string NormalizeExtension(string? extension) => extension?.ToLowerInvariant() switch
    {
        ".png" => ".png",
        ".jpg" or ".jpeg" => ".jpg",
        ".bmp" => ".bmp",
        ".gif" => ".gif",
        ".webp" => ".webp",
        _ => ".jpg"
    };

    /// <summary>按文件头判断图片格式，避免把 PNG 存成 .jpg。</summary>
    private static string DetectExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return ".gif";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D) return ".bmp";
        if (bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return ".webp";
        return ".jpg";
    }
}

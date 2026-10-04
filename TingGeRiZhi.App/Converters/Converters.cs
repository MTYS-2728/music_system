using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace TingGeRiZhi.App.Converters;

/// <summary>true → Visible，false → Collapsed；ConverterParameter="Invert" 时取反。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>非空字符串 → Visible。</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            _ => true
        };
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>把封面文件路径转成可复用的冻结位图；按路径 + 修改时间缓存。</summary>
public sealed class CoverImageConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Invalidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Cache)
        {
            foreach (var key in Cache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                Cache.Remove(key);
        }
    }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrWhiteSpace(path)) return null;

        string key;
        try
        {
            if (!File.Exists(path)) return null;
            key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        BitmapImage? image = null;
        try
        {
            var decodeWidth = parameter is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ? w : 144;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
        }
        catch (Exception)
        {
            image = null;
        }

        lock (Cache) Cache[key] = image;
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>秒数 → "3:45" / "1:02:03"。</summary>
public sealed class SecondsToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int seconds || seconds <= 0) return "—";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>0..1 的比例 → 图表条宽度（星号 GridLength）。</summary>
public sealed class RatioToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var ratio = value is double d ? Math.Clamp(d, 0, 1) : 0;
        var scale = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 100;
        if (string.Equals(parameter as string, "Rest", StringComparison.OrdinalIgnoreCase)) ratio = 1 - ratio;
        return new GridLength(Math.Max(ratio * scale, ratio > 0 ? 0.6 : 0), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>总秒数 → "12 小时 34 分" 之类的可读文本。</summary>
public sealed class DurationSummaryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long total || total <= 0) return "0 分钟";
        var span = TimeSpan.FromSeconds(total);
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} 小时 {span.Minutes} 分";
        return $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} 分钟";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

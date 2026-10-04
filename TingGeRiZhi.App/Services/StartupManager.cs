using Microsoft.Win32;

namespace TingGeRiZhi.App.Services;

/// <summary>开机自启动（写入当前用户的 Run 键）。</summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "聆迹";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(path)) key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception)
        {
            // 注册表被策略限制时静默失败，界面会重新读取真实状态。
        }
    }
}

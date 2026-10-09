using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TingGeRiZhi.App.Dialogs;
using TingGeRiZhi.App.Services;
using TingGeRiZhi.App.ViewModels;
using TingGeRiZhi.Core;
using Forms = System.Windows.Forms;

namespace TingGeRiZhi.App;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly List<NavItem> _navItems;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _allowClose;
    private int _pageIndex = -1;

    public MainWindow()
    {
        InitializeComponent();

        _navItems = BuildNavItems();
        NavList.ItemsSource = _navItems;
        _trayIcon = CreateTrayIcon();

        AppServices.StatusReported += message => Post(() => SetStatus(message));
        AppServices.RecordsChanged += () => Post(() =>
        {
            RefreshCurrentPage();
            UpdateFooter();
        });
        AppServices.SettingsChanged += () => Post(ApplySettingsToUi);

        AppServices.Tracker.SnapshotUpdated += (_, snapshot) => Post(() => NowPlayingPage.UpdateSnapshot(snapshot));
        AppServices.Tracker.StatusChanged += (_, message) => Post(() => SetStatus(message));
        AppServices.Tracker.RecordCreated += (_, _) => Post(() => SetStatus("已自动记录一首歌曲"));

        NowPlayingPage.RecordsRequested += () => NavList.SelectedIndex = 1;

        _ticker.Tick += (_, _) => UpdateLiveStatus();
        _ticker.Start();

        ApplySettingsToUi();
        NavList.SelectedIndex = 0;
        SetStatus("准备就绪");
    }

    /// <summary>由 App 在窗口显示后调用，启动后台轮询。</summary>
    public async Task StartTrackingAsync()
    {
        AppServices.Tracker.Start();
        try { await AppServices.Tracker.TickAsync(); }
        catch (Exception) { /* 首次读取失败不影响启动 */ }
        RefreshCurrentPage();
        UpdateFooter();
    }

    // ------------------------------------------------------------------ 导航

    private List<NavItem> BuildNavItems() => new()
    {
        Item("now", "正在播放", "当前媒体会话", "G.Play"),
        Item("records", "听歌记录", "搜索 · 编辑 · 导入导出", "G.List"),
        Item("stats", "数据统计", "收听习惯一览", "G.Chart"),
        Item("settings", "设置", "记录 · 外观 · 数据", "G.Gear")
    };

    private NavItem Item(string key, string title, string description, string glyph) => new()
    {
        Key = key,
        Title = title,
        Description = description,
        Glyph = glyph,
        Icon = FindResource(glyph) as Geometry
    };

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = NavList.SelectedIndex;
        if (index < 0) return;
        _pageIndex = index;

        NowPlayingPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordsPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        StatsPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;

        PageTitleText.Text = _navItems[index].Title;
        PageSubtitleText.Text = _navItems[index].Description;
        if (index == 0) NowPlayingPage.ScrollToTop();
        RefreshCurrentPage();
    }

    private void RefreshCurrentPage()
    {
        switch (_pageIndex)
        {
            case 0: NowPlayingPage.Refresh(); break;
            case 1: RecordsPage.Refresh(); break;
            case 2: StatsPage.Refresh(); break;
            case 3: SettingsPage.Refresh(); break;
        }
    }

    private void UpdateLiveStatus()
    {
        if (!IsVisible) return;
        var tracker = AppServices.Tracker;
        var settings = AppServices.Settings;

        if (_pageIndex == 0)
            NowPlayingPage.UpdateProgress(tracker.ContinuousSeconds, tracker.SecondsUntilRecord,
                tracker.IsAutoRecording, tracker.CurrentCoverPath);

        SideAutoDetail.Text = tracker.IsAutoRecording
            ? DescribeAutoRule(settings)
            : "已暂停，可随时手动记录";
    }

    /// <summary>侧边栏里那句"当前记录规则"的说明。</summary>
    private static string DescribeAutoRule(AppSettings settings) => settings.OnlyRecordMusicApps
        ? $"仅记录 {SourceApps.RecordableNames} · 每 {settings.PollSeconds} 秒检查一次"
        : $"每 {settings.PollSeconds} 秒检查一次 · 连续 {settings.RecordThresholdSeconds} 秒后记录";

    private void ApplySettingsToUi()
    {
        ThemeManager.Apply(AppServices.Settings.Theme);

        var tracker = AppServices.Tracker;
        var auto = tracker.IsAutoRecording;
        var settings = AppServices.Settings;

        PauseButton.Content = auto ? "暂停自动记录" : "继续自动记录";
        var musicOnly = settings.OnlyRecordMusicApps;
        SideAutoText.Text = auto ? (musicOnly ? "仅记录音乐软件" : "自动记录中") : "自动记录已暂停";
        AutoChipText.Text = auto ? (musicOnly ? "仅记录音乐软件" : "自动记录中") : "自动记录已暂停";

        var dot = (Brush)FindResource(auto ? "B.StatusOnline" : "B.StatusPaused");
        SideAutoDot.Fill = dot;
        AutoChipDot.Fill = dot;
        AutoChipText.Foreground = (Brush)FindResource(auto ? "B.AccentSoftText" : "B.Warning");

        SideAutoDetail.Text = auto ? DescribeAutoRule(settings) : "已暂停，可随时手动记录";

        RecordsPage.SyncSettings();
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        var path = AppServices.Database.DatabasePath;
        var temp = Path.GetTempPath();
        var fallback = path.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        FooterText.Text = fallback ? $"数据目录（已回退到临时目录）：{path}" : $"数据目录：{path}";
        FooterText.ToolTip = path;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        StatusDot.Fill = (Brush)FindResource("B.StatusOnline");
    }

    /// <summary>非 UI 线程安全地投递到界面线程。</summary>
    private void Post(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try { Dispatcher.BeginInvoke(action); }
        catch (Exception) { /* 关闭过程中忽略 */ }
    }

    // ------------------------------------------------------------------ 标题栏

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            // WindowChrome 最大化时会把不可见的调整边框算进客户区，这里补回来。
            RootFrame.BorderThickness = new Thickness(SystemParameters.WindowResizeBorderThickness.Left + 1);
            MaxIcon.Data = FindResource("G.Restore") as Geometry;
            MaxButton.ToolTip = "还原";
        }
        else
        {
            RootFrame.BorderThickness = new Thickness(1);
            MaxIcon.Data = FindResource("G.Max") as Geometry;
            MaxButton.ToolTip = "最大化";
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            const int DwmwaWindowCornerPreference = 33;
            const int DwmcpRound = 2;
            var preference = DwmcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (Exception)
        {
            // 旧版 Windows 不支持圆角，忽略即可。
        }
    }

    // ------------------------------------------------------------------ 快捷键

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            RefreshCurrentPage();
            SetStatus("已刷新");
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.F:
                    NavList.SelectedIndex = 1;
                    RecordsPage.FocusSearch();
                    e.Handled = true;
                    return;
                case Key.D1 or Key.NumPad1: NavList.SelectedIndex = 0; e.Handled = true; return;
                case Key.D2 or Key.NumPad2: NavList.SelectedIndex = 1; e.Handled = true; return;
                case Key.D3 or Key.NumPad3: NavList.SelectedIndex = 2; e.Handled = true; return;
                case Key.D4 or Key.NumPad4: NavList.SelectedIndex = 3; e.Handled = true; return;
            }
        }

        if (e.Key == Key.Escape && _pageIndex == 1)
        {
            RecordsPage.ClearFilters();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _pageIndex == 1 && Keyboard.FocusedElement is not TextBox)
        {
            RecordsPage.DeleteSelectedRows();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ 自动记录

    private void Pause_Click(object sender, RoutedEventArgs e) => ToggleAuto();

    private void ToggleAuto()
    {
        var tracker = AppServices.Tracker;
        var next = !tracker.IsAutoRecording;
        tracker.SetAutoRecording(next);
        AppServices.Settings.AutoRecord = next;
        AppServices.SaveSettings();
        ApplySettingsToUi();
        SetStatus(next ? "自动记录已开启" : "自动记录已暂停，仍可手动记录");
    }

    private async Task ManualRecordAsync()
    {
        await AppServices.Tracker.TickAsync();
        AppServices.Tracker.TryManualRecord(out var message);
        SetStatus(message);
        AppServices.NotifyRecordsChanged();
    }

    // ------------------------------------------------------------------ 托盘

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var icon = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "聆迹 · 听歌日志"
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示聆迹", null, (_, _) => ShowFromTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("记录当前歌曲", null, async (_, _) => await ManualRecordAsync());
        menu.Items.Add("暂停 / 继续自动记录", null, (_, _) => ToggleAuto());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var startupItem = new Forms.ToolStripMenuItem("开机自动启动") { Checked = StartupManager.IsEnabled() };
        startupItem.Click += (_, _) =>
        {
            StartupManager.SetEnabled(!startupItem.Checked);
            startupItem.Checked = StartupManager.IsEnabled();
            SetStatus(startupItem.Checked ? "已开启开机自动启动" : "已关闭开机自动启动");
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _allowClose = true;
            Close();
        });

        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
            if (stream is not null)
            {
                using (stream) return new System.Drawing.Icon(stream);
            }
        }
        catch (Exception)
        {
            // 资源缺失时退回系统图标。
        }
        return System.Drawing.SystemIcons.Application;
    }

    public void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    // ------------------------------------------------------------------ 关闭

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && AppServices.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
            SetStatus("已最小化到托盘，双击托盘图标可再次打开");
            _trayIcon.ShowBalloonTip(2500, "聆迹仍在后台记录", "双击托盘图标可以重新打开窗口。", Forms.ToolTipIcon.Info);
            return;
        }

        _ticker.Stop();
        AppServices.Tracker.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Dispatcher.BeginInvoke(new Action(() => System.Windows.Application.Current.Shutdown()));
    }
}

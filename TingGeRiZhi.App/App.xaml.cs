using System.IO;
using System.Threading;
using System.Windows;

namespace TingGeRiZhi.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\TingGeRiZhi.SingleInstance";
    private const string ShowEventName = @"Local\TingGeRiZhi.ShowWindow";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private MainWindow? _window;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);
    }

    private static void WriteCrashLog(Exception? exception)
    {
        if (exception is null) return;
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tingge-rizhi-error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch (Exception)
        {
            // 记日志本身失败就放弃，不能影响主流程。
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 只允许一个实例：第二个实例负责把已有窗口唤到前台。
        _instanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
            catch (Exception) { /* 已有实例没在监听就直接退出 */ }
            Shutdown();
            return;
        }

        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            StartShowSignalListener();
        }
        catch (Exception)
        {
            _showEvent = null;
        }

        AppServices.Initialize();

        _window = new MainWindow();
        MainWindow = _window;

        var settings = AppServices.Settings;
        if (!(settings.StartMinimized && settings.MinimizeToTray)) _window.Show();
        else _window.Hide();

        _ = _window.StartTrackingAsync();
    }

    private void StartShowSignalListener()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (_showEvent is null || !_showEvent.WaitOne()) break;
                    Dispatcher.Invoke(() => _window?.ShowFromTray());
                }
                catch (Exception)
                {
                    break;
                }
            }
        })
        { IsBackground = true, Name = "TingGeRiZhi.ShowWindow" };
        thread.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _showEvent?.Set();
            _showEvent?.Dispose();
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
        catch (Exception)
        {
            // 退出阶段的清理异常无需上报。
        }
        base.OnExit(e);
    }
}

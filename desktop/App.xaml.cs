using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace GoGame;

/// <summary>
/// 应用入口。仅做一件事：接住所有"没被业务代码捕获的异常"，写堆栈到桌面，
/// 弹一个友好错误框，避免 WPF 默认行为直接把进程 kill。
/// 三层兜底：
///   1) DispatcherUnhandledException：UI 线程同步异常 / async void 路径异常
///   2) TaskScheduler.UnobservedTaskException：被 fire-and-forget 的 Task 抛出后未观察
///   3) AppDomain.CurrentDomain.UnhandledException：最后一道防线，记录后让进程退出
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUiThreadException;
        TaskScheduler.UnobservedTaskException += OnTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainException;
    }

    /// <summary>
    /// v1.5.0：去掉登录门禁。
    /// 玩家打开软件直接进主界面，不再有账号密码；身份用本机匿名档案
    /// （LocalProfile，无 UI、无密码，仅用于反馈溯源）。
    /// 默认 ShutdownMode 即 OnLastWindowClose，关掉主窗口应用正常退出，无需额外处理。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // v1.5.2：应用用户上次选择的语言（默认英文 en-US），必须在创建 MainWindow 前完成，
        // 这样窗口首次渲染就拿到正确字典。首次运行 lang.txt 即写入 en-US。
        LocalizationManager.Init();

        var main = new MainWindow();
        main.ApplyCurrentUser(LocalProfile.GetOrCreate());
        main.Show();
    }

    private static void OnUiThreadException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("UI thread exception (DispatcherUnhandledException)", e.Exception);
        try
        {
            if (Current?.MainWindow is MainWindow mw)
                StyledDialog.ShowError(mw, LocalizationManager.Get("CrashTitle"),
                    LocalizationManager.Get("CrashMsg") + "\n\n" + (e.Exception?.Message ?? "(no message)"));
        }
        catch { }
        e.Handled = true;   // 关键：阻止 WPF 默认把进程杀掉
    }

    private static void OnTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("Unobserved Task exception (UnobservedTaskException)", e.Exception);
        e.SetObserved();   // 标记为已观察到，避免进程被 GC 回收时终结
    }

    private static void OnAppDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashLog("Process-level exception (AppDomain.UnhandledException)", ex);
        // 这一层进程很可能已经崩了，能做的不多，只剩写日志。
    }

    private static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var path = Path.Combine(dir,
                $"go-crash-{DateTime.Now:yyyyMMdd-HHmmssfff}-{SanitizeFileName(source)}.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"Source : {source}");
            sb.AppendLine($"When   : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Thread : {System.Threading.Thread.CurrentThread.ManagedThreadId} ({System.Threading.Thread.CurrentThread.Name})");
            sb.AppendLine($"Msg    : {ex?.Message}");
            sb.AppendLine();
            sb.AppendLine("== StackTrace ==");
            sb.AppendLine(ex?.StackTrace ?? "(null)");

            int depth = 0;
            var inner = ex?.InnerException;
            while (inner != null && depth++ < 5)
            {
                sb.AppendLine();
                sb.AppendLine($"== Inner[{depth}] {inner.GetType().FullName} : {inner.Message} ==");
                sb.AppendLine(inner.StackTrace);
                inner = inner.InnerException;
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch { /* 写日志失败不能再次抛 */ }
    }

    private static string SanitizeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }
}

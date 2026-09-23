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
    /// v1.1.0：登录门禁。
    /// 流程：先弹 LoginWindow（模态）→ 成功才 new MainWindow 并注入账号 → 失败直接退出。
    ///
    /// 两个容易踩的坑，这里都规避了：
    ///   1) 必须先把 ShutdownMode 设成 OnExplicitShutdown。默认的 OnLastWindowClose
    ///      会在登录窗口关闭、主窗口还没创建出来的那一瞬判定"最后一个窗口关了"而退出进程。
    ///   2) 主窗口 Show 之后再交还给默认的 OnLastWindowClose，
    ///      这样玩家关掉主窗口时应用仍会正常退出。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var login = new LoginWindow();
        var passed = login.ShowDialog() == true;

        if (!passed || login.LoggedInUser == null)
        {
            Shutdown();
            return;
        }

        var main = new MainWindow();
        main.ApplyCurrentUser(login.LoggedInUser);
        main.Show();

        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private static void OnUiThreadException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("UI 线程异常 (DispatcherUnhandledException)", e.Exception);
        try
        {
            if (Current?.MainWindow is MainWindow mw)
                StyledDialog.ShowError(mw, "发生未预期错误",
                    "应用拦截到一个错误，已把详细信息保存到桌面 go-crash-*.txt：\n\n" +
                    (e.Exception?.Message ?? "(无消息)") +
                    "\n\n可以继续操作，也建议截图上述文件后重启应用。");
        }
        catch { }
        e.Handled = true;   // 关键：阻止 WPF 默认把进程杀掉
    }

    private static void OnTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("未观察的 Task 异常 (UnobservedTaskException)", e.Exception);
        e.SetObserved();   // 标记为已观察到，避免进程被 GC 回收时终结
    }

    private static void OnAppDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashLog("进程级异常 (AppDomain.UnhandledException)", ex);
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

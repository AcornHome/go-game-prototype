using System.Collections.Concurrent;
using System.Diagnostics;

namespace GoPrototype;

/// <summary>
/// KataGo 子进程封装，走标准 GTP 协议（stdin/stdout）。
/// 启动时调用 boardsize 完成握手（首次会编译神经网络，30s~3min）。
/// </summary>
public class KataGoClient : IDisposable
{
    private Process? _proc;
    private StreamWriter? _stdin;
    private readonly BlockingCollection<string> _lines = new();
    private readonly object _lock = new();
    private bool _disposed;

    public bool IsRunning => _proc is { HasExited: false };

    public KataGoClient(string exePath, string modelPath, string configPath, int boardSize)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"KataGo 可执行文件不存在: {exePath}\n" +
                "下载: https://github.com/lightvector/KataGo/releases");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"KataGo 模型权重不存在: {modelPath}");
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"KataGo 配置文件不存在: {configPath}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"gtp -model \"{modelPath}\" -config \"{configPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
        };

        _proc = new Process { StartInfo = psi };

        // 启动前把 stderr 输出实时打印出来（启动信息 / 编译进度有用）
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.Error.WriteLine($"[KataGo] {e.Data}");
        };

        _proc.Start();
        _proc.BeginErrorReadLine();
        _stdin = _proc.StandardInput;

        // 后台读取 stdout 行
        _ = Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = _proc.StandardOutput.ReadLine()) != null)
                {
                    _lines.Add(line);
                }
            }
            catch { }
        });

        // 给 KataGo 一点时间输出启动日志，避免 sendCommand 误读
        Thread.Sleep(300);

        // 初始化棋盘大小（首次会编译神经网络，耗时较长）
        Console.Error.WriteLine($"[Setup] Sending boardsize {boardSize} (首次会编译神经网络，请耐心等待)...");
        SendCommand($"boardsize {boardSize}");
        Console.Error.WriteLine("[Setup] KataGo ready.");
    }

    /// <summary>
    /// 发送 GTP 命令并读取完整响应（直到空行）。
    /// 响应以 "= " 开头 = 成功，"= " 后是返回内容；以 "? " 开头 = 失败。
    /// </summary>
    public string SendCommand(string cmd)
    {
        if (!IsRunning) throw new InvalidOperationException("KataGo 进程已退出");

        lock (_lock)
        {
            _stdin!.WriteLine(cmd);
            _stdin!.Flush();

            var lines = new List<string>();
            while (true)
            {
                // _lines.Take() 阻塞直到有行
                string line = _lines.Take();
                if (string.IsNullOrEmpty(line)) break;
                lines.Add(line);
            }

            var joined = string.Join("\n", lines);
            // 调试用：取消注释可看每条 GTP 通信
            // Console.Error.WriteLine($"  >> {cmd}");
            // Console.Error.WriteLine($"  << {joined}");
            return joined;
        }
    }

    /// <summary>
    /// 让 AI 走一手。返回 GTP 顶点字符串（"D4"），或 "resign" / "pass"。
    /// </summary>
    public string GenMove(string color)
    {
        // kata-genmove_analyze 后台分析 + genmove 直接出招
        // 用 genmove 就够了；想要分析数据再切到 kata-genmove_analyze
        var resp = SendCommand($"genmove {color}");
        if (resp.StartsWith("?"))
            throw new Exception($"KataGo genmove 失败: {resp}");
        // 响应可能是 "= Q16" 或多行
        var firstLine = resp.Split('\n')[0].Trim();
        return firstLine.TrimStart('=', ' ').Trim();
    }

    /// <summary>
    /// 告诉 KataGo 走了一步棋。返回是否成功。
    /// </summary>
    public bool TryPlay(string color, string gtpVertex)
    {
        var resp = SendCommand($"play {color} {gtpVertex}");
        return !resp.StartsWith("?");
    }

    public void ClearBoard() => SendCommand("clear_board");

    public string Showboard() => SendCommand("showboard");

    public void Quit()
    {
        try
        {
            if (IsRunning) SendCommand("quit");
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Quit(); } catch { }
        try { if (IsRunning) _proc?.Kill(); } catch { }
        _proc?.Dispose();
        _lines.Dispose();
    }
}

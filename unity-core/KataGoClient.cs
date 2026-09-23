using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GoGame.Core;

/// <summary>
/// kata-genmove_analyze 输出的胜率/目差/PV 变化（前 5 手）。
/// 胜率 0..1，目差正=当前玩家（color 参数）领先。
/// </summary>
public class AnalyzeResult
{
    public double Winrate { get; set; }
    public double ScoreLead { get; set; }
    public int Visits { get; set; }
    public List<string> Pv { get; set; } = new();

    public string PvText => Pv.Count == 0 || string.IsNullOrEmpty(Pv[0]) ? "-" : Pv[0];
}

/// <summary>
/// KataGo 子进程封装，走标准 GTP 协议（stdin/stdout）。
/// 启动时调用 boardsize 完成握手（首次会编译神经网络，30s~3min）。
/// 所有命令通过 SendCommand 串行执行（内部加锁，线程安全）。
///
/// **GTP 文本解析**（KataGo v1.18）：
///   kata-genmove_analyze 实际响应是 GTP 文本格式（不是 JSON）：
///     = info move R17 visits ... winrate 0.347028 scoreLead -0.787 ... pv R17 info move Q4 ... pv Q4 ...
///   每个 "info move X" 块是一手推荐；**最后一个 info 块才是 AI 真正下的子**。
///
/// 与引擎无关（无 Unity 依赖），dotnet 可独立跑通冒烟测试。
/// </summary>
public class KataGoClient : IDisposable
{
    private Process? _proc;
    private StreamWriter? _stdin;
    private readonly object _lock = new();
    private bool _disposed;
    private readonly BlockingCollection<string> _responseLines = new();
    private readonly ConcurrentQueue<string> _logQueue = new();
    private CancellationTokenSource? _cts;
    private Task? _readerTask;

    public bool IsRunning => _proc is { HasExited: false };
    public event Action<string>? LogLine;

    public KataGoClient(string exePath, string modelPath, string configPath, int boardSize)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"KataGo 可执行文件不存在: {exePath}");
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
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        _proc = new Process { StartInfo = psi };
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                EnqueueLog(e.Data);
        };
        _proc.Start();
        _proc.BeginErrorReadLine();
        _stdin = _proc.StandardInput;

        _cts = new CancellationTokenSource();
        _readerTask = Task.Run(() => ReaderLoop(_proc.StandardOutput, _cts.Token));

        Thread.Sleep(300);

        EnqueueLog($"正在初始化 {boardSize} 路棋盘（首次会编译神经网络，请耐心等待）...");
        SendCommand($"boardsize {boardSize}");
        EnqueueLog($"KataGo 就绪（{boardSize} 路棋盘）。");
    }

    private async Task ReaderLoop(StreamReader stdout, CancellationToken ct)
    {
        try
        {
            string? line;
            while (!ct.IsCancellationRequested && (line = stdout.ReadLine()) != null)
            {
                try { _responseLines.Add(line, ct); } catch { }
                await Task.Yield();
            }
        }
        catch (Exception ex)
        {
            EnqueueLog($"[reader] 退出：{ex.Message}");
        }
    }

    private void EnqueueLog(string msg)
    {
        _logQueue.Enqueue(msg);
        try { LogLine?.Invoke(msg); } catch { }
    }

    /// <summary>UI 定时取走待显示的日志行（dotnet 冒烟测试用）。</summary>
    public IEnumerable<string> DequeueLogs()
    {
        while (_logQueue.TryDequeue(out var line))
            yield return line;
    }

    /// <summary>
    /// 发送 GTP 命令并读取完整响应（直到空行）。响应以 "= " 开头 = 成功，"? " 开头 = 失败。
    /// </summary>
    public string SendCommand(string cmd)
    {
        if (!IsRunning) throw new InvalidOperationException("KataGo 进程已退出");

        lock (_lock)
        {
            try
            {
                _stdin!.WriteLine(cmd);
                _stdin!.Flush();

                var lines = new List<string>();
                while (true)
                {
                    string line;
                    try
                    {
                        line = _responseLines.Take(_cts!.Token);
                    }
                    catch (OperationCanceledException) { break; }
                    if (string.IsNullOrEmpty(line)) break;
                    lines.Add(line);
                }
                return string.Join("\n", lines);
            }
            finally { /* 模式切换无需 */ }
        }
    }

    /// <summary>让 AI 走一手。返回 GTP 顶点字符串（"D4"），或 "resign" / "pass"。</summary>
    public string GenMove(string color)
    {
        var resp = SendCommand($"genmove {color}");
        if (resp.StartsWith("?")) throw new Exception($"KataGo genmove 失败: {resp}");
        return resp.Split('\n')[0].Trim().TrimStart('=', ' ').Trim();
    }

    /// <summary>告诉 KataGo 走了一步棋。返回是否成功。</summary>
    public bool TryPlay(string color, string gtpVertex)
    {
        var resp = SendCommand($"play {color} {gtpVertex}");
        return !resp.StartsWith("?");
    }

    public void ClearBoard() => SendCommand("clear_board");

    public void SetTimeSettings(double mainTime, double byoYomiSeconds, int byoYomiStones)
        => SendCommand($"time_settings {mainTime} {byoYomiSeconds} {byoYomiStones}");

    public string SetBoardSize(int boardSize)
    {
        if (boardSize != 9 && boardSize != 13 && boardSize != 19)
            throw new ArgumentOutOfRangeException(nameof(boardSize), $"棋盘必须是 9 / 13 / 19，实际 {boardSize}");
        return SendCommand($"boardsize {boardSize}");
    }

    public void SetKomi(double komi)
        => SendCommand($"komi {komi.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>一次性 kata-genmove_analyze：让 AI 思考若干秒，同时输出最终推荐手 + 胜率/目差。
    /// 返回 (AI 落子顶点, AnalyzeResult)。异常时抛出。</summary>
    public (string vertex, AnalyzeResult? analysis) GenMoveAnalyzeWithResult(string color, double seconds)
    {
        var resp = SendCommand($"kata-genmove_analyze {color} {seconds.ToString(CultureInfo.InvariantCulture)}");
        if (resp.StartsWith("?"))
            throw new Exception($"KataGo kata-genmove_analyze 失败: {resp}");

        var body = resp.TrimStart('=', ' ', '?').Trim();

        var infoPositions = new List<int>();
        int from = 0;
        while ((from = body.IndexOf("info ", from, StringComparison.Ordinal)) >= 0)
        {
            infoPositions.Add(from);
            from++;
        }

        if (infoPositions.Count == 0)
        {
            var vertex = GenMove(color);
            return (vertex, null);
        }

        int lastInfoStart = infoPositions[^1];
        string lastInfo = body.Substring(lastInfoStart);

        var result = new AnalyzeResult
        {
            Winrate = ReadDouble(lastInfo, "winrate") ?? 0.0,
            ScoreLead = ReadDouble(lastInfo, "scoreLead") ?? 0.0,
            Visits = (int)(ReadDouble(lastInfo, "visits") ?? 0)
        };

        string? chosenMove = ReadToken(lastInfo, "move");

        foreach (Match m in Regex.Matches(lastInfo, @"pv\s+([A-T]\d+|pass|resign)"))
            result.Pv.Add(m.Groups[1].Value);

        if (string.IsNullOrEmpty(chosenMove) && result.Pv.Count > 0)
            chosenMove = result.Pv[0];

        return (chosenMove ?? "pass", result);
    }

    private static string? ReadToken(string text, string key)
    {
        int i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return null;
        i += key.Length;
        while (i < text.Length && text[i] == ' ') i++;
        int start = i;
        while (i < text.Length && text[i] != ' ' && text[i] != '\n' && text[i] != '\t') i++;
        return text.Substring(start, i - start).Trim();
    }

    private static double? ReadDouble(string text, string key)
    {
        var t = ReadToken(text, key);
        if (t == null) return null;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>终局后计算胜负，返回如 "B+3.5" / "W+2.5" / "0"。</summary>
    public string FinalScore()
    {
        var resp = SendCommand("final_score");
        return resp.Split('\n')[0].Trim().TrimStart('=', ' ').Trim();
    }

    public string SendRaw(string cmd) => SendCommand(cmd);

    public (int black, int white) CountStones()
    {
        var sb = SendCommand("showboard");
        int b = 0, w = 0;
        foreach (var ch in sb) { if (ch == 'X') b++; else if (ch == 'O') w++; }
        return (b, w);
    }

    public void Quit()
    {
        try
        {
            if (!IsRunning) return;
            lock (_lock)
            {
                _stdin!.WriteLine("quit");
                _stdin!.Flush();
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Quit(); } catch { }
        try
        {
            _cts?.Cancel();
            _readerTask?.Wait(500);
        }
        catch { }
        try { if (IsRunning) _proc?.Kill(); } catch { }
        _proc?.Dispose();
        _cts?.Dispose();
        _responseLines.Dispose();
    }
}

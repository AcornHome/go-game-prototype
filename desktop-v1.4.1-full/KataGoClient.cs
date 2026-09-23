using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GoGame;

/// <summary>
/// KataGo 子进程封装，走标准 GTP 协议（stdin/stdout）。
/// 启动时调用 boardsize 完成握手（首次会编译神经网络，30s~3min）。
/// 所有命令通过 SendCommand 串行执行（内部加锁，线程安全）。
/// 日志全部先入队，UI 通过 DispatcherTimer 读取，避免后台线程直接碰 UI。
///
/// **统一 stdio reader**：
///   一个后台 Reader 任务持续读取 KataGo 的 stdout，按当前协议状态路由：
///     - 普通 GTP 模式：把行累积到 _responseBlocks，直到收到空行，认为一个完整响应
///     - kata-analyze 模式：每行解析 JSON，触发 AnalyzeUpdate
///   切换模式时发送"打断命令"（在 analyze 模式发 `name`，普通模式立即生效）。
/// </summary>
public class KataGoClient : IDisposable
{
    private Process? _proc;
    private StreamWriter? _stdin;

    private readonly object _lock = new();
    private bool _disposed;

    // 协议状态机：Mode 由 SendCommand 控制（lock 内），Reader 按模式路由行
    private enum ProtoMode { Idle, GtpWaitingResponse, KataAnalyze }
    private volatile ProtoMode _mode = ProtoMode.Idle;

    // 完整 GTP 响应：多行 + 末尾空行
    private readonly BlockingCollection<string> _responseLines = new();

    // 日志：stderr + 启动信息等普通日志
    private readonly ConcurrentQueue<string> _logQueue = new();

    private CancellationTokenSource? _cts;
    private Task? _readerTask;

    /// <summary>v1.2.2: 当前 KataGo 的 max_visits 设置（每次 SetMaxVisits 后更新）。提示功能临时调高 visits 后能还原回 AI 档位。</summary>
    private int _currentMaxVisits = 0;

    public bool IsRunning => _proc is { HasExited: false };

    public event Action<string>? LogLine;
#pragma warning disable CS0067
    public event Action<AnalyzeResult>? AnalyzeUpdate;
#pragma warning restore CS0067

    public KataGoClient(string exePath, string modelPath, string configPath, int boardSize)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"KataGo 可执行文件不存在: {exePath}\n下载: https://github.com/lightvector/KataGo/releases");
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
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        _proc = new Process { StartInfo = psi };

        // stderr 实时输出（启动信息 / 编译进度）
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                EnqueueLog(e.Data);
        };

        _proc.Start();
        _proc.BeginErrorReadLine();
        _stdin = _proc.StandardInput;

        // 启动统一 reader
        _cts = new CancellationTokenSource();
        _readerTask = Task.Run(() => ReaderLoop(_proc.StandardOutput, _cts.Token));

        // 给 KataGo 一点时间输出启动日志
        Thread.Sleep(300);

        EnqueueLog($"正在初始化 {boardSize} 路棋盘（首次会编译神经网络，请耐心等待）...");
        SendCommand($"boardsize {boardSize}");
        EnqueueLog($"KataGo 就绪（{boardSize} 路棋盘）。");
    }

    /// <summary>统一 reader 循环：将 KataGo 的 stdout 行全部入队到 _responseLines，等待 GTP 命令读取。</summary>
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

    /// <summary>UI 定时取走待显示的日志行。</summary>
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
            // 实时分析已禁用，不需要 ISR 切协议模式
            _mode = ProtoMode.GtpWaitingResponse;
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
            finally
            {
                _mode = ProtoMode.Idle;
            }
        }
    }

    /// <summary>让 AI 走一手。返回 GTP 顶点字符串（"D4"），或 "resign" / "pass"。</summary>
    public string GenMove(string color)
    {
        var resp = SendCommand($"genmove {color}");
        if (resp.StartsWith("?"))
            throw new Exception($"KataGo genmove 失败: {resp}");
        return resp.Split('\n')[0].Trim().TrimStart('=', ' ').Trim();
    }

    /// <summary>告诉 KataGo 走了一步棋。返回是否成功。</summary>
    public bool TryPlay(string color, string gtpVertex)
    {
        var resp = SendCommand($"play {color} {gtpVertex}");
        return !resp.StartsWith("?");
    }

    public void ClearBoard() => SendCommand("clear_board");

    /// <summary>设置 AI 每手思考时间（秒）。数值越小越弱。</summary>
    public void SetTimeSettings(double mainTime, double byoYomiSeconds, int byoYomiStones)
    {
        SendCommand($"time_settings {mainTime} {byoYomiSeconds} {byoYomiStones}");
    }

    /// <summary>设置每手最大搜索节点数（maxVisits）。**这是控制 AI 棋力最精准的开关**——比时间限制更稳。
    /// - 50 = 入门级（人类新手水平）
    /// - 500 = 业余初段
    /// - 5000 = 业余高段
    /// - 50000+ = 职业级
    /// 注意 KataGo 的 `genmove` 命令**总是尝试**搜到 maxVisits 上限；
    /// 同时设了 maxTime 的话哪个先到就以哪个为准。
    /// v1.2.2: 缓存 _currentMaxVisits，提示功能临时调高 visits 后能还原回 AI 档位。
    /// </summary>
    public void SetMaxVisits(int maxVisits)
    {
        if (maxVisits <= 0) return;
        _currentMaxVisits = maxVisits;
        SendCommand($"kata-set-rules default");
        SendCommand($"kata-set-max-visits {maxVisits}");
    }

    /// <summary>运行时切换棋盘大小（GTP boardsize）。成功后 KataGo 内部棋盘重置为空盘。
    /// 失败时 KataGo 会返回 "?" 开头的响应，正常返回 "=" 开头。
    /// 用途：让用户在不重启 KataGo（30s~3min 启动）的前提下切换 9/13/19 路。</summary>
    public string SetBoardSize(int boardSize)
    {
        if (boardSize != 9 && boardSize != 13 && boardSize != 19)
            throw new ArgumentOutOfRangeException(nameof(boardSize), $"棋盘必须是 9 / 13 / 19，实际 {boardSize}");
        return SendCommand($"boardsize {boardSize}");
    }

    /// <summary>设置贴目（komi）。中国规则通常 7.5（黑贴白 7.5 子）。设为 0 即不贴目。</summary>
    public void SetKomi(double komi)
    {
        SendCommand($"komi {komi.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// 一次性 kata-genmove_analyze：让 AI 思考若干秒，同时输出最终推荐手 + 胜率/目差。
    /// 返回 (AI 落子顶点, AnalyzeResult)。KataGo 不支持该命令（返回 ?）时**自动降级**到 genmove，
    /// 不抛异常，保证旧版 KataGo / 配置缺失时 V1.0 仍可用。
    /// **KataGo v1.18 实际响应**：
    ///   思考期间持续输出多行 `info move X visits N winrate W scoreLead L ... pv X P1 P2 ...`
    ///   （每个 visit 更新一行，每次若干 KB），最终以 GTP 标准响应 `= MOVE\n\n` 结束。
    /// **chosen move** 必须从最后一行 `= MOVE` 解析（GTP 规范），不能从 info 块推断：
    ///   - 早期 info 块的 visits=0 是无效的初始化信息
    ///   - 内嵌 `pv` 的子节点 info 是子树评估，**不是** AI 真下的子
    /// **analysis** 用最后一个（visits 最大的）info 块，那是 rootInfo 最终状态。
    /// </summary>
    public (string vertex, AnalyzeResult? analysis) GenMoveAnalyzeWithResult(string color, double seconds)
    {
        var resp = SendCommand($"kata-genmove_analyze {color} {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (resp.StartsWith("?"))
        {
            // KataGo 拒绝 / 不支持（极旧版本 / 编译时未开分析模块）→ 降级到普通 genmove，V1.0 仍可用
            EnqueueLog($"[warn] kata-genmove_analyze 不支持，降级 genmove: {resp.Split('\n')[0].Trim()}");
            var fallbackVertex = GenMove(color);
            return (fallbackVertex, null);
        }

        // === 1) 解析 chosen move ===
        string chosenMove = ParseChosenMove(resp);

        // === 2) 解析 analysis：找 visits 最大的 info 块（rootInfo 最终状态）===
        AnalyzeResult? result = null;
        var infoPositions = new List<int>();
        int from = 0;
        while ((from = resp.IndexOf("info ", from, StringComparison.Ordinal)) >= 0)
        {
            infoPositions.Add(from);
            from++;
        }
        if (infoPositions.Count > 0)
        {
            // 找出 visits 最大的 info 块作为 rootInfo（思考最深入时 stats 最准）
            int bestStart = infoPositions[0];
            int bestVisits = -1;
            for (int i = 0; i < infoPositions.Count; i++)
            {
                int blockEnd = (i + 1 < infoPositions.Count) ? infoPositions[i + 1] : resp.Length;
                if (blockEnd <= infoPositions[i]) continue;
                var block = resp.Substring(infoPositions[i], blockEnd - infoPositions[i]);
                int visits = (int)(ReadDouble(block, "visits") ?? 0);
                if (visits > bestVisits)
                {
                    bestVisits = visits;
                    bestStart = infoPositions[i];
                }
            }
            int finalEnd = resp.Length;
            int finalLen = Math.Min(finalEnd - bestStart, resp.Length - bestStart);
            var rootBlock = resp.Substring(bestStart, finalLen);

            result = new AnalyzeResult
            {
                Winrate = ReadDouble(rootBlock, "winrate") ?? 0.0,
                ScoreLead = ReadDouble(rootBlock, "scoreLead") ?? 0.0,
                Visits = bestVisits,
            };
            // 主推变化：扫描该 info 块内 "pv X"
            var pvMatches = System.Text.RegularExpressions.Regex.Matches(rootBlock, @"pv\s+([A-T]\d+|pass|resign)");
            foreach (System.Text.RegularExpressions.Match m in pvMatches)
                result.Pv.Add(m.Groups[1].Value);
        }

        return (chosenMove, result);
    }

    // ==================== 提示（Hint）====================

    /// <summary>
    /// **只在玩家回合调用**。让 KataGo 替玩家试算一手，返回若干候选落点，**不真正落子**。
    ///
    /// 实现思路（刻意复用已验证的 kata-genmove_analyze，不再新开 kata-analyze 协议）：
    ///   1. 发 `kata-genmove_analyze {玩家色} {秒数}` —— KataGo 会真的落一手并给出全部候选信息
    ///   2. 解析所有 `info move X visits N winrate W scoreLead L pv ...` 得到候选排名
    ///   3. 立刻发 GTP `undo` 把那手试算棋撤掉，棋盘回到原状
    ///
    /// 为什么不直接用 kata-analyze：持续输出模式与单 stdin/stdout 管道上的其他 GTP 命令
    /// 存在时序竞态（见 StartAnalyze 的注释），而 genmove_analyze + undo 已在 AiTurn 里
    /// 跑得很稳，照抄这条路风险最低。
    ///
    /// 注意：这里 color 传的是**玩家颜色**，所以返回的 Winrate / ScoreLead 直接就是玩家视角，
    /// 不需要像 AI 落子那样做 1 - x 的换算。
    /// </summary>
    /// <param name="color">"B" 或 "W"，必须是当前该走棋的一方（玩家）</param>
    /// <param name="maxVisits">提示的搜索节点上限（v1.2.2 新增：保证提示方比 AI 强）</param>
    /// <param name="maxSeconds">思考秒数上限（防止引擎卡死）</param>
    /// <param name="maxCandidates">最多返回几个候选点</param>
    /// <returns>null = KataGo 不支持该命令 / 引擎未就绪</returns>
    public HintResult? AnalyzeHint(string color, int maxVisits, double maxSeconds, int maxCandidates = 3)
    {
        // v1.2.2: 临时给 KataGo 设置更高的 max_visits（提示用），算完后立刻还原成 AI 档位的 visits
        // 否则下次 AI 落子也会用 hint 的高 visits，玩家会等很久。
        int originalVisits = _currentMaxVisits;
        if (maxVisits > originalVisits)
        {
            SendCommand($"kata-set-max-visits {maxVisits}");
        }

        try
        {
            var resp = SendCommand(
                $"kata-genmove_analyze {color} {maxSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (resp.StartsWith("?"))
            {
                EnqueueLog($"[warn] 提示功能不可用（kata-genmove_analyze 被拒）: {resp.Split('\n')[0].Trim()}");
                return null;
            }

            return ParseAndUndoHint(resp, color, maxCandidates);
        }
        finally
        {
            // 无论成败都要还原 max_visits，否则下一手 AI 会用提示的高算力
            if (maxVisits > originalVisits)
            {
                SendCommand($"kata-set-max-visits {originalVisits}");
            }
        }
    }

    private HintResult ParseAndUndoHint(string resp, string color, int maxCandidates)
    {
        // ⚠ 两个"最佳手"不是一回事，必须分开用（这是踩过的坑）：
        //   actualMove = KataGo **真的落下去**的那手（只出现在响应末尾的 play/= 行）
        //   candidates[0] = info 块里 visits 最高的候选（纯粹的"最强手"）
        //   两者绝大多数情况相同，但 KataGo 带采样时可能不同（曾出现 info 说 R14、实际下 R17）。
        //   - 撤销与否只看 actualMove（它才决定 KataGo 内部有没有多出一手）
        //   - 展示给玩家用 candidates[0]（不受采样随机性影响，是真正的推荐手）
        string actualMove = ParseChosenMove(resp);

        var result = new HintResult
        {
            BestMove = actualMove,   // 先放 actualMove，下面若有候选再换成 visits 最高的
        };

        // 候选手：同一个顶点会随每次 interval 重复出现，只保留 visits 最大的那条（思考最充分）
        var byVertex = new Dictionary<string, HintCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in resp.Split('\n'))
        {
            var t = rawLine.Trim();
            if (!t.StartsWith("info move ", StringComparison.Ordinal)) continue;

            var rest = t.Substring("info move ".Length).Trim();
            int sp = rest.IndexOf(' ');
            if (sp <= 0) continue;
            var vertex = rest.Substring(0, sp);
            if (vertex.Length == 0) continue;

            int visits = (int)(ReadDouble(t, "visits") ?? 0);
            if (byVertex.TryGetValue(vertex, out var existing) && existing.Visits >= visits)
                continue;

            byVertex[vertex] = new HintCandidate
            {
                Vertex = vertex,
                Visits = visits,
                Winrate = ReadDouble(t, "winrate") ?? 0.0,
                ScoreLead = ReadDouble(t, "scoreLead") ?? 0.0,
                Pv = ReadPvTokens(t, 4),
            };
        }

        var ordered = byVertex.Values.OrderByDescending(c => c.Visits).ToList();

        if (ordered.Count > 0)
        {
            // 剔除"几乎没被搜索过"的候选：KataGo 有时会顺手碰一下某个点（visits=1），
            // 这种点没有参考价值，标到棋盘上反而误导玩家。
            // 门槛 = 最佳手 visits 的 3%（且至少 2 次）。
            int floor = Math.Max(2, (int)(ordered[0].Visits * 0.03));
            ordered = ordered.Where(c => c.Visits >= floor)
                             .Take(Math.Max(1, maxCandidates))
                             .ToList();
        }
        result.Candidates = ordered;

        // 展示用的胜率/目差取"最佳手"的值（比 rootInfo 的加权平均更能代表"照我说的下会怎样"）
        if (result.Candidates.Count > 0)
        {
            var best = result.Candidates[0];
            // 试算手若不是解析出来的顶点，就以候选里 visits 最高的为准（更可靠）
            result.BestMove = best.Vertex;
            result.Winrate = best.Winrate;
            result.ScoreLead = best.ScoreLead;
        }

        // === 撤销试算手，把棋盘还原 ===
        // pass 也占一个手数（KataGo 会记一手虚手），同样要撤；
        // 只有 resign 不落子，撤了反而会误撤玩家/AI 上一手真棋。
        // 依据必须是 actualMove（真落的那手），**不能**用 candidates[0]。
        if (!string.Equals(actualMove, "resign", StringComparison.OrdinalIgnoreCase)
            && IsPlausibleVertex(actualMove))
        {
            var undoResp = SendCommand("undo");
            if (undoResp.StartsWith("?"))
            {
                // 极少见。置位后由 GameController 重建整个局面（clear_board + 重放历史）兜底。
                result.UndoFailed = true;
                EnqueueLog("[warn] 提示试算手撤销失败，将重建 KataGo 局面");
            }
        }

        return result;
    }

    /// <summary>
    /// 从 kata-genmove_analyze 响应里解析出 AI 实际选择的手。
    /// KataGo v1.18 的响应**末尾**是 `play MOVE`（自定义格式），不是标准 GTP 的 `= MOVE`，
    /// 两种都要兼容：从末尾向前扫，取第一个非空且匹配的 `play MOVE` 或 `= MOVE`。
    /// **不能从 info 块推断**：早期 info 的 visits=0 是无效初始化值，内嵌 pv 的子节点
    /// info 是子树评估，都不是 AI 真下的子。
    /// </summary>
    private static string ParseChosenMove(string resp, string fallback = "pass")
    {
        var lines = resp.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (string.IsNullOrEmpty(t)) continue;

            // KataGo 专属: `play MOVE`
            if (t.Length > 4 && t.StartsWith("play ", StringComparison.OrdinalIgnoreCase))
                return t.Substring(5).Trim();

            // 标准 GTP: `= MOVE`
            if (t.StartsWith("=") && t.Length >= 2 && t[1] != '=' && !t.StartsWith("=?"))
                return t.Substring(1).Trim();
        }
        return fallback;
    }

    /// <summary>判断字符串是否像一个 GTP 顶点（"D4"）或 pass。
    /// 用于决定"试算手是否真的占了手数、需不需要 undo"，避免撤销错位。</summary>
    private static bool IsPlausibleVertex(string v)
    {
        if (string.IsNullOrEmpty(v)) return false;
        if (string.Equals(v, "pass", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Length < 2 || v.Length > 3) return false;
        char c = char.ToUpperInvariant(v[0]);
        if (c < 'A' || c > 'T' || c == 'I') return false;   // GTP 坐标跳过 I
        return int.TryParse(v.Substring(1), out int n) && n >= 1 && n <= 25;
    }

    /// <summary>取 info 行末尾 `pv` 后的前 max 个手顺（跳过 pv 自身重复的顶点由调用方处理）。</summary>
    private static List<string> ReadPvTokens(string text, int max)
    {
        var result = new List<string>();
        int i = text.LastIndexOf(" pv ", StringComparison.Ordinal);
        if (i < 0) return result;
        i += 4;
        while (result.Count < max)
        {
            while (i < text.Length && text[i] == ' ') i++;
            if (i >= text.Length) break;
            int start = i;
            while (i < text.Length && text[i] != ' ' && text[i] != '\n' && text[i] != '\t') i++;
            var tok = text.Substring(start, i - start).Trim();
            if (tok.Length == 0) break;
            result.Add(tok);
        }
        return result;
    }

    /// <summary>在 text 中找 "key value" 紧跟的一个 token（空格分隔）。</summary>
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

    /// <summary>在 text 中找 "key value"，value 是数字。</summary>
    private static double? ReadDouble(string text, string key)
    {
        var t = ReadToken(text, key);
        if (t == null) return null;
        if (double.TryParse(t, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }

    /// <summary>终局后计算胜负，返回如 "B+3.5" / "W+2.5" / "0"。</summary>
    public string FinalScore()
    {
        var resp = SendCommand("final_score");
        return resp.Split('\n')[0].Trim().TrimStart('=', ' ').Trim();
    }

    /// <summary>发送任意 GTP 命令并返回完整响应（用于调试）。</summary>
    public string SendRaw(string cmd)
    {
        return SendCommand(cmd);
    }

    /// <summary>统计当前黑白子数（kata-showboard 解析）。</summary>
    public (int black, int white) CountStones()
    {
        var sb = SendCommand("showboard");
        int b = 0, w = 0;
        foreach (var ch in sb) { if (ch == 'X') b++; else if (ch == 'O') w++; }
        return (b, w);
    }

    /// <summary>
    /// 启动 kata-analyze 后台持续分析（每 50 厘秒 = 0.5s 更新一次）。
    /// 锁内切换 reader 协议模式：reader 继续读同一 stdout，但按 ProtoMode.KataAnalyze 路由。
    /// </summary>
    public void StartAnalyze(string color, int intervalCs = 50)
    {
        // ⚠ 实时分析功能已禁用：kata-analyze 持续输出与 GTP 命令在单 stdin/stdout 管道
        // 上有不可避免的时序竞态（reader 异步读 vs main thread 切模式）。等独立分析模块。
        // 保留接口不让 UI 报错。
    }

    /// <summary>外部调用停止 kata-analyze。</summary>
    public void StopAnalyze()
    {
        // 已禁用，无操作
    }

    /// <summary>
    /// 打断 KataGo 当前 kata-analyze，并把 "= cmdname" 响应排掉，保证 _responseLines 干净。
    /// **必须在 lock 内调用**。
    /// </summary>
    private void InterruptAnalyzeInternal()
    {
        // 实时分析已禁用，无操作
    }

    public void Quit()
    {
        try
        {
            if (!IsRunning) return;
            lock (_lock)
            {
                if (_mode == ProtoMode.KataAnalyze) InterruptAnalyzeInternal();
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
        try { StopAnalyze(); } catch { }
        try { Quit(); } catch { }
        try
        {
            // 先取消 reader，避免 ReadLine 阻塞 Dispose
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

/// <summary>kata-analyze 输出 JSON 解析结果。胜率/目差单位都是给当前玩家（即参数 color）。</summary>
public class AnalyzeResult
{
    public double Winrate { get; set; }       // 0..1，当前玩家胜率
    public double ScoreLead { get; set; }     // 目差（当前玩家视角，正=领先）
    public List<string> Pv { get; set; } = new();  // 推荐变化（前几个手顺）
    public int Visits { get; set; }

    public static AnalyzeResult? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var r = new AnalyzeResult();

            if (root.TryGetProperty("winrate", out var w))
                r.Winrate = w.GetDouble();
            if (root.TryGetProperty("scoreLead", out var s))
                r.ScoreLead = s.GetDouble();
            if (root.TryGetProperty("visits", out var v))
                r.Visits = v.GetInt32();

            if (root.TryGetProperty("pv", out var pv) && pv.ValueKind == JsonValueKind.Array)
            {
                int count = 0;
                foreach (var m in pv.EnumerateArray())
                {
                    if (count++ >= 5) break;
                    r.Pv.Add(m.GetString() ?? "");
                }
            }
            return r;
        }
        catch
        {
            return null;
        }
    }

    public string PvText => Pv.Count == 0 || string.IsNullOrEmpty(Pv[0]) ? "-" : Pv[0];
}

/// <summary>提示功能返回的单个候选落点。</summary>
public class HintCandidate
{
    /// <summary>GTP 顶点，如 "D4"</summary>
    public string Vertex { get; set; } = "";
    /// <summary>KataGo 给这手的搜索次数，越多代表 AI 越确信</summary>
    public int Visits { get; set; }
    /// <summary>走了这手之后的胜率（玩家视角，0..1）</summary>
    public double Winrate { get; set; }
    /// <summary>走了这手之后的目差（玩家视角，正=领先）</summary>
    public double ScoreLead { get; set; }
    /// <summary>后续主要变化（前几手，含本手）</summary>
    public List<string> Pv { get; set; } = new();

    /// <summary>后续变化文字（跳过本手，只显示从第 2 手开始的变化）。</summary>
    public string FollowUpText => Pv.Count > 1 ? string.Join(" ", Pv.Skip(1).Take(3)) : "";
}

/// <summary>提示功能的完整结果：最佳手 + 若干备选。</summary>
public class HintResult
{
    /// <summary>AI 最推荐的一手（GTP 顶点）。可能是 "pass"。</summary>
    public string BestMove { get; set; } = "";
    /// <summary>候选列表，已按推荐度（visits）从高到低排序，第 0 个即 BestMove。</summary>
    public List<HintCandidate> Candidates { get; set; } = new();
    /// <summary>走最佳手后的玩家胜率（0..1）</summary>
    public double Winrate { get; set; }
    /// <summary>走最佳手后的玩家目差（正=领先）</summary>
    public double ScoreLead { get; set; }
    /// <summary>
    /// 试算手撤销失败 = KataGo 内部棋盘多了一手、与本局状态不一致。
    /// 置位时调用方**必须**重建局面（clear_board + 重放历史），否则后续落子会错序。
    /// </summary>
    public bool UndoFailed { get; set; }
}

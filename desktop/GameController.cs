namespace GoGame;

/// <summary>
/// 单局游戏状态机：跟踪回合、谁是 AI、落子流程。
/// 关键原则：先让 KataGo 接受（验证合法性），再更新本地棋盘。
/// 所有棋盘状态变化都走 ApplyMove，自动维护快照栈（用于棋谱回放）。
/// </summary>
public class GameController
{
    private readonly Board _board;
    private readonly KataGoClient _ai;

    public Stone HumanColor { get; }
    public Stone AiColor => HumanColor == Stone.Black ? Stone.White : Stone.Black;
    public Board Board => _board;

    /// <summary>上一次 AI 走完后的形势分析（胜率/目差/主推变化）。null = 还没走第一步。</summary>
    public AnalyzeResult? LastAnalysis { get; private set; }

    /// <summary>
    /// 每手棋走完后的局面评估历史。**长度等于 MoveHistory.Count**。
    /// 每条记录是"该手棋刚落完，AI 视角的胜率/目差"。胜率统一换算成玩家视角（人类胜率 = 1 - AI 胜率）。
    /// 用途：终局后生成 AI 复盘报告（胜率曲线 / 转折点 / 妙招 vs 坏手）。
    /// </summary>
    public IReadOnlyList<MoveAnalysis> MoveAnalyses => _analyses;
    private readonly List<MoveAnalysis> _analyses = new();

    public GameController(Board board, KataGoClient ai, Stone humanColor)
    {
        _board = board;
        _ai = ai;
        HumanColor = humanColor;
        _board.NextToPlay = Stone.Black;
    }

    public bool IsHumanTurn => _board.NextToPlay == HumanColor;

    /// <summary>人类落子。先发给 KataGo 验证，成功后才落本地。</summary>
    public (bool ok, string message, IReadOnlyList<(int x, int y)> captures) PlayHuman(int x, int y)
    {
        if (!IsHumanTurn) return (false, "Not your turn", Array.Empty<(int, int)>());
        if (!_board.IsInBounds(x, y)) return (false, "Point is outside the board", Array.Empty<(int, int)>());
        if (_board.Get(x, y) != Stone.Empty) return (false, "That point is already occupied", Array.Empty<(int, int)>());

        var vertex = Board.ToGtpVertex(x, y, _board.Size);
        var color = HumanColor == Stone.Black ? "B" : "W";

        if (!_ai.TryPlay(color, vertex))
            return (false, $"KataGo rejected the move: {vertex}", Array.Empty<(int, int)>());

        var captures = ApplyMove(x, y, HumanColor);
        return (true, $"You played {vertex}", captures);
    }

    /// <summary>AI 落子。返回 (GTP 顶点, 被提位置列表)。
    /// 顶点 = "D4" / "pass" / "resign"。
    /// 使用 kata-genmove_analyze 落子：顺带拿到当前局面的胜率/目差，零额外开销。</summary>
    public async Task<(string vertex, IReadOnlyList<(int x, int y)> captures)> PlayAiAsync(double thinkSeconds)
    {
        if (IsHumanTurn) throw new InvalidOperationException("It is not the AI's turn yet");

        var color = AiColor == Stone.Black ? "B" : "W";
        // kata-genmove_analyze 会落子 + 返回 rootInfo，放后台线程不卡 UI
        (string vertex, AnalyzeResult? analysis) = await Task.Run(() => _ai.GenMoveAnalyzeWithResult(color, thinkSeconds));
        LastAnalysis = analysis;

        IReadOnlyList<(int x, int y)> captures = Array.Empty<(int, int)>();

        if (vertex == "pass" || vertex == "resign")
        {
            ApplyPassOrResign(AiColor, vertex);
        }
        else
        {
            var (x, y) = Board.FromGtpVertex(vertex, _board.Size);
            captures = ApplyMove(x, y, AiColor);
        }

        // 记录这一手棋走完后的局面评估（AI 视角胜率 → 玩家视角）
        if (analysis != null)
        {
            _analyses.Add(new MoveAnalysis
            {
                MoveIndex = _board.MoveHistory.Count,
                WhoMoved = AiColor,
                Vertex = vertex,
                HumanWinrate = Math.Clamp(1 - analysis.Winrate, 0, 1),
                HumanScoreLead = -analysis.ScoreLead,
                PvText = analysis.Pv.Count > 0 ? string.Join(" ", analysis.Pv.Take(3)) : "",
            });
        }
        return (vertex, captures);
    }

    /// <summary>AI 提示：让玩家在不知道怎么下时求一手建议。**不改变本局任何状态**（不落子、不入历史）。
    /// 返回 null = 引擎不支持或不在玩家回合。</summary>
    /// <param name="maxVisits">提示的搜索节点上限（保证比 AI 强）</param>
    /// <param name="maxSeconds">提示的时间上限（防止 KataGo 引擎异常卡死）</param>
    public async Task<HintResult?> RequestHintAsync(int maxVisits, double maxSeconds)
    {
        // 只在玩家回合有意义：AI 回合时棋盘轮次不同，算出来的手不是玩家能下的
        if (!IsHumanTurn) return null;

        var color = HumanColor == Stone.Black ? "B" : "W";
        var result = await Task.Run(() => _ai.AnalyzeHint(color, maxVisits, maxSeconds));

        // 试算手没撤干净 → KataGo 内部比本局多一手。必须重建，否则后面所有落子都会错序。
        if (result is { UndoFailed: true })
            await Task.Run(() => ResyncAiFromBoard());

        return result;
    }

    /// <summary>人类虚手（pass）。</summary>
    public (bool ok, string message) PassHuman()
    {
        if (!IsHumanTurn) return (false, "Not your turn");
        var color = HumanColor == Stone.Black ? "B" : "W";
        if (!_ai.TryPlay(color, "pass"))
            return (false, "KataGo rejected the pass");
        ApplyPassOrResign(HumanColor, "pass");
        return (true, "You passed");
    }

    public void ResignHuman()
    {
        var color = HumanColor == Stone.Black ? "B" : "W";
        _ai.TryPlay(color, "resign");
        ApplyPassOrResign(HumanColor, "resign");
    }

    /// <summary>悔棋：撤销最后两步（AI 一步 + 人类一步），回到人类回合。
    /// 通过重放 MoveHistory 重建棋盘和 KataGo 状态。</summary>
    public (bool ok, string message) Undo()
    {
        if (_board.MoveHistory.Count < 2)
            return (false, "Not enough moves to undo");

        var moves = new List<string>(_board.MoveHistory);
        moves.RemoveAt(moves.Count - 1);
        moves.RemoveAt(moves.Count - 1);

        // 重建 KataGo 状态（clear_board + 重放，比 undo 命令更可靠）
        _ai.ClearBoard();
        foreach (var m in moves)
        {
            char color = char.ToUpper(m[0]);
            string v = m.Substring(1);
            _ai.TryPlay(color.ToString(), v);
        }

        RebuildLocalFromHistory(moves);
        return (true, "Undid two moves");
    }

    /// <summary>终局数子：双方 pass 后让 KataGo 计算胜负。</summary>
    public string FinishAndScore()
    {
        var humanC = HumanColor == Stone.Black ? "B" : "W";
        var aiC = AiColor == Stone.Black ? "B" : "W";
        _ai.TryPlay(humanC, "pass");
        _ai.TryPlay(aiC, "pass");
        ApplyPassOrResign(HumanColor, "pass");
        ApplyPassOrResign(AiColor, "pass");
        return _ai.FinalScore();
    }

    // ---- 内部辅助 ----

    /// <summary>落子并自动更新快照栈、NextToPlay、MoveHistory。返回被提位置列表。</summary>
    private IReadOnlyList<(int x, int y)> ApplyMove(int x, int y, Stone stone)
    {
        var (placed, captures) = _board.Place(x, y, stone);  // Place 自动维护快照 + 提子
        if (!placed)
            throw new InvalidOperationException($"Local rules rejected the move at ({x},{y}) (suicide or conflict)");
        var vertex = Board.ToGtpVertex(x, y, _board.Size);
        var colorLower = stone == Stone.Black ? "b" : "w";
        _board.RecordMove($"{colorLower}{vertex}");
        _board.NextToPlay = stone == Stone.Black ? Stone.White : Stone.Black;
        return captures;
    }

    private void ApplyPassOrResign(Stone stone, string action)
    {
        // pass/resign 不改变 Grid，但要更新快照（标记回合切换）和 NextToPlay
        _board.Snapshots.Add((Stone[,])_board.Grid.Clone());
        var colorLower = stone == Stone.Black ? "b" : "w";
        _board.RecordMove($"{colorLower}{action}");
        _board.NextToPlay = stone == Stone.Black ? Stone.White : Stone.Black;
    }

    /// <summary>回放 / 悔棋后，把当前棋盘的本地状态同步给 KataGo（让 AI 重新接管）。
    /// 用 clear_board + 重放整段历史，比 setboard + 一条条 play 更可靠。</summary>
    public void ResyncAiFromBoard()
    {
        _ai.ClearBoard();
        foreach (var m in _board.MoveHistory)
        {
            char color = char.ToUpper(m[0]);
            string v = m.Substring(1);
            _ai.TryPlay(color.ToString(), v);
        }
        _board.NextToPlay = HumanColor;   // 重放后强制回到人类回合
    }

    private void RebuildLocalFromHistory(List<string> moves)
    {
        _board.Reset();
        Stone expected = Stone.Black;
        foreach (var m in moves)
        {
            char color = m[0];
            Stone s = color == 'b' ? Stone.Black : Stone.White;
            if (s != expected)
                throw new InvalidOperationException($"Move {m} color does not match the turn (expected {expected})");
            string v = m.Substring(1);
            if (v == "pass" || v == "resign")
            {
                ApplyPassOrResign(s, v);
            }
            else
            {
                var (x, y) = Board.FromGtpVertex(v, _board.Size);
                ApplyMove(x, y, s);
            }
            expected = s == Stone.Black ? Stone.White : Stone.Black;
        }
        // RebuildLocalFromHistory 用于悔棋，期望回到人类回合
        _board.NextToPlay = HumanColor;
    }
}

/// <summary>
/// 一手棋走完后的局面评估（玩家视角）。由 GameController 累积，供终局复盘报告使用。
/// 注意：评估是 AI 走完子时 KataGo 给出的（kata-genmove_analyze），所以 MoveAnalyses[i] 对应 MoveHistory[i]（含 AI 与人类交替落子）。
/// </summary>
public class MoveAnalysis
{
    /// <summary>1-based 步数（即 MoveHistory 里这手棋的位置）</summary>
    public int MoveIndex { get; set; }
    /// <summary>该手棋是谁下的</summary>
    public Stone WhoMoved { get; set; }
    /// <summary>GTP 顶点（"D4" / "pass" / "resign"）</summary>
    public string Vertex { get; set; } = "";
    /// <summary>玩家视角胜率（0..1）。</summary>
    public double HumanWinrate { get; set; }
    /// <summary>玩家视角目差（正=领先，负=落后）。</summary>
    public double HumanScoreLead { get; set; }
    /// <summary>AI 当时主推的前 3 手变化</summary>
    public string PvText { get; set; } = "";
}

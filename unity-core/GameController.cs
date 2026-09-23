namespace GoGame.Core;

/// <summary>
/// 一手棋走完后的局面评估（玩家视角）。由 GameController 累积，供终局复盘报告使用。
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

/// <summary>
/// 单局游戏状态机：跟踪回合、谁是 AI、落子流程。
/// 关键原则：先让 KataGo 接受（验证合法性），再更新本地棋盘。
/// 所有棋盘状态变化都走 ApplyMove，自动维护快照栈（用于棋谱回放）。
///
/// 与引擎无关（无 Unity 依赖），dotnet 可独立跑通冒烟测试。
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
        if (!IsHumanTurn) return (false, "还没轮到你", Array.Empty<(int, int)>());
        if (!_board.IsInBounds(x, y)) return (false, "坐标超出棋盘", Array.Empty<(int, int)>());
        if (_board.Get(x, y) != Stone.Empty) return (false, "该位置已有棋子", Array.Empty<(int, int)>());

        var vertex = Board.ToGtpVertex(x, y, _board.Size);
        var color = HumanColor == Stone.Black ? "B" : "W";

        if (!_ai.TryPlay(color, vertex))
            return (false, $"KataGo 判定为非法手: {vertex}", Array.Empty<(int, int)>());

        var captures = ApplyMove(x, y, HumanColor);
        return (true, $"你下了 {vertex}", captures);
    }

    /// <summary>AI 落子。返回 (GTP 顶点, 被提位置列表)。
    /// 顶点 = "D4" / "pass" / "resign"。
    /// 使用 kata-genmove_analyze 落子：顺带拿到当前局面的胜率/目差，零额外开销。</summary>
    public async Task<(string vertex, IReadOnlyList<(int x, int y)> captures)> PlayAiAsync(double thinkSeconds)
    {
        if (IsHumanTurn) throw new InvalidOperationException("还没轮到 AI");

        var color = AiColor == Stone.Black ? "B" : "W";
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

    /// <summary>人类虚手（pass）。</summary>
    public (bool ok, string message) PassHuman()
    {
        if (!IsHumanTurn) return (false, "还没轮到你");
        var color = HumanColor == Stone.Black ? "B" : "W";
        if (!_ai.TryPlay(color, "pass"))
            return (false, "KataGo 拒绝虚手");
        ApplyPassOrResign(HumanColor, "pass");
        return (true, "你虚手");
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
            return (false, "没有足够的着法可悔棋");

        var moves = new List<string>(_board.MoveHistory);
        moves.RemoveAt(moves.Count - 1);
        moves.RemoveAt(moves.Count - 1);

        _ai.ClearBoard();
        foreach (var m in moves)
        {
            char color = char.ToUpper(m[0]);
            string v = m.Substring(1);
            _ai.TryPlay(color.ToString(), v);
        }

        RebuildLocalFromHistory(moves);
        return (true, "已悔棋两步");
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

    /// <summary>同步 KataGo 状态（clear_board + 重放），用于悔棋后或外部加载棋谱时。</summary>
    public void ResyncAiFromBoard()
    {
        _ai.ClearBoard();
        foreach (var m in _board.MoveHistory)
        {
            char color = char.ToUpper(m[0]);
            string v = m.Substring(1);
            _ai.TryPlay(color.ToString(), v);
        }
        _board.NextToPlay = HumanColor;
    }

    // ---- 内部辅助 ----

    private IReadOnlyList<(int x, int y)> ApplyMove(int x, int y, Stone stone)
    {
        var (placed, captures) = _board.Place(x, y, stone);
        if (!placed)
            throw new InvalidOperationException($"落子 ({x},{y}) 被本地规则拒绝（自杀或冲突）");
        var vertex = Board.ToGtpVertex(x, y, _board.Size);
        var colorLower = stone == Stone.Black ? "b" : "w";
        _board.RecordMove($"{colorLower}{vertex}");
        _board.NextToPlay = stone == Stone.Black ? Stone.White : Stone.Black;
        return captures;
    }

    private void ApplyPassOrResign(Stone stone, string action)
    {
        _board.Snapshots.Add((Stone[,])_board.Grid.Clone());
        var colorLower = stone == Stone.Black ? "b" : "w";
        _board.RecordMove($"{colorLower}{action}");
        _board.NextToPlay = stone == Stone.Black ? Stone.White : Stone.Black;
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
                throw new InvalidOperationException($"着法 {m} 颜色与回合不匹配（期望 {expected}）");
            string v = m.Substring(1);
            if (v == "pass" || v == "resign")
                ApplyPassOrResign(s, v);
            else
            {
                var (x, y) = Board.FromGtpVertex(v, _board.Size);
                ApplyMove(x, y, s);
            }
            expected = s == Stone.Black ? Stone.White : Stone.Black;
        }
        _board.NextToPlay = HumanColor;
    }
}

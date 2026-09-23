namespace GoPrototype;

/// <summary>
/// 单局游戏状态机：跟踪回合、谁是 AI、落子流程。
/// 关键原则：先让 KataGo 接受（验证合法性），再更新本地棋盘。
/// </summary>
public class GameController
{
    private readonly Board _board;
    private readonly KataGoClient _ai;
    public Stone HumanColor { get; }
    public Stone AiColor => HumanColor == Stone.Black ? Stone.White : Stone.Black;

    public GameController(Board board, KataGoClient ai, Stone humanColor)
    {
        _board = board;
        _ai = ai;
        HumanColor = humanColor;
    }

    public bool IsHumanTurn => _board.NextToPlay == HumanColor;

    /// <summary>
    /// 人类落子。先发给 KataGo 验证，成功后才落本地。
    /// </summary>
    public (bool ok, string message) PlayHuman(int x, int y)
    {
        if (!IsHumanTurn) return (false, "不是你的回合");
        if (!_board.IsInBounds(x, y)) return (false, "坐标超出棋盘");
        if (_board.Get(x, y) != Stone.Empty) return (false, "该位置已有棋子");

        var vertex = Board.ToGtpVertex(x, y, _board.Size);
        var color = HumanColor == Stone.Black ? "B" : "W";

        if (!_ai.TryPlay(color, vertex))
            return (false, $"KataGo 判定为非法手: {vertex}");

        _board.Place(x, y, HumanColor);
        _board.RecordMove($"{color.ToLower()}{vertex}");
        _board.NextToPlay = AiColor;
        return (true, $"你下了 {vertex}");
    }

    /// <summary>
    /// AI 落子。返回 GTP 顶点（"D4" / "pass" / "resign"）。
    /// </summary>
    public async Task<string> PlayAiAsync()
    {
        if (IsHumanTurn) throw new InvalidOperationException("不是 AI 的回合");

        var color = AiColor == Stone.Black ? "B" : "W";
        // KataGo 计算放到后台线程，不阻塞主线程
        var vertex = await Task.Run(() => _ai.GenMove(color));

        if (vertex == "pass" || vertex == "resign")
        {
            _board.RecordMove($"{color.ToLower()}{vertex}");
        }
        else
        {
            var (x, y) = Board.FromGtpVertex(vertex, _board.Size);
            _board.Place(x, y, AiColor);
            _board.RecordMove($"{color.ToLower()}{vertex}");
        }
        _board.NextToPlay = HumanColor;
        return vertex;
    }

    /// <summary>
    /// 人类虚手（pass）。
    /// </summary>
    public (bool ok, string message) PassHuman()
    {
        if (!IsHumanTurn) return (false, "不是你的回合");
        var color = HumanColor == Stone.Black ? "B" : "W";
        _ai.TryPlay(color, "pass");
        _board.RecordMove($"{color.ToLower()}pass");
        _board.NextToPlay = AiColor;
        return (true, "你虚手");
    }

    public void ResignHuman()
    {
        var color = HumanColor == Stone.Black ? "B" : "W";
        _ai.TryPlay(color, "resign");
    }
}

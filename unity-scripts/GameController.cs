using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 人机对弈控制器（状态机）。
/// V1.0 不实现：劫争禁手、自杀手检测、形势判断。
/// </summary>
public class GameController : MonoBehaviour
{
    public enum GameState
    {
        Idle,
        HumanTurn,
        WaitingAI,
        GameOver,
    }

    [SerializeField] private StoneColor humanColor = StoneColor.Black;

    public KataGoClient KataGo { get; private set; }
    public BoardView Board { get; private set; }
    public GameState State => _state;

    private GameState _state = GameState.Idle;
    private StoneColor _turn = StoneColor.Black;
    private readonly List<Move> _history = new();

    public void Setup(KataGoClient client, BoardView board)
    {
        KataGo = client;
        Board = board;
        board.OnPositionClicked += HandleHumanMove;
    }

    public void StartNewGame(int boardSize = 19, int visits = 1000)
    {
        Board.ClearBoard();
        _history.Clear();
        _turn = StoneColor.Black;

        KataGo.SendCommand($"boardsize {boardSize}");
        KataGo.SendCommand("clear_board");
        KataGo.SendCommand("komi 7.5");
        KataGo.SendCommand($"set_rules chinese");

        _state = _turn == humanColor ? GameState.HumanTurn : GameState.WaitingAI;
        Debug.Log($"[GameController] 新对弈: 棋盘 {boardSize}x{boardSize}, 人类={humanColor}, AI visits={visits}");

        if (_state == GameState.WaitingAI) RequestAiMove();
    }

    public void SetVisits(int visits)
    {
        // KataGo 没有直接 set visits 的 GTP 命令，常规做法是改 default_gtp.cfg
        // V1.1 可通过重启 KataGo 子进程传入不同配置
        Debug.LogWarning("[GameController] V1.0 重启 KataGo 切 visits");
    }

    public void Resign()
    {
        _state = GameState.GameOver;
        Debug.Log("[GameController] 游戏结束（认输）");
    }

    public void Pass()
    {
        if (_state == GameState.HumanTurn)
        {
            KataGo.SendCommand($"play {ColorToGtp(_turn)} pass");
            _turn = _turn == StoneColor.Black ? StoneColor.White : StoneColor.Black;
            if (_state == GameState.HumanTurn)
                _state = GameState.WaitingAI;
            RequestAiMove();
        }
    }

    private void HandleHumanMove(BoardCoord coord)
    {
        if (_state != GameState.HumanTurn)
        {
            Debug.Log($"[GameController] 忽略落子（当前状态 {_state}）");
            return;
        }

        // V1.0 不做合法性校验（KataGo 会返回错误）
        PlaceStone(coord, _turn);
        KataGo.SendCommand($"play {ColorToGtp(_turn)} {coord}");

        _history.Add(new Move { Coord = coord, Color = _turn });
        _turn = _turn == StoneColor.Black ? StoneColor.White : StoneColor.Black;
        _state = GameState.WaitingAI;
        RequestAiMove();
    }

    private void RequestAiMove()
    {
        if (KataGo == null) return;

        var colorChar = ColorToGtp(_turn);
        var response = KataGo.SendCommand($"genmove {colorChar}");

        if (response == null)
        {
            Debug.LogError("[GameController] AI 响应失败，游戏结束");
            _state = GameState.GameOver;
            return;
        }

        if (response.Trim().Equals("pass", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log("[GameController] AI 跳过");
            _turn = _turn == StoneColor.Black ? StoneColor.White : StoneColor.Black;
            _state = GameState.HumanTurn;
            return;
        }

        if (response.Trim().Equals("resign", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log("[GameController] AI 认输");
            _state = GameState.GameOver;
            return;
        }

        if (TryParseCoord(response, out var coord))
        {
            PlaceStone(coord, _turn);
            _history.Add(new Move { Coord = coord, Color = _turn });
            _turn = _turn == StoneColor.Black ? StoneColor.White : StoneColor.Black;
            _state = GameState.HumanTurn;
        }
        else
        {
            Debug.LogError($"[GameController] 无法解析 AI 响应: {response}");
            _state = GameState.GameOver;
        }
    }

    private void PlaceStone(BoardCoord coord, StoneColor color)
    {
        Board.PlaceStone(coord.X, coord.Y, color);
    }

    private static string ColorToGtp(StoneColor color)
        => color == StoneColor.Black ? "B" : "W";

    private static bool TryParseCoord(string gtpCoord, out BoardCoord coord)
    {
        coord = default;
        if (string.IsNullOrEmpty(gtpCoord) || gtpCoord.Length < 2) return false;
        var trimmed = gtpCoord.Trim();
        var ch0 = char.ToLower(trimmed[0]);
        var ch1 = char.ToLower(trimmed[1]);
        if (ch0 < 'a' || ch0 > 's' || ch1 < 'a' || ch1 > 's') return false;
        coord = new BoardCoord(ch0 - 'a', ch1 - 'a');
        return true;
    }

    public struct Move
    {
        public BoardCoord Coord;
        public StoneColor Color;
    }
}

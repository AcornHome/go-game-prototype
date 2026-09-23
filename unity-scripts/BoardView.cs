using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 围棋棋盘视图（UI Toolkit）。
/// 监听用户落子回调，更新 UI 显示。
/// </summary>
public class BoardView : MonoBehaviour
{
    [SerializeField] private int boardSize = 19;
    [SerializeField] private float cellSize = 28f;
    [SerializeField] private float stoneSize = 24f;

    /// <summary>用户点击棋盘坐标时触发（已通过合法性校验前）</summary>
    public System.Action<BoardCoord> OnPositionClicked;

    private VisualElement _boardContainer;
    private readonly List<StoneData> _stones = new();

    public void Initialize(VisualElement root)
    {
        _boardContainer = root.Q<VisualElement>("board");
        if (_boardContainer == null)
        {
            Debug.LogError("[BoardView] 找不到 name=\"board\" 的 VisualElement");
            return;
        }

        _boardContainer.Clear();
        _stones.Clear();

        // 设置棋盘容器大小
        _boardContainer.style.width = boardSize * cellSize;
        _boardContainer.style.height = boardSize * cellSize;
        _boardContainer.style.position = Position.Relative;

        // 动态生成 cell（生产代码建议用 UITemplate）
        for (int y = 0; y < boardSize; y++)
        {
            for (int x = 0; x < boardSize; x++)
            {
                var cell = new VisualElement();
                cell.style.width = cellSize;
                cell.style.height = cellSize;
                cell.style.position = Position.Absolute;
                cell.style.left = x * cellSize;
                cell.style.top = y * cellSize;
                cell.style.justifyContent = Justify.Center;
                cell.style.alignItems = Align.Center;

                int cx = x, cy = y;
                cell.RegisterCallback<ClickEvent>(_ =>
                {
                    OnPositionClicked?.Invoke(new BoardCoord(cx, cy));
                });

                _boardContainer.Add(cell);
            }
        }
    }

    public void PlaceStone(int x, int y, StoneColor color)
    {
        if (color == StoneColor.None) return;
        if (x < 0 || x >= boardSize || y < 0 || y >= boardSize) return;

        var cellIndex = y * boardSize + x;
        var cell = _boardContainer.ElementAt(cellIndex);

        var stone = new VisualElement();
        stone.style.width = stoneSize;
        stone.style.height = stoneSize;
        stone.style.borderTopLeftRadius = stoneSize / 2;
        stone.style.borderTopRightRadius = stoneSize / 2;
        stone.style.borderBottomLeftRadius = stoneSize / 2;
        stone.style.borderBottomRightRadius = stoneSize / 2;
        stone.style.backgroundColor = color == StoneColor.Black
            ? new StyleColor(new Color(0.1f, 0.1f, 0.1f))
            : new StyleColor(Color.white);

        // 增加边框让白棋可见
        if (color == StoneColor.White)
        {
            stone.style.borderTopWidth = 1;
            stone.style.borderBottomWidth = 1;
            stone.style.borderLeftWidth = 1;
            stone.style.borderRightWidth = 1;
            stone.style.borderTopColor = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            stone.style.borderBottomColor = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            stone.style.borderLeftColor = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            stone.style.borderRightColor = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
        }

        cell.Add(stone);
        _stones.Add(new StoneData { X = x, Y = y, Color = color });
    }

    public void ClearBoard()
    {
        _stones.Clear();
        foreach (var cell in _boardContainer.Children())
        {
            cell.Clear();
        }
    }
}

/// <summary>围棋棋盘坐标（左下原点）</summary>
public struct BoardCoord
{
    public int X;
    public int Y;

    public BoardCoord(int x, int y) { X = x; Y = y; }

    /// <summary>转换为 GTP 协议字符串（如 "qd"）</summary>
    public override string ToString() => $"{(char)('a' + X)}{(char)('a' + Y)}";
}

public enum StoneColor
{
    None,
    Black,
    White,
}

public struct StoneData
{
    public int X;
    public int Y;
    public StoneColor Color;
}

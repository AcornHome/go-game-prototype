using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GoGame.Core;

/// <summary>
/// 围棋棋盘视图（Unity 6 LTS，UI Toolkit 渲染）。
/// 接收 GoGame.Core.Board 的状态变化，把 Stone[,] 转成 VisualElement 子树。
/// 所有 UI 用 C# 代码构造（不依赖 UXML / USS 拖拽）。
/// </summary>
public class BoardView : MonoBehaviour
{
    [SerializeField] private int boardSize = 19;
    [SerializeField] private float cellSize = 28f;
    [SerializeField] private float stoneSize = 24f;

    /// <summary>用户点击棋盘坐标时触发（已通过 IsHumanTurn 校验前）</summary>
    public System.Action<int, int> OnPositionClicked;

    private VisualElement? _boardContainer;
    private readonly List<VisualElement> _cellElements = new();

    public int BoardSize => boardSize;

    public void Initialize(VisualElement root)
    {
        var board = root.Q<VisualElement>("board");
        if (board == null)
        {
            Debug.LogError("[BoardView] 找不到 name=\"board\" 的 VisualElement");
            return;
        }
        _boardContainer = board;

        _boardContainer.Clear();
        _cellElements.Clear();

        _boardContainer.style.width = boardSize * cellSize;
        _boardContainer.style.height = boardSize * cellSize;
        _boardContainer.style.position = Position.Relative;
        _boardContainer.style.backgroundColor = new StyleColor(new Color(0.95f, 0.85f, 0.6f));
        _boardContainer.style.borderTopWidth = 2;
        _boardContainer.style.borderBottomWidth = 2;
        _boardContainer.style.borderLeftWidth = 2;
        _boardContainer.style.borderRightWidth = 2;
        _boardContainer.style.borderTopColor = new StyleColor(new Color(0.4f, 0.3f, 0.2f));
        _boardContainer.style.borderBottomColor = new StyleColor(new Color(0.4f, 0.3f, 0.2f));
        _boardContainer.style.borderLeftColor = new StyleColor(new Color(0.4f, 0.3f, 0.2f));
        _boardContainer.style.borderRightColor = new StyleColor(new Color(0.4f, 0.3f, 0.2f));

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
                    OnPositionClicked?.Invoke(cx, cy);
                });

                _boardContainer.Add(cell);
                _cellElements.Add(cell);
            }
        }
    }

    public void Render(Board board)
    {
        if (_boardContainer == null || _cellElements.Count != boardSize * boardSize) return;

        for (int y = 0; y < boardSize; y++)
        {
            for (int x = 0; x < boardSize; x++)
            {
                var cell = _cellElements[y * boardSize + x];
                var stone = board.Get(x, y);
                cell.Clear();

                if (stone == Stone.Empty) continue;
                AddStone(cell, stone);
            }
        }
    }

    private void AddStone(VisualElement cell, Stone stone)
    {
        var visual = new VisualElement();
        visual.style.width = stoneSize;
        visual.style.height = stoneSize;
        visual.style.borderTopLeftRadius = stoneSize / 2;
        visual.style.borderTopRightRadius = stoneSize / 2;
        visual.style.borderBottomLeftRadius = stoneSize / 2;
        visual.style.borderBottomRightRadius = stoneSize / 2;
        visual.style.backgroundColor = stone == Stone.Black
            ? new StyleColor(new Color(0.1f, 0.1f, 0.1f))
            : new StyleColor(Color.white);

        if (stone == Stone.White)
        {
            visual.style.borderTopWidth = 1;
            visual.style.borderBottomWidth = 1;
            visual.style.borderLeftWidth = 1;
            visual.style.borderRightWidth = 1;
            visual.style.borderTopColor = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            visual.style.borderBottomColor = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            visual.style.borderLeftColor = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            visual.style.borderRightColor = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
        }

        cell.Add(visual);
    }

    public void ClearBoard()
    {
        if (_boardContainer == null) return;
        foreach (var cell in _cellElements)
            cell.Clear();
    }
}

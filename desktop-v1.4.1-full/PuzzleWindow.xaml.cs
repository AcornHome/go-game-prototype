using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GoGame;

/// <summary>
/// 互动死活题窗口。
/// - 左侧题库列表
/// - 中部棋盘（用户和 KataGo 轮转落子，到 MaxMoveTurns 轮后让 KataGo final_score）
/// - 右侧题目说明 + KataGo 评判
/// </summary>
public partial class PuzzleWindow : Window
{
    private List<Puzzle> _puzzles;
    private Puzzle? _currentPuzzle;
    private Board? _board;
    private KataGoClient? _ai;
    private int _turnCount = 0;       // 已完成的轮数（每轮 = 用户 1 手 + KataGo 1 手）
    private bool _busy = false;
    private bool _judging = false;

    // 与 MainWindow 一致：走 KataGoLocator 探测，不写死开发机路径
    private static readonly string KatagoExe = KataGoLocator.ExePath;
    private static readonly string ModelPath = KataGoLocator.ModelPath;
    private static readonly string ConfigPath = KataGoLocator.ConfigPath;

    public PuzzleWindow()
    {
        InitializeComponent();
        _puzzles = PuzzleLibrary.All.ToList();
        BoardView.MoveRequested += OnBoardClick;
        Loaded += OnLoadedAsync;
        Closing += (_, _) => { try { _ai?.Dispose(); } catch { } };
    }

    // ============ 题库列表 ============
    private void BuildPuzzleList()
    {
        PuzzleList.Children.Clear();
        for (int i = 0; i < _puzzles.Count; i++)
        {
            var p = _puzzles[i];
            bool isCurrent = _currentPuzzle == p;
            var border = new Border
            {
                Background = isCurrent
                    ? (Brush)FindResource("BrushCardHi")
                    : (Brush)FindResource("BrushCard"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                BorderBrush = isCurrent
                    ? (Brush)FindResource("BrushAccent")
                    : (Brush)FindResource("BrushCard"),
                BorderThickness = new Thickness(isCurrent ? 2 : 1),
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {p.Title}",
                Foreground = (Brush)FindResource(isCurrent ? "BrushAccent" : "BrushText"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{p.Category} · 难度 {new string('★', p.Difficulty)}{new string('☆', 5 - p.Difficulty)}",
                Foreground = (Brush)FindResource("BrushTextDim"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });
            border.Child = stack;
            int captured = i;
            border.MouseLeftButtonUp += (_, _) => LoadPuzzle(captured);
            PuzzleList.Children.Add(border);
        }
    }

    // ============ 加载题目 ============
    private void LoadPuzzle(int index)
    {
        var p = _puzzles[index];
        _currentPuzzle = p;

        // 确保 KataGo 在跑
        if (_ai == null || !_ai.IsRunning)
        {
            try
            {
                _ai = new KataGoClient(KatagoExe, ModelPath, ConfigPath, p.BoardSize);
                _ai.SetTimeSettings(0, 0.5, 1);  // 死活题很快，每手 0.5s 够用
                _ai.SetKomi(7.5);                  // 中国规则标准贴目
            }
            catch (Exception ex)
            {
                StyledDialog.ShowError(this,
                    $"启动 KataGo 失败：\n{ex.Message}\n\n{KataGoLocator.DescribeSearch()}",
                    "死活题不可用");
                return;
            }
        }
        else if (_board?.Size != p.BoardSize)
        {
            // 棋盘大小切换：调 boardsize 命令
            try { _ai.SetBoardSize(p.BoardSize); } catch { }
        }

        // 重置棋盘 + 加载初始局面
        _ai.ClearBoard();
        _board = new Board(p.BoardSize);
        _board.NextToPlay = p.NextToPlay;
        foreach (var stone in p.InitialStones)
        {
            string v = Board.ToGtpVertex(stone.X, stone.Y, p.BoardSize);
            string color = stone.Color == Stone.Black ? "B" : "W";
            _ai.TryPlay(color, v);
            _board.Grid[stone.X, stone.Y] = stone.Color;
        }
        _board.Snapshots.Add(CloneGridLocal(_board));   // 初始局面快照

        BoardView.Board = _board;
        BoardView.LastMove = null;
        BoardView.IsHumanTurn = (_board.NextToPlay == p.PlayerColor);

        _turnCount = 0;
        ResultPanel.Visibility = Visibility.Collapsed;
        HintTextBlock.Visibility = Visibility.Collapsed;

        TitleText.Text = p.Title;
        CategoryText.Text = $"{p.Category} · 难度 {p.Difficulty}/5";
        GoalText.Text = p.Goal;
        ColorText.Text = $"你执 {(p.PlayerColor == Stone.Black ? "黑" : "白")} · {(p.NextToPlay == p.PlayerColor ? "你先走" : "KataGo 先走")}";
        StatusText.Text = _board.NextToPlay == p.PlayerColor
            ? "轮到你落子" : "KataGo 思考中…";

        BuildPuzzleList();
    }

    private static Stone[,] CloneGridLocal(Board b)
    {
        var g = new Stone[b.Size, b.Size];
        for (int x = 0; x < b.Size; x++)
            for (int y = 0; y < b.Size; y++)
                g[x, y] = b.Grid[x, y];
        return g;
    }

    // ============ 用户落子 ============
    private async void OnBoardClick(int x, int y)
    {
        if (_busy || _judging || _currentPuzzle == null || _board == null || _ai == null) return;
        if (_board.NextToPlay != _currentPuzzle.PlayerColor) return;
        if (_board.Grid[x, y] != Stone.Empty) return;

        var color = _currentPuzzle.PlayerColor;
        string v = Board.ToGtpVertex(x, y, _currentPuzzle.BoardSize);
        if (!_ai.TryPlay(color == Stone.Black ? "B" : "W", v))
        {
            StyledDialog.ShowWarning(this, "KataGo 拒绝了这步棋（非法或自杀）。", "无效");
            return;
        }

        var (placed, captures) = _board.Place(x, y, color);
        if (!placed) return;
        if (captures.Count > 0) BoardView.FlashCaptures(captures);
        BoardView.LastMove = (x, y);
        BoardView.AnimateMove(x, y);
        BoardView.InvalidateVisual();

        _board.NextToPlay = color == Stone.Black ? Stone.White : Stone.Black;

        // 让 KataGo 反击 1 手
        await AiReplyAsync();
    }

    // ============ KataGo 反击 ============
    private async Task AiReplyAsync()
    {
        if (_board == null || _ai == null || _currentPuzzle == null) return;
        _busy = true;
        StatusText.Text = "KataGo 思考中…";
        BoardView.IsHumanTurn = false;
        try
        {
            var oppColor = _currentPuzzle.PlayerColor == Stone.Black ? Stone.White : Stone.Black;
            string oppStr = oppColor == Stone.Black ? "B" : "W";
            string v = await Task.Run(() => _ai.GenMove(oppStr));
            if (v == "pass" || v == "resign")
            {
                StatusText.Text = v == "pass" ? "KataGo 虚手" : "KataGo 认输";
                _busy = false;
                BoardView.IsHumanTurn = true;
                return;
            }
            var (x, y) = Board.FromGtpVertex(v, _board.Size);
            var (placed, captures) = _board.Place(x, y, oppColor);
            if (placed)
            {
                if (captures.Count > 0) BoardView.FlashCaptures(captures);
                BoardView.LastMove = (x, y);
                BoardView.AnimateMove(x, y);
                BoardView.InvalidateVisual();
                _board.NextToPlay = oppColor == Stone.Black ? Stone.White : Stone.Black;
            }
            _turnCount++;
            StatusText.Text = _turnCount >= _currentPuzzle.MaxMoveTurns
                ? "可点「KataGo 评判」看结果"
                : $"轮到你落子（第 {_turnCount + 1}/{_currentPuzzle.MaxMoveTurns} 轮）";
        }
        catch (Exception ex)
        {
            StyledDialog.ShowError(this, $"KataGo 出错：{ex.Message}", "评判失败");
        }
        finally
        {
            _busy = false;
            BoardView.IsHumanTurn = (_board.NextToPlay == _currentPuzzle.PlayerColor);
        }
    }

    // ============ KataGo 评判 ============
    private async void Judge_Click(object sender, RoutedEventArgs e)
    {
        if (_judging || _board == null || _ai == null || _currentPuzzle == null) return;
        _judging = true;
        JudgeBtn.IsEnabled = false;
        ResultPanel.Visibility = Visibility.Visible;
        VerdictText.Text = "KataGo 正在评判…";
        VerdictDetailText.Text = "";
        VerdictMovesText.Text = "";
        VerdictText.Foreground = (Brush)FindResource("BrushText");

        try
        {
            // 让 KataGo 续走剩余回合：把双方补到 MaxMoveTurns 轮
            // 当前回合 = NextToPlay。如果还轮到 KataGo，先走；如果轮到用户，自动走"次佳"。
            int remaining = (_currentPuzzle.MaxMoveTurns - _turnCount) * 2;
            var movesPlayed = new List<string>();
            while (remaining > 0)
            {
                string who = _board.NextToPlay == Stone.Black ? "B" : "W";
                string v = await Task.Run(() => _ai.GenMove(who));
                if (v == "pass" || v == "resign")
                {
                    movesPlayed.Add($"{who}{(v == "pass" ? "pass" : "resign")}");
                    break;
                }
                var (x, y) = Board.FromGtpVertex(v, _board.Size);
                var stone = _board.NextToPlay;
                var (placed, captures) = _board.Place(x, y, stone);
                if (placed)
                {
                    if (captures.Count > 0) BoardView.FlashCaptures(captures);
                    BoardView.LastMove = (x, y);
                    BoardView.AnimateMove(x, y);
                    BoardView.InvalidateVisual();
                    _board.NextToPlay = stone == Stone.Black ? Stone.White : Stone.Black;
                }
                movesPlayed.Add($"{who}{v}");
                remaining--;
            }

            // final_score 评判（KataGo 默认会自动清理死子并按 chinese 规则算目）
            string score = await Task.Run(() => _ai.FinalScore());
            VerdictMovesText.Text = "KataGo 续走：" + string.Join(" ", movesPlayed);

            // 解析 B+3.5 / W+1.5 / 0
            bool isWin = false;
            string reason = "";
            if (score.StartsWith("B+"))
            {
                if (double.TryParse(score.Substring(2), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var diff))
                {
                    isWin = _currentPuzzle.PlayerColor == Stone.Black && diff > 0;
                    reason = $"黑净胜 {diff:F1} 目";
                }
            }
            else if (score.StartsWith("W+"))
            {
                if (double.TryParse(score.Substring(2), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var diff))
                {
                    isWin = _currentPuzzle.PlayerColor == Stone.White && diff > 0;
                    reason = $"白净胜 {diff:F1} 目";
                }
            }
            else if (score == "0")
            {
                reason = "平局";
            }

            if (isWin)
            {
                VerdictText.Text = "✓ 达成目标！";
                VerdictText.Foreground = (Brush)FindResource("BrushSuccess");
                VerdictDetailText.Text = $"{reason}。{(_currentPuzzle.PlayerColor == Stone.Black ? "黑" : "白")}棋成功。";
            }
            else
            {
                VerdictText.Text = "✗ 未达成目标";
                VerdictText.Foreground = (Brush)FindResource("BrushDanger");
                VerdictDetailText.Text = reason.Length > 0
                    ? $"{reason}。试试「查看正解」或「重置局面」重新走。"
                    : "KataGo 评估无胜负。试试「查看正解」或「重置局面」重新走。";
            }
        }
        catch (Exception ex)
        {
            VerdictText.Text = "评判失败";
            VerdictText.Foreground = (Brush)FindResource("BrushDanger");
            VerdictDetailText.Text = ex.Message;
        }
        finally
        {
            _judging = false;
            JudgeBtn.IsEnabled = true;
            StatusText.Text = "可继续操作";
        }
    }

    // ============ 重置 / 提示 / 正解 ============
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPuzzle == null) return;
        LoadPuzzle(_puzzles.IndexOf(_currentPuzzle));
    }

    private void Hint_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPuzzle?.Hint == null) return;
        HintTextBlock.Text = "💡 " + _currentPuzzle.Hint;
        HintTextBlock.Visibility = Visibility.Visible;
    }

    private void Solution_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPuzzle == null || _currentPuzzle.Solution.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("正解（用户执方）：");
        for (int i = 0; i < _currentPuzzle.Solution.Count; i++)
        {
            var m = _currentPuzzle.Solution[i];
            string v = Board.ToGtpVertex(m.X, m.Y, _currentPuzzle.BoardSize);
            sb.AppendLine($"  {i + 1}. {(m.Color == Stone.Black ? "●" : "○")} {v}");
        }
        StyledDialog.ShowInfo(this, sb.ToString().TrimEnd(), "查看正解");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ============ 窗口加载时尝试启动 KataGo ============
    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        BuildPuzzleList();
        StatusText.Text = "KataGo 启动中（首次约 30s ~ 3min）…";
        try
        {
            await Task.Run(() =>
            {
                _ai ??= new KataGoClient(KatagoExe, ModelPath, ConfigPath, 9);
                _ai.SetTimeSettings(0, 0.5, 1);
                _ai.SetKomi(7.5);
            });
            StatusText.Text = "KataGo 就绪。点击左侧题目开始";
            if (_puzzles.Count > 0) LoadPuzzle(0);
        }
        catch (Exception ex)
        {
            StatusText.Text = "KataGo 启动失败";
            StyledDialog.ShowError(this,
                $"KataGo 启动失败：\n{ex.Message}\n\n点击题目时仍会尝试启动。",
                "死活题不可用");
        }
    }
}

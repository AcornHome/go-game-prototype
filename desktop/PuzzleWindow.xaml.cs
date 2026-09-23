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
/// Interactive life-and-death puzzle window.
/// - Left: puzzle library list
/// - Center: board (user and KataGo alternate moves; after MaxMoveTurns rounds, KataGo runs final_score)
/// - Right: puzzle description + KataGo verdict
/// </summary>
public partial class PuzzleWindow : Window
{
    private List<Puzzle> _puzzles;
    private Puzzle? _currentPuzzle;
    private Board? _board;
    private KataGoClient? _ai;
    private int _turnCount = 0;       // Completed rounds (1 round = 1 user move + 1 KataGo move)
    private bool _busy = false;
    private bool _judging = false;

    // Same as MainWindow: probe via KataGoLocator, don't hardcode the dev-machine path
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

    // ============ Puzzle list ============
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
                Text = $"{p.Category} · Difficulty {new string('★', p.Difficulty)}{new string('☆', 5 - p.Difficulty)}",
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

    // ============ Load puzzle ============
    private void LoadPuzzle(int index)
    {
        var p = _puzzles[index];
        _currentPuzzle = p;

        // Make sure KataGo is running
        if (_ai == null || !_ai.IsRunning)
        {
            try
            {
                _ai = new KataGoClient(KatagoExe, ModelPath, ConfigPath, p.BoardSize);
                _ai.SetTimeSettings(0, 0.5, 1);  // Puzzles are quick; 0.5s per move is enough
                _ai.SetKomi(7.5);                  // Chinese-rule standard komi
            }
            catch (Exception ex)
            {
                StyledDialog.ShowError(this,
                    $"Failed to start KataGo:\n{ex.Message}\n\n{KataGoLocator.DescribeSearch()}",
                    "Puzzles unavailable");
                return;
            }
        }
        else if (_board?.Size != p.BoardSize)
        {
            // Board-size switch: call the boardsize command
            try { _ai.SetBoardSize(p.BoardSize); } catch { }
        }

        // Reset board + load initial position
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
        _board.Snapshots.Add(CloneGridLocal(_board));   // Initial-position snapshot

        BoardView.Board = _board;
        BoardView.LastMove = null;
        BoardView.IsHumanTurn = (_board.NextToPlay == p.PlayerColor);

        _turnCount = 0;
        ResultPanel.Visibility = Visibility.Collapsed;
        HintTextBlock.Visibility = Visibility.Collapsed;

        TitleText.Text = p.Title;
        CategoryText.Text = $"{p.Category} · Difficulty {p.Difficulty}/5";
        GoalText.Text = p.Goal;
        ColorText.Text = $"You play {(p.PlayerColor == Stone.Black ? "Black" : "White")} · {(p.NextToPlay == p.PlayerColor ? "You move first" : "KataGo moves first")}";
        StatusText.Text = _board.NextToPlay == p.PlayerColor
            ? "Your turn" : "KataGo thinking…";

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

    // ============ User move ============
    private async void OnBoardClick(int x, int y)
    {
        if (_busy || _judging || _currentPuzzle == null || _board == null || _ai == null) return;
        if (_board.NextToPlay != _currentPuzzle.PlayerColor) return;
        if (_board.Grid[x, y] != Stone.Empty) return;

        var color = _currentPuzzle.PlayerColor;
        string v = Board.ToGtpVertex(x, y, _currentPuzzle.BoardSize);
        if (!_ai.TryPlay(color == Stone.Black ? "B" : "W", v))
        {
            StyledDialog.ShowWarning(this, "KataGo rejected this move (illegal or suicide).", "Invalid");
            return;
        }

        var (placed, captures) = _board.Place(x, y, color);
        if (!placed) return;
        if (captures.Count > 0) BoardView.FlashCaptures(captures);
        BoardView.LastMove = (x, y);
        BoardView.AnimateMove(x, y);
        BoardView.InvalidateVisual();

        _board.NextToPlay = color == Stone.Black ? Stone.White : Stone.Black;

        // Let KataGo counter one move
        await AiReplyAsync();
    }

    // ============ KataGo counter ============
    private async Task AiReplyAsync()
    {
        if (_board == null || _ai == null || _currentPuzzle == null) return;
        _busy = true;
        StatusText.Text = "KataGo thinking…";
        BoardView.IsHumanTurn = false;
        try
        {
            var oppColor = _currentPuzzle.PlayerColor == Stone.Black ? Stone.White : Stone.Black;
            string oppStr = oppColor == Stone.Black ? "B" : "W";
            string v = await Task.Run(() => _ai.GenMove(oppStr));
            if (v == "pass" || v == "resign")
            {
                StatusText.Text = v == "pass" ? "KataGo passes" : "KataGo resigns";
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
                ? "Click \"Judge with KataGo\" to see the result"
                : $"Your turn (round {_turnCount + 1}/{_currentPuzzle.MaxMoveTurns})";
        }
        catch (Exception ex)
        {
            StyledDialog.ShowError(this, $"KataGo error: {ex.Message}", "Judging failed");
        }
        finally
        {
            _busy = false;
            BoardView.IsHumanTurn = (_board.NextToPlay == _currentPuzzle.PlayerColor);
        }
    }

    // ============ KataGo verdict ============
    private async void Judge_Click(object sender, RoutedEventArgs e)
    {
        if (_judging || _board == null || _ai == null || _currentPuzzle == null) return;
        _judging = true;
        JudgeBtn.IsEnabled = false;
        ResultPanel.Visibility = Visibility.Visible;
        VerdictText.Text = "KataGo is judging…";
        VerdictDetailText.Text = "";
        VerdictMovesText.Text = "";
        VerdictText.Foreground = (Brush)FindResource("BrushText");

        try
        {
            // Let KataGo play out the remaining rounds: fill both sides up to MaxMoveTurns rounds.
            // Current turn = NextToPlay. If it's KataGo's turn, play first; if it's the user's, auto-play the "second-best".
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

            // final_score verdict (KataGo auto-clears dead stones and scores by Chinese rules by default)
            string score = await Task.Run(() => _ai.FinalScore());
            VerdictMovesText.Text = "KataGo continued: " + string.Join(" ", movesPlayed);

            // Parse B+3.5 / W+1.5 / 0
            bool isWin = false;
            string reason = "";
            if (score.StartsWith("B+"))
            {
                if (double.TryParse(score.Substring(2), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var diff))
                {
                    isWin = _currentPuzzle.PlayerColor == Stone.Black && diff > 0;
                    reason = $"Black wins by {diff:F1} points";
                }
            }
            else if (score.StartsWith("W+"))
            {
                if (double.TryParse(score.Substring(2), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var diff))
                {
                    isWin = _currentPuzzle.PlayerColor == Stone.White && diff > 0;
                    reason = $"White wins by {diff:F1} points";
                }
            }
            else if (score == "0")
            {
                reason = "Draw";
            }

            if (isWin)
            {
                VerdictText.Text = "✓ Goal achieved!";
                VerdictText.Foreground = (Brush)FindResource("BrushSuccess");
                VerdictDetailText.Text = $"{reason}. {(_currentPuzzle.PlayerColor == Stone.Black ? "Black" : "White")} succeeds.";
            }
            else
            {
                VerdictText.Text = "✗ Goal not achieved";
                VerdictText.Foreground = (Brush)FindResource("BrushDanger");
                VerdictDetailText.Text = reason.Length > 0
                    ? $"{reason}. Try \"Show Solution\" or \"Reset\" to replay."
                    : "KataGo found no win/loss. Try \"Show Solution\" or \"Reset\" to replay.";
            }
        }
        catch (Exception ex)
        {
            VerdictText.Text = "Judging failed";
            VerdictText.Foreground = (Brush)FindResource("BrushDanger");
            VerdictDetailText.Text = ex.Message;
        }
        finally
        {
            _judging = false;
            JudgeBtn.IsEnabled = true;
            StatusText.Text = "Ready";
        }
    }

    // ============ Reset / Hint / Solution ============
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
        sb.AppendLine("Solution (your color):");
        for (int i = 0; i < _currentPuzzle.Solution.Count; i++)
        {
            var m = _currentPuzzle.Solution[i];
            string v = Board.ToGtpVertex(m.X, m.Y, _currentPuzzle.BoardSize);
            sb.AppendLine($"  {i + 1}. {(m.Color == Stone.Black ? "●" : "○")} {v}");
        }
        StyledDialog.ShowInfo(this, sb.ToString().TrimEnd(), "Show Solution");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ============ Try to start KataGo on window load ============
    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        BuildPuzzleList();
        StatusText.Text = "Starting KataGo (first run ~30s–3min)…";
        try
        {
            await Task.Run(() =>
            {
                _ai ??= new KataGoClient(KatagoExe, ModelPath, ConfigPath, 9);
                _ai.SetTimeSettings(0, 0.5, 1);
                _ai.SetKomi(7.5);
            });
            StatusText.Text = "KataGo ready. Click a puzzle on the left to start";
            if (_puzzles.Count > 0) LoadPuzzle(0);
        }
        catch (Exception ex)
        {
            StatusText.Text = "KataGo failed to start";
            StyledDialog.ShowError(this,
                $"Failed to start KataGo:\n{ex.Message}\n\nIt will retry when you click a puzzle.",
                "Puzzles unavailable");
        }
    }
}

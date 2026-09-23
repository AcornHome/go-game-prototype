using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GoGame;

/// <summary>
/// SGF replay / review window.
/// Construction:
///   new SgfReplayWindow(filePath)            read from disk
///   new SgfReplayWindow(sgfText, label)       pass SGF content directly
///   Notifies the main window to "start a new game from this position" via the StartFromHereRequested callback.
/// The board maintains its own Board + Snapshots, decoupled from the main game state.
/// </summary>
public partial class SgfReplayWindow : Window
{
    private Board _board = new(19);          // Dedicated Board for review (does not reference the main window)
    private readonly List<string> _moves;    // "bD4" / "wQ16" / "bpass" / "bresign"
    private SgfMeta _meta = new();
    private int _currentStep;                // 0..moves.Count

    /// <summary>Fired when the user clicks "New game from here"; arg (nextColor: 1=Black / 2=White, ...).</summary>
    public event Action<int>? StartFromHereRequested;

    /// <summary>Snapshot of the position reached in the review (Stone[,]). Can be serialized to MainWindow as the "opening position".</summary>
    public Stone[,]? CurrentSnapshot
    {
        get
        {
            if (_board.Snapshots.Count == 0) return null;
            int snap = Math.Clamp(_currentStep, 0, _board.Snapshots.Count - 1);
            return (Stone[,])_board.Snapshots[snap].Clone();
        }
    }

    /// <summary>Review-window board size (19/13/9).</summary>
    public int BoardSize => _board.Size;

    public SgfReplayWindow() : this(emptyLabel: "(empty game)")
    {
    }

    public SgfReplayWindow(string sgfOrLabel, bool isFilePath = true) : this()
    {
        if (!isFilePath) { Title = $"SGF Replay / Review · {sgfOrLabel}"; return; }
        try
        {
            var (b, m, meta) = SgfReader.Read(sgfOrLabel);
            InitializeBoard(b, m, meta);
            Title = $"SGF Replay / Review · {Path.GetFileName(sgfOrLabel)}";
            MetaFileName.Text = $"File: {Path.GetFileName(sgfOrLabel)}";
        }
        catch (Exception ex)
        {
            StyledDialog.ShowError(this,
                $"Failed to open game:\n{Path.GetFileName(sgfOrLabel)}\n\n{ex.Message}",
                "Replay failed");
            Loaded += (_, _) => Close();
        }
    }

    private SgfReplayWindow(string emptyLabel)
    {
        InitializeComponent();
        _moves = new List<string>();
        _currentStep = 0;
        BoardView.Board = _board;
        BoardView.IsHumanTurn = false;    // Review is read-only; no hover preview
        StepSlider.Minimum = 0;
        StepSlider.Maximum = 0;
        StepSlider.Value = 0;
        UpdateStepDisplay();
        UpdateMetaDisplay();
        Title = $"SGF Replay / Review · {emptyLabel}";
    }

    public SgfReplayWindow(Board fromBoard, IEnumerable<string> moves, SgfMeta meta, string label = "(current game)")
        : this(emptyLabel: label)
    {
        // Copy the board
        var copy = new Board(fromBoard.Size);
        var src = fromBoard.Snapshots.Count > 0 ? fromBoard.Snapshots[^1] : fromBoard.Grid;
        for (int x = 0; x < fromBoard.Size; x++)
            for (int y = 0; y < fromBoard.Size; y++)
                copy.Grid[x, y] = src[x, y];
        // Snapshots[0] = current full position ("start = current position, then walk _moves forward")
        copy.Snapshots.Clear();
        copy.Snapshots.Add(CloneGrid(copy.Grid, copy.Size));
        InitializeBoard(copy, new List<string>(moves), meta);
    }

    /// <summary>Loads the passed Board as the review start, walks all _moves once and pushes every snapshot onto the stack.</summary>
    private void InitializeBoard(Board board, List<string> moves, SgfMeta meta)
    {
        _board = board;
        _moves.Clear();
        _moves.AddRange(moves);
        _meta = meta;
        _currentStep = 0;
        BoardView.Board = _board;
        BoardView.LastMove = null;

        // Apply all _moves: each step Place + Snapshots.Add
        _board.MoveHistory.Clear();
        foreach (var rawMove in _moves)
        {
            char who = rawMove[0];
            Stone color = who == 'b' ? Stone.Black : Stone.White;
            string vertex = rawMove.Substring(1);

            if (vertex == "pass")
            {
                _board.NextToPlay = color == Stone.Black ? Stone.White : Stone.Black;
            }
            else if (vertex == "resign")
            {
                _board.NextToPlay = Stone.Empty;
            }
            else
            {
                try
                {
                    var (x, y) = Board.FromGtpVertex(vertex, _board.Size);
                    var (placed, _) = _board.Place(x, y, color);
                    if (placed)
                        _board.NextToPlay = color == Stone.Black ? Stone.White : Stone.Black;
                    // Skip illegal suicide moves (SGF occasionally has historical data errors)
                }
                catch (FormatException) { /* skip invalid coordinate */ }
            }
            // Every step (whether or not Place succeeded) gets a snapshot, so the Slider step count aligns
            _board.Snapshots.Add(CloneGrid(_board.Grid, _board.Size));
        }

        // Start = current position (Snapshots[0]); after all moves it is _board.Snapshots[^1]
        GoToStep(0);

        StepSlider.Minimum = 0;
        StepSlider.Maximum = _moves.Count;
        StepSlider.Value = 0;

        UpdateMetaDisplay();
        UpdateStepDisplay();
    }

    private static Stone[,] CloneGrid(Stone[,] src, int size)
    {
        var dst = new Stone[size, size];
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                dst[x, y] = src[x, y];
        return dst;
    }

    // ---- Navigation ----

    private void First_Click(object sender, RoutedEventArgs e) => GoToStep(0);
    private void Last_Click(object sender, RoutedEventArgs e) => GoToStep(_moves.Count);
    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0) GoToStep(_currentStep - 1);
    }
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < _moves.Count) GoToStep(_currentStep + 1);
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int target = (int)Math.Round(e.NewValue);
        if (target != _currentStep) GoToStep(target, fromSlider: true);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:  Prev_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Right: Next_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Home:  First_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.End:   Last_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Escape: Close_Click(this, new RoutedEventArgs()); e.Handled = true; break;
        }
    }

    /// <summary>Jump to step: 0 = start state, n = after all _moves.</summary>
    private void GoToStep(int step, bool fromSlider = false)
    {
        int target = Math.Clamp(step, 0, _moves.Count);
        _currentStep = target;
        _board.RestoreToSnapshot(target);

        // Highlight the previous move (no highlight when target=0)
        (int X, int Y)? lastMovePos = null;
        if (target > 0)
        {
            var raw = _moves[target - 1];
            string v = raw.Length >= 2 ? raw.Substring(1) : "";
            if (v != "pass" && v != "resign")
            {
                try
                {
                    var (x, y) = Board.FromGtpVertex(v, _board.Size);
                    lastMovePos = (x, y);
                }
                catch { /* skip */ }
            }
        }
        BoardView.LastMove = lastMovePos;
        BoardView.InvalidateVisual();

        if (!fromSlider) StepSlider.Value = target;
        UpdateStepDisplay();
    }

    private void UpdateStepDisplay()
    {
        StepCurrentText.Text = _currentStep.ToString();
        StepTotalText.Text = _moves.Count.ToString();

        string whoText;
        if (_moves.Count == 0)
        {
            whoText = "(empty game)";
        }
        else if (_currentStep >= _moves.Count)
        {
            whoText = "✔ Review complete";
        }
        else
        {
            char c = _moves[_currentStep][0];
            string side = c == 'b' ? "Black" : "White";
            string name = c == 'b' ? (_meta.BlackName ?? "Black") : (_meta.WhiteName ?? "White");
            whoText = $"Next: {side} ({name})";
        }
        WhoPlayText.Text = whoText;

        BtnFirst.IsEnabled = _currentStep > 0;
        BtnPrev.IsEnabled  = _currentStep > 0;
        BtnNext.IsEnabled  = _currentStep < _moves.Count;
        BtnLast.IsEnabled  = _currentStep < _moves.Count;
        BtnStartFromHere.IsEnabled = _currentStep > 0 && _currentStep < _moves.Count;
    }

    private void UpdateMetaDisplay()
    {
        MetaMoveCount.Text = $"{_moves.Count} moves";

        string pb = _meta.BlackName ?? "Black";
        string pw = _meta.WhiteName ?? "White";
        if (!string.IsNullOrEmpty(_meta.BlackRank)) pb += $" ({_meta.BlackRank})";
        if (!string.IsNullOrEmpty(_meta.WhiteRank)) pw += $" ({_meta.WhiteRank})";
        MetaPlayers.Text = $"⚫ {pb}  vs  🔘 {pw}";

        string title = _meta.Event ?? "Game review";
        if (!string.IsNullOrEmpty(_meta.Date)) title += $" · {_meta.Date}";
        MetaTitle.Text = title;

        MetaChips.Children.Clear();
        // Reuse XAML resource brushes (FindResource)
        var brushAccent    = (System.Windows.Media.Brush?)FindResource("BrushAccent");
        var brushAccentDim = (System.Windows.Media.Brush?)FindResource("BrushAccentDim");
        var brushAi        = (System.Windows.Media.Brush?)FindResource("BrushAI");
        var brushHuman     = (System.Windows.Media.Brush?)FindResource("BrushHuman");

        if (_meta.Komi.HasValue)
            AddChip($"Komi {_meta.Komi.Value:0.0}", brushAccent);
        if (!string.IsNullOrEmpty(_meta.Rule))
            AddChip(_meta.Rule!, brushAi);
        if (!string.IsNullOrEmpty(_meta.Result))
        {
            string r = _meta.Result!;
            System.Windows.Media.Brush c = r.StartsWith("B+") ? (brushHuman ?? System.Windows.Media.Brushes.Gray) :
                                          r.StartsWith("W+") ? (brushAi ?? System.Windows.Media.Brushes.Gray) : (brushAccent ?? System.Windows.Media.Brushes.Gray);
            AddChip($"Result {r}", c);
        }
        if (_meta.Handicap.HasValue && _meta.Handicap.Value > 0)
            AddChip($"Handicap {_meta.Handicap.Value}", brushAccentDim);
        if (_board.Size != 19)
            AddChip($"{_board.Size}-line", brushAccentDim);
    }

    private void AddChip(string text, System.Windows.Media.Brush? bg)
    {
        var b = new Border
        {
            Background = bg ?? System.Windows.Media.Brushes.Gray,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        b.Child = new TextBlock
        {
            Text = text,
            Foreground = System.Windows.Media.Brushes.Black,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };
        MetaChips.Children.Add(b);
    }

    // ---- Actions ----

    private void StartFromHere_Click(object sender, RoutedEventArgs e)
    {
        // Notify the caller: start a new game with the "player who should move next"
        char who;
        if (_currentStep >= _moves.Count)
        {
            // Already finished; guess the next player from the result in the meta info
            who = (_meta.Result ?? "").StartsWith("W+") ? 'b' : 'w';
        }
        else
        {
            who = _moves[_currentStep][0];
        }
        StartFromHereRequested?.Invoke(who == 'b' ? 1 : 2);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

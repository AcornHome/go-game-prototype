using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GoGame;

/// <summary>
/// 打谱 / 复盘窗口。
/// 构造方式：
///   new SgfReplayWindow(filePath)            从磁盘读取
///   new SgfReplayWindow(sgfText, label)       直接传 SGF 内容
///   可通过 StartFromHereRequested 回调通知主窗口"以此局面开新对局"。
/// 棋盘独立维护 Board + Snapshots，不与主游戏状态耦合。
/// </summary>
public partial class SgfReplayWindow : Window
{
    private Board _board = new(19);          // 复盘专用独立 Board（不引用主窗口）
    private readonly List<string> _moves;    // "bD4" / "wQ16" / "bpass" / "bresign"
    private SgfMeta _meta = new();
    private int _currentStep;                // 0..moves.Count

    /// <summary>用户点"从此局面开新对弈"时触发，参数 (nextColor: 1=黑 / 2=白, ...)。</summary>
    public event Action<int>? StartFromHereRequested;

    /// <summary>当前复盘到的局面快照（Stone[,]）。可序列化到 MainWindow 用作"开局局面"。</summary>
    public Stone[,]? CurrentSnapshot
    {
        get
        {
            if (_board.Snapshots.Count == 0) return null;
            int snap = Math.Clamp(_currentStep, 0, _board.Snapshots.Count - 1);
            return (Stone[,])_board.Snapshots[snap].Clone();
        }
    }

    /// <summary>复盘窗口棋盘大小（19/13/9）。</summary>
    public int BoardSize => _board.Size;

    public SgfReplayWindow() : this(emptyLabel: "(空棋谱)")
    {
    }

    public SgfReplayWindow(string sgfOrLabel, bool isFilePath = true) : this()
    {
        if (!isFilePath) { Title = $"打谱 / 复盘 · {sgfOrLabel}"; return; }
        try
        {
            var (b, m, meta) = SgfReader.Read(sgfOrLabel);
            InitializeBoard(b, m, meta);
            Title = $"打谱 / 复盘 · {Path.GetFileName(sgfOrLabel)}";
            MetaFileName.Text = $"文件：{Path.GetFileName(sgfOrLabel)}";
        }
        catch (Exception ex)
        {
            StyledDialog.ShowError(this,
                $"打开棋谱失败：\n{Path.GetFileName(sgfOrLabel)}\n\n{ex.Message}",
                "打谱失败");
            Loaded += (_, _) => Close();
        }
    }

    private SgfReplayWindow(string emptyLabel)
    {
        InitializeComponent();
        _moves = new List<string>();
        _currentStep = 0;
        BoardView.Board = _board;
        BoardView.IsHumanTurn = false;    // 复盘只读，不显示悬停预览
        StepSlider.Minimum = 0;
        StepSlider.Maximum = 0;
        StepSlider.Value = 0;
        UpdateStepDisplay();
        UpdateMetaDisplay();
        Title = $"打谱 / 复盘 · {emptyLabel}";
    }

    public SgfReplayWindow(Board fromBoard, IEnumerable<string> moves, SgfMeta meta, string label = "(当前对局)")
        : this(emptyLabel: label)
    {
        // 复制棋盘
        var copy = new Board(fromBoard.Size);
        var src = fromBoard.Snapshots.Count > 0 ? fromBoard.Snapshots[^1] : fromBoard.Grid;
        for (int x = 0; x < fromBoard.Size; x++)
            for (int y = 0; y < fromBoard.Size; y++)
                copy.Grid[x, y] = src[x, y];
        // Snapshots[0] = 当前完整局面（"起点 = 当前局面，再往前走 _moves"）
        copy.Snapshots.Clear();
        copy.Snapshots.Add(CloneGrid(copy.Grid, copy.Size));
        InitializeBoard(copy, new List<string>(moves), meta);
    }

    /// <summary>将传入的 Board 装载为复盘起点，走完 _moves 一遍把所有快照入栈。</summary>
    private void InitializeBoard(Board board, List<string> moves, SgfMeta meta)
    {
        _board = board;
        _moves.Clear();
        _moves.AddRange(moves);
        _meta = meta;
        _currentStep = 0;
        BoardView.Board = _board;
        BoardView.LastMove = null;

        // 把 _moves 全部应用：每步 Place + Snapshots.Add
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
                    // 自杀手不合法时跳过（SGF 偶尔有历史数据错误）
                }
                catch (FormatException) { /* 跳过非法坐标 */ }
            }
            // 每步（无论是否 Place 成功）都加一个快照，使 Slider 步数对齐
            _board.Snapshots.Add(CloneGrid(_board.Grid, _board.Size));
        }

        // 起点 = 当前局面（Snapshots[0]），走完所有着法后在 _board.Snapshots[^1]
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

    // ---- 导航 ----

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

    /// <summary>跳到第 step 步：0 = 起始状态，n = 走完全部 _moves。</summary>
    private void GoToStep(int step, bool fromSlider = false)
    {
        int target = Math.Clamp(step, 0, _moves.Count);
        _currentStep = target;
        _board.RestoreToSnapshot(target);

        // 上一手高亮（target=0 时不高亮）
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
                catch { /* 跳过 */ }
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
            whoText = "（空棋谱）";
        }
        else if (_currentStep >= _moves.Count)
        {
            whoText = "✔ 复盘结束";
        }
        else
        {
            char c = _moves[_currentStep][0];
            string side = c == 'b' ? "黑" : "白";
            string name = c == 'b' ? (_meta.BlackName ?? "黑方") : (_meta.WhiteName ?? "白方");
            whoText = $"下一步：{side}（{name}）";
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
        MetaMoveCount.Text = $"共 {_moves.Count} 手棋";

        string pb = _meta.BlackName ?? "黑方";
        string pw = _meta.WhiteName ?? "白方";
        if (!string.IsNullOrEmpty(_meta.BlackRank)) pb += $" ({_meta.BlackRank})";
        if (!string.IsNullOrEmpty(_meta.WhiteRank)) pw += $" ({_meta.WhiteRank})";
        MetaPlayers.Text = $"⚫ {pb}  vs  🔘 {pw}";

        string title = _meta.Event ?? "棋谱复盘";
        if (!string.IsNullOrEmpty(_meta.Date)) title += $" · {_meta.Date}";
        MetaTitle.Text = title;

        MetaChips.Children.Clear();
        // 复用 XAML 资源 brush（FindResource）
        var brushAccent    = (System.Windows.Media.Brush?)FindResource("BrushAccent");
        var brushAccentDim = (System.Windows.Media.Brush?)FindResource("BrushAccentDim");
        var brushAi        = (System.Windows.Media.Brush?)FindResource("BrushAI");
        var brushHuman     = (System.Windows.Media.Brush?)FindResource("BrushHuman");

        if (_meta.Komi.HasValue)
            AddChip($"贴目 {_meta.Komi.Value:0.0}", brushAccent);
        if (!string.IsNullOrEmpty(_meta.Rule))
            AddChip(_meta.Rule!, brushAi);
        if (!string.IsNullOrEmpty(_meta.Result))
        {
            string r = _meta.Result!;
            System.Windows.Media.Brush c = r.StartsWith("B+") ? (brushHuman ?? System.Windows.Media.Brushes.Gray) :
                                          r.StartsWith("W+") ? (brushAi ?? System.Windows.Media.Brushes.Gray) : (brushAccent ?? System.Windows.Media.Brushes.Gray);
            AddChip($"结果 {r}", c);
        }
        if (_meta.Handicap.HasValue && _meta.Handicap.Value > 0)
            AddChip($"让子 {_meta.Handicap.Value}", brushAccentDim);
        if (_board.Size != 19)
            AddChip($"{_board.Size}路", brushAccentDim);
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

    // ---- 动作 ----

    private void StartFromHere_Click(object sender, RoutedEventArgs e)
    {
        // 通知调用方：以"下一手应该执子方"开新对局
        char who;
        if (_currentStep >= _moves.Count)
        {
            // 已经走完，从元信息取胜负猜下一步
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

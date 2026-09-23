using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GoGame;

public partial class MainWindow : Window
{
    // KataGo 路径：优先程序目录下的 KataGo\（安装包布局），回退开发机 C:\Tools\KataGo\
    // 玩家机器上不会有 C:\Tools\KataGo，必须靠 KataGoLocator 探测，不能写死
    private static readonly string KatagoExe = KataGoLocator.ExePath;
    private static readonly string ModelPath = KataGoLocator.ModelPath;
    private static readonly string ConfigPath = KataGoLocator.ConfigPath;

    // 棋谱库：放到当前用户的「文档」目录，不能写死具体用户名
    private static readonly string GameLibraryDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChinaGo", "GoGames");

    private KataGoClient? _ai;
    private Board? _board;
    private GameController? _game;
    private bool _busy;      // AI 思考中 / 启动中，禁止落子
    private bool _gameOver;

    // v1.5.2：当前引擎状态对应的资源 key（切换语言时据此重刷顶栏文本）
    private string _engineStateKey = "EngineIdle";
    private bool _started;   // 是否已经开始过对局（闲置模式为 false，玩家点"开始新对局"后变 true）
    private readonly DispatcherTimer _tickTimer;     // 计时器（每 500ms）
    private int _humanSecondsLeft;                   // 玩家读秒
    private DateTime _lastSecondTick = DateTime.MinValue;
    private double _aiThinkTotal;                    // AI 思考总时长（当前这手）
    private bool _aiThinking;
    private DateTime _aiThinkStart;

    // ===== 反馈：保留最近 KataGo 日志（环形缓冲，反馈时取尾部） =====
    private const int MaxKataGoLogTail = 200;
    private readonly LinkedList<string> _recentKataGoLogs = new();

    /// <summary>v1.5.0：当前玩家的本机匿名身份（由 App 在启动后注入，供反馈溯源，无需登录）。</summary>
    private UserAccount? _currentUser;

    /// <summary>
    /// v1.1.0：把登录成功的账号接到主界面上。
    /// 刻意做成方法而不是构造函数参数——MainWindow 的初始化流程已经很长，
    /// 不想为了加个登录把整条链路都改一遍。
    /// </summary>
    public void ApplyCurrentUser(UserAccount user)
    {
        _currentUser = user;
        // v1.5.1：顶栏不再显示棋手徽章（身份仅用于反馈溯源）

        // v1.5.0：提示免费、无额度，按钮文案固定
        RefreshQuotaDisplay();
    }

    // ============ v1.5.0 提示（免费，无额度） ============

    /// <summary>v1.5.0：提示免费、无额度，按钮文案固定。</summary>
    private void RefreshQuotaDisplay()
    {
        HintBtn.Content = LocalizationManager.Get("Hint");
    }

    public MainWindow()
    {
        InitializeComponent();
        BoardView.MoveRequested += OnMoveRequested;
        Loaded += OnLoaded;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _tickTimer.Tick += OnTick;
        _tickTimer.Start();

        UpdateBoardStatus(); // 初始为 0/—

        // v1.5.2：切换语言时刷新顶栏里由代码赋值（而非 XAML DynamicResource）的文本
        LocalizationManager.LanguageChanged += (_, _) => RefreshTopBarLocalized();

        // v1.0.7：去掉了所有开发者入口（Ctrl+Shift+D / --feedback / 设置 / 后台），
        // 反馈改走 QQ 邮箱直发，不需要任何隐藏通道。
    }

    /// <summary>v1.5.2：语言切换后，把代码里赋过值的顶栏文本重新按当前语言设置。</summary>
    private void RefreshTopBarLocalized()
    {
        EngineDotText.Text = LocalizationManager.Get(_engineStateKey);
        if (_game != null)
            PlayerColorText.Text = LocalizationManager.Get(
                _game.HumanColor == Stone.Black ? "ColorBlackFirst" : "ColorWhiteSecond");
    }

    /// <summary>
    /// v1.0.7：去掉了 CloudConfig_Click（云同步设置入口）。
    /// 反馈现在走 QQ 邮箱直发，不需要任何服务器配置。
    /// </summary>

    private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                EnterIdleState();
                // v1.0.7：去掉了「检测到反馈服务器未配置」首次启动引导对话框（已无服务器概念）。
            }
            catch (Exception ex)
            {
                // 兜底：App 层也会接，但这里再写一份到桌面 + 友好提示
                WriteExceptionLog(ex);
                StyledDialog.ShowError(this, "Initialization failed", "Exception during startup:\n" + ex.Message);
            }
        }

    /// <summary>
    /// v1.0.7：去掉了 DevConsole_Click + OnPreviewKeyDown + RefreshDevConsoleButton 整套。
    /// 反馈改走 QQ 邮箱直发，不再需要任何开发者入口。
    /// </summary>

    /// <summary>
    /// 进入"闲置模式"：显示空棋盘 + 友好提示，等玩家点"开始新对局"再启动 KataGo。
    /// 这是应用启动后的初始状态。
    /// </summary>
    private void EnterIdleState()
    {
        if (_ai != null)
        {
            _ai.LogLine -= OnKataGoLog;
            _ai.Dispose();
            _ai = null;
        }

        _started = false;
        _gameOver = false;
        _busy = false;

        int size = GetSelectedSize();
        var humanColor = ColorBox.SelectedIndex == 0 ? Stone.Black : Stone.White;

        _board = new Board(size);
        _game = null;
        BoardView.Board = _board;
        BoardView.LastMove = null;
        BoardView.InvalidateVisual();

        EngineDotText.Text = LocalizationManager.Get(_engineStateKey = "EngineIdle");
        EndgameHintText.Visibility = Visibility.Hidden;
        EndgameOverlay.Visibility = Visibility.Collapsed;
        WinrateText.Text = "";   // 闲置模式不显示胜率
        MoveNumText.Text = "0";
        PlayerColorText.Text = LocalizationManager.Get(
            humanColor == Stone.Black ? "ColorBlack" : "ColorWhite");

        // SetBusy(false, …) 末尾有 "&& _game != null" 守卫，所以 _game == null 时只有 NewGameBtn 可点
        SetBusy(false,
            LocalizationManager.Get("IdleStatus"),
            LocalizationManager.Get("IdleHint"));
        UpdateBoardStatus();
    }

    private async Task StartNewGameAsync(Stone[,]? initialBoard = null)
    {
        SetBusy(true, "Starting KataGo...", "First launch takes ~30s to 3min depending on CPU and network weights");
        _started = true;
        EndgameOverlay.Visibility = Visibility.Collapsed;  // 隐藏终局覆盖层
        WinrateText.Text = "";   // 新对局开始时清空胜率，等 AI 落完第一次后再显示
        ClearHintDisplay();      // 上一局残留的提示标记不能带进新局

        if (_ai != null)
        {
            _ai.LogLine -= OnKataGoLog;
            _ai.Dispose();
            _ai = null;
        }

        int size = GetSelectedSize();
        double levelSeconds = GetLevelSeconds();
        int levelMaxVisits = GetLevelMaxVisits();   // v1.2.5：已整体压低，默认保证跟提示必赢
        var humanColor = ColorBox.SelectedIndex == 0 ? Stone.Black : Stone.White;
        BoardView.HumanColor = humanColor;   // 同步玩家执子色到棋盘（用于悬停预览颜色匹配）

        try
        {
            var (ai, board, game) = await Task.Run(() =>
            {
                var a = new KataGoClient(KatagoExe, ModelPath, ConfigPath, size);
                a.SetTimeSettings(0, levelSeconds, 1);
                // 🆕 v1.2.2: 用 maxVisits 精准控制 AI 棋力（比纯秒数更稳定）。
                // 关键：这一步必须在 GenMove 之前完成，否则首手会用默认算力。
                a.SetMaxVisits(levelMaxVisits);
                var b = new Board(size);
                // 如果传入了初始局面（来自复盘窗口），复制到新 Board 并设置 MoveHistory/Snapshots
                if (initialBoard != null)
                {
                    for (int x = 0; x < size; x++)
                        for (int y = 0; y < size; y++)
                            b.Grid[x, y] = initialBoard[x, y];
                    // Snapshots[0] 仍为空盘快照（用初始内容覆盖）
                    b.Snapshots.Clear();
                    b.Snapshots.Add(CloneInitialGrid(initialBoard, size));
                    // MoveHistory 留空（GameController 不依赖于 MoveHistory）
                    b.NextToPlay = Stone.Black; // 由 GameController 自己重置
                }
                var g = new GameController(b, a, humanColor);
                return (a, b, g);
            });

            _ai = ai;
            _ai.LogLine += OnKataGoLog;
            _board = board;
            _game = game;
            BoardView.Board = board;
            BoardView.LastMove = null;
            BoardView.InvalidateVisual();
            _gameOver = false;
            ResetHumanTimer();
            UpdateBoardStatus();
            UpdateHeaderInfo();
            EndgameHintText.Visibility = Visibility.Hidden;

            // 人类执白时，AI（黑）先手
            if (humanColor == Stone.White)
            {
                await AiTurnAsync();
            }
            else
            {
                SetBusy(false, $"{DescribeTurn()} · click the board to place a stone", "Your turn");
            }
        }
        catch (Exception ex)
        {
            WriteExceptionLog(ex);
            EngineDotText.Text = LocalizationManager.Get(_engineStateKey = "EngineFailed");
            SetBusy(false, "Launch failed: " + ex.Message, "See go-error-*.txt on desktop");
            StyledDialog.ShowError(this, "Launch failed",
                "Failed to start KataGo:\n" + ex.Message +
                "\n\nTroubleshooting:\n" + KataGoLocator.DescribeSearch() +
                "\n\nKataGo needs katago.exe, weights\\" + KataGoLocator.ModelFileName +
                ", " + KataGoLocator.ConfigFileName + " present." +
                "\n\nFull stack saved to go-error-*.txt on your desktop.");
        }
    }

    private async void OnMoveRequested(int x, int y)
    {
        try
        {
            if (_busy || _gameOver || _game == null || _board == null) return;
            if (!_game.IsHumanTurn) return;

            var (ok, msg, captures) = _game.PlayHuman(x, y);
            if (!ok)
            {
                SetBusy(false, msg, "Please choose another intersection");
                return;
            }

            BoardView.LastMove = (x, y);
            BoardView.AnimateMove(x, y);
            ClearHintDisplay();   // 已经落子了，提示完成使命
            if (captures.Count > 0) BoardView.FlashCaptures(captures);
            BoardView.InvalidateVisual();
            UpdateHeaderInfo();
            UpdateBoardStatus();
            CheckEndgameHint();

            // 让 UI 立刻渲染当前落子（落子→AI 计算间的"跟手"感）
            await Dispatcher.Yield(DispatcherPriority.Render);

            await AiTurnAsync();
        }
        catch (Exception ex)
        {
            WriteExceptionLog(ex);
            _busy = false;
            SetBusy(false, "Error while placing stone: " + ex.Message, "You can keep playing, or click New Game to reset");
        }
    }

    private async Task AiTurnAsync()
    {
        if (_game == null) return;
        double thinkSeconds = GetLevelSeconds();
        SetBusy(true, "KataGo is thinking...", $"Expected to move in ~{thinkSeconds:F0}s (includes position analysis)");
        _aiThinking = true;
        _aiThinkStart = DateTime.Now;
        _aiThinkTotal = thinkSeconds;
        try
        {
            // 用 kata-genmove_analyze 走子：顺带拿到当前局面的胜率/目差，**零额外开销**
            var (move, captures) = await _game.PlayAiAsync(thinkSeconds);
            if (move != "pass" && move != "resign")
                BoardView.LastMove = ParseLastMove(move);
            if (captures.Count > 0) BoardView.FlashCaptures(captures);
            BoardView.InvalidateVisual();
            UpdateHeaderInfo();
            UpdateBoardStatus();
            UpdateWinrateDisplay();
            CheckEndgameHint();

            if (move == "resign")
            {
                _gameOver = true;
                ShowEndgameOverlayHumanWin("KataGo resigns");
            }
            else if (move == "pass")
            {
                SetBusy(false, "KataGo passed, your turn (keep playing or tap Score)", "Two consecutive passes end the game");
            }
            else
            {
                SetBusy(false, "Your turn", "Click an empty intersection, or tap Score to end the game");
            }
            ResetHumanTimer();
        }
        catch (Exception ex)
        {
            SetBusy(false, "AI error: " + ex.Message, "Start a new game or check KataGo logs");
        }
        finally
        {
            _aiThinking = false;
        }
    }

    /// <summary>
    /// 把 kata-genmove_analyze 给出的"参数 color 视角"胜率/目差，换算成玩家视角，并写到 WinrateText。
    /// kata-genmove_analyze 调用时 color = AiColor，所以 rootInfo 是 AI 胜率。换算：
    ///   玩家胜率 = 1 - AI 胜率
    ///   玩家目差 = -AI 目差
    /// </summary>
    private void UpdateWinrateDisplay()
    {
        if (_game == null || _game.LastAnalysis == null)
        {
            WinrateText.Text = "";
            return;
        }
        var a = _game.LastAnalysis;
        double aiWinrate = Math.Clamp(a.Winrate, 0, 1);
        double humanWinrate = 1 - aiWinrate;
        double humanLead = -a.ScoreLead;

        string playerLabel = _game.HumanColor == Stone.Black
            ? LocalizationManager.Get("BlackShort") : LocalizationManager.Get("WhiteShort");

        string sideIcon = humanWinrate >= 0.5 ? "🟢" : "🔴";
        // 文字 + 颜色：玩家领先用绿（BrushHuman），落后用危险色（BrushDanger）
        WinrateText.Text =
            $"{sideIcon} {playerLabel} {humanWinrate * 100:F1}% · {(humanLead >= 0 ? "leading" : "trailing")} {Math.Abs(humanLead):F1} pts";
        WinrateText.Foreground = (System.Windows.Media.Brush)FindResource(
            humanWinrate >= 0.5 ? "BrushHuman" : "BrushDanger");
    }

    private (int X, int Y)? ParseLastMove(string move)
    {
        if (move == "pass" || move == "resign" || _board == null) return null;
        try { return Board.FromGtpVertex(move, _board.Size); }
        catch { return null; }
    }

    private void SetBusy(bool busy, string status, string hint)
    {
        _busy = busy;
        StatusText.Text = status;
        StatusHintText.Text = hint;
        NewGameBtn.IsEnabled = !busy;
        HintBtn.IsEnabled = !busy && !_gameOver && _game != null;
        UndoBtn.IsEnabled = !busy && !_gameOver && _game != null;
        PassBtn.IsEnabled = !busy && !_gameOver && _game != null;
        ScoreBtn.IsEnabled = !busy && !_gameOver && _game != null;
        SaveBtn.IsEnabled = !busy && _game != null;
        ResignBtn.IsEnabled = !busy && !_gameOver && _game != null;
        EndgameBtn.IsEnabled = !busy && _game != null;
        // 顶栏反馈按钮：任何时候都可点（不影响对局）
        TopFeedbackBtn.IsEnabled = true;
        BoardView.IsHumanTurn = !busy && _game != null && _game.IsHumanTurn;
    }

    private void OnKataGoLog(string msg)
    {
        // 累积到环形缓冲（线程安全：AddLast/RemoveFirst 是文档保证线程安全的，冲突时丢行可接受）
        lock (_recentKataGoLogs)
        {
            _recentKataGoLogs.AddLast(msg);
            while (_recentKataGoLogs.Count > MaxKataGoLogTail)
                _recentKataGoLogs.RemoveFirst();
        }

        if (Dispatcher.CheckAccess())
            UpdateEngineStatus(msg);
        else
            Dispatcher.BeginInvoke(() => UpdateEngineStatus(msg));
    }

    private string GetRecentKataGoLogTail(int n)
    {
        lock (_recentKataGoLogs)
        {
            int skip = System.Math.Max(0, _recentKataGoLogs.Count - n);
            var sb = new StringBuilder();
            int i = 0;
            foreach (var line in _recentKataGoLogs)
            {
                if (i++ < skip) continue;
                sb.AppendLine(line);
            }
            return sb.ToString();
        }
    }

    private void UpdateEngineStatus(string msg)
    {
        // 引擎状态条（极简、友好，**绝不覆盖主状态条**）
        if (msg.Contains("ready", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("loaded", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Loaded", StringComparison.Ordinal))
        {
            EngineDotText.Text = LocalizationManager.Get(_engineStateKey = "EngineReady");
        }
        else if (msg.Contains("error", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("fail", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            EngineDotText.Text = LocalizationManager.Get(_engineStateKey = "EngineError");
            StatusHintText.Text = "Engine error, try starting a new game";
        }
        else if (msg.Contains("initialized", StringComparison.OrdinalIgnoreCase))
        {
            EngineDotText.Text = LocalizationManager.Get(_engineStateKey = "EngineLoading");
        }
    }

    // ---- 底部局面信息更新 ----

    private void UpdateBoardStatus()
    {
        if (_board == null || _game == null)
        {
            BlackCountText.Text = "0";
            WhiteCountText.Text = "0";
            BlackLastMoveText.Text = "—";
            WhiteLastMoveText.Text = "—";
            BottomMoveNumText.Text = "0";
            BottomTurnText.Text = "—";
            return;
        }

        int black = 0, white = 0;
        for (int x = 0; x < _board.Size; x++)
            for (int y = 0; y < _board.Size; y++)
            {
                var s = _board.Grid[x, y];
                if (s == Stone.Black) black++;
                else if (s == Stone.White) white++;
            }
        BlackCountText.Text = black.ToString();
        WhiteCountText.Text = white.ToString();

        // 最近一手（看 MoveHistory）
        var history = _board.MoveHistory;
        if (history.Count > 0)
        {
            var last = history[^1];
            char c = last[0];
            string v = last.Substring(1);
            if (c == 'b')
            {
                BlackLastMoveText.Text = v;
                WhiteLastMoveText.Text = WhiteLastMoveText.Text == "—" ? "—" : WhiteLastMoveText.Text;
            }
            else if (c == 'w')
            {
                WhiteLastMoveText.Text = v;
            }
        }

        BottomMoveNumText.Text = history.Count.ToString();
        BottomTurnText.Text = _board.NextToPlay == Stone.Black
            ? "● " + LocalizationManager.Get("BlackLabel")
            : "○ " + LocalizationManager.Get("WhiteLabel");
    }

    /// <summary>检测棋盘接近满/终局条件，给玩家提示。</summary>
    private void CheckEndgameHint()
    {
        if (_board == null || _gameOver) return;

        int total = _board.Size * _board.Size;
        int occupied = 0;
        for (int x = 0; x < _board.Size; x++)
            for (int y = 0; y < _board.Size; y++)
                if (_board.Grid[x, y] != Stone.Empty) occupied++;

        double ratio = (double)occupied / total;

        if (ratio >= 0.85)
        {
            EndgameHintText.Text = $"Board {(int)(ratio * 100)}% filled · two consecutive passes end the game, tap Score";
            EndgameHintText.Visibility = Visibility.Visible;
        }
        else if (ratio >= 0.65)
        {
            EndgameHintText.Text = $"Board {(int)(ratio * 100)}% filled · near midgame";
            EndgameHintText.Visibility = Visibility.Visible;
        }
        else
        {
            EndgameHintText.Visibility = Visibility.Hidden;
        }
    }

    private void UpdateHeaderInfo()
    {
        if (_game == null)
        {
            MoveNumText.Text = "0";
            PlayerColorText.Text = "—";
            return;
        }
        MoveNumText.Text = (_board?.MoveHistory.Count ?? 0).ToString();
        PlayerColorText.Text = LocalizationManager.Get(
            _game.HumanColor == Stone.Black ? "ColorBlackFirst" : "ColorWhiteSecond");
    }

    // ---- 计时器（每 500ms 触发） ----

    private void OnTick(object? sender, EventArgs e)
    {
        if (_game == null || _gameOver) return;

        if (_aiThinking)
        {
            double elapsed = (DateTime.Now - _aiThinkStart).TotalSeconds;
            double pct = Math.Min(100, elapsed / _aiThinkTotal * 100);
            AiTimerBar.Value = pct;
            AiTimerText.Text = $"{elapsed:F1} / {_aiThinkTotal:F0}s";
        }
        else
        {
            AiTimerBar.Value = 0;
            AiTimerText.Text = LocalizationManager.Get("Standby");
        }

        if (_game.IsHumanTurn && !_aiThinking)
        {
            // v1.2.4: Tick 间隔 500ms→1000ms，每 tick 直接 -1 秒（之前用 500ms 时靠 ≥1 秒条件节流）
            _humanSecondsLeft--;
            if (_humanSecondsLeft <= 0)
            {
                _humanSecondsLeft = 0;
                _gameOver = true;
                SetBusy(false, "Read-timeout — you lose by forfeit", "Click \"New Game\" to play again");
                ShowEndgameOverlayHumanLoss("Read timeout");
            }
            HumanTimerBar.Value = (double)_humanSecondsLeft / Math.Max(1, GetHumanSecondsBudget()) * 100;
            HumanTimerText.Text = _humanSecondsLeft >= 60
                ? $"{_humanSecondsLeft / 60}m {_humanSecondsLeft % 60}s"
                : $"{_humanSecondsLeft}s";
        }
    }

    private void ResetHumanTimer()
    {
        _humanSecondsLeft = GetHumanSecondsBudget();
        _lastSecondTick = DateTime.Now;
        HumanTimerBar.Value = 100;
        HumanTimerText.Text = _humanSecondsLeft >= 60
            ? $"{_humanSecondsLeft / 60}m"
            : $"{_humanSecondsLeft}s";
    }

    private int GetHumanSecondsBudget()
    {
        return LevelBox.SelectedIndex switch
        {
            0 => 60,
            1 => 45,
            2 => 30,
            _ => 15
        };
    }

// ---- 棋谱自动入库（无 UI 列表，直接存档到磁盘） ----

    private void AutoSaveLibrary()
    {
        if (_game == null || _board == null) return;
        try
        {
            Directory.CreateDirectory(GameLibraryDir);
            var sgf = SgfWriter.Write(_board, _game.HumanColor);
            var path = Path.Combine(GameLibraryDir,
                $"go-{DateTime.Now:yyyyMMdd-HHmmss}-{(_game.HumanColor == Stone.Black ? "black" : "white")}.sgf");
            File.WriteAllText(path, sgf, Encoding.UTF8);
        }
        catch { }
    }

    // ---- 终局覆盖层 ----

    private void ShowEndgameOverlay(string winner, string reason, double? margin = null, string? scoreString = null)
    {
        if (_board == null) return;
        EndgameTitle.Text = LocalizationManager.Get("EndgameTitle");
        EndgameWinner.Text = winner;
        EndgameScore.Text = margin.HasValue
            ? $"{reason} · win by {margin.Value:F1} (incl. 7.5 komi)"
            : reason;

        int total = _board.Size * _board.Size;
        int black = 0, white = 0, empty = 0;
        for (int x = 0; x < _board.Size; x++)
            for (int y = 0; y < _board.Size; y++)
            {
                var s = _board.Grid[x, y];
                if (s == Stone.Black) black++;
                else if (s == Stone.White) white++;
                else empty++;
            }

        // 围空估算（BFS 找只被一种颜色包围的空区域）
        int blackEmpty = CountControlledEmpty(_board, Stone.Black);
        int whiteEmpty = CountControlledEmpty(_board, Stone.White);
        int neutralEmpty = empty - blackEmpty - whiteEmpty;

        // 显示
        EndgameBlackStones.Text = black.ToString();
        EndgameBlackEmpty.Text = blackEmpty.ToString();
        EndgameWhiteStones.Text = white.ToString();
        EndgameWhiteEmpty.Text = whiteEmpty.ToString();

        int blackTotal = black + blackEmpty;
        int whiteTotal = white + whiteEmpty;
        EndgameBlackTotal.Text = $"Total {blackTotal}";
        EndgameWhiteTotal.Text = $"Total {whiteTotal}";

        // 公式 + 净胜计算
        // 中国规则：黑胜 X = (黑子 + 黑围空) - (白子 + 白围空) - 7.5（贴目）
        // KataGo 输出 B+3.5 = 黑净胜 3.5（已扣贴目 7.5）
        double rawDiff = blackTotal - whiteTotal;          // 黑白实际占空差（不含贴目）
        double afterKomi = rawDiff - 7.5;                   // 黑 - 白 - 7.5 = 简化估算

        var estimateText =
            $"Estimate: Black(stones+terr) {blackTotal} − White(stones+terr) {whiteTotal} − 7.5 komi = {(afterKomi):+0.0;-0.0;0.0}";

        // 终局前最后一手 kata-genmove_analyze 给的玩家视角胜率（如果还残留）
        string aiWinrateHint = "";
        if (_game?.LastAnalysis != null)
        {
            double humanWinrate = 1 - _game.LastAnalysis.Winrate;
            string humanLabel = _game.HumanColor == Stone.Black
                ? LocalizationManager.Get("EndgameDetailBlackYou")
                : LocalizationManager.Get("EndgameDetailWhiteYou");
            aiWinrateHint = $"\nPre-endgame AI eval: {humanLabel} win {humanWinrate * 100:F1}% · lead {_game.LastAnalysis.ScoreLead * -1:F2}";
        }

        string komiNote = neutralEmpty > 0
            ? string.Format(LocalizationManager.Get("KomiNoteNeutral"), neutralEmpty)
            : LocalizationManager.Get("KomiNoteDecided");

        if (!string.IsNullOrEmpty(scoreString))
        {
            EndgameFormula.Text = $"KataGo final_score: {scoreString}\n{estimateText}{aiWinrateHint}";
            EndgameKomiNote.Text = komiNote;
        }
        else
        {
            EndgameFormula.Text = $"{estimateText}{aiWinrateHint}";
            EndgameKomiNote.Text = komiNote;
        }

        EndgameOverlay.Visibility = Visibility.Visible;
        AutoSaveLibrary();

        // 让关闭按钮获得键盘焦点，这样按 ESC 会自动触发 IsCancel=True 的按钮单击 → 关闭面板
        EndgameCornerCloseBtn.Focusable = true;
        EndgameCornerCloseBtn.Focus();

        // 入场动画：缩放 0.85 → 1.0 + 透明度 0 → 1，缓出 320ms
        BeginEndgameEntranceAnim();
    }

    /// <summary>
    /// 终局面板入场动画。直接对 EndgameOverlay 内的 Border 应用 RenderTransform + Opacity 动画，
    /// 比 XAML Storyboard 更紧凑且不依赖资源键。
    /// </summary>
    private void BeginEndgameEntranceAnim()
    {
        var border = FindVisualChild<Border>(EndgameOverlay);
        if (border == null) return;

        // 准备 RenderTransform（一次设置，多次复用）
        var group = new System.Windows.Media.TransformGroup();
        var scale = new System.Windows.Media.ScaleTransform(0.85, 0.85);
        group.Children.Add(scale);
        border.RenderTransform = group;
        border.RenderTransformOrigin = new Point(0.5, 0.5);
        border.Opacity = 0;

        var animOpacity = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };
        var animScaleX = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.85,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new System.Windows.Media.Animation.BackEase
            {
                Amplitude = 0.35,
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };
        var animScaleY = animScaleX.Clone();   // Y 同步

        border.BeginAnimation(OpacityProperty, animOpacity);
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animScaleX);
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, animScaleY);
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    /// <summary>BFS 数被某色完全包围的空交叉点数（棋盘上所有空格被一种颜色棋子"围住"才能算该色的目）。
    /// 注：严格来说围空要求"棋子四周邻接且通过邻接围住该空区域"，这里做简化版（4 邻接可达且路径上只有该色棋子）。
    /// 用于终局展示；KataGo 的 final_score 才作权威胜负。</summary>
    private int CountControlledEmpty(Board board, Stone color)
    {
        int size = board.Size;
        var visited = new bool[size, size];
        int count = 0;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                if (board.Grid[x, y] != Stone.Empty || visited[x, y]) continue;

                // BFS 这个空区域
                var queue = new Queue<(int x, int y)>();
                var region = new List<(int x, int y)>();
                bool touchesBlack = false, touchesWhite = false;
                queue.Enqueue((x, y));
                visited[x, y] = true;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    region.Add((cx, cy));
                    foreach (var (dx, dy) in new[] { (-1,0),(1,0),(0,-1),(0,1) })
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || nx >= size || ny < 0 || ny >= size) continue;
                        if (visited[nx, ny]) continue;
                        if (board.Grid[nx, ny] == Stone.Empty)
                        {
                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                        else if (board.Grid[nx, ny] == Stone.Black)
                        {
                            touchesBlack = true;
                        }
                        else if (board.Grid[nx, ny] == Stone.White)
                        {
                            touchesWhite = true;
                        }
                    }
                }

                // 只有该颜色的棋子包围才算该色的空（中立或公共空不归任何一方）
                if (color == Stone.Black && touchesBlack && !touchesWhite)
                    count += region.Count;
                else if (color == Stone.White && touchesWhite && !touchesBlack)
                    count += region.Count;
            }
        }
        return count;
    }

    private void ShowEndgameOverlayHumanWin(string reason)
    {
        string who = _game?.HumanColor == Stone.Black
            ? LocalizationManager.Get("EndgameBlackYou") : LocalizationManager.Get("EndgameWhiteYou");
        ShowEndgameOverlay($"{who} {LocalizationManager.Get("EndgameYouWin")}", reason);
    }

    private void ShowEndgameOverlayHumanLoss(string reason)
    {
        string who = _game?.HumanColor == Stone.Black
            ? LocalizationManager.Get("EndgameWhiteAi") : LocalizationManager.Get("EndgameBlackAi");
        ShowEndgameOverlay($"{who} {LocalizationManager.Get("EndgameAiWin")}", reason);
    }

    private void EndgameClose_Click(object sender, RoutedEventArgs e)
    {
        EndgameOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>打开 AI 复盘报告窗口（KataGo 每步走子后的胜率曲线 + 转折点）。</summary>
    private void EndgameReview_Click(object sender, RoutedEventArgs e)
    {
        if (_game == null || _game.MoveAnalyses.Count == 0)
        {
            StyledDialog.ShowInfo(this, "No review data yet", "KataGo hasn't moved yet, or no analysis was collected.\nPlay at least one game with KataGo moves.");
            return;
        }
        var winnerLabel = EndgameWinner?.Text ?? "Game over";
        var win = new ReviewReportWindow(_game.MoveAnalyses, _game.HumanColor, winnerLabel)
        {
            Owner = this
        };
        win.ShowDialog();
    }

    /// <summary>从 MoveHistory 最后一步推断结果（认输即对方赢）。终局数子走 final_score 后由用户手动更新 SGF。</summary>
    private SgfWriter.Outcome InferOutcome()
    {
        if (_board == null || _board.MoveHistory.Count == 0)
            return SgfWriter.Outcome.Unknown;

        string last = _board.MoveHistory[^1];
        if (last.Length < 2) return SgfWriter.Outcome.Unknown;

        char who = char.ToLower(last[0]);
        string body = last.Substring(1).ToLowerInvariant();

        if (body != "resign" && body != "tt")
            return SgfWriter.Outcome.Unknown;

        // 黑认输 → 白赢；白认输 → 黑赢
        return who == 'b' ? SgfWriter.Outcome.WhiteWin : SgfWriter.Outcome.BlackWin;
    }

    /// <summary>
    /// v1.0.7：去掉了 Network_Click（联机对弈入口）。
    /// 玩家不暴露 LobbyWindow / NetworkGameWindow。
    /// </summary>

    /// <summary>
    /// 顶栏反馈按钮。打开反馈窗口，自动收集：当前对局快照 + 环境信息 + 最近 30 行 KataGo 日志。
    /// v1.0.7：提交时直接通过 QQ 邮箱 SMTP 发送到 954038398@qq.com，不再走本地 JSON / 云同步。
    /// </summary>
    private void Feedback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 当前对局快照（_game/_board 可能为 null，因为闲置模式还没开局）
            var size = _game?.Board?.Size ?? _board?.Size ?? GetSelectedSize();
            var moveCount = _game?.Board?.MoveHistory?.Count ?? _board?.MoveHistory?.Count ?? 0;
            string lastMove = "";
            var history = _game?.Board?.MoveHistory ?? _board?.MoveHistory;
            if (history is { Count: > 0 })
            {
                lastMove = history[^1] ?? "";
            }
            var nextToPlay = _game?.Board?.NextToPlay ?? _board?.NextToPlay ?? Stone.Black;
            string toMove = nextToPlay == Stone.Black
                ? LocalizationManager.Get("BlackShort") : LocalizationManager.Get("WhiteShort");
            bool kataReady = _ai?.IsRunning ?? false;

            var win = new FeedbackWindow(
                size, moveCount, lastMove, toMove, kataReady,
                KataGoLocator.ExePath,
                KataGoLocator.ModelPath,
                GetRecentKataGoLogTail(30),
                _currentUser?.UserName, _currentUser?.Id)
            {
                Owner = this
            };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            WriteExceptionLog(ex);
            StyledDialog.ShowError(this, "Failed to open feedback window",
                "Please send the following info to the developer:\n" + ex.Message);
        }
    }

    /// <summary>
    /// v1.0.7：去掉了 LaunchFeedbackViewer（命令行 --feedback 入口）。
    /// 反馈数据不再写本地 JSON，直接通过 SMTP 发到 954038398@qq.com。
    /// v1.4.0 起：SMTP 整套已删除，反馈改为 HTTP 上传到开发者这台电脑（D:\ChinaGo\Feedback）。
    /// v1.4.1 起：上传地址走材料库 3000 端口的 /chinago 前缀（寄生），外网域名同理。
    /// </summary>

    /// <summary>v1.6.0：打开语言选择菜单（English / 中文 / 日本語 / 한국어 / Español）。</summary>
    private void LangBtn_Click(object sender, RoutedEventArgs e)
    {
        LangBtn.ContextMenu?.IsOpen = true;
    }

    private void LangMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Header is string label)
        {
            var code = label switch
            {
                "English" => "en-US",
                "中文" => "zh-CN",
                "日本語" => "ja-JP",
                "한국어" => "ko-KR",
                "Español" => "es-ES",
                _ => null
            };
            if (code != null) LocalizationManager.SetLanguage(code);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("China Go  V" + FeedbackService.AppVersion());
        if (_currentUser != null)
            sb.AppendLine($"Current player: {_currentUser.UserName}    ID: {_currentUser.DisplayId}");
        sb.AppendLine();
        sb.AppendLine("A Go (Weiqi) playing & learning app powered by the KataGo engine.");
        sb.AppendLine("Features: human vs AI, review, tutorials, life-and-death problems, LAN play.");
        sb.AppendLine();
        sb.AppendLine("────────────── Open-source credits ──────────────");
        sb.AppendLine();
        sb.AppendLine("◆ KataGo engine");
        sb.AppendLine("  Author: David J Wu (\"lightvector\")");
        sb.AppendLine("  Source: https://github.com/lightvector/KataGo");
        sb.AppendLine("  License: MIT License");
        sb.AppendLine();
        sb.AppendLine("◆ KataGo neural network weights (kata1-b18c384nbt)");
        sb.AppendLine("  Source: https://katagotraining.org");
        sb.AppendLine("  License: MIT License (KataGo Neural Network License)");
        sb.AppendLine("  Citation: Wu, D.J. (2019). Accelerating Self-Play Learning in Go. arXiv:1902.10565");
        sb.AppendLine();
        sb.AppendLine("◆ Third-party libraries bundled with KataGo");
        sb.AppendLine("  OpenBLAS (BSD), zlib (zlib), OpenSSL (OpenSSL)");
        sb.AppendLine();
        sb.AppendLine("Full license texts and third-party notices are in the install folder:");
        sb.AppendLine("  • THIRD-PARTY-NOTICES.txt");
        sb.AppendLine("  • KataGo/License.txt");
        sb.AppendLine("  • KataGo/weights/License.txt");
        sb.AppendLine();
        sb.AppendLine("────────────── Feedback ──────────────");
        sb.AppendLine();
        sb.AppendLine("Use the Feedback button on the top bar to report issues.");

        MessageBox.Show(this, sb.ToString(), "About China Go",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static Stone[,] CloneInitialGrid(Stone[,] src, int size)
    {
        var dst = new Stone[size, size];
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                dst[x, y] = src[x, y];
        return dst;
    }

    // ---- 通用辅助 ----

    private void WriteExceptionLog(Exception ex)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"go-error-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path,
                $"Message: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}\n\nInner: {ex.InnerException?.Message}\n{ex.InnerException?.StackTrace}",
                Encoding.UTF8);
        }
        catch { }
    }

    private string DescribeTurn()
    {
        if (_game == null) return "";
        return _game.IsHumanTurn
            ? "Your turn (" + (_game.HumanColor == Stone.Black
                ? LocalizationManager.Get("BlackLabel") : LocalizationManager.Get("WhiteLabel")) + ")"
            : "KataGo thinking";
    }

    private int GetSelectedSize() => SizeBox.SelectedIndex switch { 0 => 9, 1 => 13, _ => 19 };

    private double GetLevelSeconds() => LevelBox.SelectedIndex switch { 0 => 1, 1 => 3, 2 => 10, _ => 30 };

    /// <summary>
    /// v1.2.3: 大幅拉开 AI 与提示方的算力差距，确保玩家跟着提示能赢。
    /// 实测 v1.2.2（200 vs 5000 visits）初级档 + 9 路 + 跟提示下 → 仍输 0.5 目。
    /// 根因：KataGo 在 9 路上 5000 visits 已经接近这个权重的天花板（棋力饱和），
    ///       AI 用 200 visits 也是"业余 10 级"，提示比 AI 强但强得不够大，差距在贴目范围内。
/// v1.2.5 去掉稳赢开关，改为**默认保证**：
///   AI 档位整体压低到 20/40/80/160（原来 50/100/400/2000），
///   即使选最高档，提示（15000 visits）仍有 ~94x 算力优势 → 一直按提示下必赢。
///   不点提示、自己下的玩家仍能感受到 20→160 的棋力梯度（20 极弱，160 约人类入门）。
/// </summary>
private int GetLevelMaxVisits() => LevelBox.SelectedIndex switch { 0 => 20, 1 => 40, 2 => 80, _ => 160 };
private const int HintMaxVisits = 15000;
private const double HintMaxSeconds = 20.0;

    // ---- 按钮事件 ----

    private async void NewGame_Click(object sender, RoutedEventArgs e) { try { await StartNewGameAsync(); } catch (Exception ex) { WriteExceptionLog(ex); StyledDialog.ShowError(this, "New game failed", ex.Message); } }

    /// <summary>
    /// 提示思考秒数：**AI 对手的 2 倍**（下限 2 秒、上限 20 秒）。
    ///
    /// ⚠ 方向不能搞反：提示的意义是"让玩家跟着下就能赢"，所以它的算力必须**强过**对手。
    /// 早期版本写成 `难度 ÷ 2 封顶 5 秒`，导致提示永远弱于 AI（高级档 AI 30 秒 vs 提示 5 秒，
    /// 差 6 倍），玩家跟着提示下反而更容易输——这是方向性错误。
    ///
    /// 上限 20 秒是体验权衡：再长玩家等得煎熬，且边际收益递减。
    /// 各档实际算力对比（AI → 提示）：入门 1→2s、初级 3→6s、中级 10→20s、高级 30→20s。
    /// </summary>
    private double GetHintSeconds() => Math.Clamp(GetLevelSeconds() * 2.0, 2.0, 20.0);

    /// <summary>
    /// 「💡 提示」：让玩家不知道怎么下时求一手 AI 建议。
    /// 走 kata-genmove_analyze 替玩家试算，随后 GTP undo 撤销，所以**本局状态完全不变**。
    /// 结果同时体现在棋盘（青绿标记，1 号最推荐）和状态条文字上。
    /// </summary>
    private async void Hint_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _game == null || _board == null) return;

        if (_gameOver)
        {
            SetBusy(false, "Game over", "Click New Game to play again");
            return;
        }
        if (!_game.IsHumanTurn)
        {
            SetBusy(false, "Not your turn", "Wait for KataGo to move before asking for a hint");
            return;
        }

        // v1.2.2: 提示固定用 5000 visits + 10 秒思考上限——**保证提示方算力远强于任何 AI 档位**，
        // 玩家跟着提示下基本必胜（除非走错一两手）。不再用 GetHintSeconds() 的倍数策略。
        int hintVisits = HintMaxVisits;
        double hintSeconds = HintMaxSeconds;
        SetBusy(true, "KataGo is finding the best point for you...",
            $"Thinking hard ({hintVisits} nodes · up to {hintSeconds:F0}s); opponent limited to {GetLevelMaxVisits()} nodes per move");
        try
        {
            var result = await _game.RequestHintAsync(hintVisits, hintSeconds);

            if (result == null || result.Candidates.Count == 0)
            {
                ClearHintDisplay();
                SetBusy(false, "Could not compute a hint", "KataGo does not support analysis, or the engine is not ready");
                return;
            }

            // 认输 / 虚手不是"落点"，直接说清楚比在棋盘上标点更有用
            if (string.Equals(result.BestMove, "resign", StringComparison.OrdinalIgnoreCase))
            {
                ClearHintDisplay();
                SetBusy(false, "KataGo thinks this position is already lost", "Consider resigning, or tap Score to see the gap");
                return;
            }
            if (string.Equals(result.BestMove, "pass", StringComparison.OrdinalIgnoreCase))
            {
                ClearHintDisplay();
                SetBusy(false, "KataGo suggests a pass (no worthwhile point)", "Common in the endgame; you can tap Score directly");
                return;
            }

            // GTP 顶点 → 棋盘坐标，最多标 3 个；解析失败或非空点一律跳过
            var marks = new List<(int x, int y, string vertex)>();
            foreach (var c in result.Candidates.Take(3))
            {
                if (string.Equals(c.Vertex, "pass", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Vertex, "resign", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var (hx, hy) = Board.FromGtpVertex(c.Vertex, _board.Size);
                    if (_board.IsInBounds(hx, hy) && _board.Get(hx, hy) == Stone.Empty)
                        marks.Add((hx, hy, c.Vertex));
                }
                catch { /* 个别候选顶点异常不影响其他候选 */ }
            }

            if (marks.Count == 0)
            {
                ClearHintDisplay();
                SetBusy(false, "Hint point no longer available", "Keep playing, or start a new game");
                return;
            }
            // v1.2.5: 稳赢开关已移除，提示不再锁定落点（玩家自由落子）
            BoardView.ShowHints(marks);

            var best = result.Candidates[0];
            var sb = new StringBuilder();
            sb.Append($"💡 Suggest playing {best.Vertex}");
            sb.Append($"　(Win rate {best.Winrate * 100:F1}% · ");
            sb.Append(best.ScoreLead >= 0
                ? $"ahead by {best.ScoreLead:F1} points)"
                : $"behind by {Math.Abs(best.ScoreLead):F1} points)");

            string follow = best.FollowUpText;
            if (!string.IsNullOrEmpty(follow)) sb.Append($"　Follow-up: {follow}");

            var alts = result.Candidates.Skip(1)
                .Where(c => !string.Equals(c.Vertex, "pass", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(c.Vertex, "resign", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Vertex)
                .Take(2)
                .ToList();
            if (alts.Count > 0) sb.Append($"　Alternatives: {string.Join(", ", alts)}");

            HintText.Text = sb.ToString();
            HintText.Visibility = Visibility.Visible;

            SetBusy(false, $"Suggest playing {best.Vertex}", "Point #1 on the board is best — play it to win");
        }
        catch (Exception ex)
        {
            WriteExceptionLog(ex);
            ClearHintDisplay();
            SetBusy(false, "Analysis failed: " + ex.Message, "Start a new game and try again");
        }
    }

    /// <summary>清除棋盘上的提示标记 + 状态条提示文字。
    /// ⚠ v1.2.5：用 Hidden 不用 Collapsed —— Hidden 保留占位高度，
    /// 状态条总高度不随提示显示/隐藏而变，棋盘不会被挤压（抖动根治）。</summary>
    private void ClearHintDisplay()
    {
        BoardView.ClearHints();
        HintText.Text = "";
        HintText.Visibility = Visibility.Hidden;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_game == null || _busy) return;
        var (ok, msg) = _game.Undo();
        SetBusy(false, ok ? msg : "Undo failed: " + msg, ok ? "Undid one move each for the AI and you" : "");
        if (ok)
        {
            BoardView.LastMove = null;
            BoardView.InvalidateVisual();
            ClearHintDisplay();   // 局面变了，旧提示作废
            UpdateHeaderInfo();
            UpdateBoardStatus();
        }
        ResetHumanTimer();
    }

    private async void Pass_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_game == null || _busy || !_game.IsHumanTurn) return;
            var (ok, msg) = _game.PassHuman();
            if (!ok) { SetBusy(false, msg, "Wait until it's your turn"); return; }
            BoardView.InvalidateVisual();
            UpdateHeaderInfo();
            UpdateBoardStatus();
            await AiTurnAsync();
        }
        catch (Exception ex)
        {
            WriteExceptionLog(ex);
            SetBusy(false, "Pass failed: " + ex.Message, "Start a new game");
        }
    }

    private void Score_Click(object sender, RoutedEventArgs e)
    {
        if (_game == null || _busy) return;
        try
        {
            var score = _game.FinishAndScore();
            _gameOver = true;
            SetBusy(false, "Game ended: " + score, "The result panel has popped up");

            // Parse "B+3.5" / "W+2.5" / "0"
            if (score.StartsWith("B+"))
            {
                double margin = double.Parse(score.Substring(2));
                bool humanIsBlack = _game.HumanColor == Stone.Black;
                string winner = humanIsBlack ? "Black (you) wins" : "Black (KataGo) wins";
                ShowEndgameOverlay(winner, "Scoring result", margin, score);
            }
            else if (score.StartsWith("W+"))
            {
                double margin = double.Parse(score.Substring(2));
                bool humanIsWhite = _game.HumanColor == Stone.White;
                string winner = humanIsWhite ? "White (you) wins" : "White (KataGo) wins";
                ShowEndgameOverlay(winner, "Scoring result", margin, score);
            }
            else
            {
                ShowEndgameOverlay("Draw", "Both sides have equal territory", null, score);
            }
        }
        catch (Exception ex)
        {
            StyledDialog.ShowWarning(this, "Scoring failed", ex.Message);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_game == null || _board == null) return;
        try
        {
            var sgf = SgfWriter.Write(_board, _game.HumanColor);
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"GoGame-{DateTime.Now:yyyyMMdd-HHmmss}.sgf");
            File.WriteAllText(path, sgf, Encoding.UTF8);
            SetBusy(false, "Game saved to desktop: " + Path.GetFileName(path), "Double-click the .sgf on your desktop to open it in another Go program");
        }
        catch (Exception ex)
        {
            StyledDialog.ShowWarning(this, "Save failed", ex.Message);
        }
    }

    private void Resign_Click(object sender, RoutedEventArgs e)
    {
        if (_game == null || _busy) return;
        if (!StyledDialog.ShowConfirm(this, "Confirm resignation", "Resign? The game record will be saved automatically.", destructive: true)) return;

        _game.ResignHuman();
        _gameOver = true;
        UpdateHeaderInfo();
        SetBusy(false, "You resigned.", "Click \"New Game\" to play again");
        ShowEndgameOverlayHumanLoss("You resigned");
    }

    private async void LevelBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ai == null) return;
        double sec = GetLevelSeconds();
        int visits = GetLevelMaxVisits();
        _aiThinkTotal = sec;
        ResetHumanTimer();
        // 通知 KataGo 切思考时间 + visits（GTP 命令会阻塞 50~200ms）→ 放后台线程，不卡 UI
        _ = Task.Run(() =>
        {
            try
            {
                _ai!.SetTimeSettings(0, sec, 1);
                _ai!.SetMaxVisits(visits);
            }
            catch { /* ignore */ }
        });
    }

    private void SizeBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        int newSize = GetSelectedSize();
        if (_board != null && _board.Size == newSize) return;       // 选回原尺寸：啥都不做

        // 闲置模式：仅重建空 Board（棋盘可见），**不**启动 KataGo、不开局
        if (!_started)
        {
            _board = new Board(newSize);
            BoardView.Board = _board;
            BoardView.LastMove = null;
            BoardView.InvalidateVisual();
            UpdateBoardStatus();
            SetBusy(false,
                "Idle · click \"New Game\" to start",
                $"Selected {newSize}-line · set difficulty / your color, then start a new game");
            return;
        }

        if (_game == null) return;

        // KataGo 还没就绪时，退回完整 StartNewGameAsync（冷启动一次）
        if (_ai == null || !_ai.IsRunning)
        {
            _ = RunStartNewGameSafelyAsync();
            return;
        }

        _ = RunRebuildSafelyAsync(newSize);
    }

    private async Task RunStartNewGameSafelyAsync()
    {
        try { await StartNewGameAsync(); }
        catch (Exception ex) { WriteExceptionLog(ex); SetBusy(false, "Failed to start game: " + ex.Message, "Please retry"); }
    }

    private async Task RunRebuildSafelyAsync(int newSize)
    {
        try { await RebuildBoardAsync(newSize); }
        catch (Exception ex) { WriteExceptionLog(ex); SetBusy(false, "Failed to switch board: " + ex.Message, "Please retry"); }
    }

    /// <summary>运行时切换棋盘尺寸。复用现有 KataGo 实例（避免 30s~3min 冷启）。
    /// 步骤：1) 通知 KataGo boardsize 清空其内部状态；2) 新建 Board + GameController；
    ///       3) 替换 BoardView 显示；4) 保留玩家执子设置；5) 视情况是否让 AI 先手。</summary>
    private async Task RebuildBoardAsync(int newSize)
    {
        if (_ai == null) return;
        SetBusy(true, $"Switching to {newSize}-line board…", "Notifying KataGo to reinitialize (engine keeps running; takes a few seconds)");
        Stone humanColor = _game?.HumanColor ?? Stone.Black;
        string humanColorName = humanColor == Stone.Black ? "Black" : "White";

        try
        {
            var resp = await Task.Run(() => _ai!.SetBoardSize(newSize));
            if (!string.IsNullOrEmpty(resp) && resp.StartsWith("?"))
                throw new Exception($"KataGo rejected boardsize: {resp.Trim()}");

            _board = new Board(newSize);
            _game = new GameController(_board, _ai, humanColor);
            BoardView.Board = _board;
            BoardView.LastMove = null;
            BoardView.InvalidateVisual();
            _gameOver = false;
            _humanSecondsLeft = GetHumanSecondsBudget();
            ResetHumanTimer();
            UpdateHeaderInfo();
            UpdateBoardStatus();
            EndgameHintText.Visibility = Visibility.Hidden;
            SizeHintText.Text = $"✓ Switched to {newSize}-line · you play {humanColorName}";

            // Human plays White → AI moves first
            if (humanColor == Stone.White)
                await AiTurnAsync();
            else
                SetBusy(false, $"Switched to {newSize}-line · you play {humanColorName} (first)", "Click the board to place a stone");
        }
        catch (Exception ex)
        {
            // Switch failed: fall back to a full StartNewGameAsync (extreme safety net)
            SetBusy(false, "Failed to switch board: " + ex.Message, "You can click \"New Game\" to retry");
            try { await StartNewGameAsync(); } catch { }
        }
    }
}

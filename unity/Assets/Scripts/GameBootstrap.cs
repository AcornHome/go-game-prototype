using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using GoGame.Core;

/// <summary>
/// Unity 6 LTS 启动入口。
/// 挂在一个 GameObject 上，要求场景里有 UIDocument 组件。
///
/// 工作流：
///   1. Start：拉起 KataGo 子进程（首次会编译神经网络 30s~3min）
///   2. 构造棋盘 + 控制面板（全 C# 代码，无 UXML/USS 拖拽）
///   3. BoardView 把棋盘状态变化反映到 UI
///   4. GameController 调度人类 vs KataGo 的回合
///
/// **版本**：Unity 6 LTS MVP（仅 V1.0 等价功能：人 vs KataGo 4 难度 + SGF 存读 + 认输/虚手/悔棋/终局）
/// **未移植**（V2.x）：实时胜率 / AI 复盘报告 / 打谱复盘 / 死活题 / 联机对战 / Steamworks
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [SerializeField] private UIDocument? uiDocument;

    [Header("KataGo 路径（按本机实际填写）")]
    [SerializeField] private string katagoExePath = @"C:\Tools\KataGo\katago.exe";
    [SerializeField] private string modelPath = @"C:\Tools\KataGo\weights\kata1-b18c384nbt.bin.gz";
    [SerializeField] private string configPath = @"C:\Tools\KataGo\default_gtp.cfg";

    [Header("对弈默认设置")]
    [SerializeField] private int defaultBoardSize = 19;
    [SerializeField] private Stone humanColor = Stone.Black;
    [SerializeField] private float defaultThinkSeconds = 1.0f;   // V1.0 最弱档；后续可加 Slider 调难度

    private KataGoClient? _client;
    private BoardView? _boardView;
    private Board? _board;
    private GameController? _controller;

    private Label? _statusLabel;
    private bool _gameOver;

    private void Start()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            Debug.LogError("[GameBootstrap] UIDocument 未配置");
            return;
        }

        var root = uiDocument.rootVisualElement;
        root.Clear();

        var loadingLabel = new Label("KataGo 启动中（首次 30s-3min）...");
        loadingLabel.style.fontSize = 20;
        loadingLabel.style.position = Position.Absolute;
        loadingLabel.style.left = 30;
        loadingLabel.style.top = 30;
        root.Add(loadingLabel);

        try
        {
            _client = new KataGoClient(katagoExePath, modelPath, configPath, defaultBoardSize);
        }
        catch (System.Exception ex)
        {
            loadingLabel.text = $"KataGo 启动失败: {ex.Message}";
            return;
        }

        if (!_client.IsRunning)
        {
            loadingLabel.text = "KataGo 启动失败！请检查路径。";
            return;
        }

        _board = new Board(defaultBoardSize);

        root.Clear();
        BuildBoardArea(root);
        BuildControlPanel(root);
        BuildStatusBar(root);

        _boardView = gameObject.AddComponent<BoardView>();
        _boardView.BoardSize = defaultBoardSize;
        _boardView.Initialize(root);
        _boardView.OnPositionClicked = OnHumanClick;
        _boardView.Render(_board);

        _controller = new GameController(_board, _client, humanColor);
        SetStatus("轮到你了（点击棋盘落子）");
    }

    private void BuildBoardArea(VisualElement root)
    {
        var board = new VisualElement { name = "board" };
        board.style.position = Position.Absolute;
        board.style.left = 30;
        board.style.top = 80;
        root.Add(board);
    }

    private void BuildControlPanel(VisualElement root)
    {
        var panel = new VisualElement();
        panel.style.position = Position.Absolute;
        panel.style.left = 30 + defaultBoardSize * 28 + 30;   // 棋盘右边 30px
        panel.style.top = 80;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.width = 220;

        var title = new Label("控制台");
        title.style.fontSize = 18;
        title.style.marginBottom = 12;
        panel.Add(title);

        AddButton(panel, "🆕 新对弈 (19 路)", () => OnNewGame(19));
        AddButton(panel, "🆕 新对弈 (13 路)", () => OnNewGame(13));
        AddButton(panel, "🆕 新对弈 (9 路)", () => OnNewGame(9));
        AddButton(panel, "✋ 虚手 (Pass)", OnPass);
        AddButton(panel, "🏳 认输 (Resign)", OnResign);
        AddButton(panel, "↩ 悔棋 (Undo)", OnUndo);
        AddButton(panel, "📊 终局数子", OnFinish);
        AddButton(panel, "💾 保存 SGF", OnSaveSgf);

        root.Add(panel);
    }

    private void BuildStatusBar(VisualElement root)
    {
        _statusLabel = new Label("等待开始") { name = "status" };
        _statusLabel.style.position = Position.Absolute;
        _statusLabel.style.left = 30;
        _statusLabel.style.top = 30 + defaultBoardSize * 28 + 20;
        _statusLabel.style.fontSize = 16;
        _statusLabel.style.color = new StyleColor(new Color(0.2f, 0.4f, 0.8f));
        root.Add(_statusLabel);
    }

    private void AddButton(VisualElement parent, string label, System.Action onClick)
    {
        var btn = new Button(onClick) { text = label };
        btn.style.height = 36;
        btn.style.marginBottom = 6;
        btn.style.fontSize = 14;
        parent.Add(btn);
    }

    private void SetStatus(string text)
    {
        if (_statusLabel != null) _statusLabel.text = text;
    }

    // ---- 人类回合 ----

    private void OnHumanClick(int x, int y)
    {
        if (_controller == null || _board == null || _boardView == null) return;
        if (_gameOver) return;
        if (!_controller.IsHumanTurn) return;

        var (ok, msg, _) = _controller.PlayHuman(x, y);
        if (!ok)
        {
            SetStatus(msg);
            return;
        }

        _boardView.Render(_board);
        SetStatus(msg);
        TriggerAiTurn();
    }

    private async void TriggerAiTurn()
    {
        if (_controller == null || _board == null || _boardView == null) return;
        if (_controller.IsHumanTurn) return;

        SetStatus("KataGo 思考中...");
        try
        {
            var (vertex, _) = await _controller.PlayAiAsync(defaultThinkSeconds);
            _boardView.Render(_board);
            SetStatus($"AI 下了 {vertex}");
        }
        catch (System.Exception ex)
        {
            SetStatus($"AI 出错: {ex.Message}");
        }
    }

    // ---- 控制按钮回调 ----

    private void OnNewGame(int size)
    {
        if (_client == null) return;
        _client.SetBoardSize(size);
        _board = new Board(size);
        _controller = new GameController(_board, _client, humanColor);
        _boardView?.Render(_board);
        _gameOver = false;
        SetStatus($"新对弈 {size} 路，轮到你了");

        // 若 AI 执黑先手，让 KataGo 自动出首手
        if (_controller.AiColor == Stone.Black) TriggerAiTurn();
    }

    private void OnPass()
    {
        if (_controller == null || _boardView == null || _board == null) return;
        if (_gameOver) return;
        var (ok, msg) = _controller.PassHuman();
        if (ok) _boardView.Render(_board);
        SetStatus(msg);
        if (ok) TriggerAiTurn();
    }

    private void OnResign()
    {
        if (_controller == null) return;
        _controller.ResignHuman();
        _gameOver = true;
        SetStatus("你认输了");
    }

    private void OnUndo()
    {
        if (_controller == null || _boardView == null || _board == null) return;
        var (ok, msg) = _controller.Undo();
        if (ok) _boardView.Render(_board);
        SetStatus(msg);
    }

    private void OnFinish()
    {
        if (_controller == null || _client == null) return;
        var score = _controller.FinishAndScore();
        _gameOver = true;
        SetStatus($"终局: {score}");
    }

    private void OnSaveSgf()
    {
        if (_board == null) return;
        var path = Path.Combine(Application.persistentDataPath, $"go-{System.DateTime.Now:yyyyMMdd-HHmmss}.sgf");
        SgfWriter.WriteFile(path, _board, humanColor);
        SetStatus($"已保存: {path}");
    }

    private void OnDestroy()
    {
        _client?.Dispose();
    }
}

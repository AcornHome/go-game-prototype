using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 启动场景 MonoBehaviour 入口。
/// 挂在一个 GameObject 上，UIDocument 组件引用场景里的 UI。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;

    [Header("KataGo 路径（按本机实际填写）")]
    [SerializeField] private string katagoExePath = @"C:\Tools\KataGo\katago.exe";
    [SerializeField] private string modelPath = @"C:\Tools\KataGo\weights\kata1-b18c384nbt-s5378693760-d2026470963.txt.gz";
    [SerializeField] private string configPath = @"C:\Tools\KataGo\default_gtp.cfg";

    private KataGoClient _client;
    private BoardView _boardView;
    private GameController _controller;

    private void Start()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            Debug.LogError("[GameBootstrap] UIDocument 未配置");
            return;
        }

        var root = uiDocument.rootVisualElement;

        // 显示启动提示
        root.Clear();
        var loadingLabel = new Label("KataGo 启动中（首次 30s-3min）...");
        loadingLabel.style.fontSize = 20;
        loadingLabel.style.position = Position.Absolute;
        loadingLabel.style.left = 30;
        loadingLabel.style.top = 30;
        root.Add(loadingLabel);

        // 启动 KataGo 子进程
        _client = new KataGoClient();
        _client.Start(katagoExePath, modelPath, configPath);

        if (!_client.IsRunning)
        {
            loadingLabel.text = "KataGo 启动失败！请检查路径和 check-env.ps1";
            return;
        }

        // 初始化 UI
        root.Clear();
        SetupBoardUI(root, loadingLabel);
        SetupControlPanel(root);

        _boardView = gameObject.AddComponent<BoardView>();
        _boardView.Initialize(root);

        _controller = gameObject.AddComponent<GameController>();
        _controller.Setup(_client, _boardView);
    }

    private void SetupBoardUI(VisualElement root, Label placeholder)
    {
        var board = new VisualElement { name = "board" };
        board.style.position = Position.Absolute;
        board.style.left = 30;
        board.style.top = 80;
        board.style.backgroundColor = new StyleColor(new Color(0.95f, 0.85f, 0.6f));
        board.style.borderTopWidth = 2;
        board.style.borderBottomWidth = 2;
        board.style.borderLeftWidth = 2;
        board.style.borderRightWidth = 2;
        board.style.borderTopColor = new StyleColor(new Color(0.4f, 0.3f, 0.2f));
        board.style.borderBottomColor = new StyleColor(new Color(0.4f, 0.3f, 0.2f));
        board.style.borderLeftColor = new StyleColor(new Color(0.4f, 0.3f, 0.2f));
        board.style.borderRightColor = new StyleColor(new Color(0.4f, 0.3f, 0.2f));
        root.Add(board);
    }

    private void SetupControlPanel(VisualElement root)
    {
        var panel = new VisualElement();
        panel.style.position = Position.Absolute;
        panel.style.left = 600;
        panel.style.top = 80;
        panel.style.flexDirection = FlexDirection.Column;

        var startBtn = new Button(() => _controller.StartNewGame(19)) { text = "开始对弈 (19 路)" };
        var resignBtn = new Button(() => _controller.Resign()) { text = "认输" };
        var passBtn = new Button(() => _controller.Pass()) { text = "跳过" };
        var status = new Label("等待开始") { name = "status" };

        foreach (var btn in new[] { startBtn, resignBtn, passBtn })
        {
            btn.style.height = 32;
            btn.style.marginBottom = 8;
        }

        panel.Add(startBtn);
        panel.Add(resignBtn);
        panel.Add(passBtn);
        panel.Add(status);

        root.Add(panel);
    }

    private void OnDestroy()
    {
        _client?.Dispose();
    }
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GoGame;

/// <summary>
/// 围棋棋盘渲染控件。OnRender 绘制木质棋盘、网格、星位、棋子（渐变+阴影）、最后一步标记、坐标。
/// 鼠标点击换算到最近交叉点并抛出 MoveRequested 落子事件；鼠标悬停显示预览小圆点。
///
/// 支持：
///   - 落子缩放动画（AnimateMove，180ms 0→1）
///   - 提子闪烁高亮（FlashCaptures，600ms 红→淡）
///   - 悬停预览（HoverIndex + 透明小圆点）
///   - 回放模式：外部切换 DisplaySnapshotIndex → RestoreToSnapshot
/// </summary>
public class BoardControl : FrameworkElement
{
    private const double BoardMarginOuter = 36;
    private const int AnimMoveMs = 220;
    private const int AnimCaptureMs = 400;
    private const int LastMoveBreathMs = 1500;

    public Board? Board { get; set; }

    /// <summary>最后一步落点（用于高亮标记）。</summary>
    public (int X, int Y)? LastMove { get; set; }

    /// <summary>鼠标点击交叉点 (x, y) 时触发。</summary>
    public event Action<int, int>? MoveRequested;

    // 动画状态：位置 → 动画开始时刻 (ms)
    private readonly Dictionary<(int, int), long> _moveAnimStart = new();
    /// <summary>提子缩小淡出动画：位置 → 动画开始时刻 (ms)</summary>
    private readonly Dictionary<(int, int), long> _captureAnimStart = new();
    /// <summary>当前最后手的呼吸动画开始时刻 (ms)，null = 不显示呼吸</summary>
    private long? _lastMoveBreathStart;

    /// <summary>
    /// AI 提示候选点。按推荐度排序，**第 0 个即最佳手**。
    /// 元素：(x, y, GTP 顶点)。null / 空 = 当前没有提示。
    /// 由 MainWindow 在玩家点「💡 提示」后填入，落子 / 新对局 / 悔棋时调用 ClearHints 清掉。
    /// </summary>
    private List<(int X, int Y, string Vertex)>? _hintMarks;
    /// <summary>提示显示的起始时刻 (ms)，用于脉冲动画。null = 无提示动画。</summary>
    private long? _hintStart;
    // 当前鼠标悬停的交叉点
    private (int X, int Y)? _hover;
    private bool _isHumanTurn = true;     // 是否显示预览
    private readonly DispatcherTimer _animTimer;

    // 缓存的 brush（freeze 后可安全多线程共享，避免每帧 new）
    private static readonly Brush BgBoard = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xEF, 0xE0)));
    private static readonly Brush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x35, 0, 0, 0)));
    private static readonly Pen GridPen = FreezePen(new SolidColorBrush(Color.FromRgb(0x6B, 0x4F, 0x2A)), 1);
    private static readonly Brush CoordBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x42, 0x22)));
    private static readonly Brush StoneShadow = Freeze(new SolidColorBrush(Color.FromArgb(0x3A, 0, 0, 0)));
    private static readonly Pen StoneStroke = FreezePen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 0.8);
    private static readonly RadialGradientBrush BlackStoneGrad = FreezeGrad(Color.FromRgb(0x5A, 0x5A, 0x5A), Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly RadialGradientBrush WhiteStoneGrad = FreezeGrad(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly Brush StarOuter = Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x4F, 0x2A)));
    private static readonly Brush StarInner = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xEF, 0xE0)));
    private LinearGradientBrush _woodBrush = null!;
    private Brush _woodGrainBrush = null!;
    private Brush _woodHighlight = null!;
    private Pen _woodInnerBorder = null!;

    // 🛠 提示标记相关 brush 全部预冻结（OnRender 之前每帧 new 是 GC 卡顿 / 抖动源之一）
    private static readonly Brush HintGhostBest = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0x2E, 0xC7, 0x8B)));
    private static readonly Brush HintGhostAlt = Freeze(new SolidColorBrush(Color.FromArgb(0x38, 0x2E, 0xC7, 0x8B)));
    private static readonly Pen HintRingBest = FreezePen(new SolidColorBrush(Color.FromArgb(0xF5, 0x7C, 0xF2, 0xC0)), 2.6);
    private static readonly Pen HintRingAlt = FreezePen(new SolidColorBrush(Color.FromArgb(0x88, 0x7C, 0xF2, 0xC0)), 1.6);
    private static readonly Brush HintNumBest = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly Brush HintNumAlt = Freeze(new SolidColorBrush(Color.FromRgb(0xD8, 0xF5, 0xE8)));

    // 🛠 最后手呼吸描边也预冻结
    private static readonly Pen LastMoveRingPen = FreezePen(new SolidColorBrush(Color.FromArgb(0xE0, 0xE8, 0x9B, 0x3C)), 2.2);
    private static readonly Brush LastMoveInnerBlack = Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LastMoveInnerWhite = Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0x1A, 0x1A, 0x1A)));

    // 🛠 棋子光晕（halo）—— 每帧会被 19 路 361 次使用，预冻结避免 360 次/帧的 GC 分配
    private static readonly Brush StoneHaloBlack = Freeze(new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0x00, 0x00)));
    private static readonly Brush StoneHaloWhite = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));

    // 🛠 悬停预览（hover）—— 玩家鼠标在棋盘上时每帧最多 1 个，但 hover 期间每帧都画
    private static readonly Brush HoverPreviewBlack = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0x1A, 0x1A, 0x1A)));
    private static readonly Brush HoverPreviewWhite = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0xF5, 0xF5, 0xF0)));
    private static readonly Brush HoverStroke = Freeze(new SolidColorBrush(Color.FromArgb(0xA0, 0xE8, 0x9B, 0x3C)));
    private static readonly Pen HoverPen = FreezePen(new SolidColorBrush(Color.FromArgb(0xA0, 0xE8, 0x9B, 0x3C)), 1.2);

    // 🛠 字体（Typeface）—— 坐标 / 序号每帧都画，缓存避免每帧 new FontFamily
    private static readonly Typeface CoordFontTypeface = new(new FontFamily("Segoe UI"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface HintFontTypeface = new(new FontFamily("Segoe UI"),
        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    // 🛠 提子动画的淡红警示（halo）每帧 1 个（动画期间），但 opacity 动态 → 无法静态 cache。
    // 用一个实例级 SolidColorBrush + 每帧改 Color 属性替代（Color 是值类型，赋值不分配 GC）。
    // 不可 Freeze（冻结后 Color 属性写不进去）。
    private readonly SolidColorBrush _captureHaloBrush = new(Color.FromRgb(0xC0, 0x39, 0x2B));

    // 说明：之前有 DrawStoneShadow / DrawStonePen 静态字段是准备替换 DrawStone 函数的；
    // 检查发现 DrawStone 从未被调用（死代码），已删除函数，对应字段也清理掉。

    public BoardControl()
    {
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;

        // 缓存木质底板渐变（每实例缓存一次，避免重复分配）
        _woodBrush = new LinearGradientBrush(
            Color.FromRgb(0xE8, 0xC2, 0x8E),
            Color.FromRgb(0xC9, 0x9C, 0x62),
            new Point(0, 0), new Point(1, 1));
        _woodBrush.Freeze();

        // 木纹细纹：几条半透明深色斜线，模拟真实木纹
        _woodGrainBrush = BuildWoodGrainBrush();
        // 顶部高光（增加立体感）
        _woodHighlight = new LinearGradientBrush(
            Color.FromArgb(0x40, 0xFF, 0xFF, 0xF0),
            Color.FromArgb(0x00, 0xFF, 0xFF, 0xF0),
            new Point(0, 0), new Point(1, 1));
        _woodHighlight.Freeze();
        // 内边框（深色细线，模拟木板拼接缝）
        var innerBorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x5A, 0x42, 0x22));
        innerBorderBrush.Freeze();
        _woodInnerBorder = new Pen(innerBorderBrush, 1);
        _woodInnerBorder.Freeze();

        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            // 🛠 从 16ms (60fps) 降到 50ms (20fps)：hint 脉冲是 900ms 周期，60fps 完全没必要；
            // 降频显著减少 KataGo 思考期间 stdout 高并发带来的 UI 帧率不稳 / 抖动感。
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _animTimer.Tick += (_, _) =>
        {
            // 只有确实有动画/悬停时才重绘。空闲时主动停 timer 省 CPU。
            bool hasAnim = _moveAnimStart.Count > 0 || _captureAnimStart.Count > 0
                           || _lastMoveBreathStart.HasValue || _hintStart.HasValue;
            bool hoverActive = _isHumanTurn && _hover.HasValue;
            if (hasAnim || hoverActive)
            {
                InvalidateVisual();
            }
            else
            {
                _animTimer.Stop();
                _running = false;
            }
        };
        _animTimer.Start();
        _running = true;

        MouseMove += OnMouseMoveInternal;
        MouseLeave += (_, _) => { _hover = null; InvalidateVisual(); };
    }

    private bool _running;
    private void EnsureAnimTimer() { if (!_running) { _animTimer.Start(); _running = true; } }

    /// <summary>由 MainWindow 在玩家回合时设为 true，AI 回合时设为 false，关闭预览。</summary>
    public bool IsHumanTurn
    {
        get => _isHumanTurn;
        set { _isHumanTurn = value; if (!_isHumanTurn) _hover = null; EnsureAnimTimer(); InvalidateVisual(); }
    }

    /// <summary>玩家执子色（用于悬停预览颜色匹配）。由 GameController 设置。</summary>
    public Stone HumanColor { get; set; } = Stone.Black;

    /// <summary>触发落子弹性动画（easeOutBack：0 → 1.15 → 1.0，220ms）。同时启动最后手呼吸。</summary>
    public void AnimateMove(int x, int y)
    {
        _moveAnimStart[(x, y)] = Environment.TickCount64;
        _lastMoveBreathStart = Environment.TickCount64;   // 新子触发最后手呼吸
        EnsureAnimTimer();
        InvalidateVisual();
    }

    /// <summary>触发一组位置的吃子缩小淡出动画。</summary>
    public void FlashCaptures(IEnumerable<(int x, int y)> captures)
    {
        long now = Environment.TickCount64;
        foreach (var (x, y) in captures)
            _captureAnimStart[(x, y)] = now;
        EnsureAnimTimer();
        InvalidateVisual();
    }

    /// <summary>
    /// 显示 AI 提示：在棋盘上叠加候选落点标记（第 1 个最醒目 = 最佳手）。
    /// marks 必须按推荐度从高到低排好序。传空集合等同于清除。
    ///
    /// **🛠 顺带消除棋盘抖动**：进入提示态时强制清掉"_lastMoveBreathStart"和"_hover"，
    /// 避免 hint 脉冲动画与上次落子的呼吸标记、悬停预览叠加出现"画面抖动"；
    /// ClearHints() 时再恢复。
    /// </summary>
    public void ShowHints(IEnumerable<(int x, int y, string vertex)> marks)
    {
        var list = marks?.Where(m => m.x >= 0 && m.y >= 0).ToList();
        _hintMarks = (list is { Count: > 0 }) ? list : null;
        _hintStart = _hintMarks == null ? null : Environment.TickCount64;
        // 关键：进入提示态时关掉所有可能与 hint 脉冲叠加的动画，避免视觉抖动
        _lastMoveBreathStart = null;
        _hover = null;
        EnsureAnimTimer();
        InvalidateVisual();
    }

    /// <summary>清除 AI 提示标记（落子 / 新对局 / 悔棋时调用）。恢复最后手呼吸。</summary>
    public void ClearHints()
    {
        if (_hintMarks == null && _hintStart == null) return;
        _hintMarks = null;
        _hintStart = null;
        // 提示消失时，最后手呼吸重新计时（避免 hint 期间持续累积时间，恢复后一开始没动画）
        _lastMoveBreathStart = Board?.Get(LastMove?.X ?? -1, LastMove?.Y ?? -1) != Stone.Empty && LastMove.HasValue
            ? Environment.TickCount64
            : null;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double side = Math.Min(availableSize.Width, availableSize.Height);
        if (double.IsInfinity(side) || double.IsNaN(side) || side <= 0) side = 600;
        return new Size(side, side);
    }

    private double CellSize => Board == null
        ? 30
        : (ActualWidth - 2 * BoardMarginOuter) / Math.Max(1, Board.Size - 1);

    private Point IndexToPixel(int x, int y) =>
        new(BoardMarginOuter + x * CellSize, BoardMarginOuter + y * CellSize);

    private (int x, int y) PixelToIndex(double px, double py)
    {
        if (Board == null) return (-1, -1);
        int x = (int)Math.Round((px - BoardMarginOuter) / CellSize);
        int y = (int)Math.Round((py - BoardMarginOuter) / CellSize);
        return (x, y);
    }

    private void OnMouseMoveInternal(object sender, MouseEventArgs e)
    {
        if (Board == null) return;
        var pos = e.GetPosition(this);
        var (x, y) = PixelToIndex(pos.X, pos.Y);
        var newHover = Board.IsInBounds(x, y) ? ((int, int)?)(x, y) : null;
        if (!Nullable.Equals(newHover, _hover))
        {
            _hover = newHover;
            EnsureAnimTimer();
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        // 整体背景
        dc.DrawRectangle(BgBoard, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (Board == null) return;

        int size = Board.Size;
        double cell = CellSize;
        long now = Environment.TickCount64;

        // 木质棋盘底板（添加阴影效果）
        double pad = cell * 0.6;
        var woodRect = new Rect(BoardMarginOuter - pad, BoardMarginOuter - pad,
                                cell * (size - 1) + pad * 2, cell * (size - 1) + pad * 2);
        // 阴影
        dc.DrawRoundedRectangle(ShadowBrush, null,
            new Rect(woodRect.X + 3, woodRect.Y + 4, woodRect.Width, woodRect.Height), 6, 6);

        // 木纹底板（渐变层）
        dc.DrawRoundedRectangle(_woodBrush, null, woodRect, 6, 6);
        // 木纹细纹层（斜线条纹叠加）
        dc.DrawRoundedRectangle(_woodGrainBrush, null, woodRect, 6, 6);
        // 顶部高光（增加立体感）
        dc.DrawRoundedRectangle(_woodHighlight, null, woodRect, 6, 6);
        // 内边框（深色细线，模拟木板拼接缝）
        dc.DrawRoundedRectangle(null, _woodInnerBorder, woodRect, 6, 6);

        // 坐标（A-S 跳过 I 国际标准，1-19 数字）—— font/brush 全静态缓存
        double coordFontSize = Math.Max(10, Math.Min(13, cell * 0.36));
        // 列号（顶/底）
        for (int x = 0; x < size; x++)
        {
            string label = ColumnLabel(x);
            var p = IndexToPixel(x, 0);
            var ft = GetCachedText(label, coordFontSize, CoordBrush, CoordFontTypeface);
            dc.DrawText(ft, new Point(p.X - ft.Width / 2, BoardMarginOuter - pad + (woodRect.Height) + 4));
            dc.DrawText(ft, new Point(p.X - ft.Width / 2, BoardMarginOuter - pad - coordFontSize - 2));
        }
        // 行号（左/右）：从下到上 1..size
        for (int y = 0; y < size; y++)
        {
            string label = (y + 1).ToString();
            var p = IndexToPixel(0, y);
            var ft = GetCachedText(label, coordFontSize, CoordBrush, CoordFontTypeface);
            dc.DrawText(ft, new Point(BoardMarginOuter - pad - coordFontSize - 4, p.Y - coordFontSize / 2.4));
            dc.DrawText(ft, new Point(BoardMarginOuter + cell * (size - 1) + pad + 4, p.Y - coordFontSize / 2.4));
        }

        // 网格线
        for (int i = 0; i < size; i++)
        {
            var h0 = IndexToPixel(0, i);
            var h1 = IndexToPixel(size - 1, i);
            dc.DrawLine(GridPen, h0, h1);

            var v0 = IndexToPixel(i, 0);
            var v1 = IndexToPixel(i, size - 1);
            dc.DrawLine(GridPen, v0, v1);
        }

        // 星位（更醒目：黑圈+白芯）
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                if (Board.IsStarPoint(x, y))
                {
                    var p = IndexToPixel(x, y);
                    double sr = Math.Max(3, cell * 0.13);
                    dc.DrawEllipse(StarOuter, null, p, sr, sr);
                    dc.DrawEllipse(StarInner, null, p, sr * 0.45, sr * 0.45);
                }

        // 吃子缩小淡出底层（在棋子下面，模拟棋子缩小飞走的视觉效果）
        var expired = new List<(int, int)>();
        foreach (var kv in _captureAnimStart)
        {
            long elapsed = now - kv.Value;
            if (elapsed >= AnimCaptureMs)
            {
                expired.Add(kv.Key);
                continue;
            }
            double t = elapsed / (double)AnimCaptureMs;       // 0 → 1
            double scale = 1.0 - t;                            // 1.0 → 0.0
            double opacity = 1.0 - t;                          // 1.0 → 0.0
            var p = IndexToPixel(kv.Key.Item1, kv.Key.Item2);
            double r = cell * 0.46 * scale;
            var stone = Board.Get(kv.Key.Item1, kv.Key.Item2);
            var grad = stone == Stone.Black ? BlackStoneGrad : WhiteStoneGrad;
            // 淡红警示（halo）—— 复用一个 SolidColorBrush，每帧只改 Color 属性（值类型，不分配 GC）
            _captureHaloBrush.Color = Color.FromArgb((byte)(opacity * 90), 0xC0, 0x39, 0x2B);
            dc.DrawEllipse(_captureHaloBrush, null, p, r * 1.4, r * 1.4);
            // 再画缩小飞走的棋子本体
            dc.PushOpacity(opacity);
            dc.DrawEllipse(grad, StoneStroke, p, r, r);
            dc.Pop();
        }
        foreach (var k in expired) _captureAnimStart.Remove(k);

        // 悬停预览（半透明棋子在落点）—— 静态缓存 brush，避免 hover 期间每帧 GC
        if (_isHumanTurn && _hover is { } hv && Board.Get(hv.X, hv.Y) == Stone.Empty)
        {
            var p = IndexToPixel(hv.X, hv.Y);
            double r = cell * 0.46;
            bool isBlackTurn = Board.NextToPlay == (HumanColor == Stone.Black ? Stone.Black : Stone.White);
            dc.DrawEllipse(isBlackTurn ? HoverPreviewBlack : HoverPreviewWhite,
                HoverPen, p, r, r);
        }

        // 棋子
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                var s = Board.Get(x, y);
                if (s == Stone.Empty) continue;

                var p = IndexToPixel(x, y);
                double r = cell * 0.46;

                // 计算缩放 + 透明度（落子弹性动画 + 透明度淡入）
                double scale = 1.0;
                double opacity = 1.0;
                if (_moveAnimStart.TryGetValue((x, y), out long startMs))
                {
                    long elapsed = now - startMs;
                    if (elapsed >= AnimMoveMs)
                    {
                        _moveAnimStart.Remove((x, y));
                    }
                    else
                    {
                        double t = elapsed / (double)AnimMoveMs;
                        // easeOutBack 弹性：t<0.7 时快速放大到 1.15，t>0.7 时回弹到 1.0
                        if (t < 0.7)
                            scale = (t / 0.7) * 1.15;
                        else
                            scale = 1.15 - ((t - 0.7) / 0.3) * 0.15;
                        opacity = Math.Min(1.0, t * 1.8);   // 前 0.55 完成淡入
                    }
                }
                double effectiveR = r * scale;

                // 光晕层（静态缓存 brush；19 路棋盘每帧 361 次画 halo，省 361 次/帧的 GC 分配）
                if (scale > 0.05)
                    dc.DrawEllipse(s == Stone.Black ? StoneHaloBlack : StoneHaloWhite,
                        null, p, effectiveR * 1.12, effectiveR * 1.12);

                // 阴影（用缓存的 brush）
                dc.DrawEllipse(StoneShadow, null, new Point(p.X + 1.5, p.Y + 2.0), effectiveR, effectiveR);

                // 棋子本体（用缓存的 RadialGradientBrush）
                var grad = s == Stone.Black ? BlackStoneGrad : WhiteStoneGrad;
                if (opacity < 1.0)
                {
                    dc.PushOpacity(opacity);
                    dc.DrawEllipse(grad, StoneStroke, p, effectiveR, effectiveR);
                    dc.Pop();
                }
                else
                {
                    dc.DrawEllipse(grad, StoneStroke, p, effectiveR, effectiveR);
                }
            }

        // ===== AI 提示标记（叠加在棋子之上，最后一步标记之下） =====
        // 只画在空点上：万一阵地上已经有子（比如悔棋后状态变了），静默跳过不画错位置。
        if (_hintMarks is { Count: > 0 } hints)
        {
            for (int i = 0; i < hints.Count; i++)
            {
                var (hx, hy, _) = hints[i];
                if (!Board!.IsInBounds(hx, hy)) continue;
                if (Board.Get(hx, hy) != Stone.Empty) continue;

                var p = IndexToPixel(hx, hy);
                double r = cell * 0.46;
                bool best = i == 0;

                // 脉冲缩放（最佳手振幅更大、更抢眼；周期 900ms）
                double pulse = 1.0;
                if (_hintStart is long hs)
                {
                    long e = now - hs;
                    pulse = 1.0 + (best ? 0.16 : 0.08) * Math.Sin(e * 2 * Math.PI / 900.0);
                }

                // 幽灵棋子（半透明青绿，与木纹/黑白子都不撞色）—— brush 全部静态缓存，避免每帧 GC
                dc.DrawEllipse(best ? HintGhostBest : HintGhostAlt, null, p, r * 0.90 * pulse, r * 0.90 * pulse);

                // 外环（最佳手用实心亮青，备选更淡更细）—— pen 已预冻结
                dc.DrawEllipse(null, best ? HintRingBest : HintRingAlt, p, r * 0.60 * pulse, r * 0.60 * pulse);

                // 序号（1 = 最佳手），居中在交叉点上 —— Typeface 已静态缓存 + FormattedText 也缓存
                double numSize = Math.Max(10, cell * 0.36);
                var hintBrush = best ? HintNumBest : HintNumAlt;
                var numFt = GetCachedText((i + 1).ToString(), numSize, hintBrush, HintFontTypeface);
                dc.DrawText(numFt, new Point(p.X - numFt.Width / 2, p.Y - numSize * 0.62));
            }
        }

        // 最后一步标记（金边描边 + 呼吸缩放，无限循环直到下一手）
        if (LastMove is { } lm && Board.Get(lm.X, lm.Y) != Stone.Empty)
        {
            var p = IndexToPixel(lm.X, lm.Y);
            double r = cell * 0.46;

            // 呼吸缩放（sin 波 0.85 ~ 1.15）
            double breathScale = 1.0;
            if (_lastMoveBreathStart is long bs)
            {
                long e = now - bs;
                // 振幅 0.15，周期 1500ms
                breathScale = 1.0 + 0.15 * Math.Sin(e * 2 * Math.PI / LastMoveBreathMs);
            }

            // 金色描边（预冻结 brush，无 GC 卡顿）
            double ringR = r * 0.52 * breathScale;
            dc.DrawEllipse(null, LastMoveRingPen, p, ringR, ringR);
            // 内圈细描边（与棋子色相反）—— 静态缓存
            dc.DrawEllipse(Board.Get(lm.X, lm.Y) == Stone.Black ? LastMoveInnerBlack : LastMoveInnerWhite,
                null, p, r * 0.18, r * 0.18);
        }
    }

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    private static Pen FreezePen(SolidColorBrush b, double w) { b.Freeze(); var p = new Pen(b, w); p.Freeze(); return p; }
    private static RadialGradientBrush FreezeGrad(Color light, Color dark)
    {
        var b = new RadialGradientBrush(light, dark) { GradientOrigin = new Point(0.35, 0.3) };
        b.Freeze();
        return b;
    }

    /// <summary>
    /// 木纹细纹 DrawingBrush：在 60x60 单元里画几条半透明斜线条纹，Tile 平铺整块棋盘。
    /// </summary>
    private static Brush BuildWoodGrainBrush()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            double unit = 60;
            var mainPen = new SolidColorBrush(Color.FromArgb(0x35, 0x5A, 0x42, 0x22));
            var thinPen = new SolidColorBrush(Color.FromArgb(0x20, 0x5A, 0x42, 0x22));
            dc.DrawLine(new Pen(mainPen, 1.6),
                new Point(0, unit * 0.25), new Point(unit, unit * 0.45));
            dc.DrawLine(new Pen(thinPen, 0.7),
                new Point(0, unit * 0.60), new Point(unit, unit * 0.78));
            dc.DrawLine(new Pen(thinPen, 0.5),
                new Point(0, unit * 0.10), new Point(unit, unit * 0.18));
            var lightPen = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xE8));
            dc.DrawLine(new Pen(lightPen, 0.8),
                new Point(0, unit * 0.85), new Point(unit, unit * 0.95));
        }
        var brush = new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 60, 60),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }

    private static string ColumnLabel(int x)
    {
        // 国际标准 A-T 跳过 I
        string s = "";
        int n = x;
        while (true)
        {
            s = (char)('A' + (n % 26)) + s;
            if (n < 26) break;
            n = n / 26 - 1;
        }
        // 跳过 I（看起来像 1）
        if (s.Length == 1 && s[0] >= 'I') s = ((char)(s[0] + 1)).ToString();
        return s;
    }

    /// <summary>
    /// 🛠 v1.2.4 FormattedText 缓存：OnRender 中坐标/序号每帧共 ~38 次 DrawCenteredText，
    /// 每次都 new FormattedText → 19 路 ~38 次/帧的字符串格式分配，在 KataGo 思考时持续
    /// invalidate 会触发 Gen0 GC，导致肉眼可见的"棋盘抖动"。
    /// 缓存按 (text, fontSize, brush) 命中：列号 A..T+行号 1..19 共 ~38 个 FormattedText，
    /// 只在窗口尺寸变化导致 fontSize 改变时才重建。
    /// </summary>
    private readonly Dictionary<(string Text, double FontSize, Brush Brush), FormattedText> _ftCache = new();

    private FormattedText GetCachedText(string text, double fontSize, Brush brush, Typeface typeface)
    {
        var key = (text, fontSize, brush);
        if (!_ftCache.TryGetValue(key, out var ft))
        {
            ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, fontSize, brush, 1.0);
            _ftCache[key] = ft;
        }
        return ft;
    }

    private static void DrawCenteredText(DrawingContext dc, string text, Point center, double fontSize,
                                          Brush brush, Typeface typeface)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, typeface, fontSize, brush, 1.0);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Board == null) return;

        var pos = e.GetPosition(this);
        var (x, y) = PixelToIndex(pos.X, pos.Y);
        if (Board.IsInBounds(x, y))
            MoveRequested?.Invoke(x, y);
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GoGame;

/// <summary>
/// AI 终局复盘报告窗口：
/// - 顶部：摘要
/// - 中部：KataGo 每步走子后的玩家视角胜率曲线（Canvas 手画）
/// - 下部：每手棋明细表 + 评价（妙招 / 坏手 / 转折）
///
/// 注意：当前 KataGoClient 没有提供玩家走子后的独立 kata-analyze，
/// 所以表格里的"胜率"只覆盖 KataGo 走的手；玩家走的棋显示"—"。
/// 转折点 / 妙招 / 坏手 按 KataGo 走子时的胜率变化判断（≥5% 显著）。
/// </summary>
public partial class ReviewReportWindow : Window
{
    private readonly IReadOnlyList<MoveAnalysis> _analyses;
    private readonly Stone _humanColor;
    private readonly string _winnerLabel;

    public ReviewReportWindow(IReadOnlyList<MoveAnalysis> analyses, Stone humanColor, string winnerLabel)
    {
        InitializeComponent();
        _analyses = analyses ?? new List<MoveAnalysis>();
        _humanColor = humanColor;
        _winnerLabel = winnerLabel;

        // ESC 关闭
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };

        Build();
    }

    private void Build()
    {
        // 1) 顶部摘要
        int totalAi = _analyses.Count;
        SummaryText.Text = $"结果：{_winnerLabel} · 共 {totalAi} 次 KataGo 走子评估 · 棋盘 {_analyses.Count} 步完成。";

        // 2) 计算转折点（KataGo 走子胜率变化最大的几步）
        var deltas = new List<(int idx, double delta, MoveAnalysis ma)>();
        for (int i = 1; i < _analyses.Count; i++)
        {
            double d = _analyses[i].HumanWinrate - _analyses[i - 1].HumanWinrate;
            deltas.Add((_analyses[i].MoveIndex, d, _analyses[i]));
        }

        // 找最大正变化（KataGo 妙招：胜率大涨）
        // 找最大负变化（KataGo 走差：胜率大跌）
        var bestAiMove = deltas.OrderByDescending(x => x.delta).FirstOrDefault();
        var worstAiMove = deltas.OrderBy(x => x.delta).FirstOrDefault();

        var kp = new List<string>();
        if (bestAiMove.ma != null && bestAiMove.delta >= 0.05)
        {
            string side = bestAiMove.ma.WhoMoved == _humanColor ? "你" : "AI";
            kp.Add($"📈 最大改善：第 {bestAiMove.idx} 手 {side} 下 {bestAiMove.ma.Vertex}，" +
                   $"局面胜率 {bestAiMove.delta * 100:+0.0;-0.0;0.0}%（{(bestAiMove.ma.HumanWinrate * 100):F1}%）");
        }
        if (worstAiMove.ma != null && Math.Abs(worstAiMove.delta) >= 0.05 && !worstAiMove.Equals(bestAiMove))
        {
            string side = worstAiMove.ma.WhoMoved == _humanColor ? "你" : "AI";
            kp.Add($"📉 最大滑坡：第 {worstAiMove.idx} 手 {side} 下 {worstAiMove.ma.Vertex}，" +
                   $"局面胜率 {worstAiMove.delta * 100:+0.0;-0.0;0.0}%（{(worstAiMove.ma.HumanWinrate * 100):F1}%）");
        }
        if (kp.Count == 0)
            kp.Add("本局胜率波动不大，没有特别显著的转折点。");

        KeyPointsText.Text = string.Join("\n", kp);

        // 3) 表格：按 MoveHistory 全步展开（玩家手 + AI 手交错）
        // _analyses 只覆盖 AI 手，玩家手显示"—"
        var analyseByIndex = _analyses.ToDictionary(a => a.MoveIndex);
        var rows = new List<MoveRow>();
        // 推断总手数 = _analyses 中最大 MoveIndex（或玩家走子数 * 2）
        int totalMoves = _analyses.Count > 0 ? _analyses[^1].MoveIndex : 0;
        if (totalMoves == 0)
        {
            MovesGrid.ItemsSource = rows;
            return;
        }

        double? prevAiWinrate = null;
        int aiStepNumber = 0;  // AI 是第几次走子
        for (int i = 1; i <= totalMoves; i++)
        {
            bool hasAnalysis = analyseByIndex.TryGetValue(i, out var a);
            if (hasAnalysis)
            {
                aiStepNumber++;
                double delta = prevAiWinrate.HasValue ? (a!.HumanWinrate - prevAiWinrate.Value) : 0;
                string verdict = VerdictForDelta(a!, delta, aiStepNumber);
                rows.Add(new MoveRow
                {
                    IndexLabel = $"{i}",
                    SideLabel = a!.WhoMoved == _humanColor ? "● 你" : "○ AI",
                    Vertex = a.Vertex,
                    WinrateLabel = $"{a.HumanWinrate * 100:F1}%",
                    LeadLabel = a.HumanScoreLead >= 0 ? $"+{a.HumanScoreLead:F1}" : $"{a.HumanScoreLead:F1}",
                    DeltaLabel = prevAiWinrate.HasValue ? $"{delta * 100:+0.0;-0.0;0.0}%" : "—",
                    Verdict = verdict,
                    RowWinrate = a.HumanWinrate,
                });
                prevAiWinrate = a.HumanWinrate;
            }
            else
            {
                // 玩家手：没评估，显示"—"
                rows.Add(new MoveRow
                {
                    IndexLabel = $"{i}",
                    SideLabel = _humanColor == Stone.Black && i % 2 == 1 ? "● 你" :
                                _humanColor == Stone.White && i % 2 == 0 ? "● 你" : "○ AI",
                    Vertex = "(未记录)",
                    WinrateLabel = "—",
                    LeadLabel = "—",
                    DeltaLabel = "—",
                    Verdict = "玩家落子后未评估",
                    RowWinrate = null,
                });
            }
        }
        MovesGrid.ItemsSource = rows;

        // 4) 画胜率曲线（Canvas）
        DrawChart();
    }

    private string VerdictForDelta(MoveAnalysis a, double delta, int aiStep)
    {
        if (aiStep == 1) return "开局评估";
        if (a.Vertex == "pass") return "虚手（局面不变）";
        if (a.Vertex == "resign") return "认输";
        if (delta >= 0.10) return "✨ 妙招";
        if (delta >= 0.05) return "好棋";
        if (delta <= -0.10) return "⚠️ 大失误";
        if (delta <= -0.05) return "坏手";
        return "常规";
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;
        if (w < 20 || h < 20) return;

        // 50% 中线 + 顶部/底部 padding
        const double padL = 36, padR = 16, padT = 12, padB = 22;
        double chartW = w - padL - padR;
        double chartH = h - padT - padB;
        double yMid = padT + chartH / 2;

        // 网格线（25% / 50% / 75%）
        var gridBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x4A, 0x40));
        gridBrush.Freeze();
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA8, 0x9F));
        labelBrush.Freeze();

        for (int pct = 0; pct <= 100; pct += 25)
        {
            double y = padT + chartH * (1 - pct / 100.0);
            var line = new Line { X1 = padL, Y1 = y, X2 = padL + chartW, Y2 = y,
                                  Stroke = gridBrush, StrokeThickness = 1,
                                  StrokeDashArray = new DoubleCollection { 4, 4 } };
            ChartCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = $"{pct}%",
                Foreground = labelBrush,
                FontSize = 10,
            };
            Canvas.SetLeft(label, 2);
            Canvas.SetTop(label, y - 8);
            ChartCanvas.Children.Add(label);
        }

        // 50% 中线（实线，强调）
        var midLine = new Line { X1 = padL, Y1 = yMid, X2 = padL + chartW, Y2 = yMid,
                                 Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0x9B, 0x3C)),
                                 StrokeThickness = 1.2 };
        midLine.Opacity = 0.5;
        ChartCanvas.Children.Add(midLine);

        // 横轴步数标签（按需抽几个）
        int totalAi = _analyses.Count;
        if (totalAi == 0) return;

        var dotBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x9B, 0x3C));
        dotBrush.Freeze();
        var lineBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xA0, 0xD0));
        lineBrush.Freeze();

        // 折线 + 圆点
        var polyPoints = new PointCollection();
        for (int i = 0; i < _analyses.Count; i++)
        {
            var a = _analyses[i];
            double x = padL + (totalAi == 1 ? chartW / 2 : chartW * i / (totalAi - 1));
            double y = padT + chartH * (1 - a.HumanWinrate);
            polyPoints.Add(new Point(x, y));
        }
        if (polyPoints.Count >= 2)
        {
            var poly = new Polyline { Points = polyPoints, Stroke = lineBrush, StrokeThickness = 2 };
            ChartCanvas.Children.Add(poly);
        }

        for (int i = 0; i < _analyses.Count; i++)
        {
            var a = _analyses[i];
            double x = padL + (totalAi == 1 ? chartW / 2 : chartW * i / (totalAi - 1));
            double y = padT + chartH * (1 - a.HumanWinrate);
            var dot = new Ellipse
            {
                Width = 7, Height = 7,
                Fill = dotBrush,
                Stroke = new SolidColorBrush(Color.FromRgb(0x1E, 0x26, 0x20)),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(dot, x - 3.5);
            Canvas.SetTop(dot, y - 3.5);
            ToolTipService.SetToolTip(dot,
                $"AI 第 {i + 1} 步：{a.Vertex}\n玩家胜率 {a.HumanWinrate * 100:F1}%\n目差 {(a.HumanScoreLead >= 0 ? "+" : "")}{a.HumanScoreLead:F2}");
            ChartCanvas.Children.Add(dot);
        }

        // X 轴底部标签
        var xLabel = new TextBlock
        {
            Text = $"AI 走子序号（1 ~ {totalAi}）",
            Foreground = labelBrush,
            FontSize = 10,
        };
        Canvas.SetLeft(xLabel, padL);
        Canvas.SetTop(xLabel, h - 14);
        ChartCanvas.Children.Add(xLabel);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>DataGrid 行绑定类型。</summary>
public class MoveRow
{
    public string IndexLabel { get; set; } = "";
    public string SideLabel { get; set; } = "";
    public string Vertex { get; set; } = "";
    public string WinrateLabel { get; set; } = "";
    public string LeadLabel { get; set; } = "";
    public string DeltaLabel { get; set; } = "";
    public string Verdict { get; set; } = "";
    public double? RowWinrate { get; set; }  // 内部用，画图时可能要用
}
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
/// AI endgame review report window:
/// - Top: summary
/// - Middle: player win-rate curve after each KataGo move (hand-drawn on Canvas)
/// - Bottom: per-move detail table + verdict (brilliant / blunder / turning point)
///
/// Note: KataGoClient does not expose an independent kata-analyze after the human move,
/// so the table "win rate" only covers KataGo moves; the human move shows "—".
/// Turning points / brilliant / blunder are judged by the win-rate change on KataGo moves (>=5% significant).
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
        // 1) Top summary
        int totalAi = _analyses.Count;
        SummaryText.Text = string.Format(
            LocalizationManager.Get("ReviewSummary"), _winnerLabel, totalAi, totalAi);

        // 2) Compute turning points (biggest win-rate swings on KataGo moves)
        var deltas = new List<(int idx, double delta, MoveAnalysis ma)>();
        for (int i = 1; i < _analyses.Count; i++)
        {
            double d = _analyses[i].HumanWinrate - _analyses[i - 1].HumanWinrate;
            deltas.Add((_analyses[i].MoveIndex, d, _analyses[i]));
        }

        // Biggest positive swing (KataGo brilliant: win rate jumps)
        // Biggest negative swing (KataGo blunder: win rate drops)
        var bestAiMove = deltas.OrderByDescending(x => x.delta).FirstOrDefault();
        var worstAiMove = deltas.OrderBy(x => x.delta).FirstOrDefault();

        var kp = new List<string>();
        if (bestAiMove.ma != null && bestAiMove.delta >= 0.05)
        {
            string side = bestAiMove.ma.WhoMoved == _humanColor ? LocalizationManager.Get("ReviewYou") : LocalizationManager.Get("ReviewAi");
            kp.Add(string.Format(LocalizationManager.Get("ReviewMaxImprove"),
                bestAiMove.idx, side, bestAiMove.ma.Vertex,
                (bestAiMove.delta * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture),
                (bestAiMove.ma.HumanWinrate * 100).ToString("F1", CultureInfo.InvariantCulture)));
        }
        if (worstAiMove.ma != null && Math.Abs(worstAiMove.delta) >= 0.05 && !worstAiMove.Equals(bestAiMove))
        {
            string side = worstAiMove.ma.WhoMoved == _humanColor ? LocalizationManager.Get("ReviewYou") : LocalizationManager.Get("ReviewAi");
            kp.Add(string.Format(LocalizationManager.Get("ReviewMaxDrop"),
                worstAiMove.idx, side, worstAiMove.ma.Vertex,
                (worstAiMove.delta * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture),
                (worstAiMove.ma.HumanWinrate * 100).ToString("F1", CultureInfo.InvariantCulture)));
        }
        if (kp.Count == 0)
            kp.Add(LocalizationManager.Get("ReviewNoTurningPoint"));

        KeyPointsText.Text = string.Join("\n", kp);

        // 3) Table: expand every move in MoveHistory (human + AI interleaved)
        // _analyses only covers AI moves; human moves show "—"
        var analyseByIndex = _analyses.ToDictionary(a => a.MoveIndex);
        var rows = new List<MoveRow>();
        // total moves = largest MoveIndex in _analyses (or human moves * 2)
        int totalMoves = _analyses.Count > 0 ? _analyses[^1].MoveIndex : 0;
        if (totalMoves == 0)
        {
            MovesGrid.ItemsSource = rows;
            return;
        }

        double? prevAiWinrate = null;
        int aiStepNumber = 0;  // which KataGo move this is
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
                    SideLabel = a!.WhoMoved == _humanColor ? "● " + LocalizationManager.Get("ReviewYou") : "○ " + LocalizationManager.Get("ReviewAi"),
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
                // Human move: no evaluation, show "—"
                rows.Add(new MoveRow
                {
                    IndexLabel = $"{i}",
                    SideLabel = _humanColor == Stone.Black && i % 2 == 1 ? "● " + LocalizationManager.Get("ReviewYou") :
                                _humanColor == Stone.White && i % 2 == 0 ? "● " + LocalizationManager.Get("ReviewYou") : "○ " + LocalizationManager.Get("ReviewAi"),
                    Vertex = LocalizationManager.Get("ReviewUnrecorded"),
                    WinrateLabel = "—",
                    LeadLabel = "—",
                    DeltaLabel = "—",
                    Verdict = LocalizationManager.Get("ReviewPlayerMoveNoEval"),
                    RowWinrate = null,
                });
            }
        }
        MovesGrid.ItemsSource = rows;

        // 4) Draw win-rate curve (Canvas)
        DrawChart();
    }

    private string VerdictForDelta(MoveAnalysis a, double delta, int aiStep)
    {
        if (aiStep == 1) return LocalizationManager.Get("ReviewVerdictOpening");
        if (a.Vertex == "pass") return LocalizationManager.Get("ReviewVerdictPass");
        if (a.Vertex == "resign") return LocalizationManager.Get("ReviewVerdictResign");
        if (delta >= 0.10) return LocalizationManager.Get("ReviewVerdictBrilliant");
        if (delta >= 0.05) return LocalizationManager.Get("ReviewVerdictGood");
        if (delta <= -0.10) return LocalizationManager.Get("ReviewVerdictBigMistake");
        if (delta <= -0.05) return LocalizationManager.Get("ReviewVerdictMistake");
        return LocalizationManager.Get("ReviewVerdictNormal");
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;
        if (w < 20 || h < 20) return;

        // 50% mid-line + top/bottom padding
        const double padL = 36, padR = 16, padT = 12, padB = 22;
        double chartW = w - padL - padR;
        double chartH = h - padT - padB;
        double yMid = padT + chartH / 2;

        // Grid lines (25% / 50% / 75%)
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

        // 50% mid-line (solid, emphasized)
        var midLine = new Line { X1 = padL, Y1 = yMid, X2 = padL + chartW, Y2 = yMid,
                                 Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0x9B, 0x3C)),
                                 StrokeThickness = 1.2 };
        midLine.Opacity = 0.5;
        ChartCanvas.Children.Add(midLine);

        // X-axis move-number label (sampled)
        int totalAi = _analyses.Count;
        if (totalAi == 0) return;

        var dotBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x9B, 0x3C));
        dotBrush.Freeze();
        var lineBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xA0, 0xD0));
        lineBrush.Freeze();

        // Polyline + dots
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
                string.Format(LocalizationManager.Get("ReviewMoveTip"), i + 1, a.Vertex,
                    (a.HumanWinrate * 100).ToString("F1", CultureInfo.InvariantCulture),
                    (a.HumanScoreLead >= 0 ? "+" : "") + a.HumanScoreLead.ToString("F2", CultureInfo.InvariantCulture)));
            ChartCanvas.Children.Add(dot);
        }

        // X-axis bottom label
        var xLabel = new TextBlock
        {
            Text = string.Format(LocalizationManager.Get("ReviewXAxis"), totalAi),
            Foreground = labelBrush,
            FontSize = 10,
        };
        Canvas.SetLeft(xLabel, padL);
        Canvas.SetTop(xLabel, h - 14);
        ChartCanvas.Children.Add(xLabel);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>DataGrid row binding type.</summary>
public class MoveRow
{
    public string IndexLabel { get; set; } = "";
    public string SideLabel { get; set; } = "";
    public string Vertex { get; set; } = "";
    public string WinrateLabel { get; set; } = "";
    public string LeadLabel { get; set; } = "";
    public string DeltaLabel { get; set; } = "";
    public string Verdict { get; set; } = "";
    public double? RowWinrate { get; set; }  // internal use, may be needed when drawing
}

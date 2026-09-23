using System.Globalization;
using System.Text;

namespace GoGame;

/// <summary>
/// 把当前对局导出为 SGF（Smart Game Format）棋谱文件，可用 Sabaki / MultiGo / KGS 等工具复盘。
/// 默认贴目 7.5（中国规则）；需要可由 caller 覆盖。
///
/// MoveHistory 格式（来自 Board.RecordMove）：
///   "bD4" / "bpass" / "bresign"      （黑 D4 / 黑停着 / 黑认输）
///   "wQ16" / "wpass" / "wresign"     （白 Q16 / 白停着 / 白认输）
///
/// SGF 坐标：列字母 a-t 跳过 i；行可字母（a-s, a=顶 = y=size-1）或数字（1=底 = y=0）。
/// GTP 坐标：D4 等同 SGF "dd"（19 路）；但行 D=3 → SGF 行字母 d = y=18-3=15 → SGF "dd" ✓。
/// </summary>
public static class SgfWriter
{
    /// <summary>中国规则默认贴目 7.5；设为 0.0/6.5 等覆盖。</summary>
    public const double DefaultKomi = 7.5;

    /// <summary>对局结果（写入 SGF RE 字段）。null = 未知（如中途保存的棋局）。</summary>
    public enum Outcome { Unknown, BlackWin, WhiteWin, Draw }

    /// <summary>
    /// 写当前对局为 SGF 文本。MoveHistory 必须按时间顺序（黑先）。
    /// </summary>
    public static string Write(
        Board board,
        Stone humanColor,
        string humanName = "Human",
        string aiName = "KataGo",
        double komi = DefaultKomi,
        Outcome outcome = Outcome.Unknown,
        string? comment = null)
    {
        if (board == null) throw new ArgumentNullException(nameof(board));

        var sb = new StringBuilder(256);
        sb.Append("(;");

        // ---- 元信息 ----
        sb.Append("GM[1]");                                          // 1 = Go
        sb.Append("FF[4]");                                          // SGF file format version 4
        sb.Append($"SZ[{board.Size}]");                             // 棋盘大小
        sb.Append("CA[UTF-8]");                                     // 字符集
        sb.Append($"PB[{(humanColor == Stone.Black ? humanName : aiName)}]");
        sb.Append($"PW[{(humanColor == Stone.Black ? aiName : humanName)}]");
        sb.Append("HA[0]");                                         // 让子数（暂不支持让子局）
        sb.Append($"KM[{komi.ToString("0.0", CultureInfo.InvariantCulture)}]");  // 贴目（用 InvariantCulture 避免德语 7,5）
        sb.Append("RU[Chinese]");                                   // 中国规则

        var dt = DateTime.Now;
        sb.Append($"DT[{dt:yyyy-MM-dd}]");
        sb.Append($"TM[{dt:HH\\:mm\\:ss}]");
        sb.Append($"AP[GoGame Desktop 1.0]");                       // 应用签名

        if (comment != null && comment.Length > 0)
            sb.Append($"C[{EscapeSgfText(comment)}]");

        // ---- 着法 ----
        foreach (var move in board.MoveHistory)
        {
            if (string.IsNullOrEmpty(move) || move.Length < 2) continue;

            char who = char.ToLower(move[0]);                       // b / w
            string body = move.Substring(1).ToLowerInvariant();     // D4 / pass / resign / tt

            if (body == "pass")
            {
                sb.Append(who == 'b' ? ";B[]" : ";W[]");
            }
            else if (body == "resign" || body == "tt")
            {
                // SGF FF[4] 规范：认输走 B[tt] / W[tt]，不是空着
                sb.Append(who == 'b' ? ";B[tt]" : ";W[tt]");
            }
            else
            {
                // GTP 顶点 (D4) → SGF 顶点 (dd)
                var (x, y) = Board.FromGtpVertex(body, board.Size);
                string vertex = ToSgfVertex(x, y, board.Size);
                sb.Append(who == 'b' ? $";B[{vertex}]" : $";W[{vertex}]");
            }
        }

        // ---- 结果（最后一手认输时显式标注；终局数子结果由 caller 传入） ----
        if (outcome == Outcome.BlackWin || outcome == Outcome.WhiteWin || outcome == Outcome.Draw)
        {
            string re = outcome switch
            {
                Outcome.BlackWin => "B+",
                Outcome.WhiteWin => "W+",
                _ => "0"
            };
            sb.Append($";RE[{re}]");
        }

        sb.Append(")");
        return sb.ToString();
    }

    /// <summary>
    /// (x, y) → SGF 顶点（x=col=0..size-1, y=row=0..size-1, 0=底）。
    /// SGF 列字母：a=0, b=1, ..., h=7, 跳过 i, j=8, k=9, ...
    /// SGF 行字母：a 在顶 → y = size-1 - aIdx, ... 但通常 SGF 文件里行用数字（1=底）。
    /// </summary>
    public static string ToSgfVertex(int x, int y, int size)
    {
        if (x < 0 || x >= size || y < 0 || y >= size)
            throw new ArgumentOutOfRangeException($"Coordinate ({x},{y}) is outside the {size}x{size} board");

        char colChar = x < 8 ? (char)('a' + x) : (char)('a' + x + 1);  // 跳过 i
        // 行用数字（1 在底 = y+1）；Sabaki / MultiGo 都接受。
        return $"{colChar}{y + 1}";
    }

    /// <summary>SGF 文本里特殊字符转义（C 字段）。</summary>
    private static string EscapeSgfText(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("]", "\\]")
            .Replace("\r\n", "\n");
    }
}
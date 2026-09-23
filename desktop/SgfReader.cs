using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GoGame;

/// <summary>
/// SGF 棋谱文件元信息（PB/PW/RE/KM/DT/RU/HA 等属性）。
/// </summary>
public record SgfMeta(
    string? BlackName = null,
    string? WhiteName = null,
    string? BlackRank = null,
    string? WhiteRank = null,
    double? Komi = null,
    string? Result = null,        // "B+1.5" / "W+R" / "0" 等
    string? Date = null,          // "2026-09-04"
    string? Rule = null,          // "Chinese" / "Japanese" / "Korean"
    int? Handicap = null,
    string? Event = null,
    string? Source = null);

/// <summary>
/// 解析 SGF（Smart Game Format）棋谱文件，返回 Board + 着法列表（GTP 格式）+ 元信息。
/// SGF 坐标：列 a-t (跳过 i)，行 字母 (a-s 跳过 i，a 在顶 = y=0) 或数字。统一转 GTP 顶点 "D4" 表示。
/// 着法格式："bD4" / "wQ16" / "bpass" / "bresign"。
/// </summary>
public static class SgfReader
{
    public static (Board board, List<string> moves, SgfMeta meta) Read(string path)
    {
        var content = File.ReadAllText(path);
        return Parse(content);
    }

    public static (Board board, List<string> moves, SgfMeta meta) Parse(string sgf)
    {
        // SZ：棋盘大小
        int size = 19;
        var szMatch = Regex.Match(sgf, @"SZ\[(\d+)\]");
        if (szMatch.Success) int.TryParse(szMatch.Groups[1].Value, out size);
        if (size != 9 && size != 13 && size != 19) size = 19;  // 兜底

        // 元信息
        var meta = new SgfMeta(
            BlackName: ReadProp(sgf, "PB"),
            WhiteName: ReadProp(sgf, "PW"),
            BlackRank: ReadProp(sgf, "BR"),
            WhiteRank: ReadProp(sgf, "WR"),
            Komi: ReadDouble(sgf, "KM"),
            Result: ReadProp(sgf, "RE"),
            Date: ReadProp(sgf, "DT"),
            Rule: ReadProp(sgf, "RU"),
            Handicap: ReadInt(sgf, "HA"),
            Event: ReadProp(sgf, "EV"),
            Source: ReadProp(sgf, "SO"));

        // 着法：扫描所有 ;B[...] / ;W[...],支持空（pass）和 tt（resign）和显式 'pass'
        var moves = new List<string>();
        foreach (Match m in Regex.Matches(sgf, @";\s*([BW])\s*\[([^\]]*)\]"))
        {
            char who = char.ToLower(m.Groups[1].Value[0]);   // 'b' or 'w'
            string vertex = m.Groups[2].Value.Trim().ToLower();
            if (string.IsNullOrEmpty(vertex) || vertex == "pass") { moves.Add($"{who}pass"); continue; }
            if (vertex == "tt" || vertex == "resign") { moves.Add($"{who}resign"); continue; }
            try
            {
                var (x, y) = FromSgfVertex(vertex, size);
                // 转回 GTP 顶点（与 Board.FromGtpVertex 兼容）
                moves.Add($"{who}{Board.ToGtpVertex(x, y, size)}");
            }
            catch { /* 非法坐标跳过 */ }
        }

        var board = new Board(size);
        return (board, moves, meta);
    }

    /// <summary>
    /// SGF 顶点 → (x, y)。列 a-t (a=0, 跳过 i)，行可字母 (a-s, a 在顶 = y=0) 或数字 (1 = 底)。
    /// 坐标 (0,0) = 左下角，(size-1, size-1) = 右上角。
    /// </summary>
    public static (int x, int y) FromSgfVertex(string v, int size)
    {
        if (string.IsNullOrEmpty(v)) throw new FormatException("SGF vertex is empty");

        // 列字母
        char col = char.ToLower(v[0]);
        if (col == 'i') throw new FormatException("SGF coordinates must not use the letter I");
        int x = col - 'a';
        if (col > 'i') x--;       // 跳过 i
        if (x < 0 || x >= size)
            throw new FormatException($"SGF column '{col}' is outside {size}x{size}");

        // 行（字母 or 数字）
        int rowFromBottom;
        if (v.Length >= 2 && char.IsDigit(v[1]))
        {
            // 数字行（如 '1' 是最底部）
            rowFromBottom = 0;
            for (int i = 1; i < v.Length; i++)
            {
                if (!char.IsDigit(v[i])) throw new FormatException($"Invalid SGF number row: {v}");
                rowFromBottom = rowFromBottom * 10 + (v[i] - '0');
            }
        }
        else if (v.Length >= 2)
        {
            // 字母行（如 'a' 在顶 = 0 from top）
            char rowC = char.ToLower(v[1]);
            if (rowC == 'i') throw new FormatException("SGF coordinates must not use the letter I");
            int ry = rowC - 'a';
            if (rowC > 'i') ry--;
            if (ry < 0 || ry >= size) throw new FormatException($"SGF row '{rowC}' is outside {size}x{size}");
            rowFromBottom = size - ry;
        }
        else
        {
            throw new FormatException($"SGF vertex is missing its row: {v}");
        }

        int y = rowFromBottom - 1;
        if (y < 0 || y >= size) throw new FormatException($"SGF row number {rowFromBottom} is outside {size}");

        return (x, y);
    }

    private static string? ReadProp(string sgf, string prop)
    {
        var m = Regex.Match(sgf, $@"{prop}\[([^\]]*)\]");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static double? ReadDouble(string sgf, string prop)
    {
        var s = ReadProp(sgf, prop);
        if (s == null) return null;
        return double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int? ReadInt(string sgf, string prop)
    {
        var s = ReadProp(sgf, prop);
        return s != null && int.TryParse(s, out var v) ? v : null;
    }
}

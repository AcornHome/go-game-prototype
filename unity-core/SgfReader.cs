using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace GoGame.Core;

/// <summary>
/// SGF 棋谱文件元信息（PB/PW/RE/KM/DT/RU/HA 等属性）。
/// </summary>
public class SgfMeta
{
    public string? BlackName { get; set; }
    public string? WhiteName { get; set; }
    public string? BlackRank { get; set; }
    public string? WhiteRank { get; set; }
    public double? Komi { get; set; }
    public string? Result { get; set; }
    public string? Date { get; set; }
    public string? Rule { get; set; }
    public int? Handicap { get; set; }
    public string? Event { get; set; }
    public string? Source { get; set; }

    public SgfMeta() { }

    public SgfMeta(
        string? blackName = null, string? whiteName = null,
        string? blackRank = null, string? whiteRank = null,
        double? komi = null, string? result = null,
        string? date = null, string? rule = null,
        int? handicap = null, string? @event = null,
        string? source = null)
    {
        BlackName = blackName;
        WhiteName = whiteName;
        BlackRank = blackRank;
        WhiteRank = whiteRank;
        Komi = komi;
        Result = result;
        Date = date;
        Rule = rule;
        Handicap = handicap;
        Event = @event;
        Source = source;
    }
}

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
        int size = 19;
        var szMatch = Regex.Match(sgf, @"SZ\[(\d+)\]");
        if (szMatch.Success) int.TryParse(szMatch.Groups[1].Value, out size);
        if (size != 9 && size != 13 && size != 19) size = 19;

        var meta = new SgfMeta(
            blackName: ReadProp(sgf, "PB"),
            whiteName: ReadProp(sgf, "PW"),
            blackRank: ReadProp(sgf, "BR"),
            whiteRank: ReadProp(sgf, "WR"),
            komi: ReadDouble(sgf, "KM"),
            result: ReadProp(sgf, "RE"),
            date: ReadProp(sgf, "DT"),
            rule: ReadProp(sgf, "RU"),
            handicap: ReadInt(sgf, "HA"),
            @event: ReadProp(sgf, "EV"),
            source: ReadProp(sgf, "SO"));

        var moves = new List<string>();
        foreach (Match m in Regex.Matches(sgf, @";\s*([BW])\s*\[([^\]]*)\]"))
        {
            char who = char.ToLower(m.Groups[1].Value[0]);
            string vertex = m.Groups[2].Value.Trim().ToLower();
            if (string.IsNullOrEmpty(vertex) || vertex == "pass") { moves.Add($"{who}pass"); continue; }
            if (vertex == "tt" || vertex == "resign") { moves.Add($"{who}resign"); continue; }
            try
            {
                var (x, y) = FromSgfVertex(vertex, size);
                moves.Add($"{who}{Board.ToGtpVertex(x, y, size)}");
            }
            catch { }
        }

        var board = new Board(size);
        return (board, moves, meta);
    }

    /// <summary>
    /// SGF 顶点 → (x, y)。列 a-t (a=0, 跳过 i)；
    /// 行可字母 (a 在顶 = y=0, a-t 跳过 i) 或数字 (1 在底 = y=size-1)。
    /// 坐标 (0,0) = 左下角，(size-1, size-1) = 右上角。
    /// </summary>
    public static (int x, int y) FromSgfVertex(string v, int size)
    {
        if (string.IsNullOrEmpty(v)) throw new FormatException("SGF 顶点为空");

        // 列字母
        char col = char.ToLower(v[0]);
        if (col == 'i') throw new FormatException("SGF 坐标不允许字母 I");
        int x = col - 'a';
        if (col > 'i') x--;
        if (x < 0 || x >= size)
            throw new FormatException($"SGF 列字符 '{col}' 超出 {size}x{size}");

        // 行（字母 = a 在顶；数字 = 1 在底）
        int y;
        if (v.Length >= 2 && char.IsDigit(v[1]))
        {
            // 数字行：'1' 在底 = y = size - 1；'size' 在顶 = y = 0
            int rowNumber = 0;
            for (int i = 1; i < v.Length; i++)
            {
                if (!char.IsDigit(v[i])) throw new FormatException($"SGF 数字行无效: {v}");
                rowNumber = rowNumber * 10 + (v[i] - '0');
            }
            y = size - rowNumber;
        }
        else if (v.Length >= 2)
        {
            // 字母行：'a' 在顶 = y = 0
            char rowC = char.ToLower(v[1]);
            if (rowC == 'i') throw new FormatException("SGF 坐标不允许字母 I");
            int ry = rowC - 'a';
            if (rowC > 'i') ry--;
            if (ry < 0 || ry >= size) throw new FormatException($"SGF 行字符 '{rowC}' 超出 {size}x{size}");
            y = ry;
        }
        else
        {
            throw new FormatException($"SGF 顶点缺行: {v}");
        }

        if (y < 0 || y >= size) throw new FormatException($"SGF 顶点 '{v}' 转 y={y} 超出 {size}");

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
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int? ReadInt(string sgf, string prop)
    {
        var s = ReadProp(sgf, prop);
        return s != null && int.TryParse(s, out var v) ? v : null;
    }
}

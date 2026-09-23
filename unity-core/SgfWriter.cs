using System.Globalization;
using System.Text;

namespace GoGame.Core;

/// <summary>
/// 把当前对局导出为 SGF（Smart Game Format）棋谱文件，可用 Sabaki / MultiGo / KGS 等工具复盘。
/// 默认贴目 7.5（中国规则）；需要可由 caller 覆盖。
/// </summary>
public static class SgfWriter
{
    /// <summary>中国规则默认贴目 7.5；设为 0.0/6.5 等覆盖。</summary>
    public const double DefaultKomi = 7.5;

    public static string Write(Board board, Stone humanColor,
        string humanName = "Human", string aiName = "KataGo",
        double komi = DefaultKomi)
    {
        var sb = new StringBuilder();
        sb.Append("(;");
        sb.Append("GM[1]");            // 1 = Go
        sb.Append("FF[4]");            // SGF file format version 4
        sb.Append($"SZ[{board.Size}]");
        sb.Append("CA[UTF-8]");
        sb.Append($"PB[{(humanColor == Stone.Black ? humanName : aiName)}]");
        sb.Append($"PW[{(humanColor == Stone.Black ? aiName : humanName)}]");
        sb.Append("HA[0]");
        sb.Append($"KM[{komi.ToString("0.0", CultureInfo.InvariantCulture)}]");
        sb.Append("RU[Chinese]");

        var dt = DateTime.Now;
        sb.Append($"DT[{dt:yyyy-MM-dd}]");
        sb.Append($"TM[{dt:HH\\:mm\\:ss}]");

        foreach (var move in board.MoveHistory)
        {
            // move 格式: "bD4" / "wQ16" / "bpass" / "bresign"
            if (move.Length < 2) continue;
            char who = move[0];
            string vertex = move.Substring(1).ToLower();
            if (who == 'b') sb.Append($";B[{vertex}]");
            else if (who == 'w') sb.Append($";W[{vertex}]");
        }
        sb.Append(")");
        return sb.ToString();
    }

    /// <summary>写入磁盘（UTF-8 无 BOM）。</summary>
    public static void WriteFile(string path, Board board, Stone humanColor,
        string humanName = "Human", string aiName = "KataGo",
        double komi = DefaultKomi)
    {
        File.WriteAllText(path, Write(board, humanColor, humanName, aiName, komi), new UTF8Encoding(false));
    }
}

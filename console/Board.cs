namespace GoPrototype;

/// <summary>
/// 棋子颜色。Empty = 空。围棋规则：黑先白后。
/// </summary>
public enum Stone
{
    Empty,
    Black,
    White
}

/// <summary>
/// 19 路（或 9/13 路）棋盘状态机。
/// 纯内存对象，不做合法性校验——把合法性丢给 KataGo，我们只负责同步本地状态。
/// </summary>
public class Board
{
    public int Size { get; }
    public Stone[,] Grid { get; }
    public Stone NextToPlay { get; set; } = Stone.Black;

    /// <summary>按 GTP 协议记录的着法列表，例如 ["bD4", "wQ16", ...]</summary>
    public List<string> MoveHistory { get; } = new();

    public Board(int size = 19)
    {
        Size = size;
        Grid = new Stone[size, size];
    }

    public Stone Get(int x, int y) => Grid[x, y];

    public bool IsInBounds(int x, int y) =>
        x >= 0 && y >= 0 && x < Size && y < Size;

    /// <summary>
    /// 本地落子（不做合法性检查）。落子前必须先由调用方确保 KataGo 接受了这一步。
    /// </summary>
    public bool Place(int x, int y, Stone stone)
    {
        if (!IsInBounds(x, y) || Grid[x, y] != Stone.Empty) return false;
        Grid[x, y] = stone;
        return true;
    }

    public void RecordMove(string gtpMove) => MoveHistory.Add(gtpMove);

    /// <summary>
    /// 0-indexed (x, y) 转 GTP 协议顶点字符串，如 (3, 15) on 19x19 -> "D4"。
    /// x 是列（字母），y 是行（数字，从底部 1 开始）。
    /// </summary>
    public static string ToGtpVertex(int x, int y, int size)
    {
        string col = "";
        int n = x;
        do
        {
            col = (char)('A' + n % 26) + col;
            n = n / 26 - 1;
        } while (n >= 0);
        return $"{col}{size - y}";
    }

    /// <summary>
    /// GTP 顶点字符串转 0-indexed (x, y)，如 "D4" on 19x19 -> (3, 15)。
    /// </summary>
    public static (int x, int y) FromGtpVertex(string v, int size)
    {
        int i = 0;
        int x = 0;
        while (i < v.Length && char.IsLetter(v[i]))
        {
            x = x * 26 + (char.ToUpper(v[i]) - 'A' + 1);
            i++;
        }
        x--;
        int y = size - int.Parse(v.Substring(i));
        return (x, y);
    }

    /// <summary>
    /// 在终端绘制棋盘。X = 黑，O = 白，. = 空，* = 星位。
    /// </summary>
    public void Render()
    {
        // 列标头（多位数棋盘时对齐）
        Console.Write("    ");
        for (int x = 0; x < Size; x++)
        {
            Console.Write($"{(char)('A' + x)} ");
        }
        Console.WriteLine();

        for (int y = 0; y < Size; y++)
        {
            int rowNum = Size - y;
            Console.Write($"{rowNum,3} ");
            for (int x = 0; x < Size; x++)
            {
                Stone s = Grid[x, y];
                char c;
                if (s == Stone.Black) c = 'X';
                else if (s == Stone.White) c = 'O';
                else if (IsStarPoint(x, y)) c = '*';
                else c = '.';
                Console.Write($"{c} ");
            }
            Console.WriteLine();
        }
    }

    private bool IsStarPoint(int x, int y)
    {
        if (Size == 19)
        {
            int[] stars = { 3, 9, 15 };
            return Array.IndexOf(stars, x) >= 0 && Array.IndexOf(stars, y) >= 0;
        }
        if (Size == 13)
        {
            int[] stars = { 3, 6, 9 };
            return Array.IndexOf(stars, x) >= 0 && Array.IndexOf(stars, y) >= 0;
        }
        if (Size == 9)
        {
            int[] stars = { 2, 4, 6 };
            return Array.IndexOf(stars, x) >= 0 && Array.IndexOf(stars, y) >= 0;
        }
        return false;
    }
}

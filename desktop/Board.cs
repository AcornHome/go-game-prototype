namespace GoGame;

/// <summary>棋子颜色。Empty = 空。围棋规则：黑先白后。</summary>
public enum Stone
{
    Empty,
    Black,
    White
}

/// <summary>
/// 19 路（或 9/13 路）棋盘状态机。纯内存对象，不做合法性校验——合法性交给 KataGo。
/// </summary>
public class Board
{
    public int Size { get; }
    public Stone[,] Grid { get; }
    public Stone NextToPlay { get; set; } = Stone.Black;

    /// <summary>按 GTP 协议记录的着法列表，例如 ["bD4", "wQ16", ...]</summary>
    public List<string> MoveHistory { get; } = new();

    /// <summary>每步的棋盘快照（克隆的 Stone[,]）。索引 i 表示"走了 i 步之后"的状态。
    /// 0 = 空盘，1 = 第一步落完，n = 走完全部 n 步。
    /// 用于棋谱回放任意步数。</summary>
    public List<Stone[,]> Snapshots { get; } = new();

    public Board(int size = 19)
    {
        Size = size;
        Grid = new Stone[size, size];
        Snapshots.Add(CloneGrid());   // 0 步快照 = 空盘
    }

    public Stone Get(int x, int y) => Grid[x, y];

    public bool IsInBounds(int x, int y) =>
        x >= 0 && y >= 0 && x < Size && y < Size;

    /// <summary>
    /// 本地落子（不做合法性检查）。落子前必须先由调用方确保 KataGo 接受了这一步。
    /// 按围棋规则自动提掉无气的相邻对方棋串，并禁止自杀手。
    /// 落子成功后更新 Snapshots 栈顶。
    /// 返回：(placed, captures) —— placed=false 表示自杀手、棋盘未修改；captures 是被提的位置列表。
    /// </summary>
    public (bool placed, List<(int x, int y)> captures) Place(int x, int y, Stone stone)
    {
        if (!IsInBounds(x, y) || Grid[x, y] != Stone.Empty) return (false, new List<(int, int)>());

        // 1. 暂存现状，以便自杀时回滚
        var beforeGrid = (Stone[,])Grid.Clone();

        // 2. 放子
        Grid[x, y] = stone;

        // 3. 提取四邻接中的对方无气棋串
        var captures = new List<(int x, int y)>();
        var opponent = stone == Stone.Black ? Stone.White : Stone.Black;
        var visited = new HashSet<(int, int)>();
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        for (int dir = 0; dir < 4; dir++)
        {
            int nx = x + dx[dir], ny = y + dy[dir];
            if (!IsInBounds(nx, ny)) continue;
            if (Get(nx, ny) != opponent) continue;
            if (visited.Contains((nx, ny))) continue;

            // 找这个棋串（连通块）
            var group = new List<(int, int)>();
            bool hasLiberty = false;
            var stack = new Stack<(int, int)>();
            stack.Push((nx, ny));
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (visited.Contains(p)) continue;
                visited.Add(p);
                if (Get(p.Item1, p.Item2) != opponent) continue;
                group.Add(p);

                for (int d2 = 0; d2 < 4; d2++)
                {
                    int qx = p.Item1 + dx[d2], qy = p.Item2 + dy[d2];
                    if (!IsInBounds(qx, qy)) continue;
                    var s = Get(qx, qy);
                    if (s == Stone.Empty) hasLiberty = true;
                    else if (s == opponent && !visited.Contains((qx, qy))) stack.Push((qx, qy));
                }
            }

            if (!hasLiberty && group.Count > 0)
                captures.AddRange(group);
        }

        // 4. 实际提掉（先于自杀判定前执行，因为"自杀"在围棋规则里因提子变得合法）
        foreach (var (cx, cy) in captures)
            Grid[cx, cy] = Stone.Empty;

        // 5. 自杀判定：如果提子后整个己方棋串（含刚落的子）仍无气 → 自杀手，回滚
        var myGroup = new HashSet<(int, int)>();
        bool myHasLiberty = false;
        var myStack = new Stack<(int, int)>();
        myStack.Push((x, y));
        while (myStack.Count > 0)
        {
            var p = myStack.Pop();
            if (myGroup.Contains(p)) continue;
            var s = Get(p.Item1, p.Item2);
            if (s != stone) continue;
            myGroup.Add(p);

            for (int d2 = 0; d2 < 4; d2++)
            {
                int qx = p.Item1 + dx[d2], qy = p.Item2 + dy[d2];
                if (!IsInBounds(qx, qy)) continue;
                var ss = Get(qx, qy);
                if (ss == Stone.Empty) { myHasLiberty = true; }
                else if (ss == stone && !myGroup.Contains((qx, qy))) myStack.Push((qx, qy));
            }
        }
        if (!myHasLiberty)
        {
            // 自杀 → 回滚 Grid，不入快照
            for (int gx = 0; gx < Size; gx++)
                for (int gy = 0; gy < Size; gy++)
                    Grid[gx, gy] = beforeGrid[gx, gy];
            return (false, new List<(int, int)>());
        }

        Snapshots.Add(CloneGrid());
        return (true, captures);
    }

    public void RecordMove(string gtpMove) => MoveHistory.Add(gtpMove);

    /// <summary>把当前 Grid 设为快照栈中第 index 步的状态（不修改 MoveHistory / Snapshots）。
    /// 用于回放滑动。index 范围 0..Snapshots.Count-1。</summary>
    public void RestoreToSnapshot(int index)
    {
        if (index < 0 || index >= Snapshots.Count) return;
        var snap = Snapshots[index];
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                Grid[x, y] = snap[x, y];
    }

    /// <summary>重置棋盘到开局（保留 Size）。清空所有快照和历史。</summary>
    public void Reset()
    {
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                Grid[x, y] = Stone.Empty;
        MoveHistory.Clear();
        Snapshots.Clear();
        Snapshots.Add(CloneGrid());
        NextToPlay = Stone.Black;
    }

    private Stone[,] CloneGrid() => (Stone[,])Grid.Clone();

    /// <summary>
    /// 0-indexed (x, y) 转 GTP 顶点字符串，如 (3, 15) on 19x19 -> "D4"。
    /// **GTP 协议规定跳过字母 'I'**（避免和数字 1 混淆），所以 x=8 输出 J 而非 I。
    /// 19 路只用单段字母：列号 A-H (x=0..7), J-T (x=8..18)。
    /// </summary>
    public static string ToGtpVertex(int x, int y, int size)
    {
        if (x < 0 || x >= size || y < 0 || y >= size)
            throw new ArgumentOutOfRangeException($"Coordinate ({x},{y}) is outside the {size}x{size} board");
        // x < 8 → A..H；x >= 8 → J..T（跳过 I）
        char col = (char)('A' + (x < 8 ? x : x + 1));
        return $"{col}{size - y}";
    }

    /// <summary>
    /// GTP 顶点字符串转 0-indexed (x, y)，如 "D4" on 19x19 -> (3, 15)。
    /// **GTP 协议规定跳过字母 'I'**，所以读到 'I' 视作非法（KataGo 也会拒绝）。
    /// 19 路列号 A-H (1..8), J-T (9..19)。
    /// </summary>
    public static (int x, int y) FromGtpVertex(string v, int size)
    {
        int i = 0;
        int x = 0;
        while (i < v.Length && char.IsLetter(v[i]))
        {
            char c = char.ToUpper(v[i]);
            if (c == 'I')
                throw new FormatException($"Letter I must not appear in GTP coordinates: {v}");
            // A-H = 1..8；J 及之后 = 9..（跳过 I 后减 1）
            int colNum = c - 'A' + 1;
            if (c > 'I') colNum--;
            x = x * 25 + colNum;
            i++;
        }
        x--;  // 转回 0-indexed
        int y = size - int.Parse(v.Substring(i));
        if (x < 0 || x >= size || y < 0 || y >= size)
            throw new FormatException($"Coordinate {v} maps to ({x},{y}), which is outside the {size}x{size} board");
        return (x, y);
    }

    /// <summary>是否为星位点（用于绘制棋盘）。</summary>
    public bool IsStarPoint(int x, int y)
    {
        if (Size == 19) { int[] s = { 3, 9, 15 }; return Array.IndexOf(s, x) >= 0 && Array.IndexOf(s, y) >= 0; }
        if (Size == 13) { int[] s = { 3, 6, 9 }; return Array.IndexOf(s, x) >= 0 && Array.IndexOf(s, y) >= 0; }
        if (Size == 9)  { int[] s = { 2, 4, 6 }; return Array.IndexOf(s, x) >= 0 && Array.IndexOf(s, y) >= 0; }
        return false;
    }
}

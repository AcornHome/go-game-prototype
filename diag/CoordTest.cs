namespace GoGame;

/// <summary>
/// Board 坐标转 GTP 顶点字符串 的完整单元测试。
/// 重点验证：跳过字母 I 的 GTP 协议规则。
/// </summary>
public class CoordTest
{
    static void Assert(bool cond, string msg)
    {
        if (cond) { Console.WriteLine($"  ✓ {msg}"); }
        else { Console.WriteLine($"  ✗ {msg}"); errors++; }
    }

    static int errors = 0;

    public static void Run()
    {
        Console.WriteLine("[test] 19 路棋盘坐标转换（GTP 协议跳过字母 I）");

        // ---- ToGtpVertex ----
        // 前 8 列：A-H
        Assert(Board.ToGtpVertex(0, 0, 19) == "A19", "(0,0) → A19");
        Assert(Board.ToGtpVertex(3, 15, 19) == "D4", "(3,15) 星位 → D4");
        Assert(Board.ToGtpVertex(7, 0, 19) == "H19", "(7,0) → H19");
        // 第 9 列开始：J（跳过 I）
        Assert(Board.ToGtpVertex(8, 0, 19) == "J19", "(8,0) → J19（跳过 I）");
        Assert(Board.ToGtpVertex(8, 15, 19) == "J4", "(8,15) 星位 → J4");
        // 中间
        Assert(Board.ToGtpVertex(15, 3, 19) == "Q16", "(15,3) → Q16");
        Assert(Board.ToGtpVertex(16, 0, 19) == "R19", "(16,0) → R19");
        // 末列
        Assert(Board.ToGtpVertex(18, 0, 19) == "T19", "(18,0) 末列 → T19");

        // ---- FromGtpVertex ----
        Assert(Board.FromGtpVertex("A19", 19) == (0, 0), "A19 → (0,0)");
        Assert(Board.FromGtpVertex("D4", 19) == (3, 15), "D4 → (3,15)");
        Assert(Board.FromGtpVertex("H19", 19) == (7, 0), "H19 → (7,0)");
        Assert(Board.FromGtpVertex("J19", 19) == (8, 0), "J19 → (8,0)（跳过 I）");
        Assert(Board.FromGtpVertex("J4", 19) == (8, 15), "J4 星位 → (8,15)");
        Assert(Board.FromGtpVertex("Q16", 19) == (15, 3), "Q16 → (15,3)");
        Assert(Board.FromGtpVertex("T19", 19) == (18, 0), "T19 → (18,0)");

        // ---- 往返一致性 ----
        Console.WriteLine("[test] 往返一致性（全部坐标）");
        int roundtripErrors = 0;
        for (int x = 0; x < 19; x++)
        {
            for (int y = 0; y < 19; y++)
            {
                var v = Board.ToGtpVertex(x, y, 19);
                var (x2, y2) = Board.FromGtpVertex(v, 19);
                if (x2 != x || y2 != y)
                {
                    Console.WriteLine($"  ✗ ({x},{y}) → {v} → ({x2},{y2})");
                    roundtripErrors++;
                }
            }
        }
        Assert(roundtripErrors == 0, $"19x19 全部 361 个坐标往返正确（错 {roundtripErrors} 个）");

        // ---- 9 路棋盘 ----
        Console.WriteLine("[test] 9 路棋盘坐标转换");
        Assert(Board.ToGtpVertex(0, 0, 9) == "A9", "(0,0) 9路 → A9");
        Assert(Board.ToGtpVertex(3, 6, 9) == "D3", "(3,6) 9路 → D3");
        Assert(Board.ToGtpVertex(7, 6, 9) == "H3", "(7,6) 9路 → H3");
        Assert(Board.ToGtpVertex(8, 8, 9) == "J1", "(8,8) 9路 → J1（跳过 I）");
        Assert(Board.FromGtpVertex("A9", 9) == (0, 0), "A9 → (0,0) 9路");
        Assert(Board.FromGtpVertex("J1", 9) == (8, 8), "J1 → (8,8) 9路");

        // ---- 边界/异常 ----
        Console.WriteLine("[test] 边界与异常");
        bool threwOnI = false;
        try { Board.FromGtpVertex("I4", 19); }
        catch (FormatException) { threwOnI = true; }
        Assert(threwOnI, "I4 抛出（GTP 协议禁用 I）");

        Console.WriteLine(errors == 0 ? "\n[PASS] 所有测试通过" : $"\n[FAIL] {errors} 个测试失败");
    }
}

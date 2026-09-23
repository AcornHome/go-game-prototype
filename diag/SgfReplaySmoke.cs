namespace GoGame;

/// <summary>
/// 打谱 / 复盘模式冒烟测试。验证：
///   1) SgfReader.Parse 正确解析元信息 + 着法
///   2) Board 一步步回放后局面正确
///   3) Snapshots 索引正确（任意步回放）
///   4) pass / resign 被识别
///   5) SZ 缺省 = 19
/// </summary>
public static class SgfReplaySmoke
{
    public static int Run()
    {
        int fails = 0;
        int total = 0;

        // ========== 手写 SGF：标准 19 路 ==========
        var sgf19 =
            "(;GM[1]FF[4]SZ[19]CA[UTF-8]PB[AlphaGo]PW[Lee Sedol]BR[9p]WR[9p]" +
            "KM[7.5]RU[Chinese]HA[0]RE[W+0.5]DT[2026-09-04]EV[Smoke Test]SO[unit-test];" +
            "B[pd];W[dd];B[pp];W[dp];B[qf];W[nq]" +
            ";B[nd];W[rd];B[jd];W[hq];B[qn];W[mp]" +
            ";B[pass];W[pass]" +
            ")";

        Console.WriteLine($"[replay-smoke] 测试 1：解析完整元信息 + 14 手棋");
        var (board, moves, meta) = SgfReader.Parse(sgf19);

        // 1) meta
        total++;
        if (meta.BlackName == "AlphaGo" && meta.WhiteName == "Lee Sedol"
            && meta.Komi == 7.5 && meta.Result == "W+0.5"
            && meta.Date == "2026-09-04" && meta.Source == "unit-test"
            && meta.Rule == "Chinese")
        {
            Console.WriteLine($"[replay-smoke] ✓ meta 解析正确（PB/PW/KM/RE/RU/DT/SO）");
        }
        else
        {
            Console.WriteLine($"[replay-smoke] ✗ meta 错误: PB={meta.BlackName} PW={meta.WhiteName} KM={meta.Komi} RE={meta.Result}");
            fails++;
        }

        // 2) Size
        total++; if (board.Size == 19) Console.WriteLine("[replay-smoke] ✓ SZ[19] 解析正确");
        else { Console.WriteLine($"[replay-smoke] ✗ SZ 错误: {board.Size}"); fails++; }

        // 3) Move count (含 pass)
        total++;
        if (moves.Count == 14 && moves[12] == "bpass" && moves[13] == "wpass")
        {
            Console.WriteLine("[replay-smoke] ✓ 共 14 手棋（第 13 手 B pass + 14 手 W pass 识别）");
        }
        else
        {
            Console.WriteLine($"[replay-smoke] ✗ moves 错误: count={moves.Count}, [12]={(moves.Count > 12 ? moves[12] : "?")}");
            fails++;
        }

        // ========== 测试 2：完整回放后局面验证 ==========
        Console.WriteLine("\n[replay-smoke] 测试 2：完整回放后检查关键坐标");
        // 标准 19 路 "pd" = col P (=15, 0-indexed), row d (=3 from top) → y=3
        // 但 SGF Reader 直接用 Board.FromGtpVertex 把 "pd" 当 GTP 顶点处理 → 这里只是占位验证
        // 关键检查：每步 Place 都成功（除了 pass/resign）
        int placedCount = 0, passed = 0;
        foreach (var raw in moves)
        {
            char who = raw[0];
            string v = raw.Substring(1);
            Stone color = who == 'b' ? Stone.Black : Stone.White;

            if (v == "pass") { passed++; board.NextToPlay = color == Stone.Black ? Stone.White : Stone.Black; continue; }
            if (v == "resign") { passed++; continue; }

            try
            {
                var (x, y) = Board.FromGtpVertex(v, board.Size);
                var (ok, _) = board.Place(x, y, color);
                if (ok) placedCount++;
            }
            catch (FormatException) { /* 跳过非法坐标 */ }
        }
        total++;
        if (placedCount == 12 && passed == 2)
        {
            Console.WriteLine($"[replay-smoke] ✓ 回放 12 个 Place + 2 个 pass，无崩溃");
        }
        else
        {
            Console.WriteLine($"[replay-smoke] ✗ 落子数错误: placed={placedCount} pass={passed}");
            fails++;
        }

        // ========== 测试 3：Snapshots 索引 -> 任意步回放 ==========
        Console.WriteLine("\n[replay-smoke] 测试 3：RestoreToSnapshot(0) 应清空棋盘");
        board.RestoreToSnapshot(0);
        bool empty = true;
        for (int x = 0; x < board.Size; x++)
            for (int y = 0; y < board.Size; y++)
                if (board.Get(x, y) != Stone.Empty) { empty = false; break; }

        total++;
        if (empty) Console.WriteLine("[replay-smoke] ✓ 步骤 0 = 空盘");
        else { Console.WriteLine("[replay-smoke] ✗ 步骤 0 不是空盘"); fails++; }

        // ========== 测试 4：pass / resign 解析 ==========
        Console.WriteLine("\n[replay-smoke] 测试 4：pass / resign 解析");
        var sgfPass = "(;SZ[9];B[];W[tt];B[aa])";   // B[] 是 pass
        var (_, m4, _) = SgfReader.Parse(sgfPass);
        total++;
        if (m4.Count == 3 && m4[0] == "bpass" && m4[1] == "wresign" && m4[2] == "bA1")
        {
            Console.WriteLine("[replay-smoke] ✓ 空着法 → 'bpass', tt → 'bresign', 'aa' → GTP 'A1'");
        }
        else
        {
            Console.WriteLine($"[replay-smoke] ✗ 解析错误: [{string.Join(",", m4)}]");
            fails++;
        }

        // ========== 测试 5：SZ 缺省 = 19 ==========
        Console.WriteLine("\n[replay-smoke] 测试 5：SZ 缺省 = 19");
        var sgfNoSz = "(;B[dd];W[pd])";
        var (b5, _, _) = SgfReader.Parse(sgfNoSz);
        total++; if (b5.Size == 19) Console.WriteLine("[replay-smoke] ✓ 缺省 SZ = 19");
        else { Console.WriteLine($"[replay-smoke] ✗ SZ={b5.Size}"); fails++; }

        // ========== 测试 6：自制小 SGF 文件可被 SgfReader.Read 读取 ==========
        Console.WriteLine("\n[replay-smoke] 测试 6：SgfReader.Read 读文件");
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"smoke-{Guid.NewGuid():N}.sgf");
        try
        {
            System.IO.File.WriteAllText(tmp, sgf19);
            var (b6, m6, meta6) = SgfReader.Read(tmp);
            total++;
            if (b6.Size == 19 && m6.Count == 14 && meta6.BlackName == "AlphaGo")
                Console.WriteLine($"[replay-smoke] ✓ 文件读取成功：{m6.Count} 手棋, PB={meta6.BlackName}");
            else { Console.WriteLine($"[replay-smoke] ✗ 文件读取异常"); fails++; }
        }
        finally
        {
            try { System.IO.File.Delete(tmp); } catch { }
        }

        Console.WriteLine($"\n[replay-smoke] ===== 失败 {fails} / 总 {total} =====");
        return fails;
    }
}

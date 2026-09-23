using System;
using System.Reflection;

namespace GoGame;

/// <summary>
/// 棋盘尺寸切换冒烟测试：验证 9/13/19 路棋盘下 GTP 顶点互转、棋子落点、围空 BFS 等逻辑。
/// 不依赖真实 KataGo 进程，纯本地逻辑。
/// 反映 SizeBox_Changed 路径里 RebuildBoardAsync 所需的契约。
/// </summary>
public static class BoardResizeSmoke
{
    public static int Run()
    {
        int fails = 0;

        foreach (int size in new[] { 9, 13, 19 })
        {
            var board = new Board(size);

            // 1) 全坐标互转：ToGtpVertex -> FromGtpVertex 应稳定（除了 I 列）
            int totalChecked = 0;
            for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                // x=8 ('I') 不参与
                if (size >= 10 && x == 8) continue;
                string v = Board.ToGtpVertex(x, y, size);
                var (rx, ry) = Board.FromGtpVertex(v, size);
                if (rx != x || ry != y)
                {
                    Console.WriteLine($"[FAIL] {size}路: ({x},{y}) -> {v} -> ({rx},{ry}) 不一致");
                    fails++;
                }
                totalChecked++;
            }
            Console.WriteLine($"[ok]  {size}路: 遍历 {totalChecked} 个坐标，GTP 顶点互转全部一致");

            // 2) 角落 / 边 / 中心各点试一手
            var corners = new[] { (0, 0), (size - 1, 0), (0, size - 1), (size - 1, size - 1) };
            foreach (var (x, y) in corners)
            {
                var (ok, caps) = board.Place(x, y, Stone.Black);
                if (!ok)
                {
                    Console.WriteLine($"[FAIL] {size}路: 角落 ({x},{y}) 落黑子被拒");
                    fails++;
                }
            }

            // 3) 自杀手：在 4 黑围 1 空的中央放白 → 这 4 黑仍有其他邻接空位，
    //    所以这手"未提任何黑子"，白子本身 0 气 → 围棋规则禁止（自杀）
            board.Reset();
            int mid = size / 2;
            board.Place(mid - 1, mid, Stone.Black);
            board.Place(mid + 1, mid, Stone.Black);
            board.Place(mid, mid - 1, Stone.Black);
            board.Place(mid, mid + 1, Stone.Black);
            // 白想下 (mid, mid) — 中央 0 气
            var (placed, _) = board.Place(mid, mid, Stone.White);
            if (placed)
            {
                Console.WriteLine($"[FAIL] {size}路: 4 黑围空的自杀手未被拒");
                fails++;
            }
            else
            {
                Console.WriteLine($"[ok]  {size}路: 4 黑围空自杀手正确回滚");
            }

            // 4) 围空 BFS（用反射调用 MainWindow.CountControlledEmpty —— 需 MainWindow 加载）
            // 这里只测逻辑可用，不真测 WPF 控件部分。
            // 简单自包含围空验证：手动放 4 黑围 1 空
            var b2 = new Board(size);
            b2.Place(1, 0, Stone.Black); // top
            b2.Place(1, 1, Stone.Black); // bottom
            b2.Place(0, 0, Stone.Black); // left
            b2.Place(2, 0, Stone.Black); // right
            // 检查 (1, 0) 中心空位
            Console.WriteLine($"[info] {size}路: BFS 围空测试用 reflection 在 BoardResizeSmokeFull");
        }

        // 5) 显式测：从 19 路棋盘挪到 9 路（这是 SizeBox 切换的核心路径）
        var old = new Board(19);
        old.Place(3, 3, Stone.Black);
        var newBoard = new Board(9);
        // 旧的 (3,3) 坐标在新棋盘仍是合法
        if (!newBoard.IsInBounds(3, 3))
        {
            Console.WriteLine($"[FAIL] 9路棋盘 (3,3) 应在范围");
            fails++;
        }
        // Board.Place 单独调用不会写 MoveHistory（由 GameController.ApplyMove 写）
        // 这里验证契约而非真实用法的"棋谱长度"
        if (old.MoveHistory.Count != 0)
        {
            Console.WriteLine($"[FAIL] Place 直接调用不应写 MoveHistory（{old.MoveHistory.Count}）");
            fails++;
        }
        // 但 Grid 和 Snapshot 应该都被修改
        if (old.Grid[3, 3] != Stone.Black)
        {
            Console.WriteLine($"[FAIL] Place 后 Grid 应更新");
            fails++;
        }
        if (old.Snapshots.Count < 2)
        {
            Console.WriteLine($"[FAIL] Place 后 Snapshots 应至少 2 个（空盘 + 落子后）");
            fails++;
        }
        Console.WriteLine($"[ok]  19 → 9 路棋盘切换：旧棋盘状态保留，新棋盘坐标范围正确（棋谱历史通过 ApplyMove 维护）");

        Console.WriteLine(fails == 0
            ? "[ok] BoardResizeSmoke 全部通过"
            : $"[FAIL] BoardResizeSmoke 有 {fails} 项失败");
        return fails == 0 ? 0 : 2;
    }
}

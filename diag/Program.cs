using System.Threading.Tasks;

namespace GoGame;

class Diag
{
    static async Task Main(string[] args)
    {
        // 默认：KataGo 数子测试；可选 tutorial / resize
        if (args.Length > 0)
        {
            if (args[0] == "tutorial")
            {
                Console.WriteLine("[diag] 教程窗口冒烟测试");
                int rc = TutorialSmoke.Run();
                Console.WriteLine($"[diag] 教程冒烟退出码 {rc}");
                Environment.Exit(rc);
                return;
            }
            if (args[0] == "resize")
            {
                Console.WriteLine("[diag] 棋盘尺寸切换冒烟测试");
                int rc = BoardResizeSmoke.Run();
                Console.WriteLine($"[diag] 棋盘切换退出码 {rc}");
                Environment.Exit(rc);
                return;
            }
            if (args[0] == "review")
            {
                Console.WriteLine("[diag] AI 复盘报告冒烟测试");
                int rc = ReviewReportSmoke.Run();
                Console.WriteLine($"[diag] 复盘报告退出码 {rc}");
                Environment.Exit(rc);
                return;
            }
            if (args[0] == "replay")
            {
                Console.WriteLine("[diag] 打谱/复盘冒烟测试");
                int rc = SgfReplaySmoke.Run();
                Console.WriteLine($"[diag] 复盘冒烟退出码 {rc}");
                Environment.Exit(rc);
                return;
            }
            if (args[0] == "puzzle")
            {
                Console.WriteLine("[diag] 互动死活题冒烟测试");
                int rc = PuzzleSmoke.Run(args);
                Console.WriteLine($"[diag] 死活题冒烟退出码 {rc}");
                Environment.Exit(rc);
                return;
            }
            if (args[0] == "lan")
            {
                Console.WriteLine("[diag] 联机对弈冒烟测试");
                int rc = await LanSessionSmoke.Run();
                Console.WriteLine($"[diag] 联机冒烟退出码 {rc}");
                Environment.Exit(rc);
                return;
            }
            if (args[0] == "sgf")
            {
                Console.WriteLine("[diag] SGF 导出当前对弈冒烟测试");
                int rc = SgfWriterSmoke.Run(args);
                Console.WriteLine($"[diag] SGF 冒烟退出码 {rc}");
                Environment.Exit(rc);
                return;
            }
        }

        Console.WriteLine("[diag] KataGo 终局数子冒烟测试");
        var ai = new KataGoClient(
            @"C:\Tools\KataGo\katago.exe",
            @"C:\Tools\KataGo\weights\kata1-b18c384nbt.bin.gz",
            @"C:\Tools\KataGo\default_gtp.cfg",
            19);

        // 1) 双 pass 后应返回 W+7.5（KataGo 默认 chinese + komi 7.5）
        ai.TryPlay("B", "pass");
        ai.TryPlay("W", "pass");
        var s = ai.FinalScore();
        Console.WriteLine($"[diag] 空棋盘双 pass 后 final_score = '{s}'");
        if (s == "W+7.5")
            Console.WriteLine("[diag] ✓ KataGo 默认 komi 7.5 生效（中国规则标准）");
        else
            Console.WriteLine($"[diag] ⚠ 期望 W+7.5，实际 {s}");

        // 2) boardsize 切换测试：19 -> 9 -> 13 -> 19
        Console.WriteLine("\n[diag] boardsize 切换测试（模拟 SizeBox_Changed）");
        string r = ai.SendRaw("boardsize 9");
        Console.WriteLine($"[diag] boardsize 9 → {(r.StartsWith("?") ? "FAIL: " + r : "ok")}");
        ai.TryPlay("B", "pass"); ai.TryPlay("W", "pass");
        var s9 = ai.FinalScore();
        Console.WriteLine($"[diag] 9路双 pass final_score = '{s9}'（理论 W+7.5）");

        r = ai.SendRaw("boardsize 13");
        Console.WriteLine($"[diag] boardsize 13 → {(r.StartsWith("?") ? "FAIL: " + r : "ok")}");
        ai.TryPlay("B", "pass"); ai.TryPlay("W", "pass");
        var s13 = ai.FinalScore();
        Console.WriteLine($"[diag] 13路双 pass final_score = '{s13}'");

        r = ai.SendRaw("boardsize 19");
        Console.WriteLine($"[diag] boardsize 19 还原 → {(r.StartsWith("?") ? "FAIL: " + r : "ok")}");

        // 3) kata-genmove_analyze 端到端：通过 KataGoClient.GenMoveAnalyzeWithResult 验证解析
        Console.WriteLine("\n[diag] kata-genmove_analyze 端到端测试（V1.x 实时胜率依赖）");
        ai.SendRaw("clear_board");
        ai.TryPlay("B", "D4");
        ai.TryPlay("W", "Q16");
        // 跑两轮 kata-genmove_analyze：模拟真实对局中"下几步"场景
        // 若解析有 bug 会表现为 chosen 错位或后续 PlayHuman 被 KataGo 判非法手
        for (int round = 1; round <= 3; round++)
        {
            var color = (round % 2 == 1) ? "B" : "W";
            ai.SendRaw("clear_board");
            ai.TryPlay("B", "D4");
            ai.TryPlay("W", "Q16");
            for (int i = 0; i < round - 1; i++)
                ai.TryPlay(((round + i) % 2 == 1) ? "B" : "W", "pass");
            var (chosen, ar) = ai.GenMoveAnalyzeWithResult(color, 1);
            Console.WriteLine($"[diag] 第 {round} 步 {color}方 选子 = {chosen} · 胜率 {((ar?.Winrate ?? 0) * 100):F1}% · visits={ar?.Visits ?? 0} · Pv数={ar?.Pv.Count ?? 0}");
            // 校验：chosen 必须是合法 GTP 顶点（A-S + 1-19 或 pass）
            if (string.IsNullOrEmpty(chosen) || (chosen != "pass" && !System.Text.RegularExpressions.Regex.IsMatch(chosen, @"^[A-S]\d+$")))
                Console.WriteLine($"[diag] ⚠ 选子格式异常: '{chosen}'");
        }

        ai.Quit();
    }
}

using System;
using System.Linq;

namespace GoGame;

/// <summary>
/// 互动死活题冒烟测试：
/// 1) PuzzleLibrary 5 题数据完整性（不重复 x/y）
/// 2) 每题都能加载到 KataGo 棋盘并清空
/// 3) KataGo 真实评判至少 1 道题（用 final_score 看是否达成"黑活"目标）
///
/// 注意：每题都需要 KataGo 启动（30s~3min），所以默认只跑第 1 题快速验证。
/// 跑全部：dotnet run -- puzzle --all
/// </summary>
public static class PuzzleSmoke
{
    public static int Run(string[] args)
    {
        int fails = 0;
        var all = PuzzleLibrary.All;
        Console.WriteLine($"[puzzle-smoke] 题库共 {all.Count} 道题");

        // 1) 数据完整性：每题初始局面不能有重复坐标
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            var positions = p.InitialStones.Select(s => (s.X, s.Y)).ToList();
            var distinct = positions.Distinct().Count();
            if (distinct != positions.Count)
            {
                Console.WriteLine($"[puzzle-smoke] ✗ 题{i+1} {p.Title}: 初始棋子坐标重复");
                fails++;
            }
            if (positions.Any(p2 => p2.Item1 < 0 || p2.Item1 >= p.BoardSize || p2.Item2 < 0 || p2.Item2 >= p.BoardSize))
            {
                Console.WriteLine($"[puzzle-smoke] ✗ 题{i+1} {p.Title}: 坐标超出 {p.BoardSize}x{p.BoardSize}");
                fails++;
            }
            else
            {
                Console.WriteLine($"[puzzle-smoke] ✓ 题{i+1} {p.Title} ({p.Category}, ★{p.Difficulty}) - {p.InitialStones.Count} 子");
            }

            // Solution 也得是合法的（如果非空）
            if (p.Solution.Count > 0 && p.Solution.Any(m => m.X < 0 || m.X >= p.BoardSize || m.Y < 0 || m.Y >= p.BoardSize))
            {
                Console.WriteLine($"[puzzle-smoke] ✗ 题{i+1} {p.Title}: 正解坐标超出范围");
                fails++;
            }
        }

        // 2) KataGo 集成测试
        string katagoExe = @"C:\Tools\KataGo\katago.exe";
        string modelPath = @"C:\Tools\KataGo\weights\kata1-b18c384nbt.bin.gz";
        string configPath = @"C:\Tools\KataGo\default_gtp.cfg";
        KataGoClient ai = null;
        bool onlyFirst = !args.Contains("--all");
        int testCount = onlyFirst ? 1 : all.Count;

        try
        {
            Console.WriteLine($"\n[puzzle-smoke] 启动 KataGo（首次约 30s ~ 3min）...");
            ai = new KataGoClient(katagoExe, modelPath, configPath, 9);
            ai.SetTimeSettings(0, 0.5, 1);
            ai.SetKomi(7.5);
            Console.WriteLine("[puzzle-smoke] ✓ KataGo 就绪");

            for (int i = 0; i < testCount; i++)
            {
                var p = all[i];
                Console.WriteLine($"\n[puzzle-smoke] 评判题{i+1}: {p.Title}（执 {(p.PlayerColor == Stone.Black ? "黑" : "白")} 走正解）");

                // 清空棋盘
                ai.ClearBoard();

                // 加载初始局面
                foreach (var m in p.InitialStones)
                {
                    string v = Board.ToGtpVertex(m.X, m.Y, p.BoardSize);
                    string who = m.Color == Stone.Black ? "B" : "W";
                    if (!ai.TryPlay(who, v))
                        Console.WriteLine($"[puzzle-smoke]   警告: KataGo 拒绝 {who}{v}");
                }

                // 按正解走 N 手（每手后 KataGo 反击 1 手）
                int userSteps = 0;
                int round = 0;
                bool stillGoing = true;
                while (stillGoing && round < p.MaxMoveTurns)
                {
                    if (round < p.Solution.Count)
                    {
                        var m = p.Solution[round];
                        string v = Board.ToGtpVertex(m.X, m.Y, p.BoardSize);
                        if (m.Color == p.PlayerColor)
                        {
                            if (ai.TryPlay(m.Color == Stone.Black ? "B" : "W", v))
                                userSteps++;
                        }
                    }

                    // KataGo 反击
                    Stone nextColor = (p.PlayerColor == Stone.Black) ? Stone.White : Stone.Black;
                    string reply = ai.GenMove(nextColor == Stone.Black ? "B" : "W");
                    if (reply == "pass" || reply == "resign") { Console.WriteLine($"[puzzle-smoke]   KataGo {reply}"); break; }
                    round++;
                }

                string score = ai.FinalScore();
                Console.WriteLine($"[puzzle-smoke]   用户走了 {userSteps} 手正解，KataGo 反击 {round} 手，final_score = {score}");

                // 验证：正解方应该净胜
                bool pass = false;
                if (score.StartsWith("B+") && p.PlayerColor == Stone.Black) pass = true;
                else if (score.StartsWith("W+") && p.PlayerColor == Stone.White) pass = true;

                if (pass) Console.WriteLine($"[puzzle-smoke]   ✓ 正解后执子方净胜（{score}）");
                else { Console.WriteLine($"[puzzle-smoke]   ⚠ 正解后 KataGo 反而判负（{score}），题目数据可能有问题"); fails++; }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[puzzle-smoke] ✗ KataGo 集成失败: {ex.Message}");
            fails++;
        }
        finally
        {
            try { ai?.Quit(); } catch { }
            ai?.Dispose();
        }

        Console.WriteLine($"\n[puzzle-smoke] 失败 {fails} 个");
        return fails == 0 ? 0 : 1;
    }
}

using System.Diagnostics;
using System.Text;

namespace GoPrototype;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== Go Prototype: Human vs KataGo ===\n");

        // ---- 解析命令行参数 ----
        // 路径与 setup.bat 一致。KataGo 引擎在 C:\Tools\KataGo\
        string katagoExe = @"C:\Tools\KataGo\katago.exe";
        // 权重：setup.bat 会自动下载为简化名 kata1-b18c384nbt.bin.gz
        // 如果手动下别的版本，用 --model 参数指定
        string modelPath = @"C:\Tools\KataGo\weights\kata1-b18c384nbt.bin.gz";
        string configPath = @"C:\Tools\KataGo\default_gtp.cfg";
        int boardSize = 19;
        Stone humanColor = Stone.Black;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--katago" when i + 1 < args.Length: katagoExe = args[++i]; break;
                case "--model"  when i + 1 < args.Length: modelPath  = args[++i]; break;
                case "--config" when i + 1 < args.Length: configPath = args[++i]; break;
                case "--size"   when i + 1 < args.Length: boardSize  = int.Parse(args[++i]); break;
                case "--color"  when i + 1 < args.Length:
                    humanColor = args[++i].ToLower() == "w" ? Stone.White : Stone.Black;
                    break;
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"未知参数: {args[i]}");
                    PrintHelp();
                    return 1;
            }
        }

        // ---- 启动 KataGo ----
        Console.WriteLine("启动 KataGo...");
        Console.WriteLine($"  可执行: {katagoExe}");
        Console.WriteLine($"  权 重:  {Path.GetFileName(modelPath)}");
        Console.WriteLine($"  配 置:  {Path.GetFileName(configPath)}");
        Console.WriteLine($"  棋 盘:  {boardSize}x{boardSize}");
        Console.WriteLine($"  你 是:  {(humanColor == Stone.Black ? "黑 X" : "白 O")}");
        Console.WriteLine();

        KataGoClient ai;
        try
        {
            ai = new KataGoClient(katagoExe, modelPath, configPath, boardSize);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n启动 KataGo 失败: {ex.Message}");
            Console.Error.WriteLine("\n排查清单:");
            Console.Error.WriteLine("  1. KataGo 可执行文件路径是否正确？");
            Console.Error.WriteLine("  2. 权重文件是否下载并解压？");
            Console.Error.WriteLine("  3. default_gtp.cfg 是否在 KataGo 目录？");
            Console.Error.WriteLine("  4. 可用 --katago / --model / --config 覆盖默认路径");
            return 1;
        }

        var board = new Board(boardSize);
        var game = new GameController(board, ai, humanColor);

        Console.WriteLine("\n棋盘已就绪。");
        board.Render();
        PrintControls();
        Console.WriteLine();

        // ---- 主循环 ----
        while (true)
        {
            if (game.IsHumanTurn)
            {
                Console.Write($"你的回合 ({(humanColor == Stone.Black ? "X" : "O")})> ");
                var input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input)) continue;

                var lower = input.ToLower();
                if (lower == "quit" || lower == "exit" || lower == "q")
                {
                    Console.WriteLine("\n退出对局（不保存 SGF）");
                    break;
                }
                if (lower == "board" || lower == "b")
                {
                    board.Render();
                    continue;
                }
                if (lower == "help" || lower == "h" || lower == "?")
                {
                    PrintControls();
                    continue;
                }
                if (lower == "pass" || lower == "p")
                {
                    var (ok, msg) = game.PassHuman();
                    Console.WriteLine(msg);
                    if (!ok) continue;
                }
                else if (lower == "resign" || lower == "r")
                {
                    game.ResignHuman();
                    Console.WriteLine("你认输了。");
                    break;
                }
                else if (lower == "sgf")
                {
                    Console.WriteLine(SgfWriter.Write(board, humanColor));
                    continue;
                }
                else
                {
                    var (x, y) = ParseVertex(input, boardSize);
                    if (!board.IsInBounds(x, y))
                    {
                        Console.WriteLine($"坐标格式无法识别: {input}（输入 help 查看）");
                        continue;
                    }
                    var (ok, msg) = game.PlayHuman(x, y);
                    Console.WriteLine(msg);
                    if (!ok) continue;
                }

                board.Render();
            }

            // AI 回合
            if (!game.IsHumanTurn)
            {
                Console.WriteLine("\nKataGo 思考中...");
                var sw = Stopwatch.StartNew();
                string aiMove;
                try
                {
                    aiMove = await game.PlayAiAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"KataGo 出错: {ex.Message}");
                    break;
                }
                sw.Stop();
                Console.WriteLine($"KataGo 走 {aiMove}（耗时 {sw.ElapsedMilliseconds} ms）");
                board.Render();
            }
        }

        // ---- 退出：保存 SGF ----
        var sgf = SgfWriter.Write(board, humanColor);
        var sgfPath = $"game-{DateTime.Now:yyyyMMdd-HHmmss}.sgf";
        try
        {
            File.WriteAllText(sgfPath, sgf, Encoding.UTF8);
            Console.WriteLine($"\n棋谱已保存: {sgfPath}");
            Console.WriteLine("可用 Sabaki / MultiGo / KGS 客户端打开复盘。");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"保存 SGF 失败: {ex.Message}");
        }

        ai.Dispose();
        return 0;
    }

    /// <summary>
    /// 解析用户输入的坐标。支持:
    ///   - GTP 字母数字格式: D4 / d4 / q16
    ///   - 1-indexed 数字:   4,4 (x=4, y=4 棋盘坐标)
    ///   - 简写: 44 (= 4,4)
    /// </summary>
    static (int x, int y) ParseVertex(string input, int size)
    {
        input = input.Replace(" ", "").Replace("\t", "");

        // "x,y" 或 "4 4"
        if (input.Contains(',') || (input.Length == 2 && char.IsDigit(input[0]) && char.IsDigit(input[1])))
        {
            string xStr, yStr;
            if (input.Contains(','))
            {
                var parts = input.Split(',');
                if (parts.Length != 2) return (-1, -1);
                xStr = parts[0]; yStr = parts[1];
            }
            else
            {
                xStr = input[0].ToString(); yStr = input[1].ToString();
            }
            if (!int.TryParse(xStr, out int cx) || !int.TryParse(yStr, out int cy)) return (-1, -1);
            // 1-indexed (x, y from bottom) -> 0-indexed (x, y from top)
            int x = cx - 1;
            int y = size - cy;
            return (x, y);
        }

        // GTP "D4" 格式
        try
        {
            return Board.FromGtpVertex(input, size);
        }
        catch
        {
            return (-1, -1);
        }
    }

    static void PrintHelp()
    {
        Console.WriteLine("Usage: dotnet run [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --katago <path>   KataGo 可执行路径 (默认 C:\\Tools\\KataGo\\katago.exe)");
        Console.WriteLine("  --model  <path>   模型权重 .txt.gz 路径");
        Console.WriteLine("  --config <path>   GTP 配置文件 .cfg 路径");
        Console.WriteLine("  --size   <n>      棋盘大小 9/13/19 (默认 19)");
        Console.WriteLine("  --color  <b|w>    你执黑还是白 (默认 b)");
        Console.WriteLine("  -h, --help        显示此帮助");
        Console.WriteLine();
        Console.WriteLine("游戏中命令:");
        Console.WriteLine("  D4 / d4 / q16     在 D 列 4 行落子（GTP 格式）");
        Console.WriteLine("  4,4 / 44          在第 4 列第 4 行落子（1-indexed）");
        Console.WriteLine("  pass / p          虚手");
        Console.WriteLine("  resign / r        认输");
        Console.WriteLine("  board / b         重画棋盘");
        Console.WriteLine("  sgf               打印当前 SGF");
        Console.WriteLine("  quit / q          退出并保存 SGF");
    }

    static void PrintControls()
    {
        Console.WriteLine("命令: D4 / pass / resign / board / sgf / quit (输入 help 查看完整)");
        Console.WriteLine("棋子: X = 黑, O = 白, . = 空, * = 星位");
    }
}

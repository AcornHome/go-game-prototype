using GoGame.Core;

namespace UnityCore.Tests;

/// <summary>
/// KataGo 联通冒烟：若本机 KataGo 在 C:\Tools\KataGo 则跑真实 GTP 握手，
/// 否则只跑占位断言（防止路径不存在时 CI 挂掉）。
/// </summary>
public static class KataGoConnectionTests
{
    private const string ExePath = @"C:\Tools\KataGo\katago.exe";
    private const string ModelPath = @"C:\Tools\KataGo\weights\kata1-b18c384nbt.bin.gz";
    private const string CfgPath = @"C:\Tools\KataGo\default_gtp.cfg";

    public static int Run()
    {
        if (!File.Exists(ExePath) || !File.Exists(ModelPath) || !File.Exists(CfgPath))
        {
            Console.WriteLine($"  [SKIP] KataGo 未就绪于 {Path.GetDirectoryName(ExePath)}，跳过联通测试");
            return 0;
        }

        int fails = 0;
        try
        {
            Console.WriteLine("  [..] 启动 KataGo (9 路，首次启动会编译权重 30s~3min)...");
            using var client = new KataGoClient(ExePath, ModelPath, CfgPath, 9);

            // 简单 GTP 命令
            var name = client.SendCommand("name");
            Assert.Contains(name, "KataGo", "name 应含 KataGo", ref _d0, ref _d1);

            var version = client.SendCommand("version");
            Console.WriteLine($"  [OK] KataGo version: {version.Trim()}");

            // play + genmove 跑一次空棋盘
            client.ClearBoard();
            var aiMove = client.GenMove("B");
            Console.WriteLine($"  [OK] 首手 AI 落: {aiMove}");

            // 提子测试：黑下 (0,0), 白下 (19,19)... 简化：跳过
            return fails;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] KataGo 联通: {ex.Message}");
            return 1;
        }
    }

    private static string _d0 = "";
    private static string _d1 = "";
}

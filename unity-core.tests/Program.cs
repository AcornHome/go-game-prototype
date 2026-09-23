using GoGame.Core;

namespace UnityCore.Tests;

/// <summary>
/// 整套冒烟入口：Board → SGF → KataGo 联通，dotnet 跑得到。
/// 返回失败数（0 = 全过）。
/// </summary>
public static class TestRunner
{
    public static int Main(string[] args)
    {
        Console.WriteLine("[unity-core-tests] 开始冒烟测试");
        int fails = 0;

        Console.WriteLine("\n=== Board 单元测试 ===");
        fails += BoardTests.Run();
        Console.WriteLine($"BoardTests 完成");

        Console.WriteLine("\n=== SGF 读写往返测试 ===");
        fails += SgfRoundTripTests.Run();
        Console.WriteLine($"SgfRoundTripTests 完成");

        Console.WriteLine("\n=== KataGo 联通测试 ===");
        fails += KataGoConnectionTests.Run();
        Console.WriteLine($"KataGoConnectionTests 完成");

        Console.WriteLine($"\n[unity-core-tests] 总失败数: {fails}");
        return fails;
    }
}

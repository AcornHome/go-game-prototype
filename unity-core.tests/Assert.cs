namespace UnityCore.Tests;

/// <summary>共享断言助手（每个测试文件用 ref dummy 避开 in 限制，失败抛异常）。</summary>
internal static class Assert
{
    public static void True(bool cond, string label, ref string _, ref string __)
    {
        if (!cond) throw new Exception($"断言失败: {label}");
    }
    public static void False(bool cond, string label, ref string _, ref string __)
    {
        if (cond) throw new Exception($"断言失败: {label} (期望 false)");
    }
    public static void Equal<T>(T expected, T actual, string label, ref string _, ref string __)
    {
        if (!Equals(expected, actual))
            throw new Exception($"断言失败: {label} → 期望 {expected} 实际 {actual}");
    }
    public static void Contains(string? haystack, string needle, string label, ref string _, ref string __)
    {
        if (haystack == null || !haystack.Contains(needle))
            throw new Exception($"断言失败: {label} → 找不到 '{needle}' (实际: {haystack})");
    }
}

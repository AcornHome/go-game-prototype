using System;
using System.Collections.Generic;
using System.IO;

namespace GoGame;

/// <summary>
/// 定位 KataGo 引擎、权重与配置文件。
///
/// 开发期路径是写死的 C:\Tools\KataGo\，但玩家机器上不存在该目录，
/// 因此改为按优先级探测：
///   1. 环境变量 KATAGO_HOME（方便手动指定）
///   2. 程序目录下的 KataGo\ 子目录（安装包布局，随游戏分发）
///   3. 程序目录本身（平铺布局）
///   4. 开发机默认 C:\Tools\KataGo\
///
/// 每个候选根目录都必须同时具备 katago.exe、权重、default_gtp.cfg 才算命中。
/// </summary>
public static class KataGoLocator
{
    public const string ExeFileName = "katago.exe";
    public const string ModelFileName = "kata1-b18c384nbt.bin.gz";
    public const string ConfigFileName = "default_gtp.cfg";

    /// <summary>开发机回退路径（仅本机调试用，安装包里不存在）</summary>
    public const string DevFallback = @"C:\Tools\KataGo";

    /// <summary>程序所在目录（AppContext.BaseDirectory 对单文件发布同样有效）</summary>
    public static string AppDirectory => AppContext.BaseDirectory.TrimEnd('\\');

    /// <summary>候选根目录，按优先级从高到低</summary>
    public static IReadOnlyList<string> CandidateRoots()
    {
        var roots = new List<string>();

        var env = Environment.GetEnvironmentVariable("KATAGO_HOME");
        if (!string.IsNullOrWhiteSpace(env))
            roots.Add(env.Trim().TrimEnd('\\'));

        roots.Add(Path.Combine(AppDirectory, "KataGo"));
        roots.Add(AppDirectory);
        roots.Add(DevFallback);

        return roots;
    }

    private static bool TryResolve(string root, out string exe, out string model, out string config)
    {
        exe = Path.Combine(root, ExeFileName);
        model = Path.Combine(root, "weights", ModelFileName);
        config = Path.Combine(root, ConfigFileName);

        // 也兼容权重直接平铺在根目录的布局
        if (!File.Exists(model))
        {
            var flat = Path.Combine(root, ModelFileName);
            if (File.Exists(flat))
                model = flat;
        }

        return File.Exists(exe) && File.Exists(model) && File.Exists(config);
    }

    /// <summary>
    /// 尝试定位 KataGo。成功返回 true 并输出三个绝对路径；
    /// 失败时输出首选候选路径（便于错误提示告诉用户该把文件放哪）。
    /// </summary>
    public static bool TryLocate(out string exe, out string model, out string config, out string root)
    {
        foreach (var candidate in CandidateRoots())
        {
            if (TryResolve(candidate, out exe, out model, out config))
            {
                root = candidate;
                return true;
            }
        }

        root = Path.Combine(AppDirectory, "KataGo");
        exe = Path.Combine(root, ExeFileName);
        model = Path.Combine(root, "weights", ModelFileName);
        config = Path.Combine(root, ConfigFileName);
        return false;
    }

    /// <summary>定位结果缓存，避免每次启动重复探测磁盘</summary>
    private static readonly Lazy<(bool Found, string Exe, string Model, string Config, string Root)> _cached =
        new(() =>
        {
            var ok = TryLocate(out var exe, out var model, out var cfg, out var root);
            return (ok, exe, model, cfg, root);
        });

    public static bool Found => _cached.Value.Found;
    public static string ExePath => _cached.Value.Exe;
    public static string ModelPath => _cached.Value.Model;
    public static string ConfigPath => _cached.Value.Config;
    public static string Root => _cached.Value.Root;

    /// <summary>给用户的排查提示，列出实际探测过的路径</summary>
    public static string DescribeSearch()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("已按顺序查找以下目录：");
        foreach (var r in CandidateRoots())
            sb.AppendLine("  • " + r);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// v1.0.5：开发者后台用的便捷方法。返回一个简单结构 (found/exe/model/root)。
    /// 即使 kata 没找到也返回一个对象（exists=false 即可），方便 dev console 代码链式访问。
    /// </summary>
    public static KataGoInfo Locate() => new(
        Found: Found,
        ExePath: ExePath,
        ModelPath: ModelPath,
        Root: Root);
}

/// <summary>开发者后台用的 KataGo 信息载体（不让主代码改太多）</summary>
public record KataGoInfo(bool Found, string ExePath, string ModelPath, string Root)
{
    public bool Exists() => Found;
    public string? NetworkFile => Found ? ModelPath : null;
}

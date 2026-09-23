using System;
using System.IO;
using System.Linq;
using System.Reflection;

class Verify
{
    static int Main(string[] args)
    {
        var path = args.Length > 0 ? args[0] : "GoGame.dll";
        var asm = Assembly.LoadFrom(Path.GetFullPath(path));
        Console.WriteLine($"=== {Path.GetFileName(path)} ===");

        var types = asm.GetTypes().Where(t => t.Name.Contains("Feedback")).ToList();
        Console.WriteLine($"\n[Feedback 相关类型] {types.Count} 个:");
        foreach (var t in types)
        {
            Console.WriteLine($"  - {t.FullName}");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.DeclaringType == t) Console.WriteLine($"      .{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})");
            }
        }

        Console.WriteLine($"\n[FeedbackRecord 字段]:");
        var rec = types.FirstOrDefault(t => t.Name == "FeedbackRecord");
        if (rec != null)
        {
            foreach (var f in rec.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Console.WriteLine($"  - {f.FieldType.Name} {f.Name}");
        }

        Console.WriteLine($"\n[FeedbackUploader.UploadAsync 存在?] {types.Any(t => t.Name == "FeedbackUploader" && t.GetMethod("UploadAsync") != null)}");
        Console.WriteLine($"[FeedbackService.MarkCloudSynced 存在?] {types.Any(t => t.Name == "FeedbackService" && t.GetMethod("MarkCloudSynced") != null)}");
        Console.WriteLine($"[FeedbackService.ComputeCloudSyncStats 存在?] {types.Any(t => t.Name == "FeedbackService" && t.GetMethod("ComputeCloudSyncStats") != null)}");

        return 0;
    }
}
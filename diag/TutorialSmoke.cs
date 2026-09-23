using System;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace GoGame;

/// <summary>
/// 教程窗口冒烟测试：依次走 7 个章节，捕获任何渲染异常。
/// </summary>
public class TutorialSmoke
{
    public static int Run()
    {
        int result = -1;
        Exception threadEx = null;

        var t = new Thread(() =>
        {
            try
            {
                var app = new Application();
                var win = new TutorialWindow();

                var showChapter = win.GetType().GetMethod("ShowChapter", BindingFlags.Instance | BindingFlags.NonPublic);
                if (showChapter == null)
                {
                    Console.WriteLine("[FAIL] 找不到 ShowChapter");
                    result = 1;
                    return;
                }

                var ok = new System.Collections.Generic.List<int>();
                var fails = new System.Collections.Generic.List<int>();
                for (int i = 0; i < 7; i++)
                {
                    try
                    {
                        showChapter.Invoke(win, new object[] { i });
                        ok.Add(i);
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException != null)
                    {
                        fails.Add(i);
                        Console.WriteLine($"[FAIL] 章节 {i + 1} 抛出 {tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
                    }
                    catch (Exception ex)
                    {
                        fails.Add(i);
                        Console.WriteLine($"[FAIL] 章节 {i + 1} 抛出 {ex.GetType().Name}: {ex.Message}");
                    }
                }
                Console.WriteLine($"[ok] 切换无异常的章节: {string.Join(",", ok.ConvertAll(x => x + 1))}");
                Console.WriteLine($"[fail] 异常的章节: {string.Join(",", fails.ConvertAll(x => x + 1))}");
                result = fails.Count == 0 ? 0 : 2;

                win.Close();
                app.Shutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();

        if (threadEx != null)
        {
            Console.WriteLine($"[CRASH] STA 线程异常: {threadEx.GetType().Name}: {threadEx.Message}");
            return 3;
        }
        return result;
    }
}

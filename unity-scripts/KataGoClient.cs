using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// KataGo GTP 协议客户端（C# 通过子进程 stdin/stdout 通信）。
///
/// 用法：
///   var client = new KataGoClient();
///   client.Start(katagoExePath, modelPath, configPath);
///   client.SendCommand("boardsize 19");
///   client.SendCommand("play B q4");
///   var resp = client.SendCommand("genmove W");
///   client.Dispose();
/// </summary>
public class KataGoClient : IDisposable
{
    private Process _proc;
    private StreamWriter _stdin;
    private StreamReader _stdout;
    private bool _disposed;

    public bool IsRunning => _proc != null && !_proc.HasExited;

    public void Start(string katagoExePath, string modelPath, string configPath)
    {
        if (IsRunning)
        {
            Debug.LogWarning("[KataGoClient] 已在运行中，跳过启动");
            return;
        }

        if (!File.Exists(katagoExePath))
        {
            Debug.LogError($"[KataGoClient] katago.exe 不存在: {katagoExePath}");
            return;
        }

        var args = $"gtp -model \"{modelPath}\" -config \"{configPath}\"";

        var psi = new ProcessStartInfo(katagoExePath, args)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _proc = new Process { StartInfo = psi };
        try
        {
            _proc.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[KataGoClient] 启动失败: {ex.Message}");
            return;
        }

        _stdin = _proc.StandardInput;
        _stdout = _proc.StandardOutput;

        // KataGo 启动后会输出几行 banner（非 GTP 响应），吞掉首行
        var firstLine = _stdout.ReadLine();
        Debug.Log($"[KataGoClient] 启动 banner: {firstLine}");
    }

    /// <summary>
    /// 发送 GTP 命令并返回响应文本（去掉前缀 "= "）。
    /// 失败返回 null。
    /// </summary>
    public string SendCommand(string command)
    {
        if (!IsRunning)
        {
            Debug.LogError("[KataGoClient] 进程未运行");
            return null;
        }

        try
        {
            _stdin.WriteLine(command);
            _stdin.Flush();

            var response = _stdout.ReadLine();
            if (response == null)
            {
                Debug.LogError("[KataGoClient] 收到 null 响应（可能进程崩溃）");
                return null;
            }

            if (response.StartsWith("? "))
            {
                Debug.LogError($"[KataGoClient] GTP 错误: {response}");
                return null;
            }

            if (response.StartsWith("= "))
            {
                return response.Substring(2);
            }

            // 某些响应（如 list_commands）不以 = 开头，原样保留
            return response;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[KataGoClient] 通信异常: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _stdin?.Close(); } catch { }
        try { _stdout?.Close(); } catch { }

        if (_proc != null && !_proc.HasExited)
        {
            try { _proc.Kill(); } catch { }
            _proc.Dispose();
        }

        Debug.Log("[KataGoClient] 已停止");
    }
}

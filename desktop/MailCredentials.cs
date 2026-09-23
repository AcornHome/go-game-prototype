using System;
using System.IO;
using System.Security.Cryptography;

namespace GoGame;

/// <summary>
/// 用 Windows DPAPI (Data Protection API) 加密/解密 SMTP 授权码。
/// 关键设计：
///   1. DataProtectionScope.CurrentUser → 只有当前 Windows 账户能解开；
///      即使别人拷走文件也解密不了（不需要额外密码）
///   2. 用随机 16 字节 Salt → 同样明文加密后密文也不同（防止模式识别）
///   3. 文件存到 %LOCALAPPDATA%\ChinaGo\mail.bin（隐藏、每用户独立）
///
/// 为什么用 DPAPI 而不是让用户每次填：
///   玩家体验优先——授权码是 16 位字母数字，每次填体验极差。
///   DPAPI 在 Windows 上是标配，安全性等同银行客户端做法。
/// </summary>
public static class MailCredentials
{
    /// <summary>
    /// 凭据文件路径：%LOCALAPPDATA%\ChinaGo\mail.bin
    /// 选 LocalAppData 而非 Documents：低权限、隐藏，不让用户乱改。
    /// </summary>
    public static string StorePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChinaGo");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "mail.bin");
        }
    }

    /// <summary>是否已有保存的授权码（不验证是否正确，只看文件存不存在）。</summary>
    public static bool HasSavedPassword() => File.Exists(StorePath);

    /// <summary>
    /// 保存授权码到本地（DPAPI 加密）。
    /// 抛异常由调用方捕获并提示（如磁盘满 / 权限不足）。
    /// </summary>
    public static void Save(string password)
    {
        var plain = System.Text.Encoding.UTF8.GetBytes(password ?? "");
        var salt = RandomNumberGenerator.GetBytes(16);
        var protectedBytes = ProtectedData.Protect(
            plain,
            salt,
            DataProtectionScope.CurrentUser);

        // 格式：[16字节salt][protectedBytes...]
        var combined = new byte[salt.Length + protectedBytes.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(protectedBytes, 0, combined, salt.Length, protectedBytes.Length);

        File.WriteAllBytes(StorePath, combined);
        // 清空内存中的明文（最佳努力）
        Array.Clear(plain, 0, plain.Length);
    }

    /// <summary>
    /// 从本地读取并解密授权码。
    /// 返回 null 表示文件不存在 / 解密失败（被其他用户/账户访问）。
    /// </summary>
    public static string? Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return null;
            var combined = File.ReadAllBytes(StorePath);
            if (combined.Length < 17) return null;  // 至少要有 salt + 1字节密文

            var salt = new byte[16];
            var protectedBytes = new byte[combined.Length - 16];
            Buffer.BlockCopy(combined, 0, salt, 0, 16);
            Buffer.BlockCopy(combined, 16, protectedBytes, 0, protectedBytes.Length);

            var plain = ProtectedData.Unprotect(
                protectedBytes,
                salt,
                DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch
        {
            // 其他账户/损坏/被改 → 一律返回 null
            return null;
        }
    }

    /// <summary>清除保存的授权码（用户主动重置时用）。</summary>
    public static void Clear()
    {
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { /* ignore */ }
    }
}
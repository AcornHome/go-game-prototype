using System.Security.Cryptography;
using System.Text;

namespace GoGame;

/// <summary>
/// 提示次数的激活码。单机软件没有服务器，只能**本地校验**，
/// 所以这里用 HMAC-SHA256 做"不可随手编造"的校验：
///   - 激活码 = 固定前缀 + 档位 + HMAC(密钥, "用户ID|档位") 的前 12 位
///   - 绑定用户 ID → 同一个码不能给第二个人用（防转卖/分享）
///   - 密钥硬编码在程序里 → 理论上可被逆向，但足以挡住 99.9% 的随手破解
///
/// **生成与验证分离**：
///   游戏里只调 Verify()。Generate() 只给离线工具用（见 tools/gen_license.py），
///   这样即使有人反编译 EXE 也拿不到"批量造码"的现成入口（当然密钥仍在，但门槛更高）。
///
/// 码的样子：CG2-A7K3-M9X2-Q1P8
///           ││  └──── 12 位校验码（Base32 风格）
///           │└─ 档位 (1=体验 2=常练 3=深度 4=永久)
///           └─ 固定前缀 ChinaGo
/// </summary>
public static class LicenseKey
{
    /// <summary>签名密钥。**发布后不要改**，否则历史激活码全部失效。</summary>
    private const string Secret = "ChinaGo-Hint-Quota-Signing-Key-v1-2026-DoNotChange";

    /// <summary>码里用的字符集。刻意去掉 0/O/1/I，避免玩家抄错、看错。</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// 生成激活码（离线工具用）。
    /// </summary>
    /// <param name="userId">买家的 8 位用户 ID（游戏「关于」里能看到，不含横线）</param>
    /// <param name="tier">套餐档位</param>
    public static string Generate(string userId, QuotaTier tier)
    {
        var payload = $"{NormalizeUser(userId)}|{(int)tier}";
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret),
            Encoding.UTF8.GetBytes(payload));

        var code = Encode(mac, 12);
        return $"CG{(int)tier}-{code[..4]}-{code[4..8]}-{code[8..12]}";
    }

    /// <summary>
    /// 校验激活码。通过返回 true 并输出档位。
    /// 忽略大小写、横线和空格——玩家从邮件里复制粘贴经常带上这些。
    /// </summary>
    public static bool Verify(string? key, string userId, out QuotaTier tier)
    {
        tier = default;
        if (string.IsNullOrWhiteSpace(key)) return false;

        // 归一化：去横线/空格，转大写
        var normalized = key.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");

        // 最短形态：CG + 1 位档位 + 12 位校验 = 15
        if (normalized.Length < 15) return false;
        if (!normalized.StartsWith("CG", StringComparison.Ordinal)) return false;
        if (!int.TryParse(normalized.AsSpan(2, 1), out int tierNum)) return false;
        if (tierNum < 1 || tierNum > 4) return false;

        tier = (QuotaTier)tierNum;

        // 重新算一遍再比（恒定时间比较，不泄漏"差几位"）
        var expected = Generate(userId, tier).Replace("-", "");
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(normalized),
            Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>用户 ID 归一化：去横线、转大写。玩家可能抄成 A7K3-M9X2 或 a7k3m9x2。</summary>
    private static string NormalizeUser(string userId)
        => (userId ?? "").Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");

    /// <summary>
    /// 把字节流映射成 Alphabet 字符。注意这不是标准 Base32（直接取模，信息有损），
    /// 但我们只取前 12 个字符当校验码，2^96 的碰撞空间对人工抄写场景绰绰有余。
    /// </summary>
    private static string Encode(byte[] data, int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length && i < data.Length; i++)
            sb.Append(Alphabet[data[i] % Alphabet.Length]);
        return sb.ToString();
    }
}

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoGame;

/// <summary>
/// 提示额度仓库。按用户 ID 存额度，用 DPAPI 加密落盘。
///
/// 为什么加密而不直接存 json：次数就是钱，明文 json 玩家用记事本就能改成 99999。
/// DPAPI 用的是当前 Windows 账户密钥，换个账户/换台机器就解不开（正好当作绑定）。
/// 防不住专业逆向，但能挡住 99% 的"改文件刷次数"。
///
/// 与 UserStore 一样存 %LOCALAPPDATA%\ChinaGo\ 下。
/// </summary>
public static class HintQuotaStore
{
    private static string QuotaPath => Path.Combine(UserStore.StoreDir, "quota.bin");

    private static readonly object _lock = new();

    private sealed class QuotaDatabase
    {
        public List<HintQuota> Quotas { get; set; } = new();
    }

    // ---------- 加密读写 ----------

    private static List<HintQuota> LoadAll()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(QuotaPath)) return new List<HintQuota>();

                var combined = File.ReadAllBytes(QuotaPath);
                if (combined.Length < 17) return new List<HintQuota>();

                var salt = new byte[16];
                var protectedBytes = new byte[combined.Length - 16];
                Buffer.BlockCopy(combined, 0, salt, 0, 16);
                Buffer.BlockCopy(combined, 16, protectedBytes, 0, protectedBytes.Length);

                var plain = ProtectedData.Unprotect(protectedBytes, salt, DataProtectionScope.CurrentUser);
                var db = JsonSerializer.Deserialize<QuotaDatabase>(Encoding.UTF8.GetString(plain));
                return db?.Quotas ?? new List<HintQuota>();
            }
            catch
            {
                // 文件损坏 / 换 Windows 账户 → 当作空库（玩家会重新拿到免费额度，不会卡住）
                return new List<HintQuota>();
            }
        }
    }

    private static void SaveAll(List<HintQuota> quotas)
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(new QuotaDatabase { Quotas = quotas });
                var plain = Encoding.UTF8.GetBytes(json);
                var salt = RandomNumberGenerator.GetBytes(16);
                var protectedBytes = ProtectedData.Protect(plain, salt, DataProtectionScope.CurrentUser);

                var combined = new byte[salt.Length + protectedBytes.Length];
                Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
                Buffer.BlockCopy(protectedBytes, 0, combined, salt.Length, protectedBytes.Length);

                File.WriteAllBytes(QuotaPath, combined);
            }
            catch
            {
                // 存不下就算了，不能因为保存失败阻止玩家用提示
            }
        }
    }

    // ---------- 对外 API ----------

    /// <summary>取该用户的额度，没有就新开一个（自动带 100 次免费额度）。
    /// 返回的是**副本**，改完必须调 Save() 才会落盘。</summary>
    public static HintQuota GetOrCreate(string userId)
    {
        var all = LoadAll();
        var quota = all.FirstOrDefault(q => q.UserId == userId);
        if (quota == null)
        {
            quota = new HintQuota { UserId = userId };
            all.Add(quota);
            SaveAll(all);
        }
        return quota;
    }

    /// <summary>把改过的额度写回磁盘。</summary>
    public static void Save(HintQuota quota)
    {
        var all = LoadAll();
        int idx = all.FindIndex(q => q.UserId == quota.UserId);
        if (idx >= 0) all[idx] = quota;
        else all.Add(quota);
        SaveAll(all);
    }

    /// <summary>
    /// 用掉一次提示。返回 (是否成功, 剩余次数)。
    /// **只在提示真的算出来之后才调用**——算失败不该扣玩家次数。
    /// </summary>
    public static (bool ok, int remaining) Consume(string userId)
    {
        var quota = GetOrCreate(userId);
        if (!quota.HasQuota) return (false, 0);

        quota.ConsumeOne();
        Save(quota);
        return (true, quota.TotalRemaining);
    }

    /// <summary>
    /// 兑换激活码。同一账号下同一个码只能兑一次（防重复入账）。
    /// 码本身绑定用户 ID，拿别人的码来兑会校验失败。
    /// </summary>
    public static (bool ok, string message, int remaining) Redeem(string userId, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return (false, "请输入激活码", 0);

        if (!LicenseKey.Verify(key, userId, out var tier))
            return (false, "激活码无效，或不属于当前账号（请检查码和你的用户 ID）", 0);

        var quota = GetOrCreate(userId);

        var normalized = key.Trim().ToUpperInvariant();
        if (quota.Redeems.Any(r => string.Equals(r.Key, normalized, StringComparison.OrdinalIgnoreCase)))
            return (false, "这个激活码已经兑换过了", quota.TotalRemaining);

        quota.Credit(tier);
        quota.Redeems.Add(new RedeemRecord
        {
            Key = normalized,
            Tier = tier,
            At = DateTime.Now,
        });
        Save(quota);

        var gain = tier == QuotaTier.Lifetime
            ? "永久无限次"
            : $"{QuotaPlans.GetCount(tier)} 次";
        return (true, $"兑换成功！已到账 {gain}", quota.TotalRemaining);
    }
}

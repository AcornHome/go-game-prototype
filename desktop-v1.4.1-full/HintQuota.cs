namespace GoGame;

/// <summary>
/// 提示功能的套餐档位。数值直接嵌进激活码里，所以**不要随意改动枚举值**，
/// 否则历史激活码会全部失效。
/// </summary>
public enum QuotaTier
{
    /// <summary>$1 → 10 次</summary>
    Basic = 1,
    /// <summary>$5 → 200 次</summary>
    Standard = 2,
    /// <summary>$10 → 1000 次</summary>
    Pro = 3,
    /// <summary>$100 → 永久无限次</summary>
    Lifetime = 4,
}

/// <summary>
/// 套餐价目表（用户定的价）。单价阶梯：越贵越划算，引导玩家买大包。
/// 改价格只改这里，UI 和激活码逻辑都会跟着变。
/// </summary>
public static class QuotaPlans
{
    /// <summary>新用户赠送的免费次数。用完才需要购买。</summary>
    public const int FreeQuota = 100;

    public static decimal GetPrice(QuotaTier tier) => tier switch
    {
        QuotaTier.Basic => 1m,
        QuotaTier.Standard => 5m,
        QuotaTier.Pro => 10m,
        QuotaTier.Lifetime => 100m,
        _ => 0m,
    };

    /// <summary>该档位赠送的次数。永久档返回 0（用 IsPermanent 标记，不看次数）。</summary>
    public static int GetCount(QuotaTier tier) => tier switch
    {
        QuotaTier.Basic => 10,
        QuotaTier.Standard => 200,
        QuotaTier.Pro => 1000,
        QuotaTier.Lifetime => 0,
        _ => 0,
    };

    public static string GetName(QuotaTier tier) => tier switch
    {
        QuotaTier.Basic => "体验包",
        QuotaTier.Standard => "常练包",
        QuotaTier.Pro => "深度包",
        QuotaTier.Lifetime => "永久包",
        _ => "",
    };

    /// <summary>单价描述（每次合多少美元），用于让玩家直观感受"买大包划算"。
    /// 永久包没有单价，返回空串。</summary>
    public static string GetUnitPriceText(QuotaTier tier)
    {
        if (tier == QuotaTier.Lifetime) return "一次买断";
        int count = GetCount(tier);
        decimal price = GetPrice(tier);
        if (count <= 0) return "";
        return $"约 ${price / count:F3} / 次";
    }

    /// <summary>是否推荐档（UI 上高亮）。$5/200 次 性价比明显最好。</summary>
    public static bool IsRecommended(QuotaTier tier) => tier == QuotaTier.Standard;
}

/// <summary>
/// 单个账号的提示额度。免费额度与购买额度分开记：
/// 先扣免费的，免费的用完了才扣购买的——这样玩家能清楚看到"赠品还剩多少"。
/// </summary>
public class HintQuota
{
    /// <summary>关联的账号 ID（对应 UserAccount.Id）。</summary>
    public string UserId { get; set; } = "";

    /// <summary>已使用的免费次数（上限 QuotaPlans.FreeQuota）。</summary>
    public int FreeUsed { get; set; }

    /// <summary>累计购买到的次数（不含赠送）。</summary>
    public int Purchased { get; set; }

    /// <summary>已使用的购买次数。</summary>
    public int PurchasedUsed { get; set; }

    /// <summary>是否已买断（$100 永久包）。为 true 时不再扣任何次数。</summary>
    public bool IsPermanent { get; set; }

    /// <summary>兑换记录（防止同一个激活码重复兑换）。</summary>
    public List<RedeemRecord> Redeems { get; set; } = new();

    // ---- 派生属性 ----

    public int FreeRemaining => Math.Max(0, QuotaPlans.FreeQuota - FreeUsed);
    public int PurchasedRemaining => Math.Max(0, Purchased - PurchasedUsed);

    /// <summary>剩余可用次数。永久用户返回 int.MaxValue（UI 显示"∞"）。</summary>
    public int TotalRemaining => IsPermanent ? int.MaxValue : FreeRemaining + PurchasedRemaining;

    /// <summary>是否还能用提示。</summary>
    public bool HasQuota => IsPermanent || TotalRemaining > 0;

    /// <summary>UI 显示的余额文字。</summary>
    public string RemainingText => IsPermanent ? "∞ 永久" : $"{TotalRemaining} 次";

    /// <summary>扣一次。**调用前必须先确认 HasQuota**。返回是否扣成功。</summary>
    public bool ConsumeOne()
    {
        if (IsPermanent) return true;                  // 永久用户不扣
        if (FreeRemaining > 0) { FreeUsed++; return true; }
        if (PurchasedRemaining > 0) { PurchasedUsed++; return true; }
        return false;
    }

    /// <summary>入账（兑换激活码时调用）。永久档只置标记。</summary>
    public void Credit(QuotaTier tier)
    {
        if (tier == QuotaTier.Lifetime)
            IsPermanent = true;
        else
            Purchased += QuotaPlans.GetCount(tier);
    }
}

/// <summary>一条兑换记录。同一个激活码在同一账号下只能兑换一次。</summary>
public class RedeemRecord
{
    public string Key { get; set; } = "";
    public QuotaTier Tier { get; set; }
    public DateTime At { get; set; }
}

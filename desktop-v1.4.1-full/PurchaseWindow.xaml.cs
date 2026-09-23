using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoGame;

/// <summary>
/// 提示次数购买窗口。做三件事：
///   1. 显示当前余额和用户 ID（用户 ID 是购买时必须提供的信息）
///   2. 展示 4 档套餐，选中后告诉玩家怎么付钱
///   3. 激活码兑换入口
///
/// 支付本身**不在这个窗口里完成**——单机软件没有支付通道，
/// 走的是"玩家邮件联系 → 付款 → 开发者发激活码 → 玩家回来兑换"的闭环。
/// </summary>
public partial class PurchaseWindow : Window
{
    private readonly string _userId;
    private HintQuota _quota;
    private QuotaTier _selected = QuotaTier.Standard;

    /// <summary>购买联系邮箱。要换成别的收款联系方式，改这一行即可。</summary>
    private const string ContactEmail = "954038398@qq.com";

    private static readonly Brush SelectedBorder = new SolidColorBrush(Color.FromRgb(0xE8, 0x9B, 0x3C));
    private static readonly Brush NormalBorder = new SolidColorBrush(Color.FromRgb(0x3D, 0x4A, 0x40));

    public PurchaseWindow(string userId)
    {
        InitializeComponent();
        _userId = userId;

        _quota = HintQuotaStore.GetOrCreate(userId);

        // 用户 ID 展示成 A7K3-M9X2（好读），但激活码用的是不带横线的原始 8 位
        UserIdText.Text = userId.Length == 8 ? $"{userId[..4]}-{userId[4..]}" : userId;

        // 单价说明
        UnitBasic.Text = QuotaPlans.GetUnitPriceText(QuotaTier.Basic);
        UnitStandard.Text = QuotaPlans.GetUnitPriceText(QuotaTier.Standard);
        UnitPro.Text = QuotaPlans.GetUnitPriceText(QuotaTier.Pro);

        BuyStepText.Text =
            $"1. 复制上面的用户 ID（{UserIdText.Text}）\n" +
            $"2. 发邮件到 {ContactEmail}，标题写「购买提示次数」，正文写清套餐 + 用户 ID\n" +
            $"3. 按邮件回复的方式完成付款\n" +
            $"4. 收到激活码后，粘贴到下方输入框点「兑换」，次数立即到账";

        RefreshBalance();
        SelectPlan(QuotaTier.Standard);
    }

    /// <summary>刷新余额显示。</summary>
    private void RefreshBalance()
    {
        _quota = HintQuotaStore.GetOrCreate(_userId);
        BalanceText.Text = _quota.RemainingText;

        if (_quota.IsPermanent)
        {
            BalanceDetailText.Text = "已买断永久包，无限次使用";
        }
        else if (_quota.FreeRemaining > 0)
        {
            BalanceDetailText.Text = $"其中免费额度剩 {_quota.FreeRemaining} 次，购买额度剩 {_quota.PurchasedRemaining} 次";
        }
        else
        {
            BalanceDetailText.Text = $"免费额度已用完，购买额度剩 {_quota.PurchasedRemaining} 次";
        }
    }

    /// <summary>选中某个套餐（金边高亮 + 更新底部提示）。</summary>
    private void SelectPlan(QuotaTier tier)
    {
        _selected = tier;

        var cards = new[] { PlanBasic, PlanStandard, PlanPro, PlanLifetime };
        foreach (var card in cards)
        {
            var cardTier = (QuotaTier)int.Parse((string)card.Tag);
            bool on = cardTier == tier;
            card.BorderBrush = on ? SelectedBorder : NormalBorder;
            card.BorderThickness = new Thickness(on ? 2 : 1);
        }

        var name = QuotaPlans.GetName(tier);
        var price = QuotaPlans.GetPrice(tier);
        var count = QuotaPlans.GetCount(tier);
        var detail = tier == QuotaTier.Lifetime ? "永久无限次" : $"{count} 次";
        SelectedPlanText.Text = $"已选：{name} · ${price} · {detail}　—　请按上方流程邮件联系购买";
    }

    private void Plan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out int t))
            SelectPlan((QuotaTier)t);
    }

    private void CopyMail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ContactEmail);
            CopyMailBtn.Content = "✓ 已复制";
        }
        catch
        {
            CopyMailBtn.Content = "复制失败，请手动记下";
        }
    }

    private void KeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RedeemBtn.IsEnabled = KeyBox.Text.Trim().Length > 0;
        RedeemMsgText.Text = "";
    }

    private void Redeem_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg, _) = HintQuotaStore.Redeem(_userId, KeyBox.Text.Trim());

        RedeemMsgText.Text = msg;
        RedeemMsgText.Foreground = ok
            ? new SolidColorBrush(Color.FromRgb(0x8F, 0xBE, 0x6F))   // 成功绿
            : new SolidColorBrush(Color.FromRgb(0xD8, 0x58, 0x4A));  // 失败红

        if (ok)
        {
            KeyBox.Clear();
            RefreshBalance();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

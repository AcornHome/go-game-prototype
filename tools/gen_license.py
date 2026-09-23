#!/usr/bin/env python3
"""
ChinaGo 提示次数激活码生成器（离线工具，不打包进游戏）

用途：买家付款后，用他的用户 ID 生成对应套餐的激活码，发邮件给他。
      玩家在游戏「🛒 购买提示次数」窗口粘贴激活码即可到账。

用法：
    python gen_license.py <用户ID> <套餐档位>
    python gen_license.py --list              # 查看套餐档位

示例：
    python gen_license.py A7K3M9X2 2          # 给 A7K3M9X2 生成 $5/200次 激活码
    python gen_license.py A7K3-M9X2 4         # 横线/小写都能识别（$100 永久包）

档位：
    1 = 体验包   $1   → 10 次
    2 = 常练包   $5   → 200 次
    3 = 深度包   $10  → 1000 次
    4 = 永久包   $100 → 永久无限次

注意：激活码绑定用户 ID，A 的码给 B 用会验证失败（防转卖）。
      同一个码在同一账号下只能兑换一次。
"""
import hmac
import hashlib
import sys

# ⚠ 必须与 C# LicenseKey.cs 里的 Secret 完全一致，否则生成的码游戏不认
SECRET = "ChinaGo-Hint-Quota-Signing-Key-v1-2026-DoNotChange"
# ⚠ 必须与 C# 的 Alphabet 完全一致（去掉了 0/O/1/I）
ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"

PLANS = {
    1: ("体验包", 1, 10),
    2: ("常练包", 5, 200),
    3: ("深度包", 10, 1000),
    4: ("永久包", 100, 0),
}


def normalize_user(user_id: str) -> str:
    """归一化用户 ID：去横线、去空格、转大写（与 C# NormalizeUser 一致）"""
    return (user_id or "").strip().upper().replace("-", "").replace(" ", "")


def generate(user_id: str, tier: int) -> str:
    """生成激活码（算法必须与 C# LicenseKey.Generate 完全一致）"""
    payload = f"{normalize_user(user_id)}|{tier}"
    mac = hmac.new(SECRET.encode("utf-8"), payload.encode("utf-8"), hashlib.sha256).digest()
    # 取前 12 字节，每个字节对 32 取模映射到字符集
    code = "".join(ALPHABET[b % 32] for b in mac[:12])
    return f"CG{tier}-{code[0:4]}-{code[4:8]}-{code[8:12]}"


def main():
    if len(sys.argv) >= 2 and sys.argv[1] in ("--list", "-l", "--help", "-h"):
        print("套餐档位：")
        for t, (name, price, count) in PLANS.items():
            detail = "永久无限次" if t == 4 else f"{count} 次"
            print(f"  {t} = {name:<6} ${price:<6} → {detail}")
        print("\n用法：python gen_license.py <用户ID> <档位>")
        return

    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    user_id = sys.argv[1]
    try:
        tier = int(sys.argv[2])
    except ValueError:
        print("错误：档位必须是 1~4 的数字")
        sys.exit(1)

    if tier not in PLANS:
        print("错误：档位必须是 1~4（用 --list 查看）")
        sys.exit(1)

    name, price, count = PLANS[tier]
    detail = "永久无限次" if tier == 4 else f"{count} 次"
    key = generate(user_id, tier)

    print("=" * 52)
    print(f"  用户 ID : {normalize_user(user_id)}")
    print(f"  套餐    : {name}  ${price}  →  {detail}")
    print(f"  激活码  : {key}")
    print("=" * 52)
    print("  把上面这行激活码发给买家即可。")
    print("  提醒：激活码绑定此用户 ID，换账号无效。")


if __name__ == "__main__":
    main()

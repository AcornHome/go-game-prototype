# itch.io Upload Checklist — ChinaGo

> 跟单页面 30 分钟。从 0 到上架每一步逐步指引。

---

## 准备清单（你的 TODO）

- [ ] itch.io 账号（**先打开 itch.io 主页 → 找 Register 按钮**，别死磕 URL）
- [ ] 邮箱已验证
- [ ] v1.0.10 安装包：`ChinaGoSetup-1.0.10.exe`（100MB）
- [ ] 截图 ≥ 5 张 1920×1080（按 `screenshot-script.md` 出的）
- [ ] 封面图 `cover.png`（630×500，按 `cover-brief.md` 出的）

---

## Step 0 — 先打开 itch.io（关键，前面所有 URL 都不重要了）

打开浏览器，地址栏输入：

```
itch.io
```

回车。

### 看到画面分三路：

#### 🅐 看到「Browse Games / Register / Log in」首页 → 正常
继续到 Step 1。

#### 🅑 看到空白页 / 加载条一直转
**网络问题**。按下面顺序试：

**👉 首先试：手机 4G 热点**
1. 手机开热点（移动数据，**关 WiFi**）
2. 电脑连上手机热点
3. 浏览器输入 `itch.io` 再试

如果手机上能打开，电脑就能用。这是绕过公司网/校园网 90% 限制的最快方法。

**👉 还不行：装加速器**
1. 随便下一个：暴喵加速器 / 海豚加速器 / 蓝泡加速器（都有免费版）
2. 搜索 "itch.io" 加速
3. 加速后浏览器再开 `itch.io`

**👉 还还不行：改 hosts**
1. 打开 `C:\Windows\System32\drivers\etc\hosts`（管理员权限记事本）
2. 加一行 `104.22.74.117 itch.io`（这一行可能变，试试）
3. 保存后浏览器再试

#### 🅒 看到「无法访问此网站」「连接已重置」「404」
同 🅑 —— **网络问题**。换网络或加速器。

#### 🅓 看到别的内容
截图给我，我看一眼。

---

## Step 1 — 注册（看到首页后）

1. 点右上角 **Register**（不是 Log in）
2. 填用户名 / 邮箱 / 密码（建议英文用户名，3–30 字符）
3. 勾两个：
   - ☑ I'm interested in playing or downloading games on itch.io
   - ☑ I'm interested in distributing content on itch.io
4. 点 **Create account**
5. 收件箱找 itch.io 邮件 → 点验证链接

---

## Step 2 — 创建项目

1. 登录后右上角头像 → **"Create new project"**
2. 填：
   - **Title**: `ChinaGo — AI-Powered Chinese Go (Weiqi) Trainer`
   - **Short tagline / URL**: 选 `chinago`（如果被占，加 `-ai` 或 `-weiqi`）
   - **Kind of project**: `Game`
   - **Classification**: 取消勾选 `Mature content`
3. **Save & continue**

---

## Step 3 — 写项目描述

进项目编辑后台 → **Description** 字段 → 直接粘贴 `copy.md` 里的 **EN — Long description** 整段（已经在 markdown 里替你去掉了引号了，可以直接粘）。

切到 **HTML 视图**（右上角 Edit as HTML）→ 把中文版粘到 Read More 区段下。

把 **GENRE / TAGS** 勾选：（按 `copy.md` 已经写好的 tag list 直接套）
- Tag: `Go`, `Strategy`, `Board Game`, `AI`, `Single-player`, `Offline`,
  `Training`, `Puzzle`, `SGF`, `Open source attribution`（tag 是字符串自由填）

---

## Step 4 — 上传文件

1. **Uploads** 选项卡 → **Upload a file** → 选 `ChinaGoSetup-1.0.10.exe`
2. **File size**: 100 MB 自动显示
3. **Operating system**: ✅ **Windows**
4. **Architecture**: ✅ **x86_64 (64-bit)**
5. ⚠️ **勾上 "This file will be available for download from itch.io"**（默认是）
6. **Optional upload**: 一个 README + 一个 ZIP（包含 THIRD-PARTY-NOTICES.txt）
7. **Save**

> 推荐同时上传一份 Windows 免安装的 GoGame.exe（从 `dist/GoGame.exe`），给那些想"试一下"的用户免安装体验。
> 这步可选，不强制。

---

## Step 5 — 截图

1. **Screenshots** 选项卡 → **Add image**
2. 上传 ≥ 5 张，按编号 `01-...08-...` 顺序
3. **Caption**（鼠标悬停提示）：写中文 1 行描述（如 "AI 在思考实时胜负率"）
4. **Save**

---

## Step 6 — 封面图 + Banner

1. **Cover image**: 上传 `cover.png`（630×500）
2. **Banner / Wide image**: 上传 `banner.png`（1200×250）
3. **Background color** 选 `#2b1810`（深木纹色）或干脆 `#000000` 黑底
4. **Save**

---

## Step 7 — 定价

1. **Pricing** 选项卡
2. **Payments**: ✅ **Accept tips**（让玩家付想付的）
3. **Minimum amount**: `$0`（让玩家可以 0 元下载；itch 默认就是 PWYW）
4. **Suggested amount**（推荐显示）: `$9`
5. 收款方式：让玩家填 Stripe / PayPal
6. **Save**

> itch 默认 Pay What You Want + 0 抽成平台费（仅支付网关 8%）。

---

## Step 8 — Visibility（可见性）

- ✅ **Public & listed**（公开可发现）
- ✅ **Allow comments**（接收评论）
- ✅ **Show donate button**（显示打赏）
- ❌ **Mature content**（除非你想刻意走暗黑）

**Save & view page** 预览。

---

## Step 9 — 发布

1. 项目页右下角 **"Publish"**（如果不是 Publish 而是 "Unpublish" 已经是上线状态）
2. 分享 URL 形如 `https://yourname.itch.io/chinago`
3. **粗大事**：截图里别忘了把 `docs/store/itch/copy.md` 里 #REDACTED# 占位符（如果有）填完
4. 守株待兔 24–72 小时，等首单

---

## Step 10 — 推一波（5 分钟）

- **Reddit** `/r/baduk`, `/r/go`, `/r/boardgames`（带 cover.png 缩略）
- **Discord**: KGS Baduk、OGS、Go Magic Discord 频道
- **Go forum**: lifein19x19.com 的 "New Tools" 板块
- **Twitter / X**: @katago 之类转发

---

## 验证清单（上线后 24 小时自查）

| 项 | 期望 |
|---|---|
| 项目页能访问 | 公开 URL 打开正常 |
| 下载链接工作 | 点 Install → 下载 Windows 二进制安装 |
| 截图都能加载 | 全部 5+ 张 1920×1080 |
| 文案排版整齐 | 长描述里段落、emoji 正常显示 |
| 搜索可发现 | itch.io search 输入 "go" / "weiqi" 能找到 |
| Pay What You Want | 默认选 0 元点 Install 也能装 |
| Tip 按钮工作 | 至少能进 Stripe/PayPal 页面 |

---

## 我能并行做的事

- [ ] 给你做 `cover.png` / `banner.png`（ImageGen 出图）
- [ ] 把上传后 URL 写回 `docs/` 备忘
- [ ] 起一份 Reddit 推广文案 + 标题
- [ ] 帮你写一个 KGS / OGS Discord 的简短推荐文

你说哪个先干。

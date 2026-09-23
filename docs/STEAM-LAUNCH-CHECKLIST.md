# Steam 上架准备清单（中国围棋 V1.0）

## 目标
- 把 ChinaGo V1.0（WPF 版）上架到 Steam 商店，定价 **$9.99 USD**
- 参照 AI Sensei / Go Magic 的极简商店页面
- 验收标准：能正常销售，玩家机器可下载、安装、启动、AI 对弈、复盘

---

## 一、Steamworks 账号与费用（约 1-2 周）

| 步骤 | 内容 | 耗时 | 备注 |
|---|---|---|---|
| 1 | 在 https://partner.steamgames.com 注册 Steamworks 账号 | 1-3 天 | 需 Steam 账号 + 手机验证 |
| 2 | 支付 Steam Direct 费用 **$100 USD** | 一次性 | 首次上架费，至少 $1000 收入才退回 |
| 3 | 通过税务问卷（W-8BEN 表） | 1 天 | 中国个人开发者按 10% 预扣 |
| 4 | 完成身份验证（身份证 + 地址） | 1-3 天 | Steam 审核 |
| 5 | 创建应用「中国围棋」，获得 AppID | 即时 | 假设 AppID = XXXXXX |

> 💡 完成后会获得 AppID（形如 1234560），后续所有 SDK 调用都用这个 ID。

---

## 二、商店页面资料（可与注册同步进行）

### 1. 基础文案（中英双语，AI Sensei/Go Magic 风格极简）

**游戏名（中）**：中国围棋
**游戏名（英）**：ChinaGo - AI Go (Baduk/Weiqi)
**类型标签**：Strategy, Board Game, Indie, Single-player
**简短描述（300 字内）**：
> 与 KataGo AI 对弈的世界级围棋软件。支持 9/13/19 路棋盘、人机对弈、AI 复盘、死活题库、局域网联机对战。基于 MIT 协议的开源 KataGo 引擎。

**详细描述**：
- 核心卖点 3 条：① 真 AI 对弈 ② AI 复盘分析 ③ 死活题训练
- 截图位置标注（见下）
- 技术致谢（KataGo / Wu D.J.）

### 2. 视觉素材（必备，最少 5 张截图）

| 资源 | 尺寸 | 数量 | 内容建议 |
|---|---|---|---|
| **头图（header）** | 460x215 | 1 | 游戏名 + 主视觉（棋盘 + 三子）|
| **主图（capsule_main）** | 616x353 | 1 | Logo + 棋盘全景 |
| **库图（library）** | 600x900 | 1 | 竖版主视觉 |
| **logo** | 120x120 | 1 | ICO 复用 |
| **截图（screenshot）** | 1920x1080 推荐 | ≥5 | 对弈/复盘/死活/联机/教程 |
| **宣传视频（movie）** | 1920x1080 mp4 | 0-1 | 可选，30 秒内最佳 |

> ⚠️ 沙盒截图抓不到 GoGame 窗口（之前已知问题），需要**真机截图**。
> 推荐用 NVIDIA GeForce Experience / Xbox Game Bar / Snipping Tool 截 WPF 窗口。

### 3. 系统需求

**最低配置**：
- Windows 10 64-bit
- 4 GB RAM
- 500 MB 可用空间（含 KataGo 引擎与权重）
- .NET 10 Desktop Runtime（首启自动下载，或安装包包含）
- CPU: 4 核 + AVX2 指令集
- GPU: OpenCL 1.2 或 CUDA 11.0+（KataGo 加速）

**推荐配置**：
- Windows 10/11 64-bit
- 8 GB RAM
- NVIDIA GTX 1060+ 或同级 GPU

---

## 三、技术集成（4-6 周，V1.x→V2.x 范围）

### 选项 A：纯上架（最快，1 周）
- 不集成 Steamworks SDK，仅靠 Steam 商店销售
- 玩家下载 `installer\ChinaGoSetup-1.0.0.exe` 直接安装
- **缺点**：没有 Steam 成就、好友、云存档
- **优点**：1 周就能上架，验证 $9.99 定价

### 选项 B：基础 Steamworks 集成（推荐，2-3 周）
- 集成 [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET)（C# 包装）到 desktop/
- 启用：Steam 成就（5-10 个）、云存档（棋谱库 100KB/局）
- **缺点**：需 C++ Redistributable 分发到桌面 Steam 客户端
- **优点**：完整 Steam 体验

### 选项 C：完整 Steam 集成（3-6 周）
- B + Steam 好友聊天、Steam 邀请联机（替换当前局域网）
- 商店页面上的「Steam 联机」按钮

> **建议先做 A**（最快拿到付费验证），根据销量决定是否升级到 B/C。

---

## 四、上架技术动作（1 周）

| 步骤 | 工具 | 内容 |
|---|---|---|
| 1 | Steamworks 后台 | 配置「Build」（上传 .exe + .dll） |
| 2 | Steamworks 后台 | 选择默认分支（default） |
| 3 | Steamworks 后台 | 设为「私有（不可见）」先做内部测试 |
| 4 | Steamworks 后台 | 加 Beta 密钥 100 个给熟人测 |
| 5 | Steamworks 后台 | 配置价格 $9.99 USD + 区域定价（CN ¥68 推荐） |
| 6 | Steamworks 后台 | 发布 → 「Coming Soon」→ 公开 |

### 上传工具
- [SteamPipe](https://partner.steamgames.com/doc/sdk/uploading) 上传（命令行）
- 或 Steamworks 后台「上传 Build」按钮（手动）

---

## 五、法务与合规（与 Steam 同步）

| 文件 | 位置 | 内容 |
|---|---|---|
| 隐私政策 | Steam 商店页 URL | 说明收集的数据：仅 Steam 用户 ID + 棋谱库本地文件 |
| EULA | Steam 商店页 URL | 标准 Steam EULA 即可 |
| 第三方致谢 | 安装目录 THIRD-PARTY-NOTICES.txt | ✅ **已完成**（KataGo MIT）|
| KataGo 版权声明 | 安装目录 KataGo/License.txt | ✅ **已完成** |
| KataGo 权重声明 | 安装目录 KataGo/weights/License.txt | ✅ **已完成** |

---

## 六、上架后运维（持续）

| 项目 | 频率 | 内容 |
|---|---|---|
| Bug 报告跟进 | 每周 | Steam 社区 / 邮件 |
| 版本更新 | 每月 | V1.0.1 修小 bug、V1.1 加功能 |
| Steam 评测回复 | 每周 | 玩家评测是 Steam 算法核心 |
| 退款政策 | 自动 | Steam 标准 2 小时/14 天 |
| 区域定价调整 | 每季度 | 看汇率调整 |

---

## 七、当前 V1.0 已就绪（确认）

- [x] ChinaGo V1.0 功能完整（28 个文件，对弈/复盘/死活/联机/教程/SGF）
- [x] 安装包 `installer\ChinaGoSetup-1.0.0.exe`（100MB，含 KataGo）
- [x] KataGo MIT 合规（License.txt + THIRD-PARTY-NOTICES.txt）
- [x] About 按钮内嵌致谢页
- [x] 桌面快捷方式 + 图标（围棋主题 ICO）

---

## 八、下一步建议

### 短期（1 周）：上架验证
1. 注册 Steamworks + 付 $100
2. 准备截图（**真机截 WPF 窗口**，非沙盒）
3. 写商店文案（中英）
4. 上传 build 到 SteamPipe，设为「私有」
5. 公开「Coming Soon」→ 拿愿望单数

### 中期（1 个月）：根据销量决策
- 销量 < 50/月：考虑降价至 $4.99 或推 Unity V2.x 重做美术
- 销量 50-200/月：维持 V1.0，做 Bug 修
- 销量 > 200/月：投 Steam 推荐位 + 集成 Steamworks 成就

### 长期（3-6 个月）：Unity V2.x
- 完整重做美术（棋子光影、棋盘木纹、动画）
- 集成 Steamworks 成就 + 云存档
- 加英文 Tutorial（海外用户）
- 准备 Switch / iPad 版本（参考 Go Magic）

---

## 参考产品链接

- **AI Sensei**（Steam $9.99 标杆）：https://store.steampowered.com/app/876440/
- **Go Magic**（移动 + 跨平台）：https://www.gomagic.org/
- **Sabaki**（开源 GUI 参考）：https://sabaki.yichuanshen.de/
- **KataGo 项目**：https://github.com/lightvector/KataGo
- **Steamworks 文档**：https://partner.steamgames.com/doc/home
- **Steam Direct 注册**：https://partner.steamgames.com/join

---

**状态**：清单已就绪，等待用户决策：是否立即注册 Steamworks + 准备截图。
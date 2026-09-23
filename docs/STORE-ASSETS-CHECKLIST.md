# Steam 商店页面素材准备清单 v1.0

> **目标**：备齐 Steam 上架所需的全部截图、文案、视觉素材
> **对象**：`中国围棋` v1.0
> **截止前可推进**：注册 Steamworks 之前必须备齐

---

## 一、Steam 商店页面元素一览

| 类型 | 规格 | 数量 | 用途 |
|------|------|------|------|
| **Header capsule** | 460×215 PNG/JPG | 1 | 商店页顶部主图 |
| **Main capsule** | 616×353 PNG/JPG | 1 | 商店列表展示 |
| **Small capsule** | 240×240 PNG/JPG | 1 | 推荐位/列表小图 |
| **Screenshot** | 1920×1080 PNG/JPG | **至少 5 张** | 商店页画廊 |
| **Logo** | 512×512 PNG（透明） | 1（可选） | 商店页角落标识 |
| **Tagline** | ≤ 256 字符 | 1 | 商店列表小标语 |
| **Short description** | ≤ 300 字符 | 1 | 搜索结果摘要 |
| **Long description** | ≤ 5000 字符 | 1 | 商店页详情正文 |

---

## 二、截图清单（5+ 张，至少 1920×1080）

### 截图 1：主对弈界面（黑子开局后）
**场景**：开 19 路 → 双方各下几手 → 体现：
- 19 路棋盘清晰可见
- 黑子 + 白子都有
- 右上实时胜率条 + 候选点
- 顶栏「引擎：KataGo」显示
- 底栏黑/白棋信息卡 + 终局数子按钮

**怎么截**：WPF 主窗口默认 1280×800，先在窗口**最大化**或调整到 1920×1080 屏幕分辨率截图（用 PrintScreen 全屏截）

### 截图 2：AI 思考中（实时胜率变化）
**场景**：下完黑子后**还没等 KataGo 落子**的瞬间截屏（胜率条变化中、候选点刷新）
- 顶栏显示"AI 思考中"
- 候选点虚线圆圈可见
- 胜率条在某个百分比

### 截图 3：打谱复盘窗口
**场景**：点「🎲 打谱」→ 加载一个 9 路对局 SGF → 走到中盘时截屏
- 复盘棋盘清晰
- 顶栏显示"第 N 手"
- 「前进」「后退」「首手」「末手」按钮可见

### 截图 4：死活题窗口
**场景**：点「🧩 死活」→ 选择一个预置死活题 → 玩家下第一手时截屏
- 死活棋盘 + 提示文字
- 「提交」「提示」「下一题」按钮可见

### 截图 5：终局数子窗口
**场景**：下完一局 → 点「📊 终局数子」→ 显示黑白目数对比时截屏
- 数子棋盘清晰
- 黑白双方目数显示
- 双方胜负判定可见

### 截图 6：联机对战窗口（可选）
**场景**：点「🌐 联机」→ 显示「等待连接 IP」界面截屏
> 即使单机也能截联机大厅界面，不需要双机

### 截图 7：围棋教程窗口
**场景**：点「📖 教程」→ 教程列表显示时截屏
- 教程章节列表
- 内容预览区

---

## 三、截图操作指引

### 推荐工具
- **Snipping Tool**（Win11 自带）：`Win + Shift + S` → 选区域/窗口 → 自动复制到剪贴板
- **PrintScreen 全屏**：`PrtScn` 键 → 整屏截图 → 粘贴到画图保存
- **Win + G** 打开 Xbox Game Bar → 截图（带边框）

### 关键注意
1. **必须设 1920×1080 或更高**（Steam 拒绝低于此分辨率的截图）
2. **不要带浏览器/资源管理器等周边界面**——只截 WPF 主窗口或具体子窗口
3. **PNG 格式优先**（体积小、无损），JPG 也可但有损
4. **保存到统一目录**：`installer/screenshots/`（待会儿我帮你批量改名+生成 manifest）

### 快速批量截图脚本（可选）
PowerShell 跑这条，可以连续截5次，每隔 5 秒一张：
```powershell
Add-Type -AssemblyName System.Windows.Forms
for ($i=1; $i -le 5; $i++) {
  $bmp = New-Object System.Drawing.Bitmap 1920, 1080
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
  $bmp.Save("C:\Users\Administrator\Pictures\screenshot_$i.png")
  $bmp.Dispose(); $g.Dispose()
  Write-Host "截图 $i 已保存"
  Start-Sleep -Seconds 5
}
```

---

## 四、Steam 商店文案模板

### 1. 游戏名（Title）
**中文**：中国围棋
**英文**：ChinaGo - AI Go Board Game
> Steam 商店名目前不支持中文标题（很多老游戏特例），默认中文名 + 英文标题

### 2. 简短描述（Short Description）≤ 300 字符
**英文版**（推荐主推）：
```
Master the ancient game of Go against KataGo, one of the world's strongest AI engines. 
Real-time winrate analysis, SGF replay, life-and-death puzzles, and end-game scoring — 
all offline, all in one elegant desktop app. No login, no ads, just you and the board.
```
（172 字符）

### 3. 长描述（Long Description）≤ 5000 字符
**建议章节**（我下面给完整中文版+英文版）：

#### 中文版（建议）

```
🎯 真正的围棋对弈，不只是 UI

中国围棋是一款以 KataGo（全球顶级开源围棋 AI）为内核的桌面围棋应用。
跑在你自己的电脑上，无需联网、无需账号、无广告。

⭐ 核心特性
• KataGo 引擎：神经网络权重 kata1-b18c384nbt，业余高段~职业水准
• 实时胜率：每步落子后立即刷新黑白胜率 + AI 候选点
• AI 复盘报告：每步评分、最佳着法、关键失误提示
• SGF 打谱复盘：导入任意 .sgf 文件，前进/后退/分支探索
• 死活题训练：内置题库，逐步提升你的局部战斗力
• 终局智能数子：自动判断黑白地盘 + 提子数
• 联机对战：局域网 TCP 直连，与好友面对面下棋
• 9 / 13 / 19 路棋盘自由切换
• 认输 / 虚手 / 悔棋：符合国际围棋规则
• 围棋教程：从入门到段位的完整体系

📋 系统要求
• Windows 10 / 11 64-bit
• .NET 10 Desktop Runtime
• 1 GB 硬盘空间（含 KataGo 引擎 113 MB）
• 建议 8 GB 内存、4 核 CPU（KataGo 思考时会占用全部核心）

🤝 开源致谢
本应用打包分发了 KataGo 引擎及其神经网络权重，均以 MIT 许可证发布。
原始项目：https://github.com/lightvector/KataGo
感谢 David J Wu 与 KataGo 社区的卓越贡献。
```

#### 英文版（推荐主推）
```
🎯 Real Go, not just a UI

ChinaGo is a desktop Go (Weiqi/Baduk) application powered by KataGo,
one of the world's strongest open-source AI engines. Runs locally on your machine —
no login, no internet required, no ads.

⭐ Core Features
• KataGo engine: neural network weights kata1-b18c384nbt, amateur high-dan to pro level
• Real-time winrate: refresh after every move with AI candidate suggestions
• AI review report: per-move scoring, best moves, key mistakes highlighted
• SGF replay: import any .sgf file, navigate forward/backward, explore branches
• Life-and-death puzzles: built-in problem library to train your tactical reading
• Automatic end-game scoring: black/white territory + captures
• Network play: LAN TCP for two-player matches against friends
• 9 / 13 / 19 board sizes
• Resign / pass / undo: follows international Go rules
• Built-in tutorial: complete curriculum from beginner to dan level

📋 System Requirements
• Windows 10 / 11 64-bit
• .NET 10 Desktop Runtime
• 1 GB disk space (includes 113 MB KataGo engine)
• Recommended 8 GB RAM, 4-core CPU (KataGo uses all cores when thinking)

🤝 Open Source Credits
This application bundles the KataGo engine and its neural network weights,
both released under the MIT License.
Original project: https://github.com/lightvector/KataGo
Thanks to David J Wu and the KataGo community.
```

### 4. Tagline ≤ 256 字符
**英文**：
```
Master Go against KataGo — real-time AI analysis, offline, no ads.
```
（71 字符）

---

## 五、价格 / 标签 / 分类

### 价格
- **建议零售价**：$9.99 USD（参照 AI Sensei 定价）
- **首发折扣**：可选 -10%（前 2 周）

### Steam 标签（最多 20 个）
```
围棋, Go, Weiqi, Baduk, 棋类, Strategy, 单人, 单机, AI, 
人工智能, 独立游戏, 休闲, 解谜, 桌面, 写实, 模拟, 教育, 开源, 教程
```
（选 10-15 个核心标签，避免堆砌）

### 分类
- 主分类：单人 / 多人（联机）
- 子分类：策略 / 模拟 / 休闲
- 主题：教育 / 历史

---

## 六、素材存放建议

在项目里建目录：

```
go-game-prototype/
├── store-assets/                # Steam 上架素材
│   ├── screenshots/             # 5+ 张 1920x1080 截图
│   ├── capsules/                # 460x215 + 616x353 + 240x240
│   ├── logo.png                 # 512x512 透明
│   ├── description-zh.md        # 中文长描述
│   └── description-en.md        # 英文长描述
```

每张截图用语义化文件名（避免中文）：
```
screenshot-01-main-game.png
screenshot-02-ai-thinking.png
screenshot-03-replay.png
screenshot-04-puzzle.png
screenshot-05-scoring.png
screenshot-06-network.png      # 可选
screenshot-07-tutorial.png     # 可选
```

---

## 七、可立即推进

1. **现在就打开 中国围棋 主窗口**，按清单截 5 张图
2. **如果想让我帮你改文案**：把你的英文版 / 中文版草稿发我，我润色
3. **capsule 图我可以做**（我已经有围棋主题 ICO 制作经验，可以扩到 460x215 + 616x353 + 240x240）

需要我做什么就告诉我。
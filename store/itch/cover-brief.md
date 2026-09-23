# Cover Image & Banner — Brief for AI Generation

> 你（或我用 ImageGen）出图时，按这个 brief 出图。最终产物：
> 1. **`cover.png`** — 630×500，itch.io 卡片头图（最重要）
> 2. **`banner.png`** — 1200×200，标题横幅（可选但推荐）

---

## 风格选择（4 选 1）

| | 风格 | 目标人群匹配度 |
|---|---|---|
| A | **木纹暖色 + 黑子高光 + 极简光晕**（与 v1.0 内嵌木纹棋盘统一） | 高（与产品自带风格一致，识别度强） |
| B | 水墨写意 + 黑白粒子 + 留白（中国风浓） | 中（意境足但不"AI"） |
| C | 极简几何 + 网格底色 + 黑子 + AI 神经网络发光 | 中（很"现代"但不"围棋"） |
| D | 赛博朋克 + 霓虹 + 棋盘 + AI 数据流 | 低（围棋气质冲突） |

**推荐 A**——和现有 v1.0 内嵌 UI 一致，玩家看一眼就知道"这是同一款产品"。

---

## Cover prompt（英文，给 ImageGen 用）

```
A serene, top-down photograph of a wooden Go board (19x19 grid, Kaya wood
grain, warm amber tones), centered on the canvas. A single polished black
stone sits on a star point with soft directional light reflecting off its
surface. Around the board, an ambient cyan glow symbolizes neural-network
activation -- thin translucent lines connecting grid intersections like a
thinking mind at work. The corners show subtle lens flare from an unseen
AI "thinking". No text on the image. Color palette: warm amber + deep ink
black + soft cyan accent. Shot in 630x500, no cropping, high-detail wood
texture. Photorealistic, calm, slightly mysterious, evokes "ancient game,
modern mind". No people, no hands, no logos.
```

---

## Banner prompt（可选）

```
A wide horizontal banner (1200x250) showing a stretched Go board viewed at
slight perspective. The left third has a dense opening pattern of black and
white stones. The middle third fades into negative space showing only the
wood grain. The right third has a single white stone with a soft radial
glow, suggesting "the AI is thinking". Subtle horizontal grid lines fade
into the background. No text on the image. Color palette: warm amber + deep
ink black + soft cyan accent. Cinematic, calm, premium feel.
```

---

## 技术规格（导出时记得）

| 维度 | Cover | Banner |
|---|---|---|
| 尺寸 | **630 × 500 px** | **1200 × 250–400 px** |
| 格式 | PNG（透明背景可选） | PNG |
| 大小 | ≤ 5 MB | ≤ 5 MB |
| 颜色 | sRGB | sRGB |
| 文件名 | `cover.png` | `banner.png` |

---

## 不出图的备选

- 🖼️ 找免费围棋主题图：Unsplash 搜 "go board" / "weiqi" / "baduk stones"
- 🎨 Figma 起手：直接以棋盘截图为主，下沉到单纯背景设计

---

## 我能直接帮你做的事

如果你给我**风格选择（A/B/C/D，告诉我选哪个）+ 是否要我去用 AI 出图**，
我可以直接调 ImageGen 工具出 cover.png + banner.png，10 分钟交付。

或者**你只用我写的截图脚本**（主程序截图），封面先占位"v1.0.10 logo 简化版"也行 —— 等上线后根据下载数据判断要不要重画。

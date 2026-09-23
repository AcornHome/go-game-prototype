# 中国围棋

海外围棋游戏，基于 **KataGo 引擎** + **WPF / Unity** 双轨开发的商业项目。

## 当前进度（2026-09-05）

| 阶段 | 状态 | 备注 |
|---|---|---|
| KataGo GTP 通信 | ✅ 已验证 | kata-genmove_analyze 端到端，3 步走子实时胜率 |
| V1.0 WPF 完整版 | ✅ 已就绪 | 28 文件，对弈/复盘/死活/联机/教程/SGF 全套 |
| 安装包打包 | ✅ 已就绪 | `installer/ChinaGoSetup-1.0.0.exe` 100MB |
| KataGo 合规 | ✅ 已就绪 | MIT 许可证全文 + 第三方声明 + About 致谢 |
| 上架 Steam | ⏳ 待启动 | 见 [docs/STEAM-LAUNCH-CHECKLIST.md](docs/STEAM-LAUNCH-CHECKLIST.md) |
| Unity V2.x | 🔄 进行中 | Unity 6 LTS 工程底座已建，UI 渲染待深化 |

## 🚀 快速跑起来（开发者模式）

### 跑 WPF 版（V1.0 当前生产版本）

```bat
:: 在项目根目录
cd desktop
dotnet build
cd ..
python .tools\deploy.py
:: 桌面双击「中国围棋」
```

`deploy.py` 会：
1. 编译产物同步到 `dist/`
2. KataGo 引擎（~108MB）增量同步到 `dist/KataGo/`
3. 许可证文件同步到 `dist/`（MIT 全文 + THIRD-PARTY-NOTICES.txt）
4. 重建桌面快捷方式

### 跑冒烟（核心回归）

```bat
cd diag
dotnet run
```

8 套冒烟：KataGo 默认 komi / 9-13-19 路切换 / kata-genmove_analyze 端到端 / SGF 写入 / SGF 复盘 / 复盘报告 / 死活题 / 联机会话。

## 🎯 上架准备

**V1.0 已就绪，立即可上架 Steam**（$9.99 USD）。详细清单见：
- [docs/STEAM-LAUNCH-CHECKLIST.md](docs/STEAM-LAUNCH-CHECKLIST.md)

关键材料：
- 安装包：`installer/ChinaGoSetup-1.0.0.exe`（100MB）
- 合规：`dist/THIRD-PARTY-NOTICES.txt` + `dist/KataGo/License.txt` + `dist/KataGo/weights/License.txt`
- 图标：`installer/app.ico`（徽章版围棋主题，6 尺寸 + 512x512 高清）
- 致谢：MainWindow 顶栏「ℹ️ 关于」按钮内嵌

## 📁 目录结构

```
go-game-prototype/
├── README.md                       # 本文件
├── docs/
│   ├── STEAM-LAUNCH-CHECKLIST.md   # Steam 上架清单（V1.0→V2.x）
│   └── (历史文档)
├── desktop/                        # ⭐ V1.0 WPF 主程序
│   ├── MainWindow.xaml + .cs       # 主窗口
│   ├── BoardControl.cs             # 棋盘渲染（带动画/光晕/木纹）
│   ├── KataGoClient.cs             # GTP 通信 + 分析
│   ├── KataGoLocator.cs            # KataGo 路径自适应查找
│   ├── GameController.cs           # 对弈状态机
│   ├── SgfWriter.cs / SgfReader.cs # 棋谱读写
│   ├── LanSession.cs               # 局域网联机
│   ├── Puzzle.cs / PuzzleWindow    # 死活题
│   └── dist/                       # 编译产物 + KataGo + 许可证
├── installer/
│   ├── GoSmart.iss                 # Inno Setup 安装脚本
│   ├── app.ico                     # 徽章版围棋图标（精致）
│   ├── logo-clearbg.png            # 512x512 Steam logo
│   └── ChinaGoSetup-1.0.0.exe       # 打包后的安装包（100MB）
├── dist-licenses/                  # 合规材料源（deploy.py 自动同步）
│   ├── KataGo/License.txt          # 引擎 MIT 全文
│   ├── KataGo/weights/License.txt  # 权重 MIT 全文
│   └── THIRD-PARTY-NOTICES.txt     # 第三方声明
├── unity/                          # Unity 6 LTS 工程底座（V2.x）
│   └── Assets/Scripts/
│       ├── BoardView.cs            # UI Toolkit 棋盘
│       └── GameBootstrap.cs        # 启动入口
├── unity-core/                     # .NET Standard 2.1 引擎无关核心
│   ├── Board.cs                    # 棋盘逻辑
│   ├── KataGoClient.cs             # GTP 通信
│   ├── GameController.cs
│   └── SgfReader.cs / SgfWriter.cs
├── unity-core.tests/               # 核心冒烟（11 项断言）
├── diag/                           # WPF 历史诊断冒烟（KataGo 端到端）
├── console/                        # 早期 C# 控制台原型（已退役）
└── .tools/
    └── deploy.py                   # 一键部署脚本
```

## 🛠️ 技术栈

- **AI 引擎**：KataGo v1.18.2（MIT License，作者 David J Wu）
- **神经网络**：kata1-b18c384nbt.bin.gz（94MB，KataGo Neural Network License）
- **UI 框架**：WPF (.NET 10) 主程序 + Unity 6 LTS (V2.x 计划)
- **通信协议**：GTP（Go Text Protocol）+ kata-genmove_analyze 扩展
- **打包**：Inno Setup 6（中文向导，lzma2/ultra64 压缩）
- **CI/CD**：本地 `deploy.py` + `ISCC.exe`

## 📜 法律合规

- ✅ KataGo 引擎 MIT 全文：见 `installer/ChinaGoSetup-1.0.0.exe` 安装后目录 `KataGo/License.txt`
- ✅ KataGo 神经网络权重 MIT 全文：`KataGo/weights/License.txt`
- ✅ 第三方声明：`THIRD-PARTY-NOTICES.txt`
- ✅ 主程序「ℹ️ 关于」菜单内嵌致谢

## 下一步建议

1. **短期（1 周）**：注册 Steamworks + 准备商店截图（真机截 WPF 窗口）+ 写文案 + 上架
2. **中期（1 个月）**：根据首月销量决定是否集成 Steamworks 成就
3. **长期（3-6 个月）**：Unity V2.x 重做美术 + Switch/iPad 跨平台

## 参考产品

- **AI Sensei**（Steam $9.99 标杆）：https://store.steampowered.com/app/876440/
- **Go Magic**：https://www.gomagic.org/
- **KataGo**：https://github.com/lightvector/KataGo
- **Sabaki**（开源 GUI 参考）：https://sabaki.yichuanshen.de/
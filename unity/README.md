# Go Game — Unity 6 LTS 移植

V1.x 第五步"联机对弈"已落地后，把 V1.0 MVP（人 vs KataGo + SGF + 认输/虚手/悔棋/终局）迁移到 Unity 6 LTS，
作为未来 Steam 上架（$9.99 买断）的引擎底座。

## 项目分层

```
go-game-prototype/
├── desktop/             ← WPF 原版（生产中，已稳定运行 V1.0 + V1.x 全部功能）
├── unity/               ← Unity 6 LTS 项目（本目录）
├── unity-core/          ← .NET Standard 2.1 类库（引擎无关，纯业务逻辑）
├── unity-core.tests/    ← dotnet 跑通的冒烟测试（不需 Editor）
├── console/             ← 早期 C# 控制台原型（KataGo 联通验证）
└── diag/                ← WPF 的 diag 项目
```

**设计原则**：所有棋盘/AI/SGF 业务逻辑写在 `unity-core/`（.NET Standard 2.1），Unity 6 通过 `GoGame.Unity.asmdef` 引用 `UnityCore.dll`。这样 dotnet 能跑通核心逻辑冒烟，Editor 装上后只需跑场景。

## 当前范围（Unity 版 V1.0 MVP）

✅ 已有：
- KataGo 子进程 + GTP 通信（`unity-core/KataGoClient.cs`）
- 棋盘状态机 + 提子 + 劫 + 自杀（`unity-core/Board.cs`）
- 回合控制 + 落子 + Pass/Resign/Undo（`unity-core/GameController.cs`）
- SGF 存读（`unity-core/SgfReader.cs` + `SgfWriter.cs`）
- 9/13/19 路切换
- 终局数子（KataGo final_score）
- UI Toolkit 棋盘渲染（`unity/Assets/Scripts/BoardView.cs`）
- MonoBehaviour 启动入口 + 控制面板（`unity/Assets/Scripts/GameBootstrap.cs`）
- Standalone Win64 打包（IL2CPP，x86_64）

❌ 暂未移植（V2.x 跟进）：
- 实时胜率 / AI 复盘报告（V1.x 功能）
- 打谱复盘（SGF 加载 + 步进回放）
- 互动死活题
- 联机对战（V1.x 第五步）
- Steamworks SDK 集成
- 商业化打磨

## 文件清单

| 文件 | 行数 | 作用 |
|---|---|---|
| `unity-core/Stone.cs` | 5 | 棋子枚举 |
| `unity-core/Board.cs` | 220 | 棋盘状态机（提子/劫/自杀/GTP 坐标） |
| `unity-core/KataGoClient.cs` | 280 | KataGo 子进程 + GTP（含 kata-genmove_analyze 文本解析） |
| `unity-core/GameController.cs` | 220 | 回合控制 + 落子流程 |
| `unity-core/SgfReader.cs` | 175 | SGF 解析（PB/PW/KM/RE/RU/DT 等元信息 + 着法坐标） |
| `unity-core/SgfWriter.cs` | 65 | SGF 输出 |
| `unity-core.tests/BoardTests.cs` | 175 | 7 套 Board 单元测试 |
| `unity-core.tests/SgfRoundTripTests.cs` | 95 | 3 套 SGF 往返测试 |
| `unity-core.tests/KataGoConnectionTests.cs` | 60 | KataGo 联通测试 |
| `unity/Assets/Scripts/BoardView.cs` | 145 | UI Toolkit 棋盘视图 |
| `unity/Assets/Scripts/GameBootstrap.cs` | 240 | MonoBehaviour 启动入口 |

## 编译 & 验证

### 1. 跑核心逻辑冒烟（不需 Editor）

```bash
cd "C:\Users\Administrator\WorkBuddy\2026-09-04-12-30-09\go-game-prototype\unity-core.tests"
dotnet run -c Debug
```

预期输出：
```
[unity-core-tests] 开始冒烟测试
=== Board 单元测试 ===
BoardTests 完成
=== SGF 读写往返测试 ===
SgfRoundTripTests 完成
=== KataGo 联通测试 ===
  [..] 启动 KataGo (9 路，首次启动会编译权重 30s~3min)...
  [OK] KataGo version: = 1.18.1+...
  [OK] 首手 AI 落: E5
KataGoConnectionTests 完成
[unity-core-tests] 总失败数: 0
```

### 2. 让 Unity 6 LTS 引用 UnityCore.dll

Unity 第一次打开本项目时，会自动生成 `Assets/csc.rsp` 和 `Library/`，但**不会自动关联 UnityCore.dll**。

**手动步骤**（只做一次）：
1. Unity Hub → Install Unity 6000.0.36f1 LTS（如果没有，下载 ~3GB）
2. File → Open Project → 选择 `unity/` 目录
3. 等 Editor 编译完成（首次 5-10 分钟）
4. 在 Project 窗口右键 → Create → Scene → 命名 `SampleScene` → 存到 `Assets/Scenes/`
5. 场景里右键 → Create Empty → 命名 `GameBootstrap`
6. Inspector → Add Component → UIDocument
7. 同一个 GameObject → Add Component → Game Bootstrap
8. **手动绑定 UnityCore.dll 引用**：
   - 在 `Assets/Scripts/` 下应该已经有 `GoGame.Unity.asmdef`
   - 选中 asmdef → Inspector → References → 把 UnityCore.dll 拖进去
   - （如果没有，先确保 unity-core 已 build：`cd unity-core && dotnet build`，然后在 `Assets/Plugins/` 目录下放入 `UnityCore.dll`）
9. File → Build Settings → Add Open Scenes → 选 Standalone Windows (x86_64)
10. 按 ▶ Play 启动

**首次启动 KataGo 会编译神经网络，30 秒到 3 分钟**（仅第一次，之后秒开）。

## 已知坑

- Unity 6 LTS 项目结构必须 Unity Editor 生成（手写 .unity 场景 YAML 极脆）。本仓库只提供 Editor 自动生成的最少文件。
- `unity-core/UnityCore.csproj` 是 dotnet 类库，**Unity 不会重新编译它**。需要 dotnet build 后手动把 `bin/Debug/netstandard2.1/UnityCore.dll` 拷到 `Assets/Plugins/`（或者用 asmdef 的 precompiledReferences 引用）。
- KataGo 路径在 GameBootstrap 的 SerializeField 里，按本机实际修改即可。
- 权重路径在本项目是 `C:\Tools\KataGo\weights\kata1-b18c384nbt.bin.gz`（不在 KataGo 根目录）。

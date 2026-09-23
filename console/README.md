# Go Prototype: 控制台可玩版

无需安装 Unity。装好 KataGo 后，直接 `dotnet run` 就能下一盘围棋。

## 跑起来（3 步）

### 1. 装 KataGo

**如果 GitHub 打得开**

1. 打开 https://github.com/lightvector/KataGo/releases
2. **有 NVIDIA 独显**：下载 `katago-v1.18.2-cuda12.1-cudnn9.8.0-windows-x64.zip`（约 9 MB）
3. **无独显 / AMD / Intel**：下载 `katago-v1.18.1-eigen-avx2-windows-x64.zip`
4. 解压到 `C:\Tools\KataGo\`
5. 里面应该有 `katago.exe` 和 `default_gtp.cfg`

**如果 GitHub 打不开（国内常见）** —— 用镜像直接下文件

镜像格式：`https://gh-proxy.com/<原 URL>`

镜像 A（推荐，速度快）：
```
https://gh-proxy.com/https://github.com/lightvector/KataGo/releases/download/v1.18.2/katago-v1.18.2-cuda12.1-cudnn9.8.0-windows-x64.zip
```

镜像 B（备份）：
```
https://ghfast.top/https://github.com/lightvector/KataGo/releases/download/v1.18.2/katago-v1.18.2-cuda12.1-cudnn9.8.0-windows-x64.zip
```

镜像 C（再备份）：
```
https://mirror.ghproxy.com/https://github.com/lightvector/KataGo/releases/download/v1.18.2/katago-v1.18.2-cuda12.1-cudnn9.8.0-windows-x64.zip
```

**CPU 版镜像**（无独显用这个）：
```
https://gh-proxy.com/https://github.com/lightvector/KataGo/releases/download/v1.18.1/katago-v1.18.1-eigen-avx2-windows-x64.zip
```

复制到浏览器地址栏按回车就开始下载。下载完解压到 `C:\Tools\KataGo\`。

### 2. 下载权重（约 150 MB）

权重**不**在 GitHub release 上，托管在 katagotraining.org。

**先试官网**：https://katagotraining.org/networks/

找文件名包含 `b18c384` 的 `.bin.gz` 文件（按字母排序，前面是 `kata1-` 开头）。

**如果 katagotraining.org 也打不开**，用 gh-proxy 镜像：

```
https://gh-proxy.com/https://katagotraining.org/networks/
```

打开后找 b18c384 系列，浏览器右键复制下载链接即可。

**常用权重选择**（按强度递增）：
| 文件名 | 强度 | 大小 |
|---|---|---|
| `kata1-b18c384nbt-s5378693760-d2026470963.bin.gz` | 业余 5 段 | ~150 MB |
| `kata1-b18c384nbt-swa-...bin.gz` | 业余 6 段（SWA 平均） | ~150 MB |

新手用第一个就够（快）。

把下载的 `.bin.gz` 放到 `C:\Tools\KataGo\weights\`。

### 3. 启动

```bash
cd "C:\Users\Administrator\WorkBuddy\2026-09-04-12-30-09\go-game-prototype\console"
dotnet run
```

首次运行：
- 第一次 `boardsize` 会编译神经网络，**30s ~ 3min**，正常
- 看到 `[Setup] KataGo ready.` 后就能下棋了

### 4. 玩

```
你的回合 (X)> D4
你的回合 (X)> pass
你的回合 (X)> resign    # 认输
你的回合 (X)> board     # 重画
你的回合 (X)> sgf       # 打印 SGF
你的回合 (X)> quit      # 退出
```

坐标支持：
- `D4` —— GTP 标准格式（列字母 + 行数字，从左到右从下到上）
- `4,4` —— 1-indexed 数字
- `44` —— 简写
- `q16` —— 小写也行

棋盘：
- `X` = 黑，`O` = 白，`.` = 空，`*` = 星位

### 5. 覆盖默认路径

如果 KataGo 没装到 `C:\Tools\KataGo\`，可以覆盖：

```bash
dotnet run -- --katago "D:\GoEngines\KataGo\katago.exe"
dotnet run -- --model "C:\Tools\KataGo\weights\kata1-b18c384nbt-xxx.bin.gz"
dotnet run -- --size 9 --color w
```

## 退出后会得到

`game-YYYYMMDD-HHMMSS.sgf`，用 Sabaki（开源 https://sabaki.yichuanshen.de/）打开就能复盘。

## 架构

```
Program.cs           # 主循环、参数解析
├── Board.cs         # 棋盘状态（19x19 内存模型）
├── KataGoClient.cs  # GTP 协议封装（stdin/stdout 异步通信）
├── GameController.cs# 状态机（先验证再落子）
└── SgfWriter.cs     # SGF 棋谱输出
```

所有核心逻辑（Board / KataGoClient / GameController）后面会平移到 Unity，零重写。

## 故障排查

| 症状 | 排查 |
|---|---|
| `KataGo 可执行文件不存在` | 检查 `C:\Tools\KataGo\katago.exe`，或用 `--katago` 指定 |
| `KataGo 模型权重不存在` | 确认权重解压到 `weights/` 目录 |
| 启动后卡 30s+ 不动 | 正常，首次编译神经网络 |
| KataGo 进程退出 | 看 stderr 报错，常见：CUDA 驱动版本不匹配 |
| `illegal move` | 自杀手或全局同形（KO），换一手 |
| 镜像下不动 | 换 `ghfast.top` 或 `mirror.ghproxy.com`，三个镜像全试 |
| KataGo 启动报 "libcudnn" 错 | CUDA 驱动太旧，下载 cudnn8.9.7 版本而不是 9.8.0 |

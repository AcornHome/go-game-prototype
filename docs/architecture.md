# 系统架构

## 总体结构

```
+---------------------------------+
|      Unity 6 应用                |
|  +-----------------------+       |
|  |   GameBootstrap.cs    | <-- 启动入口
|  +-----------------------+       |
|             |                    |
|  +----------v--------+           |
|  |  GameController   | <-- 状态机 (GameState)
|  +----------+--------+           |
|             |                    |
|  +----------v--------+           |
|  |   BoardView       | <-- UI Toolkit 棋盘渲染
|  +-------------------+           |
|             |                    |
|  +----------v--------+           |
|  |  KataGoClient     | <-- C# 子进程通信
|  +----------+--------+           |
+-------------|-------------------+
              | stdin/stdout (GTP 协议)
              v
     +-------------------+
     |   KataGo.exe 子进程 |
     +-------------------+
```

## 关键设计

### 1. GTP 协议通信

KataGo 子进程通过 stdin/stdout 通信。GTP 是围棋 AI 的标准协议，所有主流引擎（Leela Zero、GnuGo 等）都支持，未来切换 AI 不需要改前端。

- 命令格式：`boardsize 19\n`
- 响应格式：`= response_text\n` 或 `? error_message\n`

注意：KataGo 启动后会先输出 banner 行（非 GTP 响应），`KataGoClient` 启动时要吞掉第一行。

### 2. 状态机（GameController）

```
[Idle]
   |
   v
[HumanTurn] -- place stone --> [WaitingAI]
                                      |
                                      v
                                  [genmove]
                                      |
                                      v
                                  [HumanTurn]
                                      |
                                      v (pass/resign)
                                  [GameOver]
```

- **HumanTurn**：等待用户落子
- **WaitingAI**：人已落子，等待 KataGo `genmove` 返回
- **GameOver**：认输 / 跳过终局 / 数子决胜

### 3. 棋谱（SGF）

标准 SGF 格式示例：
```
(;FF[4]GM[1]CA[UTF-8]SZ[19]
  ;B[qd];W[dd];B[pq]
  C[好手]
  ;W[ip]
  )
```

V1.0 暂用 `string` 拼接，V1.x 引入 NSGF 库（成熟 C# 实现）。

## 模块职责

| 模块 | 行数 | 职责 |
|---|---|---|
| KataGoClient | ~80 | 进程管理、GTP 收发、错误恢复 |
| BoardView | ~100 | 19×19 网格 UI、坐标转换、落子动画 |
| GameController | ~90 | 状态机、落子合法性、AI 调用 |
| GameBootstrap | ~50 | 启动场景、配置路径、调试面板 |

V1.0 总代码量预计 ~400 行 C#，不含 UI Toolkit XML 模板。

## 性能瓶颈预测

| 瓶颈 | 应对 |
|---|---|
| KataGo 启动 30s+ | 启动场景加 loading 动画，异步初始化 |
| 首步 AI 慢（3-5s） | UI 显示"AI 思考中" |
| 强 AI 弱机 OOM | 默认 visits=200，用户可手动提升 |
| 多线程 stdout 竞争 | 单线程读写，生产环境可加 lock |

## 不在 V1.0 范围（V1.x 再说）

- 联网对战（需要后端匹配服务器）
- 多 AI 切换（GTP 协议本身已支持，UI 加个 dropdown 即可）
- 教程系统（独立大模块）
- 棋谱云同步（需要账号系统）
- Steamworks SDK 集成（V1.0 阶段本地版即可）

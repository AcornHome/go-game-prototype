# KataGo 安装配置详解

## 下载

### 1. KataGo 引擎

访问 https://github.com/lightvector/KataGo/releases

下载对应版本（以 `v1.15.0` 为例，可选更新的 LTS 版）：

- **GPU 用 CUDA**（推荐）：`katago-v1.15.0-cuda12.4-windows-x64.zip`
- **仅 CPU**：选 `katago-v1.15.0-eigen-windows-x64.zip`（无显卡时的备用）

解压到 `C:\Tools\KataGo\`。

### 2. 神经网络权重

同页面 `Assets` 区下载推荐权重：

- `kata1-b18c384nbt-s5378693760-d2026470963.txt.gz`（业余 5 段强，约 150 MB，最常用）

解压 `.gz` 文件后放到 `C:\Tools\KataGo\weights\`。

### 3. 配置文件

`C:\Tools\KataGo\` 自带 `default_gtp.cfg`，可直接用。

如果要自定义（推荐先看一遍）：
```ini
# 关键参数（default_gtp.cfg 内可直接修改）
logSearchInfo = true        # 输出 AI 决策详情（调试时开启）
logToStderr = false
maxVisits = 1000            # 默认思考步数（弱机降到 200）
ponderingEnabled = true     # 思考对方走子时同时预测
```

## 启动测试

打开 CMD，进入 KataGo 目录：

```cmd
cd C:\Tools\KataGo
scripts\..\..\scripts\start-katago.bat
```

或直接命令行：
```cmd
katago.exe gtp -model weights\kata1-b18c384nbt-s5378693760-d2026470963.txt.gz -config default_gtp.cfg
```

**首次启动会编译神经网络**（30 秒 - 3 分钟，依机器性能）。看到 `KataGo >` 提示符就是成功。

测试 GTP 命令：
```
boardsize 19
komi 7.5
play B q4
genmove W      # 应该 < 5s 返回一手
```

看到类似 `= d4` 表示 AI 落子白子在 d4。

## 常见问题

### CUDA 错误 "no CUDA driver"

NVIDIA 显卡驱动太旧，更新驱动。或改用 eigen 版本（仅 CPU，性能弱很多）。

### 启动超慢

首次编译神经网络，正常。加 `-t` 参数限制线程数：
```cmd
katago.exe gtp -t 4 -model ...
```

### 内存爆（弱机常见）

减小 `default_gtp.cfg` 中的 `maxVisits`：
```ini
maxVisits = 200     # 弱机标准配置
```

### KataGo 退出后端口没释放

是 GTP 协议不走 TCP 端口，而是 stdin/stdout。重启 KataGo 即可。

## 商用授权

| 组件 | 协议 | 商用 |
|---|---|---|
| KataGo 源码 | BSD 3-Clause | OK |
| 自带 ELF OpenGo 权重 | Apache 2.0 | OK |
| 社区训练权重（kata1-b18c*） | 视具体权重而异 | ⚠️ |

**V1.0 上架前建议律师过一遍 ToS**。V1.0 技术验证阶段用任何开源权重都没问题。

## 性能基准（中端笔记本 i7 + RTX 3060）

| visits | 首步响应 | 平均走步 |
|---|---|---|
| 200 | 0.5s | 0.5s |
| 1000 | 3-5s | 1-2s |
| 5000 | 15-25s | 5-10s |

弱机（无独显，CPU 模式）：`visits=500` 是上限，再高就 30s+。

## 推荐权重策略

- 默认 `kata1-b18c384nbt`（业余 5 段）适合 80% 用户
- 高级会员解锁 `kata1-b40c256x` 系列（职业级，~600MB，3-5s 首步）
- V1.x 调研是否提供云端 KataGo 调用服务

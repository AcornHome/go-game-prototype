# 中国围棋 · 阿里云反馈同步部署手册（图文版）

> **目标**：让玩家在任何电脑上提交反馈后，你能立即在**阿里云盘 APP / 阿里云 OSS 控制台**看到，无需手动同步文件。

## 📐 整体架构

```
玩家电脑                                    阿里云                    你的电脑 / 手机
═════════════                          ═══════════════           ══════════════
顶栏「📨 反馈」按钮                             函数计算 FC                  阿里云盘 APP
   ↓ 用户填反馈                                  ↑ HTTP POST                 ↑ 资源库同步
   ↓ 点提交                                       │                          OSS 控制台
   ↓                                       ┌──────┴──────┐                     ↑
FeedbackWindow ─┬─→ 本地 JSON（兜底）   FC 函数 (Node.js)              │
                 │  %USERPROFILE%\           │                          │
                 │  Documents\ChinaGo\       ↓                          │
                 └─→ HTTP POST ────────→  阿里云 OSS Bucket        看反馈
                                         feedback-chinago/
                                         ├─ 2026-09-05/
                                         │   ├─ fb-abc12345.json
                                         │   ├─ fb-def67890.json
                                         └─ 2026-09-06/
                                             └─ fb-xyz98765.json
```

## ⏱️ 预计耗时

| 步骤 | 耗时 |
|---|---|
| 1. 开通阿里云 FC | 1 分钟 |
| 2. 创建 OSS Bucket | 2 分钟 |
| 3. 创建 AccessKey | 2 分钟 |
| 4. 部署 FC 函数 | 5 分钟 |
| 5. 配置游戏客户端 | 1 分钟 |
| **总计** | **约 10 分钟** |

## 💰 费用估算

| 服务 | 免费额度 | 单条反馈成本 |
|---|---|---|
| 函数计算 FC | 100 万次/月 | ≈ 0.0001 元 |
| OSS 标准存储 | 5 GB | ≈ 0.012 元/GB/月 |
| OSS 读写请求 | GET 100 万次/月 + PUT 100 万次/月 | ≈ 0.01 元/万次 |

单条反馈约 2KB，1000 条 = 2MB ≈ **永久免费**。

---

## 📍 步骤 1：开通阿里云函数计算 FC

打开 https://fc.console.aliyun.com

- 如果是首次访问，会提示「**开通服务**」
- 选择**按量付费**（虽然有免费额度，但开通时必须选）
- 完成开通（**不要钱**，按量付费 = 用了才付钱）

> 💡 提示：阿里云账号必须**实名认证**才能开通 FC。如果还没认证，会跳转引导你认证（中国大陆身份证或营业执照，5 分钟搞定）。

---

## 📍 步骤 2：创建 OSS Bucket

打开 https://oss.console.aliyun.com → 点击「**+ 创建 Bucket**」：

| 字段 | 推荐值 | 说明 |
|---|---|---|
| **Bucket 名称** | `feedback-chinago` | 必须**全局唯一**（撞名会报错，加数字后缀） |
| **地域** | `华东1（杭州）` | 必选这个或下面 3 个之一 |
|              | `华东2（上海）` |    |
|              | `华北2（北京）` |    |
|              | `华南1（深圳）` |    |
| **存储类型** | **标准存储** |  |
| **读写权限** | **公共读** | ⭐ 必选！这样阿里云盘 APP 能看到文件 |
| **版本控制** | 不开通 |  |
| **同城冗余** | 本地冗余存储（LRS） | 省钱 |

点「**确定**」→ Bucket 创建完成。

> ⚠️ **读写权限必须选「公共读」**，否则阿里云盘 APP 看不了文件。
> 如果你担心公共读的安全性，可以选私有读写 + 在 FC 函数配置 RAM 角色授权（高级玩法，本指南从略）。

---

## 📍 步骤 3：创建 AccessKey

打开 https://ram.console.aliyun.com/manage/accesskey

1. 点击右上角「**+ 创建 AccessKey**」
2. 弹出安全提示 → 选择「**继续使用 AccessKey**」（不要选「开始使用临时 AccessKey」，那个只用于临时场景）
3. 完成手机/邮箱验证
4. **关键步骤**：记下两个值并保存！
   - **AccessKey ID**：`LTAI5txxxxxxxxxxxxx`
   - **AccessKey Secret**：`xxxxxxxxxxxxxxxxxxxxxx` ⚠️ **这个只显示一次！关掉就看不到了！**

> 🔒 **安全提醒**：这个 AccessKey 有你 OSS Bucket 的完整读写权限，**泄露 = 数据可能被删**。
>
> 如果你不放心，可以给这个 AccessKey 加最小权限策略（只授权 feedback-chinago Bucket）：
>
> ```json
> {
>   "Version": "1",
>   "Statement": [{
>     "Effect": "Allow",
>     "Action": ["oss:PutObject", "oss:GetObject"],
>     "Resource": ["acs:oss:*:*:feedback-chinago", "acs:oss:*:*:feedback-chinago/*"]
>   }]
> }
> ```
>
> 在 RAM 控制台 → 权限管理 → 新建权限策略 → 粘贴 → 给 AccessKey 授予这条策略。

---

## 📍 步骤 4：部署 FC 函数

### 4.1 打包函数代码

打开项目目录 `go-game-prototype/serverless/fc/`，在 PowerShell / Git Bash 里：

```bash
cd go-game-prototype/serverless/fc

# 装依赖（仅 aliyun-oss SDK）
npm install --production

# 打包成 zip（Windows PowerShell）:
Compress-Archive -Path index.js,node_modules,package.json -DestinationPath chinago-feedback.zip

# 或 Git Bash:
# zip -r chinago-feedback.zip index.js node_modules package.json
```

得到一个 `chinago-feedback.zip`，约 5MB。

### 4.2 上传到函数计算

打开 https://fc.console.aliyun.com → 函数 → 创建函数：

| 字段 | 推荐值 |
|---|---|
| **创建方式** | 使用**代码包**创建 |
| **函数名称** | `chinago-feedback` |
| **运行时** | **Node.js 20**（或 Node.js 18） |
| **代码包** | 上传 `chinago-feedback.zip` |
| **监听端口** | 9000（默认） |

点「**创建**」。

### 4.3 配置环境变量

进入函数详情页 → 「**配置**」标签 → 「**环境变量**」：

| Key | Value（填你自己的） |
|---|---|
| `OSS_REGION` | `oss-cn-hangzhou`（你的 Bucket 地域，杭州 = `oss-cn-hangzhou`，北京 = `oss-cn-beijing`，深圳 = `oss-cn-shenzhen`） |
| `OSS_BUCKET` | `feedback-chinago`（你的 Bucket 名） |
| `ACCESS_KEY_ID` | 第 3 步拿到的 AccessKey ID |
| `ACCESS_KEY_SECRET` | 第 3 步拿到的 AccessKey Secret |

点「**保存**」。

### 4.4 创建 HTTP 触发器

进入函数详情页 → 「**触发器**」标签 → 「**+ 创建触发器**」：

| 字段 | 推荐值 |
|---|---|
| **触发器类型** | HTTP 触发器 |
| **认证方式** | **匿名** ⭐ 必选！这样游戏客户端不需要带任何密钥 |
| **请求方法** | POST, OPTIONS（勾上这两个） |
| **集成响应** | （保持默认） |

点「**确定**」。

> 🎉 创建成功后，你会看到一个 **HTTP 触发器 URL**，类似：
> ```
> https://chinago-feedback-xxxxx.cn-hangzhou.fcapp.run
> ```
> **把这个 URL 复制下来**，下一步要用。

### 4.5 验证 FC 函数（可选）

浏览器打开 https://<你的 URL>，应该看到 `{"error":"Only POST allowed"}`，说明函数已经部署成功。

### 4.6 ⚠️ 重要：上传代码包后必须「激活」才生效

> **关键坑**：FC Console 上传新代码包后**不会自动跑新代码**，必须手动「激活」新版本（或点「发布新版本」），否则 HTTP 请求还会打到旧版本上，看起来"代码没生效"。

激活有 2 种方式（任选其一即可）：

#### 方式 A：在「代码」页面激活（推荐，最常用）

上传完 zip 后，页面会自动停留在「代码」标签 → 顶部会出现蓝色提示条 **"已上传新版本 code-XXX"** → 该提示条右侧有「**发布新版本**」按钮：

```
┌────────────────────────────────────────────────────────┐
│  ⓘ 已上传新版本 code-2                            [发布新版本] │  ← 点这里
├────────────────────────────────────────────────────────┤
│  index.js  package.json  node_modules/  ...              │
└────────────────────────────────────────────────────────┘
```

点击「**发布新版本**」→ 弹出确认框 → 填个版本说明（可填"修复字段大小写兼容"）→ 点「**确定**」。

> ⚠️ 按钮文字有时也叫「**激活此版本**」或「**设为 Latest**」，意思都一样。

#### 方式 B：在「版本管理」页面激活

左侧菜单 → 「**版本管理**」（在「代码」下方）→ 列表里会显示所有历史版本 → 找刚上传的那个 → 右侧操作列点「**激活**」链接：

```
┌────────────────────────────────────────────────────────────────────┐
│  版本号        创建时间              状态        操作                │
├────────────────────────────────────────────────────────────────────┤
│  code-1       2026-09-05 15:30     ★ 当前      [查看代码]          │
│  code-2       2026-09-05 18:25      历史      [激活] [查看代码]   │  ← 点这里
└────────────────────────────────────────────────────────────────────┘
```

#### 验证激活成功

激活后 **无需改任何配置**，HTTP 触发器 URL 保持不变，但请求会自动打到新版本。验证方法：

1. **快速测试**：浏览器打开你的 FC URL → 应该看到 `{"error":"Only POST allowed"}`（新版默认行为）
2. **真实测试**：`curl -X POST <url> -H "Content-Type: application/json" -d '{"feedbackId":"test","submittedAtUtc":"2026-09-05T10:00:00Z","title":"激活验证"}'` → 看到 `{"ok":true,...}` = 成功

> 💡 如果 curl 仍然返回旧的错误（比如 `Missing feedbackId`），说明新版本没激活，再回看本页。

---

## 📍 步骤 5：配置游戏客户端

在你**开发者电脑**上：

1. 打开文件资源管理器，地址栏输入：
   ```
   %USERPROFILE%\Documents\ChinaGo
   ```
   回车

2. 如果**没有** `config.json` 文件，新建一个，内容：
   ```json
   {
     "feedbackEndpoint": "https://chinago-feedback-xxxxx.cn-hangzhou.fcapp.run"
   }
   ```
   > 把 URL 替换成你第 4.4 步拿到的真实 URL。

3. 如果**已有** `config.json`，加一行：
   ```json
   {
     "feedbackEndpoint": "https://chinago-feedback-xxxxx.cn-hangzhou.fcapp.run"
   }
   ```

4. **重启游戏** → 顶栏「📨 反馈」→ 提交一条测试反馈

5. 等 5 秒 → 打开 https://oss.console.aliyun.com → 进入 Bucket `feedback-chinago` → 文件管理 → 应该看到 `feedback/今天日期/fb-xxxxxxxx.json` ✅

6. 打开**阿里云盘 APP** → 我的 → 资源库 → 看到 `feedback-chinago` Bucket → 进入看到反馈文件 ✅

---

## 🎉 大功告成！

现在玩家在**任何电脑**提交反馈，都会自动同步到你的阿里云 OSS / 阿里云盘。

### 怎么看反馈的 3 种方式

| 方式 | 推荐场景 |
|---|---|
| **阿里云盘 APP** | 手机看，随时随地 |
| **OSS 控制台** | 电脑看，下载 JSON |
| **游戏开发者后台**（Ctrl+Shift+D） | 看带 UI 的详情 + 一键复制 Markdown |

### 关闭同步

如果哪天你想停用，删掉 `config.json` 里的 `feedbackEndpoint` 行（或整个文件），重启游戏即可。反馈继续本地保存，不上传。

---

## 🆘 故障排查

| 症状 | 原因 | 解决 |
|---|---|---|
| 提交反馈后游戏提示 `❌ 保存失败` | 本地 JSON 写失败（磁盘满 / 权限） | 检查 Documents 目录权限、剩余空间 |
| 后台显示 `💾 仅本地` 而不是 `☁️ 已同步` | FC 函数挂了 / 网络不通 / endpoint URL 错 | 看下面「调试 FC」 |
| 后台显示 `⏳ 待传` | 上传过程出错 | 重启游戏 → 再提交一条 |
| OSS 控制台看不到文件 | OSS 地域选错 / Bucket 名写错 | 检查步骤 2 的环境变量 |
| 阿里云盘看不到 OSS | OSS 地域不在 [杭州/上海/北京/深圳] | 重建 Bucket 到这 4 个地域之一 |

### 调试 FC 函数

打开函数计算控制台 → 进入函数 → 「**调用日志**」→ 看错误堆栈。

常见错误：

1. **`FC environment variables not configured`** → 环境变量没配，回到步骤 4.3
2. **`NoSuchBucket`** → Bucket 名写错，回到步骤 4.3 检查 `OSS_BUCKET`
3. **`AccessDenied`** → AccessKey 错 / 没给 OSS 权限 → 重做步骤 3

### 手动测试 FC endpoint

打开 PowerShell：

```powershell
$body = @{
  feedbackId = "fb-test12345"
  submittedAtUtc = "2026-09-05T10:00:00Z"
  title = "测试反馈"
  detail = "这是从 PowerShell 发的"
  category = "其他"
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Method Post -Uri "https://你的FC URL" -Body $body -ContentType "application/json"
```

返回 `{"ok":true,"path":"feedback/2026-09-05/fb-test12345.json",...}` 说明正常。
然后去 OSS 控制台应该看到 `feedback/2026-09-05/fb-test12345.json`。

---

## 📋 维护清单

| 频率 | 任务 |
|---|---|
| 每周 | 打开阿里云盘看下新反馈 |
| 每月 | 检查 OSS Bucket 容量（应该 < 1MB） |
| 每年 | 续费 AccessKey（默认永久有效，不需要续） |
| 备份 | 在 OSS 控制台 → Bucket → 跨区域复制 → 自动备份到另一个 Bucket |

---

## 💡 进阶（可选）

- **多设备推送**：阿里云盘 APP 支持**文件更新提醒**，OSS 写新文件时手机会弹通知 ✅
- **团队协作**：把另一个阿里云账号加到 Bucket 访问者 → 他也能在阿里云盘看反馈
- **数据分析**：用 DataWorks 或 Quick BI 连 OSS → 自动出反馈分类统计图表
- **海外部署**：如果你主要用户在海外，FC 改用 `新加坡` 地域 + OSS 同步到新加坡 Bucket

---

需要任何帮助就问！🚀
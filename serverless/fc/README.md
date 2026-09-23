# 阿里云函数计算 · 中国围棋反馈接收

## 文件清单

```
fc/
├── index.js       FC 入口函数（HTTP 触发器）
├── package.json   依赖（ali-oss）
└── README.md      本文件
```

## 快速部署（5 步）

### 1. 开通函数计算 FC
打开 https://fc.console.aliyun.com → 首次提示「开通服务」→ 同意（**免费**，每月 100 万次免费额度）

### 2. 创建 OSS Bucket
打开 https://oss.console.aliyun.com → 创建 Bucket：
- **Bucket 名称**：`feedback-chinago`（你自定义，但要记住）
- **地域**：选「华东1（杭州）」或「华北2（北京）」或「华南1（深圳）」（这 3 个地域被阿里云盘资源库支持）
- **存储类型**：标准存储
- **读写权限**：**公共读**（这样阿里云盘 APP 能看到文件；如果选私有读写，阿里云盘看不到，需要额外配 RAM 角色授权）

### 3. 创建 AccessKey
打开 https://ram.console.aliyun.com/manage/accesskey → 创建 AccessKey：
- 选「继续使用 AccessKey」→ 填手机验证码 → 生成
- **记下 AccessKey ID 和 AccessKey Secret**（Secret 只显示一次！）

⚠️ 安全提示：AccessKey 有你 OSS 的完整读写权限，建议**给这个 Key 加最小权限**（只授权 feedback-chinago Bucket），详见部署指南。

### 4. 部署函数
打开 https://fc.console.aliyun.com → 函数 → 创建函数：
- **运行时**：Node.js 18 或 Node.js 20
- **代码上传方式**：通过代码包上传
- **上传 zip 包**：
  1. 在本目录执行 `npm install --production`
  2. 把 `index.js` + `node_modules/` + `package.json` 打成 zip：`zip -r chinago-feedback.zip . -x "*.md"`
  3. 上传这个 zip
- **触发器**：创建 HTTP 触发器
  - 认证方式：**匿名**（这样游戏客户端不需要带任何密钥就能 POST）
  - 请求方法：POST, OPTIONS
- **环境变量**（函数详情 → 配置 → 环境变量）：
  - `OSS_REGION`：例如 `oss-cn-hangzhou`（Bucket 地域）
  - `OSS_BUCKET`：例如 `feedback-chinago`
  - `ACCESS_KEY_ID`：你的 AccessKey ID
  - `ACCESS_KEY_SECRET`：你的 AccessKey Secret

### 5. 配置游戏客户端
部署成功后，FC 会给一个 HTTP 触发器 URL，类似：
```
https://chinago-feedback-xxx.cn-hangzhou.fcapp.run
```
把这个 URL 填到玩家电脑的：
```
%USERPROFILE%\Documents\ChinaGo\config.json
```
文件内容（如果不存在就新建）：
```json
{
  "feedbackEndpoint": "https://chinago-feedback-xxx.cn-hangzhou.fcapp.run"
}
```
重启游戏 → 提交反馈 → 几秒后登录阿里云盘 APP → 我的资源库 → 看到反馈文件 ✅

## 看反馈的 3 种方式

### 方式 1：阿里云盘 APP（最方便）
打开阿里云盘 APP → 我的 → 资源库 → 关联的 OSS Bucket → `feedback/` 文件夹

### 方式 2：OSS 控制台
打开 https://oss.console.aliyun.com → 进入 Bucket → 文件管理 → `feedback/` 文件夹

### 方式 3：游戏开发者后台（最详细）
游戏主窗口按 **Ctrl+Shift+D** → 开发者后台 → 看本地所有反馈（已上传的有 ☁️ 标记）

## 费用估算（个人项目几乎免费）

| 服务 | 免费额度 | 单条反馈成本 |
|---|---|---|
| 函数计算 FC | 100 万次/月 | ≈ 0.0001 元 |
| OSS 标准存储 | 5 GB | ≈ 0.012 元/GB/月 |
| OSS 读写请求 | GET 100 万次/月 | ≈ 0.01 元/万次 |

单条反馈约 2KB，1000 条 = 2MB ≈ 永久免费。
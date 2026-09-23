# 开发者指南 · 反馈系统

> ⚠️ **仅供维护者阅读**。本文档说明如何查看用户提交的反馈。
> 普通用户**看不到**后台入口，仅顶栏「📨 反馈」按钮可提交反馈。

---

## 反馈存哪里

每条反馈保存为独立 JSON 文件：

```
%USERPROFILE%\Documents\ChinaGo\Feedback\
  ├ feedback-20260905-143201-a3b4c5d6.json
  ├ feedback-20260905-150312-b7c8d9e0.json
  └ ...
```

文件名格式：`feedback-{yyyyMMdd-HHmmss}-{id8}.json`

---

## 开发者查看反馈的 3 种方式

### ✅ 方式 1（推荐）：开发者后台

启动参数加 `feedback`：

```
"安装目录\GoGame.exe" feedback
```

或在已安装程序里：

```
"C:\Users\Administrator\AppData\Local\Programs\ChinaGo\GoGame.exe" feedback
```

弹出深色后台窗口：
- 3 个大数字：总条数 / 今日新增 / 本周新增
- 6 个分类 chip（按数量降序）：KataGo 异常 / 界面卡顿 / 复盘 / 教学 / 安装 / 其他
- 工具栏 4 个：🔄 刷新 / 📋 导出全量 Markdown / 📁 打开反馈目录 / 📝 复制当前条 Markdown
- 左侧列表 + 右侧详情（标题 / 描述 / 环境信息 / KataGo 日志 / 对局状态）

### 方式 2：桌面快捷方式（开发日常用）

右键桌面 → 新建快捷方式：

| 项 | 值 |
|---|---|
| 目标 | `"C:\Users\Administrator\AppData\Local\Programs\ChinaGo\GoGame.exe" feedback` |
| 名称 | 📊 反馈后台 |
| 图标 | 任意（建议用 `app.ico`） |

以后双击桌面图标就能开后台。

### 方式 3：直接看 JSON

资源管理器地址栏粘贴 `%USERPROFILE%\Documents\ChinaGo\Feedback\` 回车。

适合批量脚本处理（grep / jq / 写正则提取问题模式）。

---

## 反馈 JSON 字段说明

```json
{
  "feedbackId": "a3b4c5d6",
  "submittedAt": "2026-09-05T14:32:01",
  "category": "KataGo 异常",
  "title": "KataGo 启动后无响应",
  "description": "点了 AI 提示按钮后 KataGo 没反应...",
  "contact": {
    "email": "",
    "steam": "user123"
  },
  "environment": {
    "appVersion": "1.0.0",
    "osVersion": "Microsoft Windows 11 Pro 10.0.22631",
    "dotnetVersion": ".NET 10.0.11",
    "kataGoPath": "C:\\Tools\\KataGo\\katago.exe",
    "kataGoNetwork": "kata1-b18c384-nn...",
    "boardSize": "19x19",
    "moveCount": 47,
    "lastMove": "B R17",
    "toMove": "白",
    "kataGoReady": true,
    "kataGoLogTail": "...最近 30 行日志..."
  }
}
```

---

## 反馈数据隐私原则

- 反馈**只保存在本机**，不上云
- 普通用户**看不到开发者后台入口**（顶栏已隐藏）
- 即使用户猜到 `--feedback` 命令行，目前单机场景下也只会看到自己机器上的反馈（不会泄露别人）
- 未来**如需上云同步**，必须在 `FeedbackViewerWindow` 启动时加权限校验（密码 / 设备指纹 / 签名 token）

---

## 把反馈发给我（开发者视角）

1. 开后台 → 选中要发的反馈条目
2. 点「📝 复制当前条 Markdown」→ 剪贴板拿到一份结构化 Markdown
3. 直接贴到对话里给我，我就能看到完整问题 + 环境信息 + KataGo 日志

或者：
1. 后台工具栏点「📋 导出全量 Markdown」→ 保存为 `feedbacks-yyyymmdd.md`
2. 拖到对话里

---

## 反馈系统文件清单

| 文件 | 作用 |
|---|---|
| `desktop/FeedbackService.cs` | 模型 + JSON 读写 + 统计 API + Markdown 格式化 |
| `desktop/FeedbackWindow.xaml/.cs` | 用户反馈提交表单（顶栏📨反馈触发） |
| `desktop/FeedbackSuccessWindow.xaml/.cs` | 提交成功致谢弹窗 |
| `desktop/FeedbackViewerWindow.xaml/.cs` | 开发者反馈后台（仅 `--feedback` 命令行） |
| `desktop/MainWindow.xaml.cs` | `LaunchFeedbackViewer()` 入口（仅命令行触发，无 UI 暴露） |

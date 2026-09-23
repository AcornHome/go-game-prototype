'use strict';

/**
 * 中国围棋 · 用户反馈服务（阿里云函数计算 FC）
 * =====================================================
 *
 * 路由：
 *   GET  /            → 浏览器友好反馈看板（HTML + JS）
 *   GET  /api/list    → JSON 反馈列表（看板前端调用）
 *   GET  /api/health  → 健康检查（游戏内「🧪 测试连通性」按钮用）
 *   POST /            → 接收游戏端提交的反馈，写入 OSS Bucket
 *
 * 工作流程：
 *   - 接收游戏客户端 POST 来的反馈 JSON
 *   - 解析校验（feedbackId + submittedAtUtc 必须存在，兼容 PascalCase + camelCase）
 *   - 用环境变量里的 AccessKey 写阿里云 OSS
 *   - 文件路径：feedback/YYYY-MM-DD/fb-xxxxxxxx.json
 *
 * 云反馈看板：
 *   - 浏览器打开 https://<fc-url> → 自动加载最新反馈
 *   - 看板前端调 /api/list 拉 JSON（一次拉所有反馈 + 内容，预读性能足够个人项目）
 *   - OSS 文件命名按天分目录，前端按日期分组展示
 *
 * 免费额度：阿里云函数计算 100 万次/月 + OSS 标准存储 5GB，个人项目几年用不完
 */

const OSS = require('ali-oss');

// CORS：浏览器看板会发 fetch，需要跨域；游戏客户端是 .NET WPF 不需要 CORS
const CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
};

// 浏览器看板 HTML（单文件，零依赖，直接走 GET / 返回）
// 设计原则：
//   - 自适应深/浅色主题（用 prefers-color-scheme 媒体查询，不写死颜色）
//   - 按日期分组，每天一个 section
//   - 点击标题展开详情（不用多页跳转）
//   - 移动端也友好（用了 flexbox / clamp()）
const HTML_DASHBOARD = `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<title>📬 中国围棋 · 反馈中心</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  :root {
    --bg: #f8f8f5;
    --fg: #1a1a1a;
    --card: #ffffff;
    --border: #e6e4dd;
    --accent: #8b6914;
    --muted: #6b6b6b;
    --tag: #e8e3d2;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg: #1f1d18;
      --fg: #e8e4d9;
      --card: #2a2722;
      --border: #3a352d;
      --accent: #d4a946;
      --muted: #a09a8b;
      --tag: #3a352d;
    }
  }
  body {
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif;
    background: var(--bg);
    color: var(--fg);
    line-height: 1.6;
    padding: 1rem;
    max-width: 880px;
    margin: 0 auto;
  }
  header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 1.5rem;
    padding-bottom: 1rem;
    border-bottom: 2px solid var(--border);
  }
  h1 { font-size: 1.4rem; color: var(--accent); }
  button {
    background: var(--accent);
    color: white;
    border: none;
    padding: 0.5rem 1rem;
    border-radius: 6px;
    font-size: 0.9rem;
    cursor: pointer;
    font-family: inherit;
  }
  button:hover { opacity: 0.85; }
  button:disabled { opacity: 0.5; cursor: not-allowed; }
  .stats {
    display: flex;
    gap: 1rem;
    margin-bottom: 1.5rem;
    flex-wrap: wrap;
  }
  .stat {
    background: var(--card);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: 0.75rem 1rem;
    flex: 1;
    min-width: 140px;
  }
  .stat-num { font-size: 1.6rem; font-weight: 700; color: var(--accent); }
  .stat-label { font-size: 0.85rem; color: var(--muted); }
  .day {
    margin-bottom: 2rem;
  }
  .day-title {
    font-size: 1rem;
    color: var(--muted);
    margin-bottom: 0.5rem;
    padding-left: 0.5rem;
    border-left: 3px solid var(--accent);
  }
  .card {
    background: var(--card);
    border: 1px solid var(--border);
    border-radius: 8px;
    margin-bottom: 0.5rem;
    overflow: hidden;
    transition: box-shadow 0.2s;
  }
  .card:hover { box-shadow: 0 2px 8px rgba(0,0,0,0.08); }
  .card-head {
    padding: 0.75rem 1rem;
    cursor: pointer;
    display: flex;
    align-items: center;
    gap: 0.75rem;
  }
  .card-head:hover { background: var(--tag); }
  .tag {
    display: inline-block;
    padding: 0.15rem 0.5rem;
    background: var(--tag);
    border-radius: 4px;
    font-size: 0.75rem;
    color: var(--accent);
    white-space: nowrap;
  }
  .card-title { flex: 1; font-weight: 500; }
  .card-meta { font-size: 0.8rem; color: var(--muted); white-space: nowrap; }
  .card-body {
    padding: 1rem;
    border-top: 1px solid var(--border);
    font-size: 0.9rem;
    color: var(--muted);
    white-space: pre-wrap;
    word-break: break-word;
    display: none;
  }
  .card.open .card-body { display: block; }
  .card.open .card-head { background: var(--tag); }
  .empty {
    text-align: center;
    padding: 3rem 1rem;
    color: var(--muted);
  }
  .err {
    background: #fee;
    border: 1px solid #fcc;
    color: #c33;
    padding: 1rem;
    border-radius: 6px;
    margin-bottom: 1rem;
  }
  @media (prefers-color-scheme: dark) {
    .err { background: #3a1a1a; border-color: #5a2a2a; color: #f88; }
  }
</style>
</head>
<body>
<header>
  <h1>📬 中国围棋 · 反馈中心</h1>
  <button id="refresh">🔄 刷新</button>
</header>
<div id="root">加载中…</div>

<script>
// 数据由 FC 服务端内嵌注入（避免 fetch API 在 Content-Disposition: attachment 下失败）
const DATA = "__CHINAGO_DATA__";

const root = document.getElementById('root');
const btn = document.getElementById('refresh');

function escapeHtml(s) {
  if (s == null) return '';
  return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[c]);
}

function render(data) {
  if (data.loadError) {
    root.innerHTML = '<div class="err">⚠️ 加载失败：' + escapeHtml(data.loadError) + '<br>请稍后刷新页面重试</div>';
    return;
  }

  const list = (data.feedbacks || []).slice();

  // 统计
  const byCat = {};
  list.forEach(f => {
    const cat = f.Category || f.category || '未分类';
    byCat[cat] = (byCat[cat] || 0) + 1;
  });

  let stats = '<div class="stats">';
  stats += '<div class="stat"><div class="stat-num">' + list.length + '</div><div class="stat-label">总反馈</div></div>';
  Object.entries(byCat).slice(0, 3).forEach(([k, v]) => {
    stats += '<div class="stat"><div class="stat-num">' + v + '</div><div class="stat-label">' + escapeHtml(k) + '</div></div>';
  });
  stats += '</div>';

  if (list.length === 0) {
    root.innerHTML = stats + '<div class="empty">📭 还没有反馈<br><small>用游戏提交一条试试</small></div>';
    return;
  }

  // 按日期分组
  const byDay = {};
  list.forEach(f => {
    const ts = f.submittedAtUtc || f.SubmittedAtUtc || '';
    const day = ts.slice(0, 10);
    (byDay[day] = byDay[day] || []).push(f);
  });

  let html = stats;
  Object.entries(byDay).forEach(([day, items]) => {
    html += '<div class="day"><div class="day-title">📅 ' + day + '（' + items.length + ' 条）</div>';
    items.forEach(f => {
      const cat = f.Category || f.category || '未分类';
      const title = f.Title || f.title || '(无标题)';
      const detail = f.Detail || f.detail || '';
      const ts = f.submittedAtUtc || f.SubmittedAtUtc || '';
      const time = ts.slice(11, 16);
      const id = f.feedbackId || f.FeedbackId || '';
      const env = f.Environment || f.environment || {};

      // 联系方式：兼容 camelCase（云端实际形态）+ PascalCase（旧反馈文件）
      const contact = f.contact || f.Contact || {};
      const email = contact.email || contact.Email || '';
      const steam = contact.steamHandle || contact.SteamHandle || '';

      html += '<div class="card" data-open="0">';
      html += '<div class="card-head">';
      html += '<span class="tag">' + escapeHtml(cat) + '</span>';
      html += '<span class="card-title">' + escapeHtml(title) + '</span>';
      // 头部小字：填了联系方式 → 显示「123456」(关键线索，让你不打开详情也能看到)
      const authorHint = email || steam || '';
      html += '<span class="card-meta">' + time + (authorHint ? ' · ' + escapeHtml(email || steam) : '') + '</span>';
      html += '</div>';
      html += '<div class="card-body">';
      if (detail) html += '<div style="margin-bottom:0.5rem">' + escapeHtml(detail) + '</div>';

      // 联系方式块：邮箱 + Steam 主名（至少有一个才显示）
      if (email || steam) {
        html += '<div style="background:var(--bg);padding:0.5rem 0.75rem;border-radius:6px;margin:0.5rem 0;font-size:0.9rem">';
        html += '<div style="font-weight:600;margin-bottom:0.25rem;color:var(--accent)">📬 联系方式</div>';
        if (email) html += '<div>📧 邮箱：' + escapeHtml(email) + '</div>';
        if (steam) html += '<div>🎮 Steam 主名：' + escapeHtml(steam) + '</div>';
        html += '</div>';
      }

      html += '<div style="font-size:0.8rem;margin-top:0.5rem;padding-top:0.5rem;border-top:1px dashed var(--border)">';
      html += '🆔 ' + escapeHtml(id) + '<br>';
      if (env.AppVersion) html += '📦 App: ' + escapeHtml(env.AppVersion) + '<br>';
      if (env.OsVersion) html += '💻 ' + escapeHtml(env.OsVersion) + '<br>';
      if (env.KataGoPath) html += '🤖 KataGo: ' + escapeHtml(env.KataGoPath) + '<br>';
      const game = f.Game || f.game;
      if (game) html += '♟️ ' + escapeHtml(game.BoardSize || game.boardSize || '') + ', ' + (game.MoveCount || game.moveCount || 0) + ' 手';
      html += '</div></div></div>';
    });
    html += '</div>';
  });

  root.innerHTML = html;
}

// 「刷新」按钮 = 整页 reload（避免 fetch 走 attachment 受阻）
btn.addEventListener('click', () => location.reload());

// 折叠卡片（事件委托，绕开 onclick 内联属性转义）
document.addEventListener('click', e => {
  const head = e.target.closest('.card-head');
  if (head) head.parentElement.classList.toggle('open');
});

// 初始渲染（数据由 FC 服务端注入）
render(DATA);

// 自动刷新：整页 reload（60 秒一次）
setInterval(() => location.reload(), 60000);
</script>
</body>
</html>`;

// ====================================================================
// 主入口
// ====================================================================
exports.handler = async (event, context) => {
  // 1) 解析 event 为统一对象（Buffer / string / object 三态）
  let evt;
  try {
    if (Buffer.isBuffer(event)) {
      const s = event.toString('utf8');
      evt = s ? JSON.parse(s) : {};
    } else if (typeof event === 'string') {
      evt = event ? JSON.parse(event) : {};
    } else if (event && typeof event === 'object') {
      evt = event;
    } else {
      evt = {};
    }
  } catch (parseErr) {
    return { statusCode: 400, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
      body: JSON.stringify({ ok: false, error: 'Event parse failed', detail: String(parseErr) }) };
  }

  // 2) 提取 method + path（兼容 v1 / v3 / web function 三种 event 格式）
  const method = (
    evt.httpMethod ||
    evt.method ||
    evt?.requestContext?.http?.method ||
    'POST'
  ).toUpperCase();
  const path = (
    evt.path ||
    evt.rawPath ||
    evt?.requestContext?.http?.path ||
    '/'
  ).split('?')[0];   // 去 query string

  // 3) OPTIONS 预检
  if (method === 'OPTIONS') {
    return { statusCode: 200, headers: CORS, body: '' };
  }

  // 4) 路由分发
  try {
    if (method === 'GET' && (path === '/' || path === '')) {
      return await handleGetDashboard();
    }
    if (method === 'GET' && path === '/api/list') {
      return await handleGetList();
    }
    if (method === 'GET' && path === '/api/health') {
      return {
        statusCode: 200, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
        body: JSON.stringify({ ok: true, message: 'Cloud feedback endpoint ready' }),
      };
    }
    if (method === 'POST' && (path === '/' || path === '/api/feedback')) {
      return await handlePostFeedback(evt);
    }
    return {
      statusCode: 404, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
      body: JSON.stringify({ ok: false, error: 'Not Found', method, path }),
    };
  } catch (err) {
    console.error('FC handler error:', err);
    return {
      statusCode: 500, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
      body: JSON.stringify({ ok: false, error: err.message || String(err) }),
    };
  }
};

// ====================================================================
// GET / — 浏览器看板（**数据直接内嵌**，完全不走前端 fetch）
// 设计原因：阿里云 FC HTTP 触发器会强制给所有响应加
//   Content-Disposition: attachment，导致浏览器 fetch 失败。
// 解决方案：FC 函数在服务端一次性读 OSS，把反馈列表嵌入 HTML 返回。
//   浏览器只渲染，不再依赖 fetch API。
// ====================================================================
async function handleGetDashboard() {
  let feedbacks = [];
  let loadError = null;
  try {
    feedbacks = await fetchAllFeedbacks();
  } catch (err) {
    loadError = err.message || String(err);
  }

  // 数据注入到 HTML（JSON 序列化后安全地嵌进 <script>）
  // 用 toJSON 序列化避免循环引用和 undefined 被丢
  const payload = JSON.stringify({ feedbacks, loadError })
    .replace(/</g, '\\u003c')
    .replace(/>/g, '\\u003e')
    .replace(/&/g, '\\u0026')
    .replace(/\u2028/g, '\\u2028')
    .replace(/\u2029/g, '\\u2029');

  // 把「__CHINAGO_DATA__」占位符替换成实际数据
  const html = HTML_DASHBOARD.replace('"__CHINAGO_DATA__"', payload);

  return {
    statusCode: 200,
    headers: {
      ...CORS,
      'Content-Type': 'text/html; charset=utf-8',
      'Content-Disposition': 'inline',
      'Cache-Control': 'no-store, no-cache, must-revalidate',
    },
    body: html,
  };
}

// ====================================================================
// GET /api/list — 反馈 JSON 列表（保留作为 API，dashboard 不依赖它）
// ====================================================================
async function handleGetList() {
  const client = getOssClient();
  if (!client) {
    return {
      statusCode: 500, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
      body: JSON.stringify({ ok: false, error: 'OSS client init failed (env vars)' }),
    };
  }

  try {
    const feedbacks = await fetchAllFeedbacks();
    return {
      statusCode: 200,
      headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline', 'Cache-Control': 'no-store' },
      body: JSON.stringify({ ok: true, feedbacks, count: feedbacks.length }),
    };
  } catch (err) {
    return {
      statusCode: 500, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
      body: JSON.stringify({ ok: false, error: err.message || String(err) }),
    };
  }
}

/**
 * 从 OSS 拉取所有反馈 JSON（共享给 dashboard 内嵌和 /api/list）
 * 返回按时间倒序的数组
 */
async function fetchAllFeedbacks() {
  const client = getOssClient();
  if (!client) throw new Error('OSS client init failed (env vars)');

  const allFeedbacks = [];
  let continuationToken = null;
  do {
    const result = await client.list({
      prefix: 'feedback/',
      'max-keys': 1000,
      continuationToken,
    });

    // 并发读所有文件（OSS 读延迟主要是 RTT，并发能省一半时间）
    const reads = (result.objects || [])
      .filter(obj => obj.name.endsWith('.json'))
      .map(async obj => {
        try {
          const file = await client.get(obj.name);
          const data = JSON.parse(file.content.toString('utf8'));
          return data;
        } catch (err) {
          console.error(`Failed to read ${obj.name}:`, err.message);
          return null;
        }
      });
    const results = await Promise.all(reads);
    allFeedbacks.push(...results.filter(Boolean));

    continuationToken = result.nextContinuationToken;
  } while (continuationToken);

  // 按 submittedAtUtc 时间倒序（最新的在最前）
  allFeedbacks.sort((a, b) => {
    const ta = a.submittedAtUtc || a.SubmittedAtUtc || '';
    const tb = b.submittedAtUtc || b.SubmittedAtUtc || '';
    return tb.localeCompare(ta);
  });

  return allFeedbacks;
}

// ====================================================================
// POST / — 接收并写入反馈
// ====================================================================
async function handlePostFeedback(evt) {
  // 1) 解析 body
  let rawBody = evt.body || evt.rawBody || evt.payload || '';
  if (evt.isBase64Encoded && rawBody) {
    rawBody = Buffer.from(rawBody, 'base64').toString('utf8');
  }
  if (!rawBody) {
    return {
      statusCode: 400, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
      body: JSON.stringify({ ok: false, error: 'Empty body', evtKeys: Object.keys(evt).slice(0, 20) }),
    };
  }

  const body = JSON.parse(rawBody);

  // 2) 字段兼容（PascalCase + camelCase 都能接受）
  const feedbackId = body.feedbackId || body.FeedbackId;
  const submittedAtUtc = body.submittedAtUtc || body.SubmittedAtUtc;

  if (!feedbackId) {
    return {
      statusCode: 400, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
      body: JSON.stringify({ ok: false, error: 'Missing feedbackId' }),
    };
  }
  if (!submittedAtUtc) {
    return {
      statusCode: 400, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
      body: JSON.stringify({ ok: false, error: 'Missing submittedAtUtc' }),
    };
  }

  // 3) 把所有字段名规范成 camelCase（写出去的反馈文件干净统一）
  const normalized = camelizeKeys({ ...body, feedbackId, submittedAtUtc });

  // 4) 写 OSS
  const client = getOssClient();
  if (!client) {
    return {
      statusCode: 500, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
      body: JSON.stringify({ ok: false, error: 'OSS client init failed (env vars)' }),
    };
  }

  const date = new Date(submittedAtUtc);
  const dateStr = isNaN(date.getTime())
    ? new Date().toISOString().slice(0, 10)
    : date.toISOString().slice(0, 10);
  const filename = `feedback/${dateStr}/${feedbackId}.json`;

  await client.put(filename, Buffer.from(JSON.stringify(normalized, null, 2), 'utf-8'), {
    headers: { 'Content-Type': 'application/json; charset=utf-8' },
  });

  return {
    statusCode: 200, headers: { ...CORS, 'Content-Type': 'application/json', 'Content-Disposition': 'inline' },
    body: JSON.stringify({
      ok: true, path: filename, message: '反馈已同步到云端',
      bucket: process.env.OSS_BUCKET, region: process.env.OSS_REGION,
    }),
  };
}

// ====================================================================
// 工具
// ====================================================================

/** 获取或缓存 OSS 客户端（同一实例复用避免重复初始化） */
let _ossClient = null;
function getOssClient() {
  if (_ossClient) return _ossClient;
  const region = process.env.OSS_REGION;
  const bucket = process.env.OSS_BUCKET;
  const accessKeyId = process.env.ACCESS_KEY_ID;
  const accessKeySecret = process.env.ACCESS_KEY_SECRET;
  if (!region || !bucket || !accessKeyId || !accessKeySecret) {
    console.error('OSS env vars missing:', { region, bucket, hasId: !!accessKeyId, hasSecret: !!accessKeySecret });
    return null;
  }
  _ossClient = new OSS({ region, accessKeyId, accessKeySecret, bucket });
  return _ossClient;
}

/**
 * 把对象的所有顶层 key 规范成 camelCase。
 * 旧反馈文件同时含 PascalCase/camelCase 时也能处理：第一次调用就把历史文件清干净。
 */
function camelizeKeys(obj) {
  if (!obj || typeof obj !== 'object') return obj;
  if (Array.isArray(obj)) return obj.map(camelizeKeys);
  const result = {};
  for (const [k, v] of Object.entries(obj)) {
    // 直接小写首字母即可（FeedbackId → feedbackId, SubmittedAtUtc → submittedAtUtc, OSVersion → osVersion）
    const ck = k.charAt(0).toLowerCase() + k.slice(1);
    result[ck] = camelizeKeys(v);
  }
  return result;
}

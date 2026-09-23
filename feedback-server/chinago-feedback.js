'use strict';
/**
 * ChinaGo 玩家反馈接收模块（寄生版）
 * ------------------------------------------------------------------
 * 这个模块被「工程材料库」的 server.js 以 require 方式加载，
 * 只处理两个路径：
 *     POST /chinago/api/feedback   —— 接收玩家反馈，写进 D:\ChinaGo\Feedback\
 *     GET  /chinago/api/health     —— 健康检查
 *
 * 设计原则：
 * 1. 零第三方依赖（和材料库一样是纯 Node）
 * 2. 完全独立：不读不写材料库的 data.json，不参与它的锁和鉴权
 * 3. 任何异常都被吞掉并转成 JSON 错误码，绝不影响材料库主流程
 * 4. 材料库重装/升级后本文件会丢失 → 重新拷贝回来即可（见 Apply Feedback Patch.bat）
 *
 * 落盘结构：
 *   D:\ChinaGo\Feedback\
 *     ├─ index.html              汇总页（每次提交后重建，按时间倒序）
 *     └─ data\2026-09-10\feedback-140233-<id8>.html / .json
 */

var fs = require('fs');
var path = require('path');

var ROOT = process.env.CHINAGO_FEEDBACK_DIR || 'D:/ChinaGo/Feedback';
var DATA_DIR = path.join(ROOT, 'data');
var MAX_BODY = 2 * 1024 * 1024;      // 2 MB
var RATE_MAX = 20;                   // 每 IP 每 60 秒最多 20 条
var RATE_WINDOW = 60;

var _rate = new Map();
var _warned = false;

/* ---------------- 小工具 ---------------- */

function mkdirp(d) { try { fs.mkdirSync(d, { recursive: true }); } catch (e) {} }

function esc(s) {
  return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
    return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
  });
}

// v1.4.1 安全修复：反馈接口是公网暴露的（网云穿隧道），客户端可以任意 POST 一个 html 字段。
// 绝不能原样落盘，否则恶意提交者塞进 <script>/<iframe> 等，开发者双击打开报告时就会在本地浏览器执行。
// 这里做最小化消毒：去掉可执行的标签与事件处理器、javascript: 协议。
function sanitizeHtml(html) {
  return String(html == null ? '' : html)
    .replace(/<\s*(script|iframe|object|embed|style|link|meta|base|form|input|button|img)\b[^>]*>[\s\S]*?<\/\s*\1\s*>|<\s*(script|iframe|object|embed|style|link|meta|base|form|input|button|img)\b[^>]*\/?>/gi, '')
    .replace(/\son\w+\s*=\s*("[^"]*"|'[^']*'|[^\s>]+)/gi, '')
    .replace(/(href|src)\s*=\s*("javascript:[^"]*"|'javascript:[^']*')/gi, '$1="#"');
}

function json(res, code, obj) {
  if (!res || res.writableEnded) return;
  try {
    var s = JSON.stringify(obj);
    res.writeHead(code, {
      'Content-Type': 'application/json; charset=utf-8',
      'Content-Length': Buffer.byteLength(s)
    });
    res.end(s);
  } catch (e) {}
}

function clientIp(req) {
  try {
    var xf = req.headers['x-forwarded-for'];
    if (xf) return String(xf).split(',')[0].trim();
  } catch (e) {}
  try { return req.socket.remoteAddress || 'unknown'; } catch (e) {}
  return 'unknown';
}

function rateOk(ip) {
  var now = Date.now();
  var arr = (_rate.get(ip) || []).filter(function (t) { return now - t < RATE_WINDOW * 1000; });
  if (arr.length >= RATE_MAX) { _rate.set(ip, arr); return false; }
  arr.push(now);
  _rate.set(ip, arr);
  if (_rate.size > 5000) _rate.clear();
  return true;
}

function p2(n) { return (n < 10 ? '0' : '') + n; }

function dayStr(d) { return d.getFullYear() + '-' + p2(d.getMonth() + 1) + '-' + p2(d.getDate()); }
function timeStr(d) { return p2(d.getHours()) + p2(d.getMinutes()) + p2(d.getSeconds()); }
function fullStr(d) { return dayStr(d) + ' ' + p2(d.getHours()) + ':' + p2(d.getMinutes()) + ':' + p2(d.getSeconds()); }

function shortId(s) {
  var v = String(s || '').replace(/[^0-9a-zA-Z]/g, '').slice(-8);
  return v || Math.random().toString(36).slice(2, 10);
}

/* ---------------- 落盘 ---------------- */

function fallbackHtml(p) {
  return '<!doctype html><meta charset="utf-8"><title>' + esc(p.title || '玩家反馈') + '</title>' +
    '<div style="font-family:system-ui;padding:24px;max-width:860px;margin:0 auto">' +
    '<h1>' + esc(p.title || '（无标题）') + '</h1>' +
    '<p style="color:#888">分类：' + esc(p.category || '其他') + ' · 用户：' + esc(p.userName || '匿名') +
    ' · 提交于 ' + esc(p.submittedAtUtc || '') + '</p>' +
    '<pre style="white-space:pre-wrap;background:#f6f6f6;padding:14px;border-radius:6px">' +
    esc(p.detail || '') + '</pre></div>';
}

function save(p) {
  mkdirp(ROOT);
  mkdirp(DATA_DIR);
  var now = new Date();
  var dayDir = path.join(DATA_DIR, dayStr(now));
  mkdirp(dayDir);

  var base = 'feedback-' + timeStr(now) + '-' + shortId(p.feedbackId);
  var htmlPath = path.join(dayDir, base + '.html');
  var jsonPath = path.join(dayDir, base + '.json');

  var meta = {};
  Object.keys(p).forEach(function (k) { meta[k] = p[k]; });
  // v1.4.1 安全修复：客户端传来的 html 必须先消毒（去 <script>/事件处理器），再落盘
  var html = (typeof p.html === 'string' && p.html.length > 0) ? sanitizeHtml(p.html) : fallbackHtml(p);
  delete meta.html;
  meta._savedAt = now.toISOString();

  try {
    fs.writeFileSync(htmlPath, html, 'utf8');
    fs.writeFileSync(jsonPath, JSON.stringify(meta, null, 2), 'utf8');
  } catch (e) {
    if (!_warned) { _warned = true; console.log(new Date().toISOString(), '[chinago] 写入失败:', e && e.message); }
    throw e;
  }
  return htmlPath;
}

/* ---------------- 汇总页 ---------------- */

function refreshIndex() {
  try {
    mkdirp(DATA_DIR);
    var days = [];
    try {
      days = fs.readdirSync(DATA_DIR).filter(function (d) { return /^\d{4}-\d{2}-\d{2}$/.test(d); });
    } catch (e) {}
    days.sort().reverse();

    var rows = [];
    days.forEach(function (day) {
      var dir = path.join(DATA_DIR, day);
      var files = [];
      try {
        files = fs.readdirSync(dir).filter(function (f) { return /\.json$/.test(f); });
      } catch (e) { return; }
      files.sort().reverse();
      files.forEach(function (f) {
        var m = null;
        try { m = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8')); } catch (e) { return; }
        if (!m || typeof m !== 'object') return;
        rows.push({ day: day, href: 'data/' + day + '/' + f.replace(/\.json$/, '.html'), m: m });
      });
    });

    var body = rows.map(function (r) {
      var m = r.m;
      var t = m.submittedAtUtc ? new Date(m.submittedAtUtc) : null;
      var when = (t && !isNaN(t.getTime())) ? fullStr(t) : (m._savedAt ? String(m._savedAt).replace('T', ' ').slice(0, 19) : r.day);
      return '<tr>' +
        '<td class="t">' + esc(when) + '</td>' +
        '<td><span class="tag">' + esc(m.category || '其他') + '</span></td>' +
        '<td class="ti">' + esc(m.title || '（无标题）') + '</td>' +
        '<td>' + esc(m.userName || '匿名') + '<div class="sub">' + esc(m.userId || '') + '</div></td>' +
        '<td>' + esc(m.appVersion || '') + '</td>' +
        '<td>' + esc(m.boardSize ? (m.boardSize + ' 路') : '') + '</td>' +
        '<td><a href="' + esc(r.href) + '" target="_blank">查看报告</a></td>' +
        '</tr>';
    }).join('\n');

    var html = '<!doctype html><html lang="zh-CN"><head><meta charset="utf-8">' +
      '<meta name="viewport" content="width=device-width,initial-scale=1">' +
      '<meta http-equiv="refresh" content="20">' +
      '<title>ChinaGo 玩家反馈（' + rows.length + '）</title><style>' +
      'body{font-family:system-ui,"Microsoft YaHei",sans-serif;background:#f5f6f8;color:#222;margin:0;padding:28px}' +
      '.wrap{max-width:1180px;margin:0 auto}' +
      'h1{font-size:22px;margin:0 0 6px}' +
      '.meta{color:#888;font-size:13px;margin-bottom:18px}' +
      '.box{background:#fff;border-radius:10px;box-shadow:0 1px 4px rgba(0,0,0,.08);overflow:hidden}' +
      'table{width:100%;border-collapse:collapse;font-size:14px}' +
      'th{background:#fafbfc;text-align:left;padding:11px 14px;color:#666;font-weight:600;border-bottom:1px solid #eee;white-space:nowrap}' +
      'td{padding:11px 14px;border-bottom:1px solid #f2f2f2;vertical-align:top}' +
      'tr:last-child td{border-bottom:none}' +
      'tr:hover td{background:#fcfcfd}' +
      '.t{color:#888;white-space:nowrap;font-size:13px}' +
      '.ti{font-weight:600;max-width:420px}' +
      '.sub{color:#aaa;font-size:12px;margin-top:2px}' +
      '.tag{display:inline-block;background:#eef4ff;color:#3b6fd4;padding:2px 8px;border-radius:10px;font-size:12px}' +
      'a{color:#3b6fd4;text-decoration:none}a:hover{text-decoration:underline}' +
      '.empty{padding:46px;text-align:center;color:#aaa}' +
      '</style></head><body><div class="wrap">' +
      '<h1>ChinaGo 玩家反馈</h1>' +
      '<div class="meta">共 ' + rows.length + ' 条 · 保存目录 ' + esc(ROOT) + ' · 本页 20 秒自动刷新</div>' +
      '<div class="box">' +
      (rows.length
        ? '<table><thead><tr><th>时间</th><th>分类</th><th>标题</th><th>玩家</th><th>版本</th><th>棋盘</th><th>报告</th></tr></thead><tbody>' + body + '</tbody></table>'
        : '<div class="empty">还没有收到反馈</div>') +
      '</div></div></body></html>';

    fs.writeFileSync(path.join(ROOT, 'index.html'), html, 'utf8');
  } catch (e) {
    if (!_warned) { _warned = true; console.log(new Date().toISOString(), '[chinago] 汇总页刷新失败:', e && e.message); }
  }
}

/* ---------------- 对外接口 ---------------- */

function handleFeedback(req, res) {
  if (!rateOk(clientIp(req))) return json(res, 429, { ok: false, error: '提交过于频繁，请稍后再试' });

  var raw = '', tooBig = false;
  try { req.on('error', function () {}); } catch (e) {}
  try {
    req.on('data', function (c) {
      if (tooBig) return;
      raw += c;
      if (raw.length > MAX_BODY) { tooBig = true; try { req.resume(); } catch (e) {} }
    });
  } catch (e) {}

  req.on('end', function () {
    try {
      if (tooBig) return json(res, 413, { ok: false, error: '反馈内容过大' });
      var p = null;
      try { p = raw ? JSON.parse(raw) : null; } catch (e) { return json(res, 400, { ok: false, error: '请求格式错误' }); }
      if (!p || typeof p !== 'object' || Array.isArray(p)) return json(res, 400, { ok: false, error: '请求格式错误' });

      var title = String(p.title || '').trim();
      var detail = String(p.detail || '').trim();
      if (!title && !detail) return json(res, 400, { ok: false, error: '标题和详情不能都为空' });

      var saved = save(p);
      refreshIndex();
      console.log(new Date().toISOString(), '[chinago] 反馈已保存:', saved);
      return json(res, 200, { ok: true, path: saved });
    } catch (e) {
      console.log(new Date().toISOString(), '[chinago] 反馈处理失败:', e && e.stack ? e.stack : e);
      return json(res, 500, { ok: false, error: '服务器内部错误' });
    }
  });
}

function handleHealth(req, res) {
  return json(res, 200, { ok: true, service: 'chinago-feedback', dir: ROOT });
}

// 模块加载时先建目录 + 刷一次汇总页
try { mkdirp(ROOT); mkdirp(DATA_DIR); refreshIndex(); } catch (e) {}

module.exports = {
  handleFeedback: handleFeedback,
  handleHealth: handleHealth,
  refreshIndex: refreshIndex,
  root: ROOT
};

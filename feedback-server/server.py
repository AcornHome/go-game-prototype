#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ChinaGo Feedback Server
=======================

Turns this PC into the feedback server for ChinaGo.

  Player game  --HTTP POST-->  this server  -->  D:\\ChinaGo\\Feedback\\

It also keeps an auto-generated index.html so you can open a browser and
browse every feedback item (auto refreshes every 20 seconds).

Usage:
    python server.py                  # listen on 8080, save to D:\\ChinaGo\\Feedback
    python server.py --port 9000
    python server.py --dir D:\\MyFeedback
    python server.py --no-browser

Public access (wangyunchuan tunnel):
    domain        rzt7s7dz.dongtaiyuming.net   (HTTP)
    tunnel ->     127.0.0.1:8080
    game end      FeedbackUploader.PublicBaseUrl = http://rzt7s7dz.dongtaiyuming.net

    !! IMPORTANT: the tunnel's "internal port" MUST equal --port here.
    !! Port 3000 was tried first but is taken by a node.exe on this PC, which
    !! silently answers HTTP there and swallows feedback. Do not use 3000.

API:
    POST /api/feedback        submit feedback (JSON)
    GET  /                    summary page (open in browser)
    GET  /api/list            feedback list (JSON)
    GET  /health              health check
    GET  /raw?d=<day>&f=<file>   single feedback HTML

NOTE: console output is intentionally ASCII-only because the Windows cmd
      console here does not render UTF-8 Chinese reliably.
"""

import argparse
import json
import os
import socket
import sys
import threading
import time
import webbrowser
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from string import Template
from urllib.parse import urlparse, parse_qs

MAX_BODY = 2 * 1024 * 1024      # 2 MB max feedback
RATE_MAX = 20                   # max submissions per IP
RATE_WINDOW = 60                # per 60 seconds

# wangyunchuan (wangyunchuan / dongtaiyuming.net) HTTP tunnel.
# The tunnel maps this public URL to 127.0.0.1:PORT on this PC.
# Change this string if the tunnel domain is replaced.
PUBLIC_URL = "http://rzt7s7dz.dongtaiyuming.net"


# ---------------------------------------------------------------- storage
def resolve_root(override=None):
    if override:
        return os.path.abspath(override)
    if os.path.isdir("D:\\"):
        return os.path.join("D:\\", "ChinaGo", "Feedback")
    docs = os.path.join(os.path.expanduser("~"), "Documents")
    return os.path.join(docs, "ChinaGo", "Feedback")


def safe_name(raw, fallback):
    """Keep only safe chars, prevent path traversal."""
    if not raw:
        return fallback
    out = []
    for ch in str(raw):
        if ch.isalnum() or ch in "-_.":
            out.append(ch)
        elif ch in " :+@":
            out.append("-")
    s = "".join(out).strip("-.")
    return s[:80] if s else fallback


class Store:
    def __init__(self, root):
        self.root = root
        self.data = os.path.join(root, "data")
        self.lock = threading.Lock()
        os.makedirs(self.data, exist_ok=True)

    def save(self, payload, client_ip):
        with self.lock:
            now = datetime.now(timezone.utc)
            ts = payload.get("submittedAtUtc") or now.isoformat()
            try:
                dt = datetime.fromisoformat(str(ts).replace("Z", "+00:00"))
                if dt.tzinfo is None:
                    dt = dt.replace(tzinfo=timezone.utc)
            except Exception:
                dt = now
            local = dt.astimezone()

            day = local.strftime("%Y-%m-%d")
            day_dir = os.path.join(self.data, day)
            os.makedirs(day_dir, exist_ok=True)

            fid = safe_name(payload.get("feedbackId"), now.strftime("%H%M%S"))
            base = "feedback-%s-%s" % (local.strftime("%H%M%S"), fid)

            meta = {
                "feedbackId": payload.get("feedbackId"),
                "category": payload.get("category") or "其他",
                "title": payload.get("title") or "",
                "detail": payload.get("detail") or "",
                "contact": payload.get("contact"),
                "userName": payload.get("userName"),
                "userId": payload.get("userId"),
                "appVersion": payload.get("appVersion"),
                "boardSize": payload.get("boardSize"),
                "moveCount": payload.get("moveCount"),
                "submittedAtUtc": dt.isoformat(),
                "submittedAtLocal": local.strftime("%Y-%m-%d %H:%M:%S"),
                "receivedAtLocal": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
                "clientIp": client_ip,
                "htmlFile": base + ".html",
            }

            with open(os.path.join(day_dir, base + ".json"), "w", encoding="utf-8") as f:
                json.dump(meta, f, ensure_ascii=False, indent=2)

            html = payload.get("html") or (
                '<!DOCTYPE html><meta charset="utf-8"><p>%s</p>'
                % html_escape(payload.get("detail") or "")
            )
            with open(os.path.join(day_dir, base + ".html"), "w", encoding="utf-8") as f:
                f.write(html)

            self._refresh_index()
            return fid, os.path.join(day, base + ".html")

    def list_items(self):
        rows = []
        if not os.path.isdir(self.data):
            return rows
        for day in sorted(os.listdir(self.data), reverse=True):
            day_dir = os.path.join(self.data, day)
            if not os.path.isdir(day_dir):
                continue
            for name in sorted(os.listdir(day_dir), reverse=True):
                if not name.endswith(".json"):
                    continue
                try:
                    with open(os.path.join(day_dir, name), "r", encoding="utf-8") as f:
                        d = json.load(f)
                    d["_day"] = day
                    rows.append(d)
                except Exception:
                    continue
        rows.sort(key=lambda r: r.get("submittedAtUtc") or "", reverse=True)
        return rows

    def resolve_safe(self, day, filename):
        if not day or not filename:
            return None
        if ".." in day or "/" in day or "\\" in day:
            return None
        if ".." in filename or "/" in filename or "\\" in filename:
            return None
        full = os.path.abspath(os.path.join(self.data, day, filename))
        root_full = os.path.abspath(self.data)
        if not full.startswith(root_full):
            return None
        return full

    def _refresh_index(self):
        try:
            with open(os.path.join(self.root, "index.html"), "w", encoding="utf-8") as f:
                f.write(build_index(self))
        except Exception:
            pass

    def build_index(self):
        return build_index(self)


def html_escape(s):
    return (str(s).replace("&", "&amp;").replace("<", "&lt;")
            .replace(">", "&gt;").replace('"', "&quot;"))


INDEX_TEMPLATE = Template("""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>ChinaGo 玩家反馈汇总</title>
<style>
*{box-sizing:border-box}
body{margin:0;padding:26px;background:#F7F4ED;color:#2B2014;font-family:"Microsoft YaHei","PingFang SC",system-ui,sans-serif;font-size:14px;line-height:1.6}
h1{font-size:22px;margin:0 0 4px;font-weight:600}
.sub{color:#8F6427;font-size:13px;margin-bottom:18px}
.stat{display:flex;gap:14px;margin-bottom:18px;flex-wrap:wrap}
.card{background:#fff;border:1px solid #E3DACB;border-radius:10px;padding:12px 18px;min-width:150px}
.card .n{font-size:20px;font-weight:600;color:#B8893B}
.card .l{font-size:12px;color:#8A7A63}
table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #E3DACB;border-radius:10px;overflow:hidden}
th{background:#F2EADA;text-align:left;padding:10px 13px;font-size:13px;font-weight:600;color:#6B5426;white-space:nowrap}
td{padding:10px 13px;border-top:1px solid #EFE7D8;vertical-align:top}
tr:hover td{background:#FDFAF3}
.tag{display:inline-block;padding:2px 9px;border-radius:20px;font-size:12px;background:#F0E4CC;color:#7A5C1E;white-space:nowrap}
.tag.kat{background:#FBE3E3;color:#A32D2D}
.tag.ui{background:#E3EFFB;color:#185FA5}
.tag.rev{background:#E7F3E1;color:#3B6D11}
.tag.ins{background:#EFE7F7;color:#5B3A93}
a{color:#B8893B;text-decoration:none}
a:hover{text-decoration:underline}
.empty{padding:46px;text-align:center;color:#A09580;background:#fff;border:1px dashed #DDD2BC;border-radius:10px}
.mono{font-family:Consolas,Monaco,monospace;font-size:12px;color:#8A7A63}
.foot{margin-top:20px;font-size:12px;color:#A09580}
</style>
</head>
<body>
<h1>ChinaGo 玩家反馈汇总</h1>
<div class="sub">保存目录：<span class="mono">$root</span></div>
<div class="stat">
<div class="card"><div class="n">$total</div><div class="l">反馈总数</div></div>
<div class="card"><div class="n">$players</div><div class="l">反馈玩家数</div></div>
<div class="card"><div class="n">$latest</div><div class="l">最近一条</div></div>
</div>
$body
<div class="foot">本页由 ChinaGo 反馈服务器自动生成 · 每收到一条反馈自动更新 · 生成时间：$now</div>
<script>setTimeout(function(){location.reload()},20000)</script>
</body>
</html>
""")


def build_index(store):
    items = store.list_items()
    if not items:
        body = ('<div class="empty">还没有收到反馈。<br><br>'
                '在游戏里点「问题反馈」提交一条试试，这个页面会自动刷新。</div>')
        latest = "-"
    else:
        latest = items[0].get("submittedAtLocal", "-")
        rows = []
        for it in items:
            cat = it.get("category", "其他")
            cls = {"KataGo 异常": "kat", "界面卡顿": "ui",
                   "复盘/SGF": "rev", "安装": "ins"}.get(cat, "")
            day = it.get("_day", "")
            f = it.get("htmlFile", "")
            rows.append(
                "<tr>"
                '<td class="mono">%s</td>'
                '<td><span class="tag %s">%s</span></td>'
                "<td>%s</td>"
                "<td>%s</td>"
                '<td class="mono">%s</td>'
                '<td class="mono">%s</td>'
                '<td><a href="/raw?d=%s&f=%s" target="_blank">打开</a></td>'
                "</tr>" % (
                    html_escape(it.get("submittedAtLocal", "-")),
                    cls, html_escape(cat),
                    html_escape(it.get("title", "")),
                    html_escape(it.get("userName") or "-"),
                    html_escape(it.get("contact") or "-"),
                    html_escape(it.get("appVersion") or "-"),
                    url_q(day), url_q(f),
                ))
        body = (
            '<table><thead><tr>'
            '<th style="width:148px">时间</th><th style="width:94px">分类</th><th>标题</th>'
            '<th style="width:110px">玩家</th><th style="width:140px">联系方式</th>'
            '<th style="width:70px">版本</th><th style="width:60px">查看</th>'
            '</tr></thead><tbody>' + "".join(rows) + "</tbody></table>"
        )

    players = len({i.get("userName") for i in items if i.get("userName")})
    return INDEX_TEMPLATE.substitute(
        root=html_escape(store.root),
        total=len(items),
        players=players,
        latest=html_escape(latest),
        body=body,
        now=datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
    )


def url_q(s):
    from urllib.parse import quote
    return quote(str(s), safe="")


# ---------------------------------------------------------------- http
class Handler(BaseHTTPRequestHandler):
    server_version = "ChinaGoFeedback/1.0"
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        pass  # keep console clean

    # ---- helpers
    def _client_ip(self):
        fwd = self.headers.get("X-Forwarded-For")
        if fwd:
            return fwd.split(",")[0].strip()
        return self.client_address[0]

    def _send(self, code, body, ctype, cors=False):
        if isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        if cors:
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Headers", "Content-Type")
            self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.end_headers()
        if body:
            self.wfile.write(body)

    def _json(self, code, obj, cors=False):
        self._send(code, json.dumps(obj, ensure_ascii=False),
                   "application/json; charset=utf-8", cors)

    # ---- routes
    def do_OPTIONS(self):
        self._send(204, b"", "text/plain", cors=True)

    def do_GET(self):
        u = urlparse(self.path)
        store = self.server.store

        if u.path == "/health":
            self._json(200, {
                "ok": True,
                "service": "ChinaGoFeedbackServer",
                "version": "1.0.0",
                "root": store.root,
                "count": len(store.list_items()),
                "now": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            }, cors=True)
            return

        if u.path == "/api/list":
            items = store.list_items()
            self._json(200, {"ok": True, "count": len(items), "items": items}, cors=True)
            return

        if u.path == "/raw":
            q = parse_qs(u.query)
            path = store.resolve_safe(q.get("d", [""])[0], q.get("f", [""])[0])
            if not path or not os.path.isfile(path):
                self._send(404, "not found", "text/plain; charset=utf-8")
                return
            with open(path, "rb") as f:
                self._send(200, f.read(), "text/html; charset=utf-8", cors=True)
            return

        if u.path in ("/", "/index.html"):
            self._send(200, store.build_index().encode("utf-8"),
                       "text/html; charset=utf-8", cors=True)
            return

        self._json(404, {"ok": False, "error": "unknown path " + u.path}, cors=True)

    def do_POST(self):
        u = urlparse(self.path)
        store = self.server.store

        if u.path != "/api/feedback":
            self._json(404, {"ok": False, "error": "unknown path " + u.path}, cors=True)
            return

        ip = self._client_ip()
        now = time.time()
        with self.server.rate_lock:
            hits = [t for t in self.server.rate.get(ip, []) if now - t < RATE_WINDOW]
            if len(hits) >= RATE_MAX:
                self.server.rate[ip] = hits
                self._json(429, {"ok": False, "error": "too many requests"}, cors=True)
                return
            hits.append(now)
            self.server.rate[ip] = hits

        try:
            length = int(self.headers.get("Content-Length") or 0)
        except ValueError:
            length = 0
        if length > MAX_BODY:
            self._json(400, {"ok": False, "error": "payload too large"}, cors=True)
            return

        raw = self.rfile.read(length) if length else b""
        try:
            payload = json.loads(raw.decode("utf-8"))
        except Exception as e:
            self._json(400, {"ok": False, "error": "bad json: %s" % e}, cors=True)
            return

        if not (payload.get("title") or "").strip():
            self._json(400, {"ok": False, "error": "title required"}, cors=True)
            return

        try:
            fid, rel = store.save(payload, ip)
        except Exception as e:
            self._json(500, {"ok": False, "error": "save failed: %s" % e}, cors=True)
            return

        title = str(payload.get("title") or "")
        if len(title) > 34:
            title = title[:34] + "..."
        print("[%s] #%s  <-  %s" % (datetime.now().strftime("%H:%M:%S"), fid, ip))
        sys.stdout.flush()
        self._json(200, {"ok": True, "id": fid, "path": rel}, cors=True)


def port_owner(port):
    """Who is answering HTTP on 127.0.0.1:port? -> 'ours' | 'other' | None.

    Windows lets two sockets share one port when SO_REUSEADDR is set, so a
    foreign program can silently swallow our traffic (this actually happened:
    a node.exe sat on :3000 and returned "Not Found" for every feedback).
    Detect it loudly instead of losing data.
    """
    from urllib.request import Request, urlopen, ProxyHandler, build_opener
    opener = build_opener(ProxyHandler({}))
    try:
        req = Request("http://127.0.0.1:%d/health" % port,
                      headers={"User-Agent": "ChinaGoProbe/1.0"})
        with opener.open(req, timeout=1.5) as r:
            body = r.read().decode("utf-8", "replace")
        try:
            data = json.loads(body)
        except Exception:
            return "other"
        return "ours" if data.get("service") == "ChinaGoFeedbackServer" else "other"
    except Exception as e:
        # HTTPError (e.g. 404) still means *something* answered -> treat as other
        if type(e).__name__ == "HTTPError":
            return "other"
        return None


class DualStackServer(ThreadingHTTPServer):
    address_family = socket.AF_INET6
    daemon_threads = True
    allow_reuse_address = True

    def server_bind(self):
        try:
            self.socket.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_V6ONLY, 0)
        except Exception:
            pass
        super().server_bind()


def lan_ips():
    out = []
    try:
        import psutil  # optional
    except Exception:
        psutil = None
    host = socket.gethostname()
    try:
        for info in socket.getaddrinfo(host, None, socket.AF_INET):
            ip = info[4][0]
            if not ip.startswith("127.") and not ip.startswith("169.254."):
                out.append(ip)
    except Exception:
        pass
    return sorted(set(out))


def public_ipv6():
    out = []
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET6):
            ip = info[4][0].split("%")[0]
            if ip.startswith("fe80") or ip == "::1":
                continue
            out.append(ip)
    except Exception:
        pass
    return sorted(set(out))


def main():
    ap = argparse.ArgumentParser(description="ChinaGo Feedback Server")
    ap.add_argument("--port", "-p", type=int, default=8080)
    ap.add_argument("--dir", "-d", default=None)
    ap.add_argument("--no-browser", action="store_true")
    args = ap.parse_args()

    root = resolve_root(args.dir)
    store = Store(root)
    store._refresh_index()

    # Refuse to share a port with another program: on Windows two sockets can
    # bind the same port and traffic gets routed at random -> lost feedback.
    owner = port_owner(args.port)
    if owner == "ours":
        print("")
        print("  [ALREADY RUNNING] A ChinaGo feedback server is already on port %d." % args.port)
        print("                   Use the window that is already open.")
        print("")
        return 1
    if owner == "other":
        print("")
        print("  [ERROR] Port %d is already used by ANOTHER program." % args.port)
        print("          Sharing a port would send feedback to the wrong program.")
        print("")
        print("  Fix it one of these ways:")
        print("    1. python server.py --port %d" % (args.port + 1))
        print("       then change the wangyunchuan tunnel's internal port to match")
        print("    2. stop the other program, then start this again")
        print("")
        return 1

    print("")
    print("  ==================================================")
    print("    ChinaGo  Feedback Server")
    print("  ==================================================")
    print("")
    print("  Save dir : %s" % root)
    print("  Port     : %d" % args.port)
    print("")
    print("  Public (wangyunchuan tunnel):")
    print("    %s" % PUBLIC_URL)
    print("    summary page: %s/" % PUBLIC_URL)
    if args.port != 8080:
        print("    [WARN] tunnel maps 127.0.0.1:8080 but server is on %d" % args.port)
        print("           -> change the tunnel's internal port to %d, or rerun with --port 8080"
              % args.port)
    print("")
    print("  Local / LAN:")
    print("    this PC    http://localhost:%d" % args.port)
    for ip in lan_ips():
        print("    LAN        http://%s:%d" % (ip, args.port))
    v6 = public_ipv6()
    if v6:
        for ip in v6:
            print("    public v6  http://[%s]:%d" % (ip, args.port))
    print("")
    print("  KEEP THIS WINDOW OPEN - closing it stops receiving feedback.")
    print("  Press Ctrl+C to stop.")
    print("")

    try:
        httpd = DualStackServer(("::", args.port), Handler)
    except OSError as e:
        print("")
        print("  [ERROR] Cannot listen on port %d: %s" % (args.port, e))
        print("          Try another port:  python server.py --port %d" % (args.port + 1))
        print("")
        return 1

    httpd.store = store
    httpd.rate = {}
    httpd.rate_lock = threading.Lock()

    if not args.no_browser:
        threading.Timer(1.0, lambda: webbrowser.open("http://localhost:%d/" % args.port)).start()

    print("  Waiting for feedback... (each one prints a line below)")
    print("")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("")
        print("  Stopped.")
    finally:
        httpd.server_close()
    return 0


if __name__ == "__main__":
    sys.exit(main())

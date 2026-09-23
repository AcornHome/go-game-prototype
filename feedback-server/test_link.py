#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ChinaGo feedback link self-test
===============================

Checks the whole feedback chain and shows the result in your browser
(so you never have to read the black console window).

  1. local server    http://127.0.0.1:8080/health
  2. public tunnel   http://rzt7s7dz.dongtaiyuming.net/health

Proxies are deliberately bypassed: if a proxy env var is set the result
would lie about whether the tunnel itself works.

Writes:  D:\\ChinaGo\\Feedback\\tunnel-test.html   (then opens it)
"""

import json
import os
import socket
import sys
import webbrowser
from datetime import datetime
from urllib.request import Request, urlopen, ProxyHandler, build_opener
from urllib.error import URLError, HTTPError

LOCAL = "http://127.0.0.1:8080"
PUBLIC = "http://rzt7s7dz.dongtaiyuming.net"
TIMEOUT = 8

# no-proxy opener: measure the real tunnel, not a local proxy
OPENER = build_opener(ProxyHandler({}))


def check(url):
    """Return (ok, detail). Uses /health so nothing is written to disk."""
    try:
        req = Request(url + "/health", headers={"User-Agent": "ChinaGoLinkTest/1.0"})
        with OPENER.open(req, timeout=TIMEOUT) as r:
            body = r.read().decode("utf-8", "replace").strip()
            code = r.getcode()
        try:
            data = json.loads(body)
        except Exception:
            return False, "HTTP %d but not our server. Got: %s" % (code, body[:120])
        if data.get("service") == "ChinaGoFeedbackServer":
            return True, "HTTP %d - count=%s - root=%s" % (
                code, data.get("count"), data.get("root"))
        return False, "HTTP %d - unexpected json: %s" % (code, body[:120])
    except HTTPError as e:
        return False, "HTTP %d - the tunnel answered, but not our server. " \
                      "Start the server first (Start Feedback Server.bat)." % e.code
    except URLError as e:
        return False, "cannot connect (%s)" % (e.reason,)
    except socket.timeout:
        return False, "timeout after %ds" % TIMEOUT
    except Exception as e:
        return False, "error: %s" % e


def port_open(port):
    s = socket.socket()
    s.settimeout(1.5)
    try:
        s.connect(("127.0.0.1", port))
        return True
    except Exception:
        return False
    finally:
        s.close()


def main():
    local_ok, local_msg = check(LOCAL)
    public_ok, public_msg = check(PUBLIC)

    if local_ok and public_ok:
        verdict = "ALL GOOD"
        vcolor = "#3B6D11"
        vbg = "#EAF3DE"
        vtext = "Everything works. Players on any network can send you feedback."
    elif local_ok and not public_ok:
        verdict = "TUNNEL DOWN"
        vcolor = "#A32D2D"
        vbg = "#FCEBEB"
        vtext = ("Your server runs fine, but the public domain cannot reach it. "
                 "Open the wangyunchuan client and make sure the tunnel is ONLINE.")
    elif not local_ok and public_ok:
        verdict = "WEIRD"
        vcolor = "#BA7517"
        vbg = "#FAEEDA"
        vtext = ("Public domain works but localhost does not - usually means another "
                 "program on this PC is answering on port 3000.")
    else:
        verdict = "SERVER NOT RUNNING"
        vcolor = "#A32D2D"
        vbg = "#FCEBEB"
        vtext = ("Neither address answers. Double-click 'Start Feedback Server.bat' "
                 "first, then run this test again.")

    def row(name, url, ok, msg):
        color = "#3B6D11" if ok else "#A32D2D"
        badge = "OK" if ok else "FAILED"
        return (
            '<tr><td style="font-weight:600">%s</td>'
            '<td class="mono">%s</td>'
            '<td style="color:%s;font-weight:600">%s</td>'
            '<td class="mono">%s</td></tr>'
            % (name, url, color, badge, msg.replace("<", "&lt;"))
        )

    rows = (row("Local server", LOCAL, local_ok, local_msg) +
            row("Public tunnel", PUBLIC, public_ok, public_msg))

    html = """<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>ChinaGo feedback link test</title>
<style>
*{box-sizing:border-box}
body{margin:0;padding:30px;background:#F7F4ED;color:#2B2014;
font-family:"Microsoft YaHei","PingFang SC",system-ui,sans-serif;font-size:14px;line-height:1.65}
h1{font-size:21px;margin:0 0 6px;font-weight:600}
.sub{color:#8A7A63;font-size:13px;margin-bottom:20px}
.verdict{background:%(vbg)s;border:1px solid %(vcolor)s33;border-left:5px solid %(vcolor)s;
border-radius:10px;padding:16px 20px;margin-bottom:22px}
.verdict .t{font-size:19px;font-weight:600;color:%(vcolor)s}
.verdict .d{margin-top:5px;color:#4A3F2F}
table{width:100%%;border-collapse:collapse;background:#fff;border:1px solid #E3DACB;
border-radius:10px;overflow:hidden}
th{background:#F2EADA;text-align:left;padding:10px 13px;font-size:13px;color:#6B5426}
td{padding:11px 13px;border-top:1px solid #EFE7D8;vertical-align:top;word-break:break-all}
.mono{font-family:Consolas,Monaco,monospace;font-size:12px;color:#8A7A63}
.steps{margin-top:22px;background:#fff;border:1px solid #E3DACB;border-radius:10px;padding:16px 20px}
.steps h2{font-size:15px;margin:0 0 10px;font-weight:600}
.steps ol{margin:0;padding-left:22px}
.steps li{margin-bottom:7px}
.foot{margin-top:18px;font-size:12px;color:#A09580}
</style></head><body>
<h1>ChinaGo feedback link test</h1>
<div class="sub">Run at %(now)s</div>
<div class="verdict"><div class="t">%(verdict)s</div><div class="d">%(vtext)s</div></div>
<table><thead><tr><th style="width:130px">Check</th><th style="width:290px">Address</th>
<th style="width:80px">Result</th><th>Detail</th></tr></thead><tbody>%(rows)s</tbody></table>
<div class="steps"><h2>If something failed</h2><ol>
<li>Double-click <b>Start Feedback Server.bat</b> and leave that window open.</li>
<li>Open the <b>wangyunchuan client</b> and make sure the tunnel
<code>rzt7s7dz.dongtaiyuming.net</code> shows <b>online</b> and points to
<b>127.0.0.1 : 8080</b> (change it from 3000 - port 3000 is taken by another
program on this PC).</li>
<li>Run this test again.</li>
<li>Still failing? Send a screenshot of this page to the developer.</li>
</ol></div>
<div class="foot">This page is regenerated every time you run the test.</div>
</body></html>""" % {
        "vbg": vbg, "vcolor": vcolor, "verdict": verdict, "vtext": vtext,
        "rows": rows, "now": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
    }

    root = "D:\\ChinaGo\\Feedback" if os.path.isdir("D:\\") else os.path.join(
        os.path.expanduser("~"), "Documents", "ChinaGo", "Feedback")
    try:
        os.makedirs(root, exist_ok=True)
        path = os.path.join(root, "tunnel-test.html")
        with open(path, "w", encoding="utf-8") as f:
            f.write(html)
        webbrowser.open("file:///" + path.replace("\\", "/"))
        print("Report saved: %s" % path)
    except Exception as e:
        print("Cannot write report: %s" % e)

    print("local  : %s - %s" % ("OK" if local_ok else "FAIL", local_msg))
    print("public : %s - %s" % ("OK" if public_ok else "FAIL", public_msg))
    return 0 if (local_ok and public_ok) else 1


if __name__ == "__main__":
    sys.exit(main())

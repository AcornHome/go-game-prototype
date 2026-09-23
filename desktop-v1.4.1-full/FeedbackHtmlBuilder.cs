using System;
using System.Net;
using System.Text;

namespace GoGame;

/// <summary>
/// 把 FeedbackRecord 渲染成可在浏览器双击打开的纯静态 HTML 报告。
/// 设计原则：
///   1. 完全静态：无 JavaScript、无外部资源（CSS 内联、图片 base64 也避免）
///   2. 美观：与游戏主 UI 一致的木纹/围棋风格配色
///   3. 阅读顺序：基本信息 → 联系方式 → 用户描述 → 环境 → 对局快照 → KataGo 日志
///   4. UTF-8 + HTML escape → 在任何浏览器都不会乱码/被注入
///   5. 单文件 ≤ 100KB（避免被 QQ 邮箱拒收）
/// </summary>
public static class FeedbackHtmlBuilder
{
    public static byte[] Build(FeedbackService.FeedbackRecord r)
    {
        var sb = new StringBuilder(8192);

        // ========== HTML 头部 ==========
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>中国围棋玩家反馈 - ");
        sb.Append(Html(r.Title));
        sb.AppendLine("</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: 'Microsoft YaHei UI', 'Segoe UI', -apple-system, sans-serif;");
        sb.AppendLine("         background: linear-gradient(135deg, #F8F4EA 0%, #F0E9D6 100%);");
        sb.AppendLine("         color: #2B2014; margin: 0; padding: 32px 20px; line-height: 1.7; }");
        sb.AppendLine("  .wrap { max-width: 760px; margin: 0 auto; }");
        sb.AppendLine("  .banner { background: linear-gradient(135deg, #2B2014 0%, #4A331C 50%, #2B2014 100%);");
        sb.AppendLine("            color: #F5E6C8; padding: 32px 36px; border-radius: 12px 12px 0 0;");
        sb.AppendLine("            box-shadow: 0 2px 8px rgba(0,0,0,.15); }");
        sb.AppendLine("  .banner .stone { font-size: 22px; color: #F5E6C8; margin-right: 4px; }");
        sb.AppendLine("  .banner h1 { margin: 8px 0 6px; font-size: 24px; font-weight: 600; }");
        sb.AppendLine("  .banner .sub { font-size: 14px; color: #DCC68B; }");
        sb.AppendLine("  .banner .id { font-size: 12px; color: #C8B47A; margin-top: 14px;");
        sb.AppendLine("                font-family: Consolas, 'Courier New', monospace; }");
        sb.AppendLine("  .body { background: #FFFFFF; padding: 28px 36px; border-radius: 0 0 12px 12px;");
        sb.AppendLine("          box-shadow: 0 2px 12px rgba(0,0,0,.08); }");
        sb.AppendLine("  .section { margin: 0 0 24px; }");
        sb.AppendLine("  .section-title { font-size: 12px; font-weight: 600; color: #8F6427;");
        sb.AppendLine("                    text-transform: uppercase; letter-spacing: .08em;");
        sb.AppendLine("                    border-bottom: 2px solid #E89B3C; padding-bottom: 6px;");
        sb.AppendLine("                    margin-bottom: 12px; }");
        sb.AppendLine("  .badge { display: inline-block; padding: 3px 10px; border-radius: 12px;");
        sb.AppendLine("           font-size: 12px; font-weight: 600; margin-right: 6px; }");
        sb.AppendLine("  .badge-cat { background: #C8964F; color: #FFF5E2; }");
        sb.AppendLine("  .badge-time { background: #F0E9D6; color: #8F6427; }");
        sb.AppendLine("  .detail-box { background: #FBF6E8; border-left: 4px solid #C8964F;");
        sb.AppendLine("                padding: 14px 18px; border-radius: 0 6px 6px 0;");
        sb.AppendLine("                white-space: pre-wrap; word-wrap: break-word;");
        sb.AppendLine("                font-size: 14px; line-height: 1.8; }");
        sb.AppendLine("  .empty-detail { color: #999; font-style: italic; }");
        sb.AppendLine("  table.info { width: 100%; border-collapse: collapse; font-size: 13px; }");
        sb.AppendLine("  table.info th { text-align: left; padding: 6px 12px 6px 0;");
        sb.AppendLine("                   color: #8F6427; font-weight: 600; width: 140px;");
        sb.AppendLine("                   vertical-align: top; }");
        sb.AppendLine("  table.info td { padding: 6px 0; color: #2B2014;");
        sb.AppendLine("                   word-break: break-all; vertical-align: top; }");
        sb.AppendLine("  .contact { background: #F0E9D6; padding: 14px 18px; border-radius: 6px;");
        sb.AppendLine("             font-size: 14px; }");
        sb.AppendLine("  .contact .row { margin: 4px 0; }");
        sb.AppendLine("  .contact .label { color: #8F6427; font-weight: 600; display: inline-block;");
        sb.AppendLine("                     min-width: 80px; }");
        sb.AppendLine("  pre.log { background: #1E2620; color: #F0F2EE; padding: 16px; border-radius: 6px;");
        sb.AppendLine("            font-family: Consolas, 'Courier New', monospace; font-size: 11px;");
        sb.AppendLine("            line-height: 1.6; overflow-x: auto; max-height: 480px;");
        sb.AppendLine("            overflow-y: auto; white-space: pre-wrap; word-wrap: break-word; }");
        sb.AppendLine("  .footer { text-align: center; color: #999; font-size: 11px; margin-top: 24px;");
        sb.AppendLine("             padding: 16px 0; }");
        sb.AppendLine("  @media (max-width: 600px) {");
        sb.AppendLine("    .banner, .body { padding-left: 20px; padding-right: 20px; }");
        sb.AppendLine("    table.info th { width: 110px; }");
        sb.AppendLine("  }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"wrap\">");

        // ========== Banner ==========
        sb.AppendLine("<div class=\"banner\">");
        sb.AppendLine("  <div><span class=\"stone\">&#9679;</span><span class=\"stone\" style=\"font-size:18px\">&#9675;</span></div>");
        sb.AppendLine("  <h1>").Append(Html(r.Title)).AppendLine("</h1>");
        sb.Append("  <div class=\"sub\">");
        sb.Append("<span class=\"badge badge-cat\">").Append(Html(r.Category)).AppendLine("</span>");
        sb.Append("<span class=\"badge badge-time\">").Append(r.SubmittedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).AppendLine("</span>");
        sb.Append("</div>");
        sb.Append("  <div class=\"id\">反馈 ID: ").Append(Html(r.FeedbackId)).AppendLine("</div>");
        sb.AppendLine("</div>");

        // ========== Body ==========
        sb.AppendLine("<div class=\"body\">");

        // —— 联系方式 ——
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">联系方式</div>");
        if (string.IsNullOrEmpty(r.Contact.Email) && string.IsNullOrEmpty(r.Contact.SteamHandle))
        {
            sb.AppendLine("  <div class=\"contact\"><div class=\"empty-detail\">用户未填联系方式</div></div>");
        }
        else
        {
            sb.AppendLine("  <div class=\"contact\">");
            if (!string.IsNullOrEmpty(r.Contact.Email))
                sb.Append("    <div class=\"row\"><span class=\"label\">邮箱</span>").Append(Html(r.Contact.Email)).AppendLine("</div>");
            if (!string.IsNullOrEmpty(r.Contact.SteamHandle))
                sb.Append("    <div class=\"row\"><span class=\"label\">Steam</span>").Append(Html(r.Contact.SteamHandle)).AppendLine("</div>");
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</div>");

        // —— 详细描述 ——
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">详细描述</div>");
        if (string.IsNullOrWhiteSpace(r.Detail))
        {
            sb.AppendLine("  <div class=\"detail-box empty-detail\">（用户未填详细描述）</div>");
        }
        else
        {
            sb.Append("  <div class=\"detail-box\">").Append(Html(r.Detail)).AppendLine("</div>");
        }
        sb.AppendLine("</div>");

        // —— 当前对局快照 ——
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">当前对局快照</div>");
        sb.AppendLine("  <table class=\"info\">");
        AppendInfoRow(sb, "棋盘大小", r.Game.BoardSize);
        AppendInfoRow(sb, "已下手数", r.Game.MoveCount.ToString());
        AppendInfoRow(sb, "上一手", string.IsNullOrEmpty(r.Game.LastMove) ? "—" : r.Game.LastMove);
        AppendInfoRow(sb, "走子方", r.Game.ToMove);
        AppendInfoRow(sb, "KataGo 就绪", r.Game.KataGoReady ? "✅ 是" : "❌ 否");
        sb.AppendLine("  </table>");
        sb.AppendLine("</div>");

        // —— 环境信息 ——
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">环境信息</div>");
        sb.AppendLine("  <table class=\"info\">");
        AppendInfoRow(sb, "App 版本", r.Environment.AppVersion);
        AppendInfoRow(sb, "操作系统", r.Environment.OsVersion);
        AppendInfoRow(sb, ".NET 版本", r.Environment.DotnetVersion);
        AppendInfoRow(sb, "KataGo 路径", r.Environment.KataGoPath);
        AppendInfoRow(sb, "神经网络文件", r.Environment.KataGoNetworkFile);
        sb.AppendLine("  </table>");
        sb.AppendLine("</div>");

        // —— KataGo 日志尾巴 ——
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">KataGo 最近日志（最多 30 行）</div>");
        if (string.IsNullOrWhiteSpace(r.Environment.KataGoLastLogTail))
        {
            sb.AppendLine("  <div class=\"empty-detail\">（暂无日志）</div>");
        }
        else
        {
            sb.Append("  <pre class=\"log\">").Append(Html(r.Environment.KataGoLastLogTail)).AppendLine("</pre>");
        }
        sb.AppendLine("</div>");

        // ========== Footer ==========
        sb.AppendLine("<div class=\"footer\">");
        sb.Append("  本反馈由中国围棋 V").Append(Html(r.Environment.AppVersion));
        sb.AppendLine(" 自动生成 · 提交时间（UTC）: ").Append(r.SubmittedAtUtc.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine();
        sb.AppendLine("</div>");

        sb.AppendLine("</div>");  // body
        sb.AppendLine("</div>");  // wrap
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        // UTF-8 + BOM（QQ 邮箱附件需要 BOM 识别中文；浏览器双击也能识别）
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var withBom = new byte[bom.Length + bytes.Length];
        Buffer.BlockCopy(bom, 0, withBom, 0, bom.Length);
        Buffer.BlockCopy(bytes, 0, withBom, bom.Length, bytes.Length);
        return withBom;
    }

    private static void AppendInfoRow(StringBuilder sb, string label, string value)
    {
        sb.Append("    <tr><th>").Append(Html(label)).Append("</th><td>").Append(Html(value)).AppendLine("</td></tr>");
    }

    /// <summary>HTML escape：把 &amp; &lt; &gt; &quot; &#39; 转义，防注入。</summary>
    private static string Html(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return WebUtility.HtmlEncode(s);
    }
}
using System;
using System.Net;
using System.Text;

namespace GoGame;

/// <summary>
/// Render a FeedbackRecord into a static HTML report that opens by double-clicking in a browser.
/// Design principles:
///   1. Fully static: no JavaScript, no external resources (inline CSS, avoid base64 images)
///   2. Pretty: wood / Go style colors consistent with the main UI
///   3. Reading order: basic info → contact → user description → environment → game snapshot → KataGo log
///   4. UTF-8 + HTML escape → never garbled or injected in any browser
///   5. Single file <= 100KB (avoid being rejected by QQ Mail)
/// </summary>
public static class FeedbackHtmlBuilder
{
    public static byte[] Build(FeedbackService.FeedbackRecord r)
    {
        var sb = new StringBuilder(8192);

        // ========== HTML head ==========
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>China Go player feedback - ");
        sb.Append(Html(r.Title));
        sb.AppendLine("</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: 'Segoe UI', 'Microsoft YaHei UI', -apple-system, sans-serif;");
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
        sb.Append("  <div class=\"id\">Feedback ID: ").Append(Html(r.FeedbackId)).AppendLine("</div>");
        sb.AppendLine("</div>");

        // ========== Body ==========
        sb.AppendLine("<div class=\"body\">");

        // -- Contact --
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">Contact</div>");
        if (string.IsNullOrEmpty(r.Contact.Email) && string.IsNullOrEmpty(r.Contact.SteamHandle))
        {
            sb.AppendLine("  <div class=\"contact\"><div class=\"empty-detail\">No contact info provided</div></div>");
        }
        else
        {
            sb.AppendLine("  <div class=\"contact\">");
            if (!string.IsNullOrEmpty(r.Contact.Email))
                sb.Append("    <div class=\"row\"><span class=\"label\">Email</span>").Append(Html(r.Contact.Email)).AppendLine("</div>");
            if (!string.IsNullOrEmpty(r.Contact.SteamHandle))
                sb.Append("    <div class=\"row\"><span class=\"label\">Steam</span>").Append(Html(r.Contact.SteamHandle)).AppendLine("</div>");
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</div>");

        // -- Description --
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">Description</div>");
        if (string.IsNullOrWhiteSpace(r.Detail))
        {
            sb.AppendLine("  <div class=\"detail-box empty-detail\">No description provided</div>");
        }
        else
        {
            sb.Append("  <div class=\"detail-box\">").Append(Html(r.Detail)).AppendLine("</div>");
        }
        sb.AppendLine("</div>");

        // -- Current game snapshot --
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">Current game snapshot</div>");
        sb.AppendLine("  <table class=\"info\">");
        AppendInfoRow(sb, "Board size", r.Game.BoardSize);
        AppendInfoRow(sb, "Moves played", r.Game.MoveCount.ToString());
        AppendInfoRow(sb, "Last move", string.IsNullOrEmpty(r.Game.LastMove) ? "—" : r.Game.LastMove);
        AppendInfoRow(sb, "To move", r.Game.ToMove);
        AppendInfoRow(sb, "KataGo ready", r.Game.KataGoReady ? "✅ Yes" : "❌ No");
        sb.AppendLine("  </table>");
        sb.AppendLine("</div>");

        // -- Environment --
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">Environment</div>");
        sb.AppendLine("  <table class=\"info\">");
        AppendInfoRow(sb, "App version", r.Environment.AppVersion);
        AppendInfoRow(sb, "OS", r.Environment.OsVersion);
        AppendInfoRow(sb, ".NET version", r.Environment.DotnetVersion);
        AppendInfoRow(sb, "KataGo path", r.Environment.KataGoPath);
        AppendInfoRow(sb, "Network file", r.Environment.KataGoNetworkFile);
        sb.AppendLine("  </table>");
        sb.AppendLine("</div>");

        // -- KataGo log tail --
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <div class=\"section-title\">Recent KataGo log (up to 30 lines)</div>");
        if (string.IsNullOrWhiteSpace(r.Environment.KataGoLastLogTail))
        {
            sb.AppendLine("  <div class=\"empty-detail\">(no logs)</div>");
        }
        else
        {
            sb.Append("  <pre class=\"log\">").Append(Html(r.Environment.KataGoLastLogTail)).AppendLine("</pre>");
        }
        sb.AppendLine("</div>");

        // ========== Footer ==========
        sb.AppendLine("<div class=\"footer\">");
        sb.Append("  This feedback was generated by China Go V").Append(Html(r.Environment.AppVersion));
        sb.AppendLine("  · submitted (UTC): ").Append(r.SubmittedAtUtc.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine();
        sb.AppendLine("</div>");

        sb.AppendLine("</div>");  // body
        sb.AppendLine("</div>");  // wrap
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        // UTF-8 + BOM (QQ Mail attachments need BOM to detect encoding; double-click also opens fine in any browser)
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

    /// <summary>HTML escape: encode &amp; &lt; &gt; &quot; &#39; to prevent injection.</summary>
    private static string Html(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return WebUtility.HtmlEncode(s);
    }
}

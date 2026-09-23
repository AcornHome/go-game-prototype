using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoGame;

/// <summary>
/// 通过 QQ 邮箱 SMTP 把玩家反馈直接发送到开发者邮箱。
///
/// QQ 邮箱 SMTP 服务器：smtp.qq.com:587（STARTTLS）或 465（SSL/TLS）
/// 我们用 587（STARTTLS），因为它对自签名证书更宽容，跨国/跨网络更稳。
///
/// 凭据：玩家在本机配置时填入的「授权码」（DPAPI 加密存储）。
/// 发件 = 收件 = 954038398@qq.com（自己发给自己，最简单）。
/// </summary>
public static class MailSender
{
    // QQ 邮箱 SMTP
    private const string SmtpHost = "smtp.qq.com";
    private const int SmtpPort = 587;
    private const string SenderDisplayName = "China Go Player Feedback";

    /// <summary>
    /// 发送单条反馈邮件到开发者邮箱。
    /// 返回：
    ///   Ok=true  → 邮件发送成功（LocalHtmlPath = null）
    ///   Ok=false + LocalHtmlPath != null → 邮件失败但已自动保存到本地兜底目录
    ///   Ok=false + LocalHtmlPath == null → 邮件失败且本地兜底也失败（双失败）
    /// 任何异常都被捕获，绝不冒泡。
    /// </summary>
    public static async Task<(bool Ok, string Error, string? LocalHtmlPath)> SendFeedbackAsync(
        FeedbackService.FeedbackRecord record)
    {
        // 1) 取授权码
        var authCode = MailCredentials.Load();
        if (string.IsNullOrEmpty(authCode))
        {
            return (false, "QQ mail authorization code not configured yet", null);
        }

        // 2) 构造邮件
        string subject = $"[China Go] {record.Category} · {record.Title}";
        var htmlBytes = FeedbackHtmlBuilder.Build(record);
        var jsonBytes = BuildJsonAttachment(record);
        string localTime = record.SubmittedAtUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss");
        string htmlFileName = $"feedback-{localTime}-{record.FeedbackId.Substring(3)}.html";
        string jsonFileName = $"feedback-{localTime}-{record.FeedbackId.Substring(3)}.json";

        using var msg = new MailMessage();
        msg.From = new MailAddress(FeedbackService.SenderEmail, SenderDisplayName, Encoding.UTF8);
        msg.To.Add(FeedbackService.DeveloperEmail);
        msg.Subject = subject;
        msg.SubjectEncoding = Encoding.UTF8;
        msg.BodyEncoding = Encoding.UTF8;
        msg.IsBodyHtml = true;

        // 邮件正文（简短摘要 + 联系方式提醒）
        var body = new StringBuilder();
        body.AppendLine("<div style=\"font-family:'Segoe UI',sans-serif;color:#2B2014;line-height:1.7\">");
        body.AppendLine("<h2 style=\"margin:0 0 12px;color:#8F6427\">New player feedback received</h2>");
        body.AppendLine("<table style=\"border-collapse:collapse;font-size:14px\">");
        body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">Feedback ID</td><td>{WebUtility.HtmlEncode(record.FeedbackId)}</td></tr>");
        body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">Type</td><td>{WebUtility.HtmlEncode(record.Category)}</td></tr>");
        body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">Title</td><td>{WebUtility.HtmlEncode(record.Title)}</td></tr>");
        body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">Time</td><td>{record.SubmittedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}</td></tr>");
        if (!string.IsNullOrEmpty(record.Contact.Email))
            body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">Email</td><td>{WebUtility.HtmlEncode(record.Contact.Email)}</td></tr>");
        if (!string.IsNullOrEmpty(record.Contact.SteamHandle))
            body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">Steam</td><td>{WebUtility.HtmlEncode(record.Contact.SteamHandle)}</td></tr>");
        body.AppendLine("</table>");
        body.AppendLine("<p style=\"margin:16px 0 0;color:#666;font-size:13px\">📎 Attachment 1: full HTML report (double-click to open in your browser)<br/>📎 Attachment 2: raw JSON data (for programmatic reading)</p>");
        body.AppendLine("</div>");
        msg.Body = body.ToString();

        // 附件 1：HTML 报告
        using (var htmlStream = new MemoryStream(htmlBytes))
        {
            var htmlAttachment = new Attachment(htmlStream, htmlFileName, MediaTypeNames.Text.Html);
            msg.Attachments.Add(htmlAttachment);
        }

        // 附件 2：JSON 原始数据
        using (var jsonStream = new MemoryStream(jsonBytes))
        {
            var jsonAttachment = new Attachment(jsonStream, jsonFileName, MediaTypeNames.Application.Json);
            msg.Attachments.Add(jsonAttachment);
        }

        // 3) SMTP 发送：先 587(STARTTLS)，失败再尝试 465(SSL)
        var err587 = await TrySendAsync(msg, authCode, 587).ConfigureAwait(false);
        if (err587.Ok) return (true, "", null);

        var err465 = await TrySendAsync(msg, authCode, 465).ConfigureAwait(false);
        if (err465.Ok) return (true, "", null);

        // 4) 双端口都失败 → 兜底：把 HTML + JSON 保存到玩家「文档\ChinaGo\Feedback\」，
        //    错误信息带上本地路径，玩家可以直接打开文件夹看报告 + 手动发给你
        string fullError =
            "Port 587 failed: " + err587.Error + "\n\n" +
            "Port 465 failed: " + err465.Error;

        try
        {
            var saved = FeedbackLocalStorage.Save(record, htmlBytes, jsonBytes);
            return (false,
                fullError + "\n\n───────────────────\n" +
                "📂 Feedback was auto-saved locally:\n" + saved.HtmlPath + "\n\n" +
                "Click the \"Open Folder\" button in the game window and send the HTML file to the developer manually.",
                saved.HtmlPath);
        }
        catch (Exception ex)
        {
            // Local fallback also failed (very rare: disk full / permission)
            return (false,
                fullError + "\n\n" +
                "❌ Local save also failed: " + ex.Message +
                "\n(Screenshot and send to the developer)",
                null);
        }
    }

    /// <summary>
    /// 测试 SMTP 连通性（首次配授权码时调用，给玩家即时反馈）。
    /// 发一封极简测试邮件到 DeveloperEmail。
    /// </summary>
    public static async Task<(bool Ok, string Error)> SendTestAsync()
    {
        var authCode = MailCredentials.Load();
        if (string.IsNullOrEmpty(authCode))
            return (false, "QQ mail authorization code not configured yet");

        using var msg = new MailMessage();
        msg.From = new MailAddress(FeedbackService.SenderEmail, SenderDisplayName, Encoding.UTF8);
        msg.To.Add(FeedbackService.DeveloperEmail);
        msg.Subject = "[China Go] SMTP connectivity test";
        msg.Body = "<p>This is a connectivity test email. If you receive it, your authorization code is valid.</p>";
        msg.IsBodyHtml = true;
        msg.BodyEncoding = Encoding.UTF8;

        var err587 = await TrySendAsync(msg, authCode, 587).ConfigureAwait(false);
        if (err587.Ok) return err587;

        var err465 = await TrySendAsync(msg, authCode, 465).ConfigureAwait(false);
        if (err465.Ok) return err465;

        return (false,
            "Port 587 failed: " + err587.Error + "\n\n" +
            "Port 465 failed: " + err465.Error);
    }

    /// <summary>在指定端口上尝试一次 SMTP 发送，返回 (ok, error)。</summary>
    private static async Task<(bool Ok, string Error)> TrySendAsync(
        MailMessage msg, string authCode, int port)
    {
        try
        {
            using var smtp = new SmtpClient(SmtpHost, port)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(FeedbackService.SenderEmail, authCode),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000  // 15 秒
            };
            await smtp.SendMailAsync(msg).ConfigureAwait(false);
            return (true, "");
        }
        catch (SmtpException ex)
        {
            return (false, $"Port {port}: " + FriendlySmtpError(ex));
        }
        catch (Exception ex)
        {
            return (false, $"Port {port}: " + ex.Message);
        }
    }

    /// <summary>把 SmtpException 转成人话错误（QQ 邮箱错误码 → 中文说明）。</summary>
    private static string FriendlySmtpError(SmtpException ex)
    {
        // SmtpStatusCode 是 enum；常见可识别值：GeneralFailure / MailboxBusy / InsufficientStorage
        // QQ 邮箱常见 SMTP 错误码（在 ex.Message 里）：535 auth failure / 530 Must Auth / 421 too many
        var code = (int)ex.StatusCode;
        var msg = ex.Message ?? "";
        if (msg.Contains("535") || msg.Contains("auth failure", StringComparison.OrdinalIgnoreCase))
            return "QQ mail authentication failed: the authorization code is wrong or expired. Regenerate it at mail.qq.com";
        if (msg.Contains("530") || msg.Contains("Must Auth", StringComparison.OrdinalIgnoreCase))
            return "QQ mail requires authentication: the SMTP service is not enabled or the authorization code is invalid";
        if (msg.Contains("421"))
            return "QQ mail temporarily refused the connection (421). Wait a few seconds and try again";
        return ex.StatusCode switch
        {
            SmtpStatusCode.GeneralFailure =>
                $"SMTP connection failed (QQ error code {code}: {msg}).\n" +
                "Common causes:\n" +
                "① Your PC / router / company network is blocking SMTP ports 587/465\n" +
                "② The firewall is blocking smtp.qq.com\n" +
                "③ The QQ mail SMTP service is not enabled or the authorization code is invalid\n" +
                "Fix: check the Windows firewall, or try a phone hotspot; make sure \"Account → SMTP service\" is enabled at mail.qq.com",
            _ => $"SMTP error ({ex.StatusCode}): {msg}"
        };
    }

    private static byte[] BuildJsonAttachment(FeedbackService.FeedbackRecord r)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(r, opts);
        var bytes = Encoding.UTF8.GetBytes(json);
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var withBom = new byte[bom.Length + bytes.Length];
        Buffer.BlockCopy(bom, 0, withBom, 0, bom.Length);
        Buffer.BlockCopy(bytes, 0, withBom, bom.Length, bytes.Length);
        return withBom;
    }
}
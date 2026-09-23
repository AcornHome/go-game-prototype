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
    private const string SenderDisplayName = "中国围棋玩家反馈";

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
            return (false, "尚未配置 QQ 邮箱授权码", null);
        }

        // 2) 构造邮件
        string subject = $"[中国围棋] {record.Category} · {record.Title}";
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
        body.AppendLine("<div style=\"font-family:'Microsoft YaHei UI',sans-serif;color:#2B2014;line-height:1.7\">");
        body.AppendLine("<h2 style=\"margin:0 0 12px;color:#8F6427\">收到一条新的玩家反馈</h2>");
        body.AppendLine("<table style=\"border-collapse:collapse;font-size:14px\">");
        body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">反馈ID</td><td>{WebUtility.HtmlEncode(record.FeedbackId)}</td></tr>");
        body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">类型</td><td>{WebUtility.HtmlEncode(record.Category)}</td></tr>");
        body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">标题</td><td>{WebUtility.HtmlEncode(record.Title)}</td></tr>");
        body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">时间</td><td>{record.SubmittedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}</td></tr>");
        if (!string.IsNullOrEmpty(record.Contact.Email))
            body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">邮箱</td><td>{WebUtility.HtmlEncode(record.Contact.Email)}</td></tr>");
        if (!string.IsNullOrEmpty(record.Contact.SteamHandle))
            body.AppendLine($"<tr><td style=\"padding:4px 12px 4px 0;color:#8F6427;font-weight:600\">Steam</td><td>{WebUtility.HtmlEncode(record.Contact.SteamHandle)}</td></tr>");
        body.AppendLine("</table>");
        body.AppendLine("<p style=\"margin:16px 0 0;color:#666;font-size:13px\">📎 附件 1：完整 HTML 报告（双击浏览器打开即可查看）<br/>📎 附件 2：JSON 原始数据（程序读取用）</p>");
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
            "端口 587 失败：" + err587.Error + "\n\n" +
            "端口 465 失败：" + err465.Error;

        try
        {
            var saved = FeedbackLocalStorage.Save(record, htmlBytes, jsonBytes);
            return (false,
                fullError + "\n\n───────────────────\n" +
                "📂 反馈已自动保存到本地：\n" + saved.HtmlPath + "\n\n" +
                "请点游戏窗口里的「打开文件夹」按钮，手动把 HTML 文件发送给开发者。",
                saved.HtmlPath);
        }
        catch (Exception ex)
        {
            // 本地兜底也失败（极少见，磁盘满 / 权限）
            return (false,
                fullError + "\n\n" +
                "❌ 本地保存也失败：" + ex.Message +
                "\n（请截屏发给开发者）",
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
            return (false, "尚未配置 QQ 邮箱授权码");

        using var msg = new MailMessage();
        msg.From = new MailAddress(FeedbackService.SenderEmail, SenderDisplayName, Encoding.UTF8);
        msg.To.Add(FeedbackService.DeveloperEmail);
        msg.Subject = "[中国围棋] SMTP 连通测试";
        msg.Body = "<p>这是一封连通测试邮件，收到说明你的授权码有效。</p>";
        msg.IsBodyHtml = true;
        msg.BodyEncoding = Encoding.UTF8;

        var err587 = await TrySendAsync(msg, authCode, 587).ConfigureAwait(false);
        if (err587.Ok) return err587;

        var err465 = await TrySendAsync(msg, authCode, 465).ConfigureAwait(false);
        if (err465.Ok) return err465;

        return (false,
            "端口 587 失败：" + err587.Error + "\n\n" +
            "端口 465 失败：" + err465.Error);
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
            return (false, $"[端口 {port}] " + FriendlySmtpError(ex));
        }
        catch (Exception ex)
        {
            return (false, $"[端口 {port}] " + ex.Message);
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
            return "QQ 邮箱认证失败：授权码错误或已过期，请到 mail.qq.com 重新生成";
        if (msg.Contains("530") || msg.Contains("Must Auth", StringComparison.OrdinalIgnoreCase))
            return "QQ 邮箱要求认证：SMTP 服务未开启或授权码无效";
        if (msg.Contains("421"))
            return "QQ 邮箱临时拒绝连接（421），稍等几秒再试";
        return ex.StatusCode switch
        {
            SmtpStatusCode.GeneralFailure =>
                $"SMTP 连接失败（QQ 错误码 {code}：{msg}）。\n" +
                "常见原因：\n" +
                "① 本机/路由器/公司网络阻断了 SMTP 端口 587/465\n" +
                "② 防火墙拦截了 smtp.qq.com\n" +
                "③ 未开启 QQ 邮箱 SMTP 服务或授权码无效\n" +
                "解决：检查 Windows 防火墙，或换手机热点再试；确认 mail.qq.com 里「账户→SMTP服务」已开启",
            _ => $"SMTP 错误（{ex.StatusCode}）：{msg}"
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
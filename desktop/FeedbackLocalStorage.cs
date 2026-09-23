using System;
using System.Diagnostics;
using System.IO;

namespace GoGame;

/// <summary>
/// 反馈本地兜底存储。
///
/// 用途：当反馈服务器连不上（服务器没开 / 网络不通）时，把 HTML 报告 + JSON 数据
/// 写到本机磁盘，避免反馈丢失。
///
/// v1.4.0：目录优先选 D 盘（这台电脑就是服务器，D 盘是反馈主战场），
///         没有 D 盘的机器（很多笔记本只有 C 盘）自动回退到「文档」目录。
///
/// 文件名：feedback-{yyyyMMdd-HHmmss}-{id}.html / .json
///   例：feedback-20260905-213045-ab12cd34.html
/// </summary>
public static class FeedbackLocalStorage
{
    /// <summary>
    /// 本地兜底目录。注意用 LocalInbox 子目录，避免和服务器写入的 data/ 混在一起
    /// （data/ 是服务器自己管的，这里放"没传上去的"，方便你一眼看出哪些没收到）。
    /// </summary>
    public static readonly string FeedbackDir = ResolveFeedbackDir();

    private static string ResolveFeedbackDir()
    {
        const string sub = "ChinaGo\\Feedback\\LocalInbox";
        try
        {
            if (Directory.Exists(@"D:\"))
                return Path.Combine(@"D:\", sub);
        }
        catch { /* 极少数环境访问 D 盘会抛异常，忽略后走回退 */ }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            sub);
    }

    /// <summary>本地兜底保存结果（两个文件路径）。</summary>
    public sealed class SaveResult
    {
        public string HtmlPath { get; }
        public string JsonPath { get; }
        public SaveResult(string html, string json) { HtmlPath = html; JsonPath = json; }
    }

    /// <summary>
    /// 把字节数组落盘到 FeedbackDir（自动建目录）。
    /// 文件名按「本地时间-反馈ID」生成，重复提交也不会撞名（秒级时间已够用）。
    /// </summary>
    public static SaveResult Save(
        FeedbackService.FeedbackRecord record,
        byte[] htmlBytes,
        byte[] jsonBytes)
    {
        Directory.CreateDirectory(FeedbackDir);

        string localTime = record.SubmittedAtUtc.ToLocalTime()
            .ToString("yyyyMMdd-HHmmss");
        string idPart = record.FeedbackId.Length > 3
            ? record.FeedbackId.Substring(3)
            : record.FeedbackId;
        string htmlName = $"feedback-{localTime}-{idPart}.html";
        string jsonName = $"feedback-{localTime}-{idPart}.json";
        string htmlPath = Path.Combine(FeedbackDir, htmlName);
        string jsonPath = Path.Combine(FeedbackDir, jsonName);

        File.WriteAllBytes(htmlPath, htmlBytes);
        File.WriteAllBytes(jsonPath, jsonBytes);

        return new SaveResult(htmlPath, jsonPath);
    }

    /// <summary>用资源管理器打开本地兜底目录（玩家确认邮件发不出去后一键看文件）。</summary>
    public static void OpenFeedbackFolder()
    {
        Directory.CreateDirectory(FeedbackDir);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = FeedbackDir,
                UseShellExecute = true,   // 让 explorer.exe 打开目录
                Verb = "open"
            });
        }
        catch { /* 极少环境 explorer 不在 PATH 上，忽略 */ }
    }

    /// <summary>
    /// v1.0.10：邮件发送失败时，调起 Windows 默认邮件客户端，
    /// 预填收件人 / 主题 / 正文，让玩家把刚弹出的 HTML 文件拖进去当附件发送。
    ///
    /// 用标准 mailto: URI（RFC 6068），只要系统装了 Outlook / QQ邮箱 / Foxmail /
    /// 网易邮箱大师等任意一个默认邮件客户端就能用。
    /// 注意：mailto: 协议自身不支持 ?attachment= 参数（Outlook 也不支持），
    /// 所以附件需要玩家手动拖入。
    /// </summary>
    public static void OpenMailClientWithPrefilled(string htmlPath)
    {
        // 收件人写死开发者邮箱
        const string recipient = "954038398@qq.com";

        // Subject: fixed prefix so the developer can filter easily
        string subject = "ChinaGo player feedback";

        // Body: fixed note telling the player which file to attach
        // Uri.EscapeDataString escapes spaces / newlines / CJK
        string body =
            "Hello, this is a ChinaGo player feedback report.\n\n" +
            "📎 Attachment note:\n" +
            "Please drag the HTML file from the folder that just opened into this email as an attachment →\n" +
            System.IO.Path.GetFileName(htmlPath) + "\n\n" +
            "(Double-click the HTML to open it in a browser; it contains the full report: category / title / description / contact / game / KataGo log / system environment)\n\n" +
            "Thanks for your support!🙏";

        string mailto =
            $"mailto:{recipient}" +
            $"?subject={Uri.EscapeDataString(subject)}" +
            $"&body={Uri.EscapeDataString(body)}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = mailto,
                UseShellExecute = true   // 让系统用默认邮件客户端打开
            });
        }
        catch
        {
            // 极少数环境没装默认邮件客户端（极少见），静默兜底，让玩家走「手动打开文件夹」路径
        }
    }
}
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GoGame;

/// <summary>
/// 玩家反馈记录模型。
/// v1.0.7：反馈不再写本地 JSON，改为直接通过 QQ 邮箱 SMTP 发送到 954038398@qq.com。
/// 本类只保留数据模型（供 MailSender 拼 HTML / JSON 邮件附件用），不再负责落盘 / 配置。
/// </summary>
public static class FeedbackService
{
    public const string SchemaVersion = "1";

    /// <summary>接收反馈的开发者邮箱（QQ 邮箱）。</summary>
    public const string DeveloperEmail = "954038398@qq.com";

    /// <summary>发件人也用同一个 QQ 邮箱（自己发给自己，避免引入第二个邮箱账号）。</summary>
    public const string SenderEmail = "954038398@qq.com";

    public record FeedbackRecord(
        string SchemaVersion,
        string FeedbackId,
        DateTime SubmittedAtUtc,
        string Category,
        string Title,
        string Detail,
        ContactInfo Contact,
        EnvInfo Environment,
        GameSnapshot Game);

    public record ContactInfo(
        string? Email,
        string? SteamHandle);

    public record EnvInfo(
        string AppVersion,
        string OsVersion,
        string DotnetVersion,
        string KataGoPath,
        string KataGoNetworkFile,
        string KataGoLastLogTail);

    public record GameSnapshot(
        string BoardSize,
        int MoveCount,
        string LastMove,
        string ToMove,
        bool KataGoReady);

    /// <summary>构造一条反馈记录（不再写本地 JSON，由 MailSender 直接发送）。</summary>
    public static FeedbackRecord BuildFeedback(
        string category, string title, string detail,
        string? email, string? steamHandle,
        int boardSize, int moveCount, string lastMove, string toMove,
        bool kataGoReady,
        string kataGoPath, string kataGoNetworkFile, string kataGoLogTail)
    {
        return new FeedbackRecord(
            SchemaVersion: SchemaVersion,
            FeedbackId: "fb-" + Guid.NewGuid().ToString("N")[..8],
            SubmittedAtUtc: DateTime.UtcNow,
            Category: category ?? "",
            Title: title?.Trim() ?? "",
            Detail: detail?.Trim() ?? "",
            Contact: new ContactInfo(email?.Trim(), steamHandle?.Trim()),
            Environment: new EnvInfo(
                AppVersion: AppVersion(),
                OsVersion: RuntimeInformation.OSDescription + " " + Environment.OSVersion,
                DotnetVersion: RuntimeInformation.FrameworkDescription,
                KataGoPath: kataGoPath ?? "",
                KataGoNetworkFile: kataGoNetworkFile ?? "",
                KataGoLastLogTail: (kataGoLogTail ?? "").Trim()),
            Game: new GameSnapshot(
                BoardSize: $"{boardSize}x{boardSize}",
                MoveCount: moveCount,
                LastMove: lastMove ?? "",
                ToMove: toMove ?? "",
                KataGoReady: kataGoReady));
    }

    public static string AppVersion()
    {
        var asm = Assembly.GetEntryAssembly();
        var v = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrEmpty(v) ? "1.0.0" : v.Split('+')[0];
    }
}
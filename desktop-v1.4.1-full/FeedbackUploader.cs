using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoGame;

/// <summary>
/// v1.4.0：把玩家反馈 POST 到开发者自己的反馈服务器（这台电脑），落进 D:\ChinaGo\Feedback\。
///
/// 设计要点：
/// 1. 上传失败**绝不抛异常到 UI** —— 全部转成 Result，由 FeedbackWindow 决定兜底策略。
/// 2. 超时只有 10 秒：服务器没开时玩家不该干等，快速失败后走本地兜底。
/// 3. 服务器地址写死在下面的候选列表：玩家端不需要任何配置（用户不碰文件系统）。
/// </summary>
public static class FeedbackUploader
{
    /// <summary>
    /// 外网固定域名 —— 网云穿（dongtaiyuming.net）HTTP 隧道，映射 127.0.0.1:3000。
    /// 玩家在任意网络下都能访问，只要开发者这台电脑开着服务器 + 隧道客户端。
    ///
    /// v1.4.1：3000 上是**工程材料库**（公司生产服务），反馈模块寄生在它里面，
    /// 路由前缀是 <c>/chinago</c> → 所以这里必须带 <c>/chinago</c>，
    /// 最终请求路径 = /chinago/api/feedback（PostOnceAsync 会再拼 /api/feedback）。
    /// 隧道内网端口保持 3000 不变，材料库完全不受影响。
    /// </summary>
    public const string PublicBaseUrl = "http://rzt7s7dz.dongtaiyuming.net/chinago";

    /// <summary>
    /// 本机直连材料库（3000）—— 开发者自己在这台电脑上提交反馈时走这条，最快。
    /// 材料库开机自启 + 崩溃自动重启，所以这条几乎永远可用。
    /// </summary>
    public const string MaterialLibBaseUrl = "http://localhost:3000/chinago";

    /// <summary>
    /// 本机独立反馈服务器（Python server.py，端口 8080）。
    /// 开发者额外开着「Start Feedback Server.bat」时才命中，作为第一条备选。
    /// </summary>
    public const string LocalBaseUrl = "http://localhost:8080";

    /// <summary>
    /// 依次尝试的地址。**本机优先**：
    /// - 开发者自己提交 → python 服务器（8080）或材料库（3000）直接命中，瞬间返回；
    /// - 玩家的电脑 → localhost 没人监听，连接被拒（**毫秒级失败**，不会卡 10 秒）
    ///   → 立刻转外网域名。
    /// 只有全部失败才返回 Fail，由 FeedbackWindow 走本地兜底。
    /// </summary>
    public static string[] CandidateUrls => new[] { LocalBaseUrl, MaterialLibBaseUrl, PublicBaseUrl };

    /// <summary>可通过环境变量覆盖（方便调试，玩家不会用到）。</summary>
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("CHINAGO_FEEDBACK_URL") is { Length: > 0 } s
            ? s.TrimEnd('/')
            : PublicBaseUrl;

    public sealed class Result
    {
        public bool Ok { get; init; }
        public string Error { get; init; } = "";
        public string? SavedPath { get; init; }

        public static Result Success(string? path = null) => new() { Ok = true, SavedPath = path };
        public static Result Fail(string error) => new() { Ok = false, Error = error };
    }

    // 静态 HttpClient：避免每次 new 导致端口耗尽（Socket 耗尽是这类代码最常见的线上坑）
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// 尝试把反馈上传到服务器。任何失败都返回 Result.Fail，不抛异常。
    /// </summary>
    public static async Task<Result> TryUploadAsync(
        FeedbackService.FeedbackRecord record,
        string? userName,
        string? userId)
    {
        // 先序列化一次，后面每个候选地址复用同一份 JSON
        string json;
        try
        {
            var htmlBytes = FeedbackHtmlBuilder.Build(record);

            var payload = new
            {
                feedbackId = record.FeedbackId,
                category = string.IsNullOrWhiteSpace(record.Category) ? "其他" : record.Category,
                title = record.Title,
                detail = record.Detail,
                contact = string.IsNullOrWhiteSpace(record.Contact?.Email)
                    ? record.Contact?.SteamHandle
                    : record.Contact?.Email,
                userName,
                userId,
                appVersion = record.Environment.AppVersion,
                boardSize = record.Game.BoardSize,
                moveCount = record.Game.MoveCount,
                submittedAtUtc = record.SubmittedAtUtc.ToString("o"),
                html = Encoding.UTF8.GetString(htmlBytes)
            };

            json = JsonSerializer.Serialize(payload);
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }

        // 依次尝试：本机 → 外网固定域名。第一个成功就返回，全失败才兜底。
        string lastErr = "连不上反馈服务器";
        foreach (var url in CandidateUrls)
        {
            var r = await PostOnceAsync(url, json);
            if (r.Ok) return r;
            lastErr = r.Error;
        }
        return Result.Fail(lastErr);
    }

    /// <summary>往单个地址发一次。任何异常都转成 Fail，不往外抛。</summary>
    private static async Task<Result> PostOnceAsync(string baseUrl, string json)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(baseUrl + "/api/feedback", content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return Result.Fail($"服务器返回 {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
            {
                var path = root.TryGetProperty("path", out var p) ? p.GetString() : null;
                return Result.Success(path);
            }

            var err = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return Result.Fail(string.IsNullOrWhiteSpace(err) ? "服务器拒绝了这条反馈" : err!);
        }
        catch (TaskCanceledException)
        {
            return Result.Fail("连接超时（服务器可能没开）");
        }
        catch (HttpRequestException)
        {
            return Result.Fail("连不上服务器（服务器可能没开）");
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
    }
}

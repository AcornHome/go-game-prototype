// ChinaGo 反馈服务器 —— 把这台电脑变成反馈服务器
//
// 玩家在游戏里提交反馈 → HTTP POST 到这里 → 写入 D:\ChinaGo\Feedback\
// 同时生成 index.html 汇总页，浏览器打开就能翻看全部反馈（每 20 秒自动刷新）。
//
// 用法：双击 ChinaGoFeedbackServer.exe
//   参数： --port 8080    --dir D:\ChinaGo\Feedback    --no-browser
//
// API：
//   POST /api/feedback   提交反馈（JSON）
//   GET  /               反馈汇总页（浏览器可直接看）
//   GET  /api/list       反馈列表（JSON）
//   GET  /health         健康检查
//   GET  /raw?d=2026-09-10&f=xxx.html   单条反馈原文
//
// 实现说明：故意不依赖 ASP.NET Core，用原始 TcpListener 手写 HTTP。
//   好处 1：零 NuGet 依赖，编译不会卡在还原
//   好处 2：不走 http.sys，不需要管理员权限或 netsh urlacl 保留
//   好处 3：单文件小，双击即用

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

const int DefaultPort = 8080;

// ---------- 命令行参数 ----------
int port = DefaultPort;
string? dirOverride = null;
bool openBrowser = true;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" or "-p" when i + 1 < args.Length:
            int.TryParse(args[++i], out port); break;
        case "--dir" or "-d" when i + 1 < args.Length:
            dirOverride = args[++i]; break;
        case "--no-browser":
            openBrowser = false; break;
        case "--help" or "-h":
            Console.WriteLine("ChinaGo 反馈服务器");
            Console.WriteLine("  --port <n>    监听端口，默认 8080");
            Console.WriteLine("  --dir <path>  反馈保存目录，默认 D:\\ChinaGo\\Feedback");
            Console.WriteLine("  --no-browser  启动时不自动打开浏览器");
            return 0;
    }
}

// ---------- 存储 ----------
var store = new FeedbackStore(dirOverride);
store.EnsureReady();

Console.OutputEncoding = Encoding.UTF8;
PrintBanner(store.Root, port);

// ---------- 限流：同 IP 每分钟最多 20 条 ----------
var hits = new ConcurrentDictionary<string, List<DateTime>>();

bool RateLimitOk(string ip)
{
    var now = DateTime.UtcNow;
    var list = hits.GetOrAdd(ip, _ => new List<DateTime>());
    lock (list)
    {
        list.RemoveAll(t => (now - t).TotalMinutes > 1);
        if (list.Count >= 20) return false;
        list.Add(now);
        return true;
    }
}

// ---------- 启动监听（IPv6 双栈，同时接受 IPv4 / IPv6） ----------
TcpListener listener;
try
{
    listener = new TcpListener(IPAddress.IPv6Any, port);
    listener.Server.DualMode = true;
    listener.Start();
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"[错误] 无法监听端口 {port}：{ex.Message}");
    Console.WriteLine("       可能已被其他程序占用，换一个端口试试：");
    Console.WriteLine($"       ChinaGoFeedbackServer.exe --port {port + 1}");
    Console.WriteLine();
    Console.WriteLine("按任意键退出...");
    Console.ReadKey(true);
    return 1;
}

if (openBrowser)
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(700);
        try { Process.Start(new ProcessStartInfo($"http://localhost:{port}/") { UseShellExecute = true }); }
        catch { /* 忽略 */ }
    });
}

Console.WriteLine("  等待反馈中...（下面每收到一条会打印一行）");
Console.WriteLine();

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); listener.Stop(); };

try
{
    while (!cts.IsCancellationRequested)
    {
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(cts.Token); }
        catch { break; }

        _ = Task.Run(() =>
        {
            try { HandleClient(client, store, RateLimitOk); }
            catch { /* 单个连接出错不影响服务 */ }
            finally { try { client.Dispose(); } catch { } }
        });
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[错误] 监听循环异常：{ex.Message}");
}

Console.WriteLine();
Console.WriteLine("服务已停止。");
return 0;

// ============ 请求处理 ============
static void HandleClient(TcpClient tcp, FeedbackStore store, Func<string, bool> rateLimitOk)
{
    var remote = tcp.Client.RemoteEndPoint as IPEndPoint;
    var rawIp = remote?.Address?.ToString() ?? "unknown";

    tcp.ReceiveTimeout = 15_000;
    tcp.SendTimeout = 15_000;

    using var stream = tcp.GetStream();
    var req = TinyHttp.ReadRequest(stream);
    if (req is null) return;

    var fwd = req.Header("X-Forwarded-For");
    var ip = !string.IsNullOrWhiteSpace(fwd) ? fwd.Split(',')[0].Trim() : rawIp;
    req.ClientIp = ip;

    try
    {
        switch (req.Method)
        {
            case "OPTIONS":
                TinyHttp.Write(stream, 204, "text/plain", Array.Empty<byte>(), cors: true);
                return;

            case "GET" when req.Path == "/":
                TinyHttp.Write(stream, 200, "text/html; charset=utf-8",
                    Encoding.UTF8.GetBytes(store.BuildIndexHtml()), cors: true);
                return;

            case "GET" when req.Path == "/health":
                TinyHttp.WriteJson(stream, 200, new
                {
                    ok = true,
                    service = "ChinaGoFeedbackServer",
                    version = "1.0.0",
                    root = store.Root,
                    count = store.List().Count,
                    now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
                return;

            case "GET" when req.Path == "/api/list":
                TinyHttp.WriteJson(stream, 200, new
                {
                    ok = true,
                    count = store.List().Count,
                    items = store.List()
                });
                return;

            case "GET" when req.Path == "/raw":
                {
                    var d = req.Query("d");
                    var f = req.Query("f");
                    var path = store.ResolveSafePath(d, f);
                    if (path is null || !File.Exists(path))
                    {
                        TinyHttp.Write(stream, 404, "text/plain; charset=utf-8",
                            Encoding.UTF8.GetBytes("文件不存在"));
                        return;
                    }
                    TinyHttp.Write(stream, 200, "text/html; charset=utf-8",
                        File.ReadAllBytes(path), cors: true);
                    return;
                }

            case "POST" when req.Path == "/api/feedback":
                HandleSubmit(stream, req, store, rateLimitOk);
                return;

            default:
                TinyHttp.WriteJson(stream, 404, new { ok = false, error = "未知接口 " + req.Path }, cors: true);
                return;
        }
    }
    catch (Exception ex)
    {
        try { TinyHttp.WriteJson(stream, 500, new { ok = false, error = ex.Message }, cors: true); }
        catch { }
    }
}

static void HandleSubmit(NetworkStream stream, HttpRequest req, FeedbackStore store, Func<string, bool> rateLimitOk)
{
    if (!rateLimitOk(req.ClientIp))
    {
        TinyHttp.WriteJson(stream, 429, new { ok = false, error = "提交太频繁，请一分钟后再试" }, cors: true);
        return;
    }

    FeedbackPayload? payload;
    try
    {
        payload = JsonSerializer.Deserialize<FeedbackPayload>(req.Body, JsonOpts.Payload);
    }
    catch (Exception ex)
    {
        TinyHttp.WriteJson(stream, 400, new { ok = false, error = "JSON 格式错误：" + ex.Message }, cors: true);
        return;
    }

    if (payload is null)
    {
        TinyHttp.WriteJson(stream, 400, new { ok = false, error = "请求体为空" }, cors: true);
        return;
    }
    if (string.IsNullOrWhiteSpace(payload.Title))
    {
        TinyHttp.WriteJson(stream, 400, new { ok = false, error = "标题不能为空" }, cors: true);
        return;
    }

    try
    {
        var saved = store.Save(payload, req.ClientIp);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] #{saved.Id} 「{Trunc(payload.Title, 34)}」 ← {req.ClientIp}");
        TinyHttp.WriteJson(stream, 200, new { ok = true, id = saved.Id, path = saved.RelativePath }, cors: true);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[错误] 保存失败：{ex.Message}");
        TinyHttp.WriteJson(stream, 500, new { ok = false, error = "服务器保存失败：" + ex.Message }, cors: true);
    }
}

// ============ 小工具 ============
static string Trunc(string? s, int max) =>
    string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");

static void PrintBanner(string root, int port)
{
    Console.WriteLine();
    Console.WriteLine("  ==================================================");
    Console.WriteLine("    ChinaGo  反馈服务器");
    Console.WriteLine("  ==================================================");
    Console.WriteLine();
    Console.WriteLine($"  保存目录 : {root}");
    Console.WriteLine($"  监听端口 : {port}");
    Console.WriteLine();
    Console.WriteLine("  访问地址：");
    Console.WriteLine($"    本机       http://localhost:{port}");

    foreach (var ip in NetworkProbe.GetLanIPv4())
        Console.WriteLine($"    局域网     http://{ip}:{port}");

    var v6 = NetworkProbe.GetPublicIPv6().ToList();
    if (v6.Count > 0)
    {
        foreach (var ip in v6)
            Console.WriteLine($"    公网 IPv6  http://[{ip}]:{port}");
        Console.WriteLine("              （公网地址可能随宽带重拨变化，且需防火墙放行端口）");
    }
    else
    {
        Console.WriteLine("    公网       未检测到公网 IPv6，外网玩家需内网穿透");
    }

    Console.WriteLine();
    Console.WriteLine("  保持这个窗口开着 —— 关掉就收不到反馈了。");
    Console.WriteLine("  按 Ctrl+C 停止。");
    Console.WriteLine();
}

// ============ 极简 HTTP ============
static class JsonOpts
{
    public static readonly JsonSerializerOptions Payload = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions Response = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

sealed class HttpRequest
{
    public string Method { get; set; } = "GET";
    public string Target { get; set; } = "/";
    public string Path { get; set; } = "/";
    public string QueryString { get; set; } = "";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = "";
    public string ClientIp { get; set; } = "";

    public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : "";

    public string Query(string key)
    {
        if (string.IsNullOrEmpty(QueryString)) return "";
        foreach (var pair in QueryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1].Replace('+', ' '));
        }
        return "";
    }
}

static class TinyHttp
{
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 2 * 1024 * 1024;

    public static HttpRequest? ReadRequest(NetworkStream s)
    {
        // 逐字节读到 \r\n\r\n，绝不多读（StreamReader 缓冲会吃掉 body，不能用）
        var head = new List<byte>(512);
        int state = 0;
        var one = new byte[1];
        while (head.Count < MaxHeaderBytes)
        {
            int n;
            try { n = s.Read(one, 0, 1); }
            catch { return null; }
            if (n <= 0) break;

            head.Add(one[0]);
            state = one[0] switch
            {
                (byte)'\r' => state is 0 or 2 ? state + 1 : 1,
                (byte)'\n' => state is 1 or 3 ? state + 1 : 0,
                _ => 0
            };
            if (state == 4) break;
        }
        if (head.Count == 0) return null;

        var text = Encoding.ASCII.GetString(head.ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;

        var start = lines[0].Split(' ');
        if (start.Length < 2) return null;

        var req = new HttpRequest
        {
            Method = start[0].ToUpperInvariant(),
            Target = start[1]
        };

        var qi = req.Target.IndexOf('?');
        if (qi >= 0)
        {
            req.Path = req.Target[..qi];
            req.QueryString = req.Target[(qi + 1)..];
        }
        else
        {
            req.Path = req.Target;
        }
        if (req.Path.Length > 1) req.Path = req.Path.TrimEnd('/');

        for (int i = 1; i < lines.Length; i++)
        {
            var c = lines[i].IndexOf(':');
            if (c > 0)
                req.Headers[lines[i][..c].Trim()] = lines[i][(c + 1)..].Trim();
        }

        if (int.TryParse(req.Header("Content-Length"), out var len) && len > 0)
        {
            if (len > MaxBodyBytes) len = MaxBodyBytes;
            var buf = new byte[len];
            int off = 0;
            while (off < len)
            {
                int n = s.Read(buf, off, len - off);
                if (n <= 0) break;
                off += n;
            }
            req.Body = Encoding.UTF8.GetString(buf, 0, off);
        }

        return req;
    }

    private static string Reason(int code) => code switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        404 => "Not Found",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        _ => "OK"
    };

    public static void Write(NetworkStream s, int code, string contentType, byte[] body, bool cors = false)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(Reason(code)).Append("\r\n");
        sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        if (cors)
        {
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
            sb.Append("Access-Control-Allow-Methods: GET,POST,OPTIONS\r\n");
        }
        sb.Append("Connection: close\r\n\r\n");

        var headBytes = Encoding.ASCII.GetBytes(sb.ToString());
        s.Write(headBytes, 0, headBytes.Length);
        if (body.Length > 0) s.Write(body, 0, body.Length);
        s.Flush();
    }

    public static void WriteJson(NetworkStream s, int code, object payload, bool cors = false)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts.Response);
        Write(s, code, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), cors);
    }
}

static class NetworkProbe
{
    public static IEnumerable<string> GetLanIPv4()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                var s = ua.Address.ToString();
                if (s.StartsWith("169.254.") || s.StartsWith("127.")) continue;
                yield return s;
            }
        }
    }

    public static IEnumerable<string> GetPublicIPv6()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                var ip = ua.Address;
                if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) continue;
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Teredo) continue;
                var s = ip.ToString();
                if (s.StartsWith("::1") || s.StartsWith("fe80")) continue;
                yield return s;
            }
        }
    }
}

// ============ 数据模型 ============
sealed class FeedbackPayload
{
    public string? FeedbackId { get; set; }
    public string? Category { get; set; }
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public string? Contact { get; set; }
    public string? UserName { get; set; }
    public string? UserId { get; set; }
    public string? SubmittedAtUtc { get; set; }
    public string? AppVersion { get; set; }
    public int BoardSize { get; set; }
    public int MoveCount { get; set; }
    public string? Html { get; set; }
}

sealed class SaveResult
{
    public string Id { get; set; } = "";
    public string RelativePath { get; set; } = "";
}

// ============ 存储 ============
sealed class FeedbackStore
{
    public string Root { get; }
    private readonly string _dataDir;
    private readonly object _lock = new();

    public FeedbackStore(string? overrideDir)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            Root = Path.GetFullPath(overrideDir);
        }
        else if (Directory.Exists(@"D:\"))
        {
            Root = Path.Combine(@"D:\", "ChinaGo", "Feedback");
        }
        else
        {
            Root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ChinaGo", "Feedback");
        }
        _dataDir = Path.Combine(Root, "data");
    }

    public void EnsureReady()
    {
        Directory.CreateDirectory(_dataDir);
        if (!File.Exists(Path.Combine(Root, "index.html")))
            File.WriteAllText(Path.Combine(Root, "index.html"), BuildIndexHtml(), new UTF8Encoding(false));
    }

    private static string Sanitize(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var sb = new StringBuilder();
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') sb.Append(c);
            else if (c is ' ' or ':' or '+' or '@') sb.Append('-');
        }
        var s = sb.ToString().Trim('-', '.');
        return s.Length == 0 ? fallback : s[..Math.Min(s.Length, 80)];
    }

    public SaveResult Save(FeedbackPayload p, string clientIp)
    {
        lock (_lock)
        {
            var ts = DateTime.UtcNow;
            if (DateTime.TryParse(p.SubmittedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                ts = parsed.ToUniversalTime();

            var local = ts.ToLocalTime();
            var dayName = local.ToString("yyyy-MM-dd");
            var dayDir = Path.Combine(_dataDir, dayName);
            Directory.CreateDirectory(dayDir);

            var id = Sanitize(p.FeedbackId, Guid.NewGuid().ToString("N")[..8]);
            var baseName = $"feedback-{local:HHmmss}-{id}";

            var meta = new Dictionary<string, object?>
            {
                ["feedbackId"] = p.FeedbackId,
                ["category"] = string.IsNullOrWhiteSpace(p.Category) ? "其他" : p.Category,
                ["title"] = p.Title ?? "",
                ["detail"] = p.Detail ?? "",
                ["contact"] = p.Contact,
                ["userName"] = p.UserName,
                ["userId"] = p.UserId,
                ["appVersion"] = p.AppVersion,
                ["boardSize"] = p.BoardSize,
                ["moveCount"] = p.MoveCount,
                ["submittedAtUtc"] = ts.ToString("o"),
                ["submittedAtLocal"] = local.ToString("yyyy-MM-dd HH:mm:ss"),
                ["receivedAtLocal"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["clientIp"] = clientIp,
                ["htmlFile"] = baseName + ".html"
            };

            File.WriteAllText(
                Path.Combine(dayDir, baseName + ".json"),
                JsonSerializer.Serialize(meta, JsonOpts.Response),
                new UTF8Encoding(false));

            var html = string.IsNullOrWhiteSpace(p.Html)
                ? "<!DOCTYPE html><meta charset=\"utf-8\"><p>" + WebUtility.HtmlEncode(p.Detail ?? "") + "</p>"
                : p.Html;
            File.WriteAllText(Path.Combine(dayDir, baseName + ".html"), html, new UTF8Encoding(false));

            RefreshIndexUnsafe();
            return new SaveResult { Id = id, RelativePath = Path.Combine(dayName, baseName + ".html") };
        }
    }

    public List<Dictionary<string, string>> List()
    {
        var result = new List<Dictionary<string, string>>();
        if (!Directory.Exists(_dataDir)) return result;

        foreach (var day in Directory.GetDirectories(_dataDir))
        {
            foreach (var f in Directory.GetFiles(day, "*.json"))
            {
                try
                {
                    var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(f));
                    if (d is null) continue;
                    var row = new Dictionary<string, string>();
                    foreach (var kv in d)
                        row[kv.Key] = kv.Value.ValueKind switch
                        {
                            JsonValueKind.String => kv.Value.GetString() ?? "",
                            _ => kv.Value.ToString()
                        };
                    row["_day"] = Path.GetFileName(day);
                    result.Add(row);
                }
                catch { /* 单条损坏不影响整体 */ }
            }
        }
        return result
            .OrderByDescending(x => x.TryGetValue("submittedAtUtc", out var t) ? t : "")
            .ToList();
    }

    /// <summary>目录穿越防护：只允许 data\{day}\{file}</summary>
    public string? ResolveSafePath(string day, string file)
    {
        if (string.IsNullOrWhiteSpace(day) || string.IsNullOrWhiteSpace(file)) return null;
        if (day.Contains("..") || day.Contains('/') || day.Contains('\\')) return null;
        if (file.Contains("..") || file.Contains('/') || file.Contains('\\')) return null;
        var full = Path.GetFullPath(Path.Combine(_dataDir, day, file));
        var rootFull = Path.GetFullPath(_dataDir);
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public string BuildIndexHtml()
    {
        var items = List();
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine("<title>ChinaGo 玩家反馈汇总</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("*{box-sizing:border-box}");
        sb.AppendLine("body{margin:0;padding:26px;background:#F7F4ED;color:#2B2014;font-family:\"Microsoft YaHei\",\"PingFang SC\",system-ui,sans-serif;font-size:14px;line-height:1.6}");
        sb.AppendLine("h1{font-size:22px;margin:0 0 4px;font-weight:600}");
        sb.AppendLine(".sub{color:#8F6427;font-size:13px;margin-bottom:18px}");
        sb.AppendLine(".stat{display:flex;gap:14px;margin-bottom:18px;flex-wrap:wrap}");
        sb.AppendLine(".card{background:#fff;border:1px solid #E3DACB;border-radius:10px;padding:12px 18px;min-width:150px}");
        sb.AppendLine(".card .n{font-size:20px;font-weight:600;color:#B8893B}");
        sb.AppendLine(".card .l{font-size:12px;color:#8A7A63}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #E3DACB;border-radius:10px;overflow:hidden}");
        sb.AppendLine("th{background:#F2EADA;text-align:left;padding:10px 13px;font-size:13px;font-weight:600;color:#6B5426;white-space:nowrap}");
        sb.AppendLine("td{padding:10px 13px;border-top:1px solid #EFE7D8;vertical-align:top}");
        sb.AppendLine("tr:hover td{background:#FDFAF3}");
        sb.AppendLine(".tag{display:inline-block;padding:2px 9px;border-radius:20px;font-size:12px;background:#F0E4CC;color:#7A5C1E;white-space:nowrap}");
        sb.AppendLine(".tag.kat{background:#FBE3E3;color:#A32D2D}");
        sb.AppendLine(".tag.ui{background:#E3EFFB;color:#185FA5}");
        sb.AppendLine(".tag.rev{background:#E7F3E1;color:#3B6D11}");
        sb.AppendLine(".tag.ins{background:#EFE7F7;color:#5B3A93}");
        sb.AppendLine("a{color:#B8893B;text-decoration:none}a:hover{text-decoration:underline}");
        sb.AppendLine(".empty{padding:46px;text-align:center;color:#A09580;background:#fff;border:1px dashed #DDD2BC;border-radius:10px}");
        sb.AppendLine(".mono{font-family:Consolas,Monaco,monospace;font-size:12px;color:#8A7A63}");
        sb.AppendLine(".foot{margin-top:20px;font-size:12px;color:#A09580}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<h1>ChinaGo 玩家反馈汇总</h1>");
        sb.Append("<div class=\"sub\">保存目录：<span class=\"mono\">").Append(WebUtility.HtmlEncode(Root)).AppendLine("</span></div>");

        var players = items
            .Select(x => x.GetValueOrDefault("userName"))
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .Count();

        sb.AppendLine("<div class=\"stat\">");
        sb.Append("<div class=\"card\"><div class=\"n\">").Append(items.Count).AppendLine("</div><div class=\"l\">反馈总数</div></div>");
        sb.Append("<div class=\"card\"><div class=\"n\">").Append(players).AppendLine("</div><div class=\"l\">反馈玩家数</div></div>");
        sb.Append("<div class=\"card\"><div class=\"n\">")
          .Append(items.Count > 0 ? items[0].GetValueOrDefault("submittedAtLocal", "-") : "-")
          .AppendLine("</div><div class=\"l\">最近一条</div></div>");
        sb.AppendLine("</div>");

        if (items.Count == 0)
        {
            sb.AppendLine("<div class=\"empty\">还没有收到 feedback。<br><br>在游戏里点「问题反馈」提交一条试试，这个页面会自动刷新。</div>");
        }
        else
        {
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th style=\"width:148px\">时间</th><th style=\"width:94px\">分类</th><th>标题</th><th style=\"width:110px\">玩家</th><th style=\"width:140px\">联系方式</th><th style=\"width:70px\">版本</th><th style=\"width:60px\">查看</th></tr></thead>");
            sb.AppendLine("<tbody>");
            foreach (var it in items)
            {
                var cat = it.GetValueOrDefault("category", "其他");
                var cls = cat switch
                {
                    "KataGo 异常" => "kat",
                    "界面卡顿" => "ui",
                    "复盘/SGF" => "rev",
                    "安装" => "ins",
                    _ => ""
                };
                sb.Append("<tr>");
                sb.Append("<td class=\"mono\">").Append(WebUtility.HtmlEncode(it.GetValueOrDefault("submittedAtLocal", "-"))).Append("</td>");
                sb.Append("<td><span class=\"tag ").Append(cls).Append("\">").Append(WebUtility.HtmlEncode(cat)).Append("</span></td>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(it.GetValueOrDefault("title", ""))).Append("</td>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(it.GetValueOrDefault("userName", "-"))).Append("</td>");
                sb.Append("<td class=\"mono\">").Append(WebUtility.HtmlEncode(it.GetValueOrDefault("contact", "-"))).Append("</td>");
                sb.Append("<td class=\"mono\">").Append(WebUtility.HtmlEncode(it.GetValueOrDefault("appVersion", "-"))).Append("</td>");
                sb.Append("<td><a href=\"/raw?d=").Append(Uri.EscapeDataString(it.GetValueOrDefault("_day", "")))
                  .Append("&f=").Append(Uri.EscapeDataString(it.GetValueOrDefault("htmlFile", "")))
                  .Append("\" target=\"_blank\">打开</a></td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.Append("<div class=\"foot\">本页由 ChinaGo 反馈服务器自动生成 · 每收到一条反馈自动更新 · 生成时间：")
          .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
          .AppendLine("</div>");
        sb.AppendLine("<script>setTimeout(function(){location.reload()},20000)</script>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private void RefreshIndexUnsafe()
    {
        try { File.WriteAllText(Path.Combine(Root, "index.html"), BuildIndexHtml(), new UTF8Encoding(false)); }
        catch { /* 索引失败不影响已存文件 */ }
    }
}

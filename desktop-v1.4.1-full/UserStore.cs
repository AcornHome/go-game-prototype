using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoGame;

/// <summary>
/// 本地账号仓库。负责：注册、登录校验、唯一 ID 生成、随机用户名、
/// 以及"记住用户名 / 记住密码"的加密存储。
///
/// 安全上的三条底线：
///   1. 密码永远不落盘明文——只存 PBKDF2(SHA256, 10 万次迭代) 哈希 + 随机盐
///   2. "记住密码"存的明文用 Windows DPAPI 加密，且只在当天有效
///   3. 校验用固定时间比较（FixedTimeEquals），不泄漏"差几个字符"这类信息
/// </summary>
public static class UserStore
{
    // ---------- 存储位置 ----------

    /// <summary>%LOCALAPPDATA%\ChinaGo —— 每用户独立、默认隐藏，玩家不会误删。</summary>
    public static string StoreDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChinaGo");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string UsersPath => Path.Combine(StoreDir, "users.json");

    /// <summary>"记住我"凭据文件（DPAPI 加密）。</summary>
    private static string RememberPath => Path.Combine(StoreDir, "remember.bin");

    // ---------- 用户库读写 ----------

    private sealed class UserDatabase
    {
        public List<UserAccount> Users { get; set; } = new();
    }

    private static List<UserAccount> LoadAll()
    {
        try
        {
            if (!File.Exists(UsersPath)) return new List<UserAccount>();
            var json = File.ReadAllText(UsersPath, Encoding.UTF8);
            var db = JsonSerializer.Deserialize<UserDatabase>(json);
            return db?.Users ?? new List<UserAccount>();
        }
        catch
        {
            // 文件损坏 / 被手工改坏：宁可当空库，也不要让玩家卡在登录界面
            return new List<UserAccount>();
        }
    }

    private static void SaveAll(List<UserAccount> users)
    {
        var db = new UserDatabase { Users = users };
        var json = JsonSerializer.Serialize(db, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(UsersPath, json, Encoding.UTF8);
    }

    /// <summary>已有账号数量（用于判断是否首次启动）。</summary>
    public static int Count => LoadAll().Count;

    // ---------- 注册 / 登录 ----------

    /// <summary>
    /// 注册新账号。成功返回 (true, 新账号)，失败返回 (false, null) 并把原因写进 error。
    /// </summary>
    public static (bool ok, UserAccount? account, string error) Register(
        string userName, string password, string confirmPassword)
    {
        var name = (userName ?? "").Trim();

        if (name.Length == 0) return (false, null, "用户名不能为空");
        if (name.Length < 3) return (false, null, "用户名至少 3 个字符");
        if (name.Length > 20) return (false, null, "用户名最多 20 个字符");
        if (!IsValidUserName(name))
            return (false, null, "用户名只能用中文、字母、数字或下划线");

        if ((password ?? "").Length < 6) return (false, null, "密码至少 6 位");
        if (password != confirmPassword) return (false, null, "两次输入的密码不一致");

        var users = LoadAll();
        if (users.Any(u => string.Equals(u.UserName, name, StringComparison.OrdinalIgnoreCase)))
            return (false, null, $"用户名「{name}」已被占用，换一个试试");

        var (hash, salt) = HashPassword(password);
        var account = new UserAccount
        {
            Id = GenerateUniqueId(users.Select(u => u.Id)),
            UserName = name,
            PasswordHash = hash,
            Salt = salt,
            CreatedAt = DateTime.Now,
            LastLoginAt = DateTime.Now,
        };

        users.Add(account);
        SaveAll(users);
        return (true, account, "");
    }

    /// <summary>
    /// 登录校验。成功会顺带刷新该账号的 LastLoginAt。
    /// </summary>
    public static UserAccount? Login(string userName, string password)
    {
        var name = (userName ?? "").Trim();
        if (name.Length == 0 || password.Length == 0) return null;

        var users = LoadAll();
        var account = users.FirstOrDefault(
            u => string.Equals(u.UserName, name, StringComparison.OrdinalIgnoreCase));

        if (account == null) return null;
        if (!VerifyPassword(password, account.PasswordHash, account.Salt)) return null;

        account.LastLoginAt = DateTime.Now;
        SaveAll(users);
        return account;
    }

    /// <summary>用户名是否已被占用（注册时实时提示用）。</summary>
    public static bool UserNameExists(string userName)
    {
        var name = (userName ?? "").Trim();
        if (name.Length == 0) return false;
        return LoadAll().Any(u => string.Equals(u.UserName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValidUserName(string name)
        => Regex.IsMatch(name, @"^[\w\u4e00-\u9fa5]+$");

    // ---------- 密码哈希 ----------

    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    private static (string hash, string salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        // 用静态 Pbkdf2 而不是 Rfc2898DeriveBytes 构造函数——后者在 .NET 10 已标记过时
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password, saltBytes, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
        return (Convert.ToBase64String(key), Convert.ToBase64String(saltBytes));
    }

    private static bool VerifyPassword(string password, string hash, string salt)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expected = Convert.FromBase64String(hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password, saltBytes, Pbkdf2Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    // ---------- 唯一 ID ----------

    /// <summary>ID 字符集：去掉了 0/O/1/I，避免玩家口头报 ID 时听错。</summary>
    private const string IdChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static string GenerateUniqueId(IEnumerable<string> existingIds)
    {
        var used = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        var chars = new char[8];

        for (int attempt = 0; attempt < 100; attempt++)
        {
            for (int i = 0; i < 8; i++)
                chars[i] = IdChars[RandomNumberGenerator.GetInt32(IdChars.Length)];

            var id = new string(chars);
            if (!used.Contains(id)) return id;
        }

        // 理论上 32^8 ≈ 1 万亿种组合，撞满 100 次几乎不可能；真撞了就退到 GUID
        return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    // ---------- 随机用户名 ----------

    /// <summary>
    /// 围棋的别称与意象。用它拼随机名，比"user8823"有味道得多，
    /// 玩家一看就知道这是围棋软件。
    /// </summary>
    private static readonly string[] GoNicknames =
    {
        "烂柯", "坐隐", "手谈", "忘忧", "乌鹭", "纹枰", "方圆", "星阵",
        "弈秋", "青锋", "白露", "松风", "流云", "听松", "观澜", "落子",
        "守拙", "藏锋", "闲云", "野鹤", "墨白", "疏影", "澄心", "静观",
        "石泉", "竹里", "棋隐", "半山", "知白", "守黑",
    };

    /// <summary>
    /// 生成一个当前未被占用的随机用户名，形如「烂柯7342」。
    /// 会撞号时自动换一个，极端情况退到「棋友123456」。
    /// </summary>
    public static string GenerateRandomUserName()
    {
        var used = new HashSet<string>(
            LoadAll().Select(u => u.UserName), StringComparer.OrdinalIgnoreCase);

        for (int attempt = 0; attempt < 200; attempt++)
        {
            var name = GoNicknames[RandomNumberGenerator.GetInt32(GoNicknames.Length)]
                       + RandomNumberGenerator.GetInt32(1000, 10000);
            if (!used.Contains(name)) return name;
        }

        return "棋友" + RandomNumberGenerator.GetInt32(100000, 1000000);
    }

    // ---------- 记住用户名 / 记住密码 ----------

    /// <summary>
    /// 登录界面"记住我"的持久化内容。
    /// 两个开关互相独立——只勾用户名就只回填用户名，只勾密码就只回填密码。
    /// </summary>
    public sealed class RememberedCredential
    {
        public bool RememberUserName { get; set; }
        public bool RememberPassword { get; set; }
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";

        /// <summary>凭据保存当天日期（yyyy-MM-dd），用于实现"每天需重新登录"。</summary>
        public string SavedDate { get; set; } = "";
    }

    private static string Today() => DateTime.Today.ToString("yyyy-MM-dd");

    /// <summary>
    /// 保存"记住我"状态。不管勾了几个，都一并写入；
    /// 只有真正勾中的字段才会在下次回填。
    /// </summary>
    public static void SaveRemember(RememberedCredential cred)
    {
        try
        {
            var normalized = new RememberedCredential
            {
                RememberUserName = cred.RememberUserName,
                RememberPassword = cred.RememberPassword,
                // 勾了"记住密码"就必须一并记住用户名——否则下次打开
                // 只有一个密码却不知道属于哪个账号，这密码毫无意义。
                UserName = (cred.RememberUserName || cred.RememberPassword) ? cred.UserName : "",
                Password = cred.RememberPassword ? cred.Password : "",
                SavedDate = Today(),
            };

            var json = JsonSerializer.Serialize(normalized);
            var plain = Encoding.UTF8.GetBytes(json);
            var salt = RandomNumberGenerator.GetBytes(16);
            var protectedBytes = ProtectedData.Protect(
                plain, salt, DataProtectionScope.CurrentUser);

            var combined = new byte[salt.Length + protectedBytes.Length];
            Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
            Buffer.BlockCopy(protectedBytes, 0, combined, salt.Length, protectedBytes.Length);

            File.WriteAllBytes(RememberPath, combined);

            // 最佳努力清掉内存里的明文密码
            Array.Clear(plain, 0, plain.Length);
        }
        catch
        {
            // 存不下就算了，不能因为保存失败阻止玩家登录
        }
    }

    /// <summary>
    /// 读取"记住我"状态。
    /// 关键规则：密码只在保存当天有效。跨天之后即使用户勾了"记住密码"，
    /// 这里也会把密码清空——玩家仍需重新输入一次，满足"每天需重新登录"。
    /// 用户名则不受日期影响，一直回填。
    /// </summary>
    public static RememberedCredential LoadRemember()
    {
        var empty = new RememberedCredential();
        try
        {
            if (!File.Exists(RememberPath)) return empty;

            var combined = File.ReadAllBytes(RememberPath);
            if (combined.Length < 17) return empty;

            var salt = new byte[16];
            var protectedBytes = new byte[combined.Length - 16];
            Buffer.BlockCopy(combined, 0, salt, 0, 16);
            Buffer.BlockCopy(combined, 16, protectedBytes, 0, protectedBytes.Length);

            var plain = ProtectedData.Unprotect(
                protectedBytes, salt, DataProtectionScope.CurrentUser);
            var cred = JsonSerializer.Deserialize<RememberedCredential>(
                Encoding.UTF8.GetString(plain)) ?? empty;

            // 每天需重新登录：密码跨天即失效
            if (!string.IsNullOrEmpty(cred.Password) && cred.SavedDate != Today())
                cred.Password = "";

            return cred;
        }
        catch
        {
            // 换 Windows 账户 / 文件损坏 → 一律当作没记住
            return empty;
        }
    }

    /// <summary>玩家主动清除"记住我"（比如换账号、公用电脑）。</summary>
    public static void ClearRemember()
    {
        try { if (File.Exists(RememberPath)) File.Delete(RememberPath); } catch { }
    }
}

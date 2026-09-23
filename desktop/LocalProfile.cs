using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoGame;

/// <summary>
/// 免登录的本地匿名身份。
///
/// v1.5.0 起去掉了用户登录 / 注册：玩家打开软件直接进主界面，不再有账号密码。
/// 但为了反馈能溯源（开发者看到反馈时知道是谁、方便回复），每台机器仍会
/// 持久化一个随机围棋昵称 + 8 位唯一 ID，存在 %LOCALAPPDATA%\ChinaGo\profile.json。
///
/// 与旧 UserStore 的区别：
///   - 无密码、无注册、无登录界面，全程无 UI；
///   - 不存任何敏感信息，ID 仅用于反馈标注，换个机器就换一份。
/// </summary>
public static class LocalProfile
{
    private static string StoreDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChinaGo");

    private static string ProfilePath => Path.Combine(StoreDir, "profile.json");

    /// <summary>ID 字符集：去掉了 0/O/1/I，避免口头报 ID 时听错。</summary>
    private const string IdChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// 读取已有身份；不存在就生成一个新的并落盘。
    /// 任何异常都退化为"当场生成一个临时身份"（不抛、不阻断启动）。
    /// </summary>
    public static UserAccount GetOrCreate()
    {
        try
        {
            if (File.Exists(ProfilePath))
            {
                var json = File.ReadAllText(ProfilePath, Encoding.UTF8);
                var p = JsonSerializer.Deserialize<ProfileDto>(json);
                if (p != null && !string.IsNullOrEmpty(p.Id) && !string.IsNullOrEmpty(p.UserName))
                    return new UserAccount
                    {
                        Id = p.Id,
                        UserName = p.UserName,
                        CreatedAt = p.CreatedAt,
                    };
            }
        }
        catch { /* 损坏就当没有，下面重新生成 */ }

        var account = new UserAccount
        {
            Id = GenerateId(),
            UserName = GenerateRandomUserName(),
            CreatedAt = DateTime.Now,
        };

        try
        {
            Directory.CreateDirectory(StoreDir);
            var dto = new ProfileDto { Id = account.Id, UserName = account.UserName, CreatedAt = account.CreatedAt };
            File.WriteAllText(ProfilePath,
                JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
        }
        catch { /* 存不下也不影响使用，下次启动再试 */ }

        return account;
    }

    private static string GenerateId()
    {
        var chars = new char[8];
        for (int i = 0; i < 8; i++)
            chars[i] = IdChars[RandomNumberGenerator.GetInt32(IdChars.Length)];
        return new string(chars);
    }

    /// <summary>围棋别称与意象，拼随机名比"user8823"有味道，一看就知道是围棋软件。</summary>
    private static readonly string[] GoNicknames =
    {
        "StillLake", "HiddenValley", "WhisperingPine", "QuietStream", "CloudRest", "RiverStone",
        "MoonlitBoard", "FirstMove", "EmptyCorner", "MountainMist", "ColdSpring", "PineWind",
        "FloatingCloud", "ListeningPine", "CalmMind", "ObservingWaves", "FallingStone", "JadeWhite",
        "InkBlack", "StoneSpring", "BambooGrove", "HalfHill", "KnowingWhite", "GuardingBlack",
        "Windswept", "WildCrane", "InkShadow", "ClearHeart", "SilentWatch", "SparseShade",
    };

    private static string GenerateRandomUserName()
        => GoNicknames[RandomNumberGenerator.GetInt32(GoNicknames.Length)]
           + RandomNumberGenerator.GetInt32(1000, 10000);

    private sealed class ProfileDto
    {
        public string Id { get; set; } = "";
        public string UserName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}

namespace GoGame;

/// <summary>
/// 一个本地玩家账号。
///
/// 重要前提：当前版本没有服务器，所有账号只存在玩家自己电脑里
/// （%LOCALAPPDATA%\ChinaGo\users.json）。换电脑、重装系统、删了 AppData
/// 数据都不会跟着走。这是刻意的取舍——先把单机体验做扎实，
/// 将来要云同步时，这套 Id / 密码哈希结构可以直接搬到服务端。
/// </summary>
public class UserAccount
{
    /// <summary>
    /// 全局唯一 ID：8 位大写字母数字。
    /// 字符集刻意剔除了 0/O/1/I 这四个容易看错的字符——
    /// 玩家可能要口头报 ID 给开发者排查问题，不能让他念错。
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>用户名（登录用，3–20 字符，全库唯一）。</summary>
    public string UserName { get; set; } = "";

    /// <summary>PBKDF2 密码哈希（Base64）。任何时候都不存明文密码。</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>密码盐（Base64），每个账号独立随机生成，防止彩虹表。</summary>
    public string Salt { get; set; } = "";

    /// <summary>账号创建时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最近一次成功登录时间。</summary>
    public DateTime LastLoginAt { get; set; }

    /// <summary>
    /// 展示用 ID：把 8 位拆成 XXXX-XXXX。
    /// 人眼读短分组比读一长串靠谱，反馈问题时也更方便抄写。
    /// </summary>
    public string DisplayId =>
        Id.Length == 8 ? Id[..4] + "-" + Id[4..] : Id;
}

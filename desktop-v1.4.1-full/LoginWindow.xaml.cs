using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GoGame;

/// <summary>
/// 登录 / 注册窗口。应用启动的第一道门——只有这里返回 true，主界面才会打开。
///
/// 关于"每天需重新登录"：
///   记住用户名 → 长期回填，不受日期影响
///   记住密码   → 只在保存当天有效，跨天自动清空（UserStore.LoadRemember 里处理）
/// 所以即便两个都勾上，第二天打开仍然要自己输一次密码、点一次登录。
/// </summary>
public partial class LoginWindow : Window
{
    /// <summary>登录（或注册）成功后，调用方从这里取账号信息。</summary>
    public UserAccount? LoggedInUser { get; private set; }

    private static readonly SolidColorBrush DangerBrush = new(Color.FromRgb(0xD8, 0x58, 0x4A));
    private static readonly SolidColorBrush DimBrush = new(Color.FromRgb(0x9C, 0xA8, 0x9F));

    public LoginWindow()
    {
        InitializeComponent();
        RestoreRemembered();

        // 回车即提交，不用每次去够鼠标
        LoginNameBox.KeyDown += OnLoginFieldEnter;
        LoginPwdBox.KeyDown += OnLoginFieldEnter;
        RegNameBox.KeyDown += OnRegisterFieldEnter;
        RegPwdBox.KeyDown += OnRegisterFieldEnter;
        RegPwd2Box.KeyDown += OnRegisterFieldEnter;
    }

    private void OnLoginFieldEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Login_Click(sender, e);
    }

    private void OnRegisterFieldEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Register_Click(sender, e);
    }

    // ---------- 回填"记住我" ----------

    private void RestoreRemembered()
    {
        var cred = UserStore.LoadRemember();

        RememberNameCheck.IsChecked = cred.RememberUserName;
        RememberPwdCheck.IsChecked = cred.RememberPassword;

        if (!string.IsNullOrEmpty(cred.UserName))
            LoginNameBox.Text = cred.UserName;

        if (!string.IsNullOrEmpty(cred.Password))
        {
            LoginPwdBox.Password = cred.Password;
        }
        else if (cred.RememberPassword)
        {
            // 勾了记住密码，但已跨天被清空 → 明确告诉玩家为什么密码是空的
            ShowLoginHint("新的一天，请重新输入密码", danger: false);
        }

        // 用户名已回填时，光标直接落到密码框，少点一次
        if (LoginNameBox.Text.Length > 0) LoginPwdBox.Focus();
        else LoginNameBox.Focus();
    }

    // ---------- 页签切换 ----------

    private void TabLogin_Click(object sender, RoutedEventArgs e) => SwitchTab(login: true);

    private void TabRegister_Click(object sender, RoutedEventArgs e) => SwitchTab(login: false);

    private void SwitchTab(bool login)
    {
        LoginPanel.Visibility = login ? Visibility.Visible : Visibility.Collapsed;
        RegisterPanel.Visibility = login ? Visibility.Collapsed : Visibility.Visible;

        TabLoginLine.Visibility = login ? Visibility.Visible : Visibility.Collapsed;
        TabRegisterLine.Visibility = login ? Visibility.Collapsed : Visibility.Visible;

        TabLoginBtn.Foreground = login
            ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xEE))
            : new SolidColorBrush(Color.FromRgb(0x6F, 0x7B, 0x73));
        TabRegisterBtn.Foreground = login
            ? new SolidColorBrush(Color.FromRgb(0x6F, 0x7B, 0x73))
            : new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xEE));

        // 切到注册页且还没填名字时，先送一个随机名——
        // 想自己起的玩家直接改掉即可，不用先去点"随机"。
        if (!login && RegNameBox.Text.Trim().Length == 0)
            RegNameBox.Text = UserStore.GenerateRandomUserName();

        if (login) LoginNameBox.Focus();
        else RegNameBox.Focus();
    }

    // ---------- 登录 ----------

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        var name = LoginNameBox.Text.Trim();
        var pwd = LoginPwdBox.Password;

        if (name.Length == 0) { ShowLoginHint("请输入用户名"); LoginNameBox.Focus(); return; }
        if (pwd.Length == 0) { ShowLoginHint("请输入密码"); LoginPwdBox.Focus(); return; }

        var account = UserStore.Login(name, pwd);
        if (account == null)
        {
            ShowLoginHint("用户名或密码不正确");
            LoginPwdBox.Clear();
            LoginPwdBox.Focus();
            return;
        }

        // 只在登录成功时才写入密钥串——登录失败不保存，避免把错误密码记下来
        UserStore.SaveRemember(new UserStore.RememberedCredential
        {
            RememberUserName = RememberNameCheck.IsChecked == true,
            RememberPassword = RememberPwdCheck.IsChecked == true,
            UserName = name,
            Password = pwd,
        });

        LoggedInUser = account;
        DialogResult = true;
    }

    // ---------- 注册 ----------

    private void RandomName_Click(object sender, RoutedEventArgs e)
    {
        RegNameBox.Text = UserStore.GenerateRandomUserName();
        RegHint.Visibility = Visibility.Collapsed;
        RegNameBox.Focus();
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        var name = RegNameBox.Text.Trim();
        var pwd = RegPwdBox.Password;
        var pwd2 = RegPwd2Box.Password;

        var (ok, account, error) = UserStore.Register(name, pwd, pwd2);
        if (!ok || account == null)
        {
            ShowRegHint(error);
            return;
        }

        // 注册成功：先把专属 ID 讲清楚，再进主界面
        StyledDialog.ShowInfo(this, "注册成功",
            $"欢迎你，{account.UserName}！\n\n" +
            $"你的专属 ID：{account.DisplayId}\n\n" +
            "这个 ID 全局唯一，反馈问题时把它报给开发者，可以快速定位到你的记录。\n" +
            "账号数据保存在本机，换电脑或重装系统不会自动同步。");

        // 注册即登录，并帮玩家记住用户名（不记密码，第二天仍需输一次）
        UserStore.SaveRemember(new UserStore.RememberedCredential
        {
            RememberUserName = true,
            RememberPassword = false,
            UserName = account.UserName,
            Password = "",
        });

        LoggedInUser = account;
        DialogResult = true;
    }

    // ---------- 提示条 ----------

    private void ShowLoginHint(string msg, bool danger = true)
    {
        LoginHint.Text = msg;
        LoginHint.Foreground = danger ? DangerBrush : DimBrush;
        LoginHint.Visibility = Visibility.Visible;
    }

    private void ShowRegHint(string msg)
    {
        RegHint.Text = msg;
        RegHint.Foreground = DangerBrush;
        RegHint.Visibility = Visibility.Visible;
    }
}

using System;
using System.Windows;
using System.Windows.Controls;

namespace GoGame;

/// <summary>
/// 让玩家输入 QQ 邮箱授权码并保存到本地（DPAPI 加密）。
/// "保存并测试"会真的发一封测试邮件到 DeveloperEmail，成功才认为配置成功。
/// </summary>
public partial class MailConfigDialog : Window
{
    public bool Configured { get; private set; }

    public MailConfigDialog()
    {
        InitializeComponent();

        // 如果之前配过，预先填一个掩码占位（明文 DPAPI 解出来一次性填充）
        var existing = MailCredentials.Load();
        if (!string.IsNullOrEmpty(existing))
        {
            AuthBox.Password = existing;
            HintText.Text = "💡 检测到已有授权码，已预填。直接点「保存并测试」即可。";
        }
    }

    private void ShowPwd_Checked(object sender, RoutedEventArgs e)
    {
        // 用普通 TextBox 临时替换以明文显示（简单方案：直接换 PasswordChar）
        AuthBox.PasswordChar = '\0';
    }

    private void ShowPwd_Unchecked(object sender, RoutedEventArgs e)
    {
        AuthBox.PasswordChar = '●';
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = Configured;
        Close();
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        // 去掉所有空格 + 首尾 trim（QQ 邮箱授权码复制常带空格）
        var pwd = (AuthBox.Password ?? "").Trim().Replace(" ", "").Replace("\t", "");
        if (pwd.Length < 8)
        {
            HintText.Text = "⚠️ 授权码通常是 16 位字符，请检查";
            HintText.Foreground = System.Windows.Media.Brushes.IndianRed;
            return;
        }

        TestBtn.IsEnabled = false;
        HintText.Text = "⏳ 正在保存并发送测试邮件到 954038398@qq.com ...";
        HintText.Foreground = System.Windows.Media.Brushes.Goldenrod;

        try
        {
            // 1. 先保存（让 MailSender.SendTestAsync 能 Load 出来）
            MailCredentials.Save(pwd);

            // 2. 立即测试
            var (ok, err) = await MailSender.SendTestAsync();
            if (ok)
            {
                Configured = true;
                HintText.Text = "✅ 测试邮件已成功发送！请到 954038398@qq.com 邮箱确认";
                HintText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                // 给玩家 1.5 秒看清结果再关
                await System.Threading.Tasks.Task.Delay(1500);
                DialogResult = true;
                Close();
            }
            else
            {
                // 测试失败：清掉保存（防止下次再误用错误密码）
                MailCredentials.Clear();
                HintText.Text = "❌ " + err + "\n\n请到 mail.qq.com 重新生成授权码后再试";
                HintText.Foreground = System.Windows.Media.Brushes.IndianRed;
            }
        }
        catch (Exception ex)
        {
            MailCredentials.Clear();
            HintText.Text = "❌ " + ex.Message;
            HintText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }
}
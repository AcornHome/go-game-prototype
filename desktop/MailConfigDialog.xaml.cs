using System;
using System.Windows;
using System.Windows.Controls;

namespace GoGame;

/// <summary>
/// Lets the player enter their QQ mail authorization code and save it locally (DPAPI encrypted).
/// "Save & Test" really sends a test email to DeveloperEmail; only on success is the config considered done.
/// </summary>
public partial class MailConfigDialog : Window
{
    public bool Configured { get; private set; }

    public MailConfigDialog()
    {
        InitializeComponent();

        // If configured before, pre-fill a masked placeholder (the plaintext DPAPI value is filled once)
        var existing = MailCredentials.Load();
        if (!string.IsNullOrEmpty(existing))
        {
            AuthBox.Password = existing;
            HintText.Text = "💡 An existing authorization code was detected and pre-filled. Just click \"Save & Test\".";
        }
    }

    private void ShowPwd_Checked(object sender, RoutedEventArgs e)
    {
        // Temporarily show the plaintext with a normal TextBox (simple approach: just swap PasswordChar)
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
        // Strip all spaces + trim (QQ mail authorization codes often carry spaces when copied)
        var pwd = (AuthBox.Password ?? "").Trim().Replace(" ", "").Replace("\t", "");
        if (pwd.Length < 8)
        {
            HintText.Text = "⚠️ The authorization code is usually 16 characters; please check";
            HintText.Foreground = System.Windows.Media.Brushes.IndianRed;
            return;
        }

        TestBtn.IsEnabled = false;
        HintText.Text = "⏳ Saving and sending a test email to 954038398@qq.com ...";
        HintText.Foreground = System.Windows.Media.Brushes.Goldenrod;

        try
        {
            // 1. Save first (so MailSender.SendTestAsync can Load it)
            MailCredentials.Save(pwd);

            // 2. Test immediately
            var (ok, err) = await MailSender.SendTestAsync();
            if (ok)
            {
                Configured = true;
                HintText.Text = "✅ Test email sent successfully! Check the 954038398@qq.com inbox to confirm";
                HintText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                // Give the player 1.5s to read the result before closing
                await System.Threading.Tasks.Task.Delay(1500);
                DialogResult = true;
                Close();
            }
            else
            {
                // Test failed: clear the save (avoid reusing a wrong password next time)
                MailCredentials.Clear();
                HintText.Text = "❌ " + err + "\n\nGo to mail.qq.com to regenerate the authorization code and try again";
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

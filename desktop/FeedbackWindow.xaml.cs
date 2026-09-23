using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace GoGame;

public partial class FeedbackWindow : Window
{
    public bool Submitted { get; private set; }

    // v1.4.0：当前登录玩家（用于让开发者知道是谁反馈的，方便回复）
    private readonly string? _userName;
    private readonly string? _userId;

    // 环境信息不在 UI 显示，但会自动写入反馈报告的 HTML/JSON 供开发者分析
    private readonly string _kataGoPath;
    private readonly string _networkFile;
    private readonly string _kataGoLogTail;
    private readonly int _boardSize;
    private readonly int _moveCount;
    private readonly string _lastMove;
    private readonly string _toMove;
    private readonly bool _kataGoReady;

    public FeedbackWindow(
        int boardSize, int moveCount, string lastMove, string toMove, bool kataGoReady,
        string kataGoPath, string networkFile, string kataGoLogTail,
        string? userName = null, string? userId = null)
    {
        InitializeComponent();

        _boardSize = boardSize;
        _moveCount = moveCount;
        _lastMove = lastMove ?? "";
        _toMove = toMove ?? "";
        _kataGoReady = kataGoReady;
        _kataGoPath = kataGoPath ?? "";
        _networkFile = networkFile ?? "";
        _kataGoLogTail = kataGoLogTail ?? "";
        _userName = userName;
        _userId = userId;

        // 标题实时计数
        TitleBox.TextChanged += (_, _) =>
            TitleCount.Text = $"{TitleBox.Text.Length} / 120";

        TitleBox.Focus();
    }

    /// <summary>取当前选中的分类标签（与 XAML RadioButton 名字映射）。</summary>
    private string GetSelectedCategory()
    {
        if (CatKataGo.IsChecked == true)  return "KataGo error";
        if (CatUi.IsChecked == true)       return "UI lag";
        if (CatReview.IsChecked == true)   return "Review / SGF";
        if (CatLearn.IsChecked == true)    return "Learning features";
        if (CatInstall.IsChecked == true)  return "Install & launch";
        if (CatOther.IsChecked == true)    return "Other / suggestion";
        return "Other / suggestion";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            ShowHint("⚠️ Please enter a title", true);
            TitleBox.Focus();
            return;
        }
        if (TitleBox.Text.Trim().Length < 3)
        {
            ShowHint("⚠️ Please make the title more specific (at least 3 characters)", true);
            TitleBox.Focus();
            return;
        }

        // v1.4.0：先上传到开发者电脑上的反馈服务器；连不上再本地兜底
        SubmitBtn.IsEnabled = false;
        SubmitBtn.Content = "📤 Sending...";
        ShowHint("Uploading to the feedback server...", false);

        try
        {
            var record = FeedbackService.BuildFeedback(
                GetSelectedCategory(), TitleBox.Text.Trim(), DetailBox.Text,
                string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim(),
                string.IsNullOrWhiteSpace(SteamBox.Text) ? null : SteamBox.Text.Trim(),
                _boardSize, _moveCount, _lastMove, _toMove, _kataGoReady,
                _kataGoPath, _networkFile, _kataGoLogTail);

            var up = await FeedbackUploader.TryUploadAsync(record, _userName, _userId);
            if (up.Ok)
            {
                Submitted = true;
                SubmitBtn.Content = "✅ Sent";
                ShowHint("✅ Feedback received by the developer (D:\\ChinaGo\\Feedback)", false);
                await System.Threading.Tasks.Task.Delay(1200);
                DialogResult = true;
                Close();
                return;
            }

            // ---- 服务器连不上 → 本地兜底，绝不丢反馈 ----
            SubmitBtn.IsEnabled = true;
            SubmitBtn.Content = LocalizationManager.Get("FbSubmit");

            string? localHtmlPath = null;
            try
            {
                var htmlBytes = FeedbackHtmlBuilder.Build(record);
                var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(record,
                    new JsonSerializerOptions { WriteIndented = true });
                var saved = FeedbackLocalStorage.Save(record, htmlBytes, jsonBytes);
                localHtmlPath = saved.HtmlPath;
            }
            catch (Exception saveEx)
            {
                ShowHint("❌ Upload failed and local save failed: " + saveEx.Message, true);
                StyledDialog.ShowError(this, "Submission failed",
                    "Could not reach the feedback server, and local save also failed.\n\n" +
                    "Reason: " + up.Error + "\n" +
                    "Save error: " + saveEx.Message);
                return;
            }

            SubmitBtn.Content = "📂 Saved locally";
            ShowHint("⚠️ Server unreachable; feedback saved locally", true);

            var r = StyledDialog.ShowThree(
                this,
                "Feedback saved locally",
                "⚠️ Could not reach the feedback server (" + up.Error + ")\n\n" +
                "But the full report is saved on this computer:\n" + localHtmlPath + "\n\n" +
                "If you are the developer: first run \"Start Feedback Server.bat\"\n" +
                "to start the server, then submit again to deliver it directly.\n\n" +
                "📂 Click \"Open folder\" to view this report directly.",
                "📧 Email now",
                "📂 Open folder",
                "Close");

            if (r == ThreeResult.First)
            {
                FeedbackLocalStorage.OpenMailClientWithPrefilled(localHtmlPath);
                FeedbackLocalStorage.OpenFeedbackFolder();
            }
            else if (r == ThreeResult.Second)
            {
                FeedbackLocalStorage.OpenFeedbackFolder();
            }
        }
        catch (Exception ex)
        {
            SubmitBtn.IsEnabled = true;
            SubmitBtn.Content = LocalizationManager.Get("FbSubmit");
            ShowHint("❌ Submission failed: " + ex.Message, true);
        }
    }

    private void ShowHint(string msg, bool isError)
    {
        HintText.Text = msg;
        HintText.Foreground = isError
            ? System.Windows.Media.Brushes.IndianRed
            : System.Windows.Media.Brushes.Gray;
    }
}

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
        if (CatKataGo.IsChecked == true)  return "KataGo 异常";
        if (CatUi.IsChecked == true)       return "界面卡顿";
        if (CatReview.IsChecked == true)   return "复盘/SGF";
        if (CatLearn.IsChecked == true)    return "教学";
        if (CatInstall.IsChecked == true)  return "安装";
        if (CatOther.IsChecked == true)    return "其他";
        return "其他";
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
            ShowHint("⚠️ 请先填标题", true);
            TitleBox.Focus();
            return;
        }
        if (TitleBox.Text.Trim().Length < 3)
        {
            ShowHint("⚠️ 标题再具体一点（至少 3 个字）", true);
            TitleBox.Focus();
            return;
        }

        // v1.4.0：先上传到开发者电脑上的反馈服务器；连不上再本地兜底
        SubmitBtn.IsEnabled = false;
        SubmitBtn.Content = "📤 发送中...";
        ShowHint("正在上传到反馈服务器...", false);

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
                SubmitBtn.Content = "✅ 已发送";
                ShowHint("✅ 反馈已送达开发者电脑（D:\\ChinaGo\\Feedback）", false);
                await System.Threading.Tasks.Task.Delay(1200);
                DialogResult = true;
                Close();
                return;
            }

            // ---- 服务器连不上 → 本地兜底，绝不丢反馈 ----
            SubmitBtn.IsEnabled = true;
            SubmitBtn.Content = "📤 提交反馈";

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
                ShowHint("❌ 上传失败且本地保存失败：" + saveEx.Message, true);
                StyledDialog.ShowError(this, "提交失败",
                    "反馈服务器连不上，本地也保存失败。\n\n" +
                    "原因：" + up.Error + "\n" +
                    "保存错误：" + saveEx.Message);
                return;
            }

            SubmitBtn.Content = "📂 已保存本地";
            ShowHint("⚠️ 服务器没连上，反馈已保存到本地", true);

            var r = StyledDialog.ShowThree(
                this,
                "反馈已保存到本地",
                "⚠️ 没连上反馈服务器（" + up.Error + "）\n\n" +
                "但反馈已经完整保存到本机：\n" + localHtmlPath + "\n\n" +
                "如果你就是开发者：先双击运行「Start Feedback Server.bat」\n" +
                "把服务器开起来，再回来重新提交一次就能直接送达。\n\n" +
                "📂 点「打开文件夹」可以直接查看这份报告。",
                "📧 一键发邮件",
                "📂 打开文件夹",
                "关闭");

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
            SubmitBtn.Content = "📤 提交反馈";
            ShowHint("❌ 提交失败：" + ex.Message, true);
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
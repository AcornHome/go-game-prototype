using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoGame;

public enum StyledDialogKind
{
    Info,
    Warning,
    Error,
    Confirm,
    ThreeChoice,   // v1.0.10 预留：用 ShowThree() 路径构造，绕过本枚举
}

/// <summary>
/// v1.0.10 三按钮选择的结果。
///  - First  = 用户选了第一个按钮（推荐主操作）
///  - Second = 用户选了第二个按钮（次操作）
///  - Cancel = 关闭，啥也不做
/// </summary>
public enum ThreeResult
{
    Cancel,
    First,
    Second,
}

public enum StyledDialogResult
{
    None,
    Ok,
    Yes,
    No,
    Cancel,
}

public partial class StyledMessageWindow : Window
{
    public StyledDialogResult Result { get; private set; } = StyledDialogResult.None;
    public ThreeResult ThreeChoice { get; private set; } = ThreeResult.Cancel;

    public StyledMessageWindow(string title, string message, StyledDialogKind kind, bool destructiveConfirm = false)
    {
        InitializeComponent();
        Title = title;
        DialogTitle.Text = title;
        DialogMessage.Text = message;

        switch (kind)
        {
            case StyledDialogKind.Error:
                StyleAsDanger();
                DialogIcon.Text = "⚠";
                AddButton(LocalizationManager.Get("DlgClose"), StyledDialogResult.Ok, isPrimary: true, isDefault: true, isCancel: true);
                break;
            case StyledDialogKind.Warning:
                StyleAsDanger();
                DialogIcon.Text = "⚠";
                AddButton(LocalizationManager.Get("DlgOk"), StyledDialogResult.Ok, isPrimary: true, isDefault: true, isCancel: true);
                break;
            case StyledDialogKind.Info:
                DialogIcon.Text = "ℹ";
                AddButton(LocalizationManager.Get("DlgOk"), StyledDialogResult.Ok, isPrimary: true, isDefault: true, isCancel: true);
                break;
            case StyledDialogKind.Confirm:
                DialogIcon.Text = "❓";
                AddButton(LocalizationManager.Get("DlgCancel"), StyledDialogResult.No, isPrimary: false, isDefault: false, isCancel: true);
                AddButton(LocalizationManager.Get("DlgConfirm"), StyledDialogResult.Yes, isPrimary: true, isDefault: true, isCancel: false, destructive: destructiveConfirm);
                break;
        }
    }

    /// <summary>v1.0.10：本地兜底三按钮（邮件发送失败时用）。
    ///   第一个按钮 = 主推荐操作（默认聚焦）
    ///   第二个按钮 = 次操作
    ///   第三个按钮 = 关闭（ESC）
    /// </summary>
    public StyledMessageWindow(
        string title, string message,
        string firstBtnLabel, bool firstIsPrimary, bool firstIsDefault,
        string secondBtnLabel,
        string cancelBtnLabel)
    {
        InitializeComponent();
        Title = title;
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        // 邮件发不出去是异常但有兜底 → 警告色（橙）
        StyleAsDanger();
        DialogIcon.Text = "📨";

        AddButton(firstBtnLabel, StyledDialogResult.Yes, isPrimary: firstIsPrimary, isDefault: firstIsDefault, isCancel: false);
        AddButton(secondBtnLabel, StyledDialogResult.No, isPrimary: !firstIsPrimary, isDefault: false, isCancel: false);
        AddButton(cancelBtnLabel, StyledDialogResult.Cancel, isPrimary: false, isDefault: false, isCancel: true);
    }

    private void StyleAsDanger()
    {
        DialogIcon.Foreground = (Brush)FindResource("BrushDanger");
        DialogTitle.Foreground = (Brush)FindResource("BrushDanger");
        RootBorder.BorderBrush = (Brush)FindResource("BrushDanger");
    }

    private void AddButton(string label, StyledDialogResult result, bool isPrimary, bool isDefault, bool isCancel, bool destructive = false)
    {
        var styleKey = destructive ? "BtnDanger" : (isPrimary ? "BtnPrimary" : "BtnBase");
        var btn = new Button
        {
            Content = label,
            Style = (Style)FindResource(styleKey),
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        btn.Click += (_, _) =>
        {
            Result = result;
            // v1.0.10：三按钮路径下要用 ThreeChoice 区分 Yes/No/Cancel
            // Yes → First，主推荐操作
            // No  → Second，次操作
            // Cancel/Ok → Cancel（Ok 在三按钮路径里不会出现）
            ThreeChoice = result switch
            {
                StyledDialogResult.Yes => ThreeResult.First,
                StyledDialogResult.No  => ThreeResult.Second,
                _ => ThreeResult.Cancel,
            };
            // ShowDialog() 返回 true/false，但 ShowThree 通过 ThreeChoice 属性判断，不依赖 DialogResult
            // 三个按钮都设 DialogResult=true，否则用户选第二个或第三个时外层拿到 null 误判
            DialogResult = true;
            Close();
        };
        ButtonPanel.Children.Add(btn);
    }
}

/// <summary>统一风格的弹窗：认输确认、错误提示、警告、通知。替代默认 MessageBox。</summary>
public static class StyledDialog
{
    /// <summary>二选一确认。destructive=true 时主按钮用危险色（认输、删除等）。</summary>
    public static bool ShowConfirm(Window owner, string title, string message, bool destructive = false)
    {
        var dlg = new StyledMessageWindow(title, message, StyledDialogKind.Confirm, destructive) { Owner = owner };
        var ok = dlg.ShowDialog() == true && dlg.Result == StyledDialogResult.Yes;
        return ok;
    }

    public static void ShowInfo(Window owner, string title, string message)
    {
        var dlg = new StyledMessageWindow(title, message, StyledDialogKind.Info) { Owner = owner };
        dlg.ShowDialog();
    }

    public static void ShowWarning(Window owner, string title, string message)
    {
        var dlg = new StyledMessageWindow(title, message, StyledDialogKind.Warning) { Owner = owner };
        dlg.ShowDialog();
    }

    public static void ShowError(Window owner, string title, string message)
    {
        var dlg = new StyledMessageWindow(title, message, StyledDialogKind.Error) { Owner = owner };
        dlg.ShowDialog();
    }

    /// <summary>v1.0.10：三按钮选择弹窗。
    /// 玩家点哪按钮，外层拿到对应 ThreeResult。默认聚焦第一个按钮（推荐主操作）。
    /// </summary>
    public static ThreeResult ShowThree(
        Window owner,
        string title,
        string message,
        string firstBtnLabel,
        string secondBtnLabel,
        string cancelBtnLabel)
    {
        var dlg = new StyledMessageWindow(
            title, message,
            firstBtnLabel, firstIsPrimary: true, firstIsDefault: true,
            secondBtnLabel,
            cancelBtnLabel) { Owner = owner };
        dlg.ShowDialog();
        return dlg.ThreeChoice;
    }
}

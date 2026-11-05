using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Helpers;

/// <summary>
/// Thay cho <see cref="MessageBox"/>: cùng chữ ký và kết quả trả về, nhưng hiển thị bằng khuôn thông báo chung
/// của app (<see cref="DigiScalePopup"/>: dải tiêu đề màu, "Lý do", nút lớn) để mọi thông báo cùng một format.
/// - Lỗi (Error) → đỏ, nội dung vào khối "Lý do".
/// - Cảnh báo (Warning) → cam, nội dung vào khối "Lý do".
/// - Thông tin (Information / không icon) → xanh dương.
/// - Có / Không (YesNo) → hộp xác nhận; <see cref="MessageBoxDefaultButton.Button2"/> = mặc định Không.
/// Kiểu nút khác (OKCancel, YesNoCancel...) chưa dùng trong app → rơi về MessageBox của Windows.
/// </summary>
public static class AppMessageBox
{
    public static DialogResult Show(string? text)
        => Show(null, text, null, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);

    public static DialogResult Show(string? text, string? caption)
        => Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons)
        => Show(null, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => Show(null, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
        => Show(null, text, caption, buttons, icon, defaultButton);

    public static DialogResult Show(IWin32Window? owner, string? text)
        => Show(owner, text, null, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption)
        => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons)
        => Show(owner, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons,
        MessageBoxIcon icon)
        => Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons,
        MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
    {
        owner ??= Form.ActiveForm;
        var body = text ?? string.Empty;

        if (buttons == MessageBoxButtons.YesNo)
        {
            var title = Title(caption, "common.confirm");
            var yes = DigiScalePopup.ShowConfirm(owner!, title, Loc.T("common.yes"), Loc.T("common.no"),
                message: body, defaultNo: defaultButton == MessageBoxDefaultButton.Button2);
            return yes ? DialogResult.Yes : DialogResult.No;
        }

        if (buttons != MessageBoxButtons.OK)
            return owner is null
                ? MessageBox.Show(text, caption, buttons, icon, defaultButton)
                : MessageBox.Show(owner, text, caption, buttons, icon, defaultButton);

        switch (icon)
        {
            case MessageBoxIcon.Error: // = Hand = Stop
                DigiScalePopup.ShowAlert(owner!, AlertKind.Error, Title(caption, "common.error"), reason: body);
                break;
            case MessageBoxIcon.Warning: // = Exclamation
                DigiScalePopup.ShowAlert(owner!, AlertKind.Warning, Title(caption, "common.warning"), reason: body);
                break;
            default: // Information / Asterisk / None
                DigiScalePopup.ShowAlert(owner!, AlertKind.Info, Title(caption, "common.info"), message: body);
                break;
        }
        return DialogResult.OK;
    }

    private static string Title(string? caption, string fallbackKey)
        => string.IsNullOrWhiteSpace(caption) ? Loc.T(fallbackKey) : caption;
}

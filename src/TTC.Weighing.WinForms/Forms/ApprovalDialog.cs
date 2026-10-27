using Microsoft.Extensions.Configuration;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Services;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>Người đã phê duyệt thao tác ngoại lệ.</summary>
public sealed record ApprovalResult(string UserName, string DisplayName, ApprovalLevel Level, DateTimeOffset ApprovedAt)
{
    /// <summary>Chuỗi lưu vào ApprovalRef của lượt cân/mẫu — truy vết ai duyệt, lúc nào.</summary>
    public string ApprovalRef => $"{Level}:{UserName}@{ApprovedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
}

/// <summary>
/// Hộp thoại xác thực user quản lý / trưởng đơn vị cho thao tác ngoại lệ
/// (nhập tay số cân, nhập tay tạp chất, nhập tay Pol/Brix). In / in lại phiếu KHÔNG cần duyệt.
/// Xác thực bằng API đăng nhập DigiScale. Mật khẩu không lưu, không ghi log.
///
/// Quy tắc người duyệt (SugarcaneFlow:Approval):
/// - Có cấu hình vai trò/quyền → tài khoản phải có một trong các vai trò/quyền đó.
/// - Chưa cấu hình (D-08 chưa chốt) → tài khoản hợp lệ và KHÁC tài khoản đang trực cân.
/// </summary>
public sealed class ApprovalDialog : Form
{
    private readonly ApprovalLevel _level;
    private readonly TextBox _txtUser = new() { Dock = DockStyle.Fill, Font = AgrisTheme.Body(11F) };
    private readonly TextBox _txtPass = new() { Dock = DockStyle.Fill, Font = AgrisTheme.Body(11F), UseSystemPasswordChar = true };
    private readonly Label _lblError = new()
    {
        Dock = DockStyle.Fill, ForeColor = AgrisTheme.Danger, Font = AgrisTheme.Body(9.5F, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
    };
    private readonly Button _btnOk;
    private readonly Button _btnCancel;

    public ApprovalResult? Result { get; private set; }

    private ApprovalDialog(ApprovalLevel level, string actionDescription)
    {
        _level = level;
        Text = level == ApprovalLevel.UnitHead ? Loc.T("approval.title_unit_head") : Loc.T("approval.title_manager");
        Size = new Size(620, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AgrisTheme.Canvas;
        Padding = new Padding(12);
        Font = AgrisTheme.Body(11F);

        var table = FormLayout.FieldTable();
        FormLayout.AddFullWidth(table, GridBuilder.Notice(Loc.T("approval.notice", actionDescription)), 56);
        FormLayout.AddField(table, Loc.T("approval.user"), _txtUser);
        FormLayout.AddField(table, Loc.T("approval.password"), _txtPass);
        FormLayout.AddFullWidth(table, _lblError, 32);

        _btnOk = GridBuilder.PrimaryButton(Loc.T("approval.confirm"), new Size(140, 36));
        _btnCancel = GridBuilder.SecondaryButton(Loc.T("common.cancel"), new Size(100, 36));
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnOk.Click += async (_, _) => await VerifyAsync();
        FormLayout.AddFullWidth(table, FormLayout.ActionBar(_btnOk, _btnCancel), FormLayout.ActionRowHeight);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
        Controls.Add(table);
        Shown += (_, _) => _txtUser.Focus();
    }

    /// <summary>Hiện hộp thoại; trả người duyệt hoặc null nếu hủy/không đạt.</summary>
    public static ApprovalResult? Request(IWin32Window owner, ApprovalLevel level, string actionDescription)
    {
        using var dlg = new ApprovalDialog(level, actionDescription);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Result : null;
    }

    private async Task VerifyAsync()
    {
        var user = _txtUser.Text.Trim();
        var pass = _txtPass.Text;
        if (user.Length == 0 || pass.Length == 0)
        {
            _lblError.Text = Loc.T("approval.missing_credentials");
            return;
        }

        var baseUrl = Program.AppConfiguration?.GetValue<string>("DigiScaleApi:BaseUrl");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _lblError.Text = Loc.T("approval.api_not_configured");
            return;
        }

        _btnOk.Enabled = false;
        _lblError.ForeColor = AgrisTheme.Info;
        _lblError.Text = Loc.T("approval.verifying");
        try
        {
            using var auth = new DigiScaleAuthService(baseUrl);
            var login = await auth.LoginAsync(user, pass);
            _lblError.ForeColor = AgrisTheme.Danger;

            if (login is null)
            {
                _lblError.Text = Loc.T("approval.invalid_credentials");
                return;
            }

            var (roles, permissions) = SugarcaneFlowSettings.Current.ApproverRule(_level);
            var configured = roles.Count > 0 || permissions.Count > 0;

            bool allowed;
            if (configured)
            {
                allowed = (login.Role is not null && roles.Contains(login.Role, StringComparer.OrdinalIgnoreCase))
                          || (login.Permissions ?? []).Any(p => permissions.Contains(p, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                // Chưa cấu hình vai trò duyệt → bắt buộc người thứ hai (khác người đang trực cân).
                allowed = !string.Equals(login.UserName, Program.LoggedInUserAccount, StringComparison.OrdinalIgnoreCase);
            }

            if (!allowed)
            {
                _lblError.Text = configured
                    ? Loc.T("approval.not_authorized", login.UserName)
                    : Loc.T("approval.must_be_other_user");
                return;
            }

            Result = new ApprovalResult(login.UserName, login.DisplayName, _level, DateTimeOffset.UtcNow);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _lblError.ForeColor = AgrisTheme.Danger;
            _lblError.Text = Loc.T("approval.api_error", ex.Message);
        }
        finally
        {
            _btnOk.Enabled = true;
            _txtPass.Clear();
        }
    }
}

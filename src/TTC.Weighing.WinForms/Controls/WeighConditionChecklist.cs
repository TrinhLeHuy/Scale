using TTC.Weighing.Domain.Rules;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Hiển thị 4 điều kiện chốt cân chính dưới dạng checklist thu gọn (Req 1.2, 1.7).
/// Mỗi hàng: ✓/✗ + tên điều kiện + mã lỗi nếu chưa đạt.
/// Cập nhật từ DryRun result mỗi 800ms qua <see cref="UpdateFromDecision"/>.
/// Dữ liệu hoàn toàn do server tính — control chỉ hiển thị.
/// </summary>
public sealed class WeighConditionChecklist : UserControl
{
    /// <summary>Mã điều kiện ưu tiên hiển thị, theo thứ tự xuất hiện trên checklist.</summary>
    private static readonly (string Code, string DisplayNameKey)[] DisplayConditions =
    [
        ("B-001.1", "control.checklist_cond_qr"),
        ("B-001.4", "control.checklist_cond_ai"),
        ("B-001.6", "control.checklist_cond_stable"),
        ("B-001.7", "control.checklist_cond_prereq"),
    ];

    private readonly ConditionRow[] _rows;
    private readonly Label _placeholder;

    public WeighConditionChecklist()
    {
        BackColor = AgrisTheme.Surface;
        Padding = new Padding(8, 6, 8, 6);
        AccessibleName = Loc.T("control.checklist_accessible");

        _placeholder = new Label
        {
            Text = Loc.T("control.checklist_waiting"),
            Dock = DockStyle.Fill,
            Font = AgrisTheme.Body(9F, FontStyle.Italic),
            ForeColor = AgrisTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleCenter,
            AccessibleName = Loc.T("control.checklist_no_data")
        };

        _rows = new ConditionRow[DisplayConditions.Length];

        for (var i = 0; i < DisplayConditions.Length; i++)
        {
            _rows[i] = new ConditionRow(Loc.T(DisplayConditions[i].DisplayNameKey));
        }

        // Dùng TableLayoutPanel cho bố cục cố định 4 hàng.
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = DisplayConditions.Length,
            BackColor = AgrisTheme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AccessibleName = Loc.T("control.checklist_accessible_table")
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < DisplayConditions.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            table.Controls.Add(_rows[i], 0, i);
        }

        Controls.Add(table);
        Controls.Add(_placeholder);

        // Ban đầu hiển thị placeholder.
        table.Visible = false;
        _placeholder.Visible = true;
    }

    /// <summary>
    /// Cập nhật checklist từ kết quả DryRun của <c>EvaluateAndCommitWeightHandler</c>.
    /// Thread-safe: tự gọi Invoke khi cần.
    /// </summary>
    public void UpdateFromDecision(WeighCommitDecision? decision)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateFromDecision(decision));
            return;
        }

        if (decision is null || decision.Checks.Count == 0)
        {
            ShowPlaceholder(true);
            return;
        }

        ShowPlaceholder(false);

        for (var i = 0; i < DisplayConditions.Length; i++)
        {
            var code = DisplayConditions[i].Code;
            var check = FindCheck(decision.Checks, code);
            _rows[i].Update(check);
        }
    }

    /// <summary>Xóa trạng thái, quay về placeholder chờ.</summary>
    public void Reset()
    {
        if (InvokeRequired)
        {
            Invoke(Reset);
            return;
        }

        ShowPlaceholder(true);
    }

    private void ShowPlaceholder(bool show)
    {
        _placeholder.Visible = show;

        // TableLayoutPanel là Controls[0] khi placeholder nằm ở Controls[1].
        if (Controls.Count > 1)
            Controls[0].Visible = !show;
    }

    private static ConditionCheck? FindCheck(IReadOnlyList<ConditionCheck> checks, string code)
    {
        for (var i = 0; i < checks.Count; i++)
        {
            if (checks[i].Code == code)
                return checks[i];
        }

        return null;
    }

    /// <summary>Một dòng điều kiện: icon ✓/✗ + tên + mã lỗi nếu chưa đạt.</summary>
    private sealed class ConditionRow : Panel
    {
        private readonly Label _icon;
        private readonly Label _name;
        private readonly Label _errorCode;

        public ConditionRow(string displayName)
        {
            Dock = DockStyle.Fill;
            Height = 36;
            Margin = new Padding(0, 0, 0, 2);
            BackColor = AgrisTheme.Surface;

            _icon = new Label
            {
                Size = new Size(26, 24),
                Location = new Point(2, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = AgrisTheme.Body(10F, FontStyle.Bold),
                ForeColor = AgrisTheme.TextOnBrand,
                BackColor = AgrisTheme.Neutral,
                Text = "–",
                AccessibleName = Loc.T("control.checklist_no_data")
            };

            _name = new Label
            {
                Location = new Point(34, 7),
                AutoSize = true,
                Font = AgrisTheme.Body(9.5F, FontStyle.Bold),
                ForeColor = AgrisTheme.TextPrimary,
                Text = displayName,
                AccessibleName = displayName
            };

            _errorCode = new Label
            {
                Location = new Point(34, 7),
                AutoSize = true,
                Font = AgrisTheme.Body(8.5F),
                ForeColor = AgrisTheme.Danger,
                Text = string.Empty,
                Visible = false,
                AccessibleName = Loc.T("control.checklist_accessible_error")
            };

            Controls.AddRange([_icon, _name, _errorCode]);

            // Đặt errorCode ở bên phải name sau khi layout xong.
            _name.Resize += (_, _) => RepositionErrorCode();
        }

        public void Update(ConditionCheck? check)
        {
            if (check is null)
            {
                // Điều kiện không có trong decision — hiển thị chờ.
                _icon.Text = "–";
                _icon.BackColor = AgrisTheme.Neutral;
                _errorCode.Visible = false;
                BackColor = AgrisTheme.Surface;
                _icon.AccessibleName = Loc.T("control.checklist_no_data");
                AccessibleDescription = null;
                return;
            }

            if (check.Passed)
            {
                _icon.Text = "✓";
                _icon.BackColor = AgrisTheme.Success;
                _icon.AccessibleName = Loc.T("control.checklist_passed");
                _errorCode.Visible = false;
                BackColor = AgrisTheme.Surface;
                AccessibleDescription = Loc.T("control.checklist_passed");
            }
            else
            {
                _icon.Text = "✗";
                _icon.BackColor = AgrisTheme.Danger;
                _icon.AccessibleName = Loc.T("control.checklist_failed");
                BackColor = AgrisTheme.DangerSoft;

                // Hiển thị mã lỗi + chi tiết ngắn gọn nếu có.
                var errorText = check.Detail is not null
                    ? $"[{check.Code}] {check.Detail}"
                    : $"[{check.Code}]";
                _errorCode.Text = errorText;
                _errorCode.ForeColor = AgrisTheme.Danger;
                _errorCode.Visible = true;
                AccessibleDescription = errorText;

                RepositionErrorCode();
            }
        }

        private void RepositionErrorCode()
        {
            _errorCode.Location = new Point(_name.Right + 8, _name.Top + 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Đường kẻ phân cách nhẹ giữa các dòng.
            using var pen = new Pen(AgrisTheme.Border);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}

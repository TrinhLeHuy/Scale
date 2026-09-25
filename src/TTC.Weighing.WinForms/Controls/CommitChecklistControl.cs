using TTC.Weighing.Domain.Rules;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Hiển thị từng điều kiện chốt cân đạt hoặc chưa đạt kèm lý do (UI-03).
/// Dữ liệu đến từ quyết định của server; control không tự đánh giá.
/// </summary>
public sealed class CommitChecklistControl : UserControl
{
    private readonly FlowLayoutPanel _list;
    private readonly Label _placeholder;

    public CommitChecklistControl()
    {
        BackColor = AgrisTheme.Surface;
        Padding = new Padding(10, 8, 10, 8);

        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = AgrisTheme.Surface,
            AccessibleName = Loc.T("control.commit_accessible")
        };

        _placeholder = new Label
        {
            Text = Loc.T("control.commit_waiting"),
            Dock = DockStyle.Fill,
            Font = AgrisTheme.Body(9.5F, FontStyle.Italic),
            ForeColor = AgrisTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleCenter
        };

        Controls.Add(_list);
        Controls.Add(_placeholder);
    }

    public void Render(WeighCommitDecision? decision)
    {
        if (InvokeRequired)
        {
            Invoke(() => Render(decision));
            return;
        }

        if (decision is null || decision.Checks.Count == 0)
        {
            _list.Visible = false;
            _placeholder.Visible = true;
            return;
        }

        _placeholder.Visible = false;
        _list.Visible = true;

        // Cập nhật tại chỗ khi số dòng không đổi để tránh nháy khi poll liên tục.
        if (_list.Controls.Count == decision.Checks.Count)
        {
            for (var i = 0; i < decision.Checks.Count; i++)
            {
                if (_list.Controls[i] is ChecklistRow row)
                    row.Update(decision.Checks[i]);
            }

            return;
        }

        _list.SuspendLayout();
        _list.Controls.Clear();

        foreach (var check in decision.Checks)
            _list.Controls.Add(new ChecklistRow(check) { Width = _list.ClientSize.Width - 26 });

        _list.ResumeLayout();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        foreach (Control row in _list.Controls)
            row.Width = _list.ClientSize.Width - 26;
    }

    /// <summary>Một dòng điều kiện: dấu trạng thái, mã, tên và chi tiết.</summary>
    private sealed class ChecklistRow : Panel
    {
        private readonly Label _badge = new();
        private readonly Label _code = new();
        private readonly Label _name = new();
        private readonly Label _detail = new();

        public ChecklistRow(ConditionCheck check)
        {
            Height = 40;
            Margin = new Padding(0, 0, 0, 4);
            BackColor = AgrisTheme.Surface;

            _badge.Size = new Size(26, 24);
            _badge.Location = new Point(4, 8);
            _badge.TextAlign = ContentAlignment.MiddleCenter;
            _badge.Font = AgrisTheme.Body(10F, FontStyle.Bold);
            _badge.ForeColor = AgrisTheme.TextOnBrand;

            _code.Location = new Point(38, 12);
            _code.Size = new Size(66, 18);
            _code.Font = AgrisTheme.Mono(8.5F);
            _code.ForeColor = AgrisTheme.TextMuted;

            _name.Location = new Point(110, 11);
            _name.Size = new Size(240, 20);
            _name.Font = AgrisTheme.Body(9.5F, FontStyle.Bold);
            _name.AutoEllipsis = true;

            _detail.Location = new Point(358, 11);
            _detail.Font = AgrisTheme.Body(9F);
            _detail.AutoEllipsis = true;

            Controls.AddRange([_badge, _code, _name, _detail]);

            Update(check);
        }

        public void Update(ConditionCheck check)
        {
            var color = check.Passed ? AgrisTheme.Success : AgrisTheme.Danger;

            _badge.Text = check.Passed ? "✓" : "✕";
            _badge.BackColor = color;

            _code.Text = check.Code;

            _name.Text = check.Name;
            _name.ForeColor = AgrisTheme.TextPrimary;

            _detail.Text = check.Detail ?? (check.Passed ? Loc.T("control.checklist_passed") : string.Empty);
            _detail.ForeColor = check.Passed ? AgrisTheme.TextMuted : color;

            BackColor = check.Passed ? AgrisTheme.Surface : AgrisTheme.DangerSoft;

            AccessibleName = $"{check.Name}: {(check.Passed ? Loc.T("control.checklist_passed") : Loc.T("control.checklist_failed"))}";
            AccessibleDescription = check.Detail;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _detail.Width = Math.Max(80, Width - _detail.Left - 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using var pen = new Pen(AgrisTheme.Border);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}

using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Domain.Rules;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Forms;

/// <summary>
/// Dialog phân bổ trọng lượng cho chuyến ghép xe (Req 9.1–9.6).
/// Cho phép chọn phương pháp (ApprovedRatio / FixedQuantityWithRemainder),
/// nhập danh sách dòng chủ mía, và ApprovalRef bắt buộc.
/// </summary>
public sealed class AllocateTripDialog : Form
{
    private readonly Guid _tripId;
    private readonly ComboBox _cboMethod = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtApprovalRef = new();
    private readonly DataGridView _linesGrid;
    private readonly Button _btnOk;
    private readonly Button _btnCancel;
    private readonly Button _btnAddLine;
    private readonly Button _btnRemoveLine;

    public AllocationMethod SelectedMethod { get; private set; }
    public IReadOnlyList<AllocationRequestLine> AllocationLines { get; private set; } = [];
    public string ApprovalRef { get; private set; } = string.Empty;

    public AllocateTripDialog(Guid tripId, string? tripNo)
    {
        _tripId = tripId;

        Text = Loc.T("allocate.title", tripNo ?? Loc.T("allocate.title_default"));
        Size = new Size(640, 480);
        MinimumSize = new Size(580, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(243, 245, 248);
        AccessibleName = "Dialog phân bổ trọng lượng ghép xe";

        // --- Phương pháp phân bổ ---
        _cboMethod.Items.AddRange([
            new MethodItem(AllocationMethod.ApprovedRatio, Loc.T("allocate.method_ratio")),
            new MethodItem(AllocationMethod.FixedQuantityWithRemainder, Loc.T("allocate.method_fixed"))
        ]);
        _cboMethod.SelectedIndex = 0;
        _cboMethod.DisplayMember = nameof(MethodItem.DisplayText);
        _cboMethod.Width = 280;
        _cboMethod.SelectedIndexChanged += (_, _) => UpdateGridColumnsForMethod();

        // --- Phê duyệt ---
        _txtApprovalRef.Width = 280;
        _txtApprovalRef.PlaceholderText = Loc.T("allocate.approval_placeholder");
        _txtApprovalRef.AccessibleName = "Mã phê duyệt phân bổ";

        // --- Lưới dòng phân bổ ---
        _linesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            AutoGenerateColumns = false,
            ColumnHeadersHeight = 30,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 26 },
            Font = new Font("Segoe UI", 9F),
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AccessibleName = "Dòng phân bổ"
        };

        _linesGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn
            {
                HeaderText = Loc.T("allocate.col_owner"),
                Name = "OwnerRef",
                FillWeight = 140
            },
            new DataGridViewTextBoxColumn
            {
                HeaderText = Loc.T("allocate.col_ratio"),
                Name = "Ratio",
                FillWeight = 80,
                DefaultCellStyle = { Format = "N4", Alignment = DataGridViewContentAlignment.MiddleRight }
            },
            new DataGridViewTextBoxColumn
            {
                HeaderText = Loc.T("allocate.col_fixed_kg"),
                Name = "FixedKg",
                FillWeight = 100,
                Visible = false,
                DefaultCellStyle = { Format = "N3", Alignment = DataGridViewContentAlignment.MiddleRight }
            },
            new DataGridViewCheckBoxColumn
            {
                HeaderText = Loc.T("allocate.col_remainder"),
                Name = "ReceivesRemainder",
                FillWeight = 60,
                Visible = false
            });

        // --- Buttons ---
        _btnAddLine = GridBuilder.SecondaryButton(Loc.T("allocate.btn_add"), new Size(110, 30));
        _btnAddLine.Click += (_, _) => AddEmptyLine();

        _btnRemoveLine = GridBuilder.SecondaryButton(Loc.T("allocate.btn_remove"), new Size(110, 30));
        _btnRemoveLine.Click += (_, _) => RemoveSelectedLine();

        _btnOk = GridBuilder.PrimaryButton(Loc.T("common.confirm"), new Size(110, 34));
        _btnOk.Click += (_, _) => OnConfirm();
        _btnOk.DialogResult = DialogResult.None; // We handle dialog result manually after validation.

        _btnCancel = GridBuilder.SecondaryButton(Loc.T("common.cancel"), new Size(90, 34));
        _btnCancel.DialogResult = DialogResult.Cancel;

        BuildDialogLayout();
        AddEmptyLine(); // Bắt đầu với 1 dòng mặc định.
    }

    private void BuildDialogLayout()
    {
        // Top panel: chọn phương pháp + ApprovalRef.
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 78,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(10, 8, 10, 4)
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        topPanel.Controls.Add(new Label { Text = Loc.T("allocate.field_method"), AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left }, 0, 0);
        topPanel.Controls.Add(_cboMethod, 1, 0);
        topPanel.Controls.Add(new Label { Text = Loc.T("allocate.field_approval"), AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left }, 0, 1);
        topPanel.Controls.Add(_txtApprovalRef, 1, 1);

        // Line button panel.
        var lineButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 4, 0, 0),
            BackColor = Color.White
        };
        lineButtonPanel.Controls.Add(_btnAddLine);
        lineButtonPanel.Controls.Add(_btnRemoveLine);

        // Bottom panel: OK + Cancel.
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 12, 0),
            BackColor = Color.White
        };
        bottomPanel.Controls.Add(_btnCancel);
        bottomPanel.Controls.Add(_btnOk);

        Controls.Add(_linesGrid);
        Controls.Add(lineButtonPanel);
        Controls.Add(topPanel);
        Controls.Add(bottomPanel);
    }

    private void UpdateGridColumnsForMethod()
    {
        var isRatio = _cboMethod.SelectedItem is MethodItem m
            && m.Method == AllocationMethod.ApprovedRatio;

        _linesGrid.Columns["Ratio"]!.Visible = isRatio;
        _linesGrid.Columns["FixedKg"]!.Visible = !isRatio;
        _linesGrid.Columns["ReceivesRemainder"]!.Visible = !isRatio;
    }

    private void AddEmptyLine()
    {
        _linesGrid.Rows.Add("", "0", "0", false);
    }

    private void RemoveSelectedLine()
    {
        if (_linesGrid.SelectedRows.Count > 0 && _linesGrid.Rows.Count > 1)
            _linesGrid.Rows.Remove(_linesGrid.SelectedRows[0]);
    }

    private void OnConfirm()
    {
        // Validate ApprovalRef (Req 9.4).
        var approvalRef = _txtApprovalRef.Text.Trim();
        if (string.IsNullOrWhiteSpace(approvalRef))
        {
            MessageBox.Show(Loc.T("allocate.approval_required"),
                Loc.T("allocate.approval_required_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtApprovalRef.Focus();
            return;
        }

        if (_linesGrid.Rows.Count == 0)
        {
            MessageBox.Show(Loc.T("allocate.no_lines"),
                Loc.T("allocate.no_lines_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var method = (_cboMethod.SelectedItem as MethodItem)?.Method ?? AllocationMethod.ApprovedRatio;
        var lines = new List<AllocationRequestLine>();

        for (var i = 0; i < _linesGrid.Rows.Count; i++)
        {
            var row = _linesGrid.Rows[i];
            var owner = row.Cells["OwnerRef"]?.Value?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(owner))
            {
                MessageBox.Show(Loc.T("allocate.owner_required", i + 1),
                    Loc.T("common.missing_info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (method == AllocationMethod.ApprovedRatio
                || method == AllocationMethod.OrderWeightRatio)
            {
                if (!decimal.TryParse(row.Cells["Ratio"]?.Value?.ToString(), out var ratio) || ratio <= 0)
                {
                    MessageBox.Show(Loc.T("allocate.ratio_invalid", i + 1),
                        Loc.T("allocate.ratio_invalid_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                lines.Add(new AllocationRequestLine(null, owner, ratio, null, false));
            }
            else // FixedQuantityWithRemainder
            {
                var isRemainder = row.Cells["ReceivesRemainder"]?.Value is true;

                if (isRemainder)
                {
                    lines.Add(new AllocationRequestLine(null, owner, null, null, true));
                }
                else
                {
                    if (!decimal.TryParse(row.Cells["FixedKg"]?.Value?.ToString(), out var fixedKg)
                        || fixedKg <= 0)
                    {
                        MessageBox.Show(
                            Loc.T("allocate.fixed_invalid", i + 1),
                            Loc.T("allocate.fixed_invalid_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    lines.Add(new AllocationRequestLine(null, owner, null, fixedKg, false));
                }
            }
        }

        // Validate: FixedQuantityWithRemainder phải có đúng 1 dòng nhận dư.
        if (method == AllocationMethod.FixedQuantityWithRemainder)
        {
            var remainderCount = lines.Count(l => l.ReceivesRemainder);
            if (remainderCount != 1)
            {
                MessageBox.Show(
                    Loc.T("allocate.remainder_required"),
                    Loc.T("allocate.remainder_required_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        SelectedMethod = method;
        AllocationLines = lines;
        ApprovalRef = approvalRef;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Helper để hiển thị tên phương pháp phân bổ trong ComboBox.</summary>
    private sealed record MethodItem(AllocationMethod Method, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }
}

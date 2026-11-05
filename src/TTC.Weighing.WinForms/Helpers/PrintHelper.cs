using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Helpers;

/// <summary>
/// Helper in phiếu cân. Hiển thị dialog chọn máy in, số bản, mẫu phiếu,
/// gọi <see cref="IWeighTicketPrintService.PrintAsync"/>.
/// Lỗi in KHÔNG ảnh hưởng dữ liệu cân đã lưu (Req 5.4).
/// In lại đánh dấu <c>isReprint = true</c> (Req 5.3).
/// </summary>
public static class PrintHelper
{
    /// <summary>
    /// Hiển thị dialog chọn máy in rồi gọi service in phiếu.
    /// Trả <c>null</c> nếu người dùng hủy dialog.
    /// </summary>
    public static async Task<PrintTicketResult?> ShowPrintDialogAndPrintAsync(
        IWeighTicketPrintService printService,
        Guid tripId,
        bool isReprint,
        IWin32Window? owner = null)
    {
        // Lấy danh sách máy in trước khi mở dialog
        IReadOnlyList<PrinterInfo> printers;
        try
        {
            printers = await printService.GetAvailablePrintersAsync();
        }
        catch (Exception ex)
        {
            Helpers.AppMessageBox.Show(
                $"Không lấy được danh sách máy in.{Environment.NewLine}{ex.Message}",
                "Lỗi máy in",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }

        if (printers.Count == 0)
        {
            Helpers.AppMessageBox.Show(
                "Không tìm thấy máy in nào. Vui lòng kiểm tra cài đặt máy in.",
                "Không có máy in",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return null;
        }

        // Hiển thị dialog chọn cấu hình in
        using var dialog = new PrintConfigDialog(printers, isReprint);

        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return null;

        var config = dialog.SelectedConfig;

        // Gọi service in — lỗi in không ảnh hưởng dữ liệu cân
        PrintTicketResult result;
        try
        {
            result = await printService.PrintAsync(tripId, config, isReprint);
        }
        catch (Exception ex)
        {
            Helpers.AppMessageBox.Show(
                $"Lỗi khi in phiếu cân.{Environment.NewLine}{ex.Message}"
                + Environment.NewLine + Environment.NewLine
                + "Dữ liệu cân không bị ảnh hưởng. Bạn có thể in lại sau.",
                "Lỗi in",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return new PrintTicketResult(false, null, ex.Message);
        }

        // Hiển thị kết quả
        if (result.IsSuccess)
        {
            var reprintNote = isReprint ? " (BẢN IN LẠI)" : string.Empty;
            Helpers.AppMessageBox.Show(
                $"In phiếu thành công.{reprintNote}{Environment.NewLine}Số phiếu: {result.TicketNumber}",
                "In phiếu cân",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            Helpers.AppMessageBox.Show(
                $"In phiếu thất bại: {result.ErrorMessage}"
                + Environment.NewLine + Environment.NewLine
                + "Dữ liệu cân không bị ảnh hưởng. Bạn có thể in lại sau.",
                "Lỗi in",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────
    //  Dialog chọn cấu hình in (nested private class)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Form nhỏ cho phép chọn máy in, số bản in, mã mẫu phiếu.
    /// </summary>
    private sealed class PrintConfigDialog : Form
    {
        private static readonly string[] DefaultTemplates = ["SUGARCANE", "MATERIAL"];

        private readonly ComboBox _cboPrinter = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Chọn máy in"
        };

        private readonly NumericUpDown _nudCopies = new()
        {
            Minimum = 1,
            Maximum = 10,
            Value = 1,
            AccessibleName = "Số bản in"
        };

        private readonly ComboBox _cboTemplate = new()
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            AccessibleName = "Mẫu phiếu"
        };

        private readonly Button _btnPrint;
        private readonly Button _btnCancel;

        public PrinterConfig SelectedConfig { get; private set; } = null!;

        public PrintConfigDialog(IReadOnlyList<PrinterInfo> printers, bool isReprint)
        {
            Text = isReprint ? "In lại phiếu cân (BẢN IN LẠI)" : "In phiếu cân";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = AgrisTheme.Canvas;
            ClientSize = new Size(380, 250);
            Font = AgrisTheme.Body();

            // ── Labels ──
            var lblPrinter = CreateLabel("Máy in:", 20);
            var lblCopies = CreateLabel("Số bản in:", 70);
            var lblTemplate = CreateLabel("Mẫu phiếu:", 120);

            // ── Printer ComboBox ──
            _cboPrinter.Location = new Point(120, 18);
            _cboPrinter.Width = 240;
            foreach (var p in printers)
            {
                var display = p.IsReady ? p.Name : $"{p.Name}  (không sẵn sàng)";
                _cboPrinter.Items.Add(display);
            }

            // Chọn máy in phiếu (lần in trước / cấu hình / mặc định Windows, bỏ qua máy in PDF ảo).
            var preferred = Printing.PrintPrinter.Current;
            var index = -1;
            for (var i = 0; i < printers.Count; i++)
                if (string.Equals(printers[i].Name, preferred, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
            if (index < 0)
                for (var i = 0; i < printers.Count; i++)
                    if (printers[i].IsReady && !Printing.PrintPrinter.IsVirtual(printers[i].Name)) { index = i; break; }
            _cboPrinter.SelectedIndex = index >= 0 ? index : 0;

            // ── Copies ──
            _nudCopies.Location = new Point(120, 68);
            _nudCopies.Width = 80;

            // ── Template ──
            _cboTemplate.Location = new Point(120, 118);
            _cboTemplate.Width = 240;
            _cboTemplate.Items.AddRange(DefaultTemplates);
            _cboTemplate.SelectedIndex = 0;

            // ── Reprint warning ──
            Label? lblReprint = null;
            if (isReprint)
            {
                lblReprint = new Label
                {
                    Text = "⚠ Phiếu sẽ được đánh dấu BẢN IN LẠI và ghi audit log.",
                    Location = new Point(20, 160),
                    AutoSize = true,
                    ForeColor = AgrisTheme.Warning,
                    Font = AgrisTheme.Caption()
                };
            }

            // ── Buttons ──
            _btnPrint = new Button
            {
                Text = "🖨 In",
                DialogResult = DialogResult.OK,
                Size = new Size(100, 34),
                Location = new Point(148, 195),
                BackColor = AgrisTheme.BrandGreen,
                ForeColor = AgrisTheme.TextOnBrand,
                FlatStyle = FlatStyle.Flat,
                Font = AgrisTheme.Body(10F, FontStyle.Bold),
                AccessibleName = "In phiếu cân"
            };
            _btnPrint.FlatAppearance.BorderSize = 0;
            _btnPrint.Click += OnPrintClick;

            _btnCancel = new Button
            {
                Text = "Hủy",
                DialogResult = DialogResult.Cancel,
                Size = new Size(80, 34),
                Location = new Point(258, 195),
                BackColor = AgrisTheme.Surface,
                ForeColor = AgrisTheme.TextPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = AgrisTheme.Body(),
                AccessibleName = "Hủy in"
            };
            _btnCancel.FlatAppearance.BorderColor = AgrisTheme.Border;

            AcceptButton = _btnPrint;
            CancelButton = _btnCancel;

            Controls.AddRange([
                lblPrinter, _cboPrinter,
                lblCopies, _nudCopies,
                lblTemplate, _cboTemplate,
                _btnPrint, _btnCancel
            ]);

            if (lblReprint is not null)
                Controls.Add(lblReprint);
        }

        private void OnPrintClick(object? sender, EventArgs e)
        {
            if (_cboPrinter.SelectedItem is not string printerDisplay)
            {
                Helpers.AppMessageBox.Show("Vui lòng chọn máy in.", "Thiếu thông tin",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            var templateCode = _cboTemplate.Text.Trim();
            if (string.IsNullOrEmpty(templateCode))
            {
                Helpers.AppMessageBox.Show("Vui lòng nhập mã mẫu phiếu.", "Thiếu thông tin",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            // Lấy tên máy in gốc (bỏ hậu tố trạng thái nếu có)
            var printerName = printerDisplay.Contains("  (")
                ? printerDisplay[..printerDisplay.IndexOf("  (", StringComparison.Ordinal)]
                : printerDisplay;

            Printing.PrintPrinter.Remember(printerName);
            SelectedConfig = new PrinterConfig(
                PrinterName: printerName,
                Copies: (int)_nudCopies.Value,
                TemplateCode: templateCode);
        }

        private static Label CreateLabel(string text, int top) => new()
        {
            Text = text,
            Location = new Point(20, top + 2),
            AutoSize = true,
            ForeColor = AgrisTheme.TextPrimary,
            Font = AgrisTheme.Body()
        };
    }
}

using System.Drawing.Drawing2D;
using System.Text;
using TTC.Weighing.WinForms.Controls;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Helpers;

public enum AlertKind { Success, Warning, Error, Question, Info }

/// <summary>
/// Hộp thông báo dùng chung cho luồng nghiệp vụ (đăng tài, cân, tạp chất, CCS, đẩy DigiScale).
/// Thiết kế cho người vận hành ở trạm cân: chữ lớn, dải màu theo mức độ, tách rõ
/// "Thông tin" — "Lý do" — "Cần làm". Không bao giờ hiện JSON / mã kỹ thuật.
/// </summary>
public static class DigiScalePopup
{
    private const int DialogWidth = 820;

    // ───────────────────────── API mới ─────────────────────────

    /// <summary>Hộp thông báo đầy đủ.</summary>
    public static void ShowAlert(IWin32Window owner, AlertKind kind, string title,
        string? reason = null, string? action = null, IEnumerable<(string Label, string? Value)>? facts = null,
        string? message = null)
    {
        using var dialog = new AlertForm(kind, title, message, facts, reason, action);
        dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Hộp XÁC NHẬN cùng khuôn với thông báo (dải tiêu đề màu, thông tin, khối nổi bật) + hai nút Có / Không.
    /// Enter = Có, Esc = Không. Trả true nếu người dùng chọn Có.
    /// </summary>
    public static bool ShowConfirm(IWin32Window owner, string title, string yesText, string noText,
        IEnumerable<(string Label, string? Value)>? facts = null, string? message = null,
        IEnumerable<(string Caption, string Text)>? highlights = null, bool defaultNo = false)
    {
        using var dialog = new AlertForm(AlertKind.Question, title, message, facts, null, null,
            highlights, yesText, noText, defaultNo);
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }

    /// <summary>Lỗi đã dịch (bị gì + cần làm gì).</summary>
    public static void ShowError(IWin32Window owner, string title, FriendlyError error,
        IEnumerable<(string Label, string? Value)>? facts = null)
        => ShowAlert(owner, AlertKind.Error, title, error.Reason, error.Action, facts);

    // ───────────────────── API cũ (giữ chữ ký) ─────────────────────

    /// <summary>
    /// Kết quả một thao tác có đẩy DigiScale. <paramref name="digiStatus"/> (thường là phản hồi thô)
    /// được dịch sang lý do + cách xử lý; khi thành công thì không hiển thị.
    /// </summary>
    public static void Show(IWin32Window owner, string title, string details, bool digiOk, string digiStatus)
    {
        if (digiOk)
        {
            ShowAlert(owner, AlertKind.Success, title, message: Clean(details));
            return;
        }

        var error = DigiScaleErrors.Describe(digiStatus);
        ShowAlert(owner, AlertKind.Error, title, error.Reason, error.Action, message: Clean(details));
    }

    /// <summary>Lỗi kiểm tra nghiệp vụ (không đăng tài / không lưu được...). Nội dung chính là lý do.</summary>
    public static void ShowWarning(IWin32Window owner, string title, string details)
    {
        // Tách dòng "Không thể ..." cuối thành phần kết luận; phần còn lại là lý do.
        var text = Clean(details);
        ShowAlert(owner, AlertKind.Error, title, reason: text);
    }

    /// <summary>Bỏ JSON thô và emoji (GDI không vẽ được emoji màu → ô vuông).</summary>
    internal static string Clean(string? text)
    {
        var sanitized = DigiScaleErrors.Sanitize(text);
        var sb = new StringBuilder(sanitized.Length);
        for (var i = 0; i < sanitized.Length; i++)
        {
            var c = sanitized[i];
            if (char.IsHighSurrogate(c)) { i++; continue; }                 // emoji 4 byte
            if (c is >= '\u2600' and <= '\u27BF' || c is >= '\u2B00' and <= '\u2BFF' || c == '\uFE0F')
                continue;                                                      // ✅ ⛔ ⚠ ⏳ ...
            sb.Append(c);
        }

        var lines = sb.ToString().Replace("\r", "").Split('\n').Select(l => l.Trim());
        return string.Join(Environment.NewLine, lines).Trim();
    }

    // ───────────────────────── Form ─────────────────────────

    private sealed class AlertForm : Form
    {
        public AlertForm(AlertKind kind, string title, string? message,
            IEnumerable<(string Label, string? Value)>? facts, string? reason, string? action,
            IEnumerable<(string Caption, string Text)>? highlights = null,
            string? yesText = null, string? noText = null, bool defaultNo = false)
        {
            var (accent, soft, glyph) = kind switch
            {
                AlertKind.Success => (AgrisTheme.Success, AgrisTheme.SuccessSoft, "✓"),
                AlertKind.Warning => (AgrisTheme.Warning, AgrisTheme.WarningSoft, "!"),
                AlertKind.Question => (AgrisTheme.BrandTeal, AgrisTheme.InfoSoft, "?"),
                AlertKind.Info => (AgrisTheme.Info, AgrisTheme.InfoSoft, "i"),
                _ => (AgrisTheme.Danger, AgrisTheme.DangerSoft, "✕")
            };

            Text = Clean(title);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = AgrisTheme.Surface;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Escape) return;
                DialogResult = kind == AlertKind.Question ? DialogResult.No : DialogResult.OK;
                Close();
            };

            var contentWidth = DialogWidth - 64;

            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                BackColor = AgrisTheme.Surface
            };

            // ── Dải tiêu đề màu theo mức độ ──
            var header = new HeaderBand(accent, glyph, Clean(title))
            {
                Width = DialogWidth,
                Margin = Padding.Empty
            };
            root.Controls.Add(header);

            var body = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(32, 20, 32, 8),
                Margin = Padding.Empty,
                BackColor = AgrisTheme.Surface
            };

            // ── Thông tin (Số đăng tài, Biển số, Lệnh đốn, Trọng lượng...) ──
            var factList = facts?.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToList() ?? [];
            if (factList.Count > 0)
            {
                var factTable = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Margin = new Padding(0, 0, 0, 12)
                };
                factTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
                factTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, contentWidth - 220));
                foreach (var (label, value) in factList)
                {
                    factTable.Controls.Add(TextLabel(label, AgrisTheme.Body(11F), AgrisTheme.TextMuted, 216));
                    factTable.Controls.Add(TextLabel(Clean(value), AgrisTheme.Body(11F, FontStyle.Bold), AgrisTheme.TextPrimary, contentWidth - 224));
                }
                body.Controls.Add(factTable);
            }

            // Khối nổi bật (vd. số thanh toán theo chính sách) — xanh lá, chữ lớn.
            foreach (var (caption, text) in highlights ?? [])
                body.Controls.Add(Section(caption, Clean(text), AgrisTheme.Success, AgrisTheme.SuccessSoft, contentWidth));

            if (!string.IsNullOrWhiteSpace(message))
                body.Controls.Add(TextLabel(Clean(message), AgrisTheme.Body(11.5F), AgrisTheme.TextPrimary, contentWidth, new Padding(0, 0, 0, 12)));

            if (!string.IsNullOrWhiteSpace(reason))
                body.Controls.Add(Section(Loc.T("alert.reason"), Clean(reason), accent, soft, contentWidth));

            if (!string.IsNullOrWhiteSpace(action))
                body.Controls.Add(Section(Loc.T("alert.action"), Clean(action), AgrisTheme.Info, AgrisTheme.InfoSoft, contentWidth));

            root.Controls.Add(body);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Width = DialogWidth,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
                BackColor = AgrisTheme.Surface
            };

            if (kind == AlertKind.Question)
            {
                // Xác nhận: nút chính (Có) bên phải, nút phụ (Không) bên trái. Enter = Có, Esc = Không.
                var btnYes = GridBuilder.PrimaryButton("✓  " + (yesText ?? Loc.T("common.yes")), new Size(240, 52));
                var btnNo = GridBuilder.SecondaryButton(noText ?? Loc.T("common.no"), new Size(180, 52));
                btnYes.Font = AgrisTheme.Body(11.5F, FontStyle.Bold);
                btnNo.Font = AgrisTheme.Body(11.5F, FontStyle.Bold);
                btnYes.DialogResult = DialogResult.Yes;
                btnNo.DialogResult = DialogResult.No;
                btnYes.Margin = new Padding(0, 4, 32, 20);
                btnNo.Margin = new Padding(0, 4, 12, 20);
                buttons.Controls.Add(btnYes);
                buttons.Controls.Add(btnNo);
                root.Controls.Add(buttons);
                Controls.Add(root);
                // Thao tác rủi ro (mặc định Không): Enter = Không, phải bấm chủ động vào Có.
                AcceptButton = defaultNo ? btnNo : btnYes;
                CancelButton = btnNo;
                Shown += (_, _) => (defaultNo ? btnNo : btnYes).Focus();
                return;
            }

            // ── Nút ──
            var btnOk = kind is AlertKind.Success or AlertKind.Info
                ? GridBuilder.PrimaryButton(Loc.T("alert.ok"), new Size(200, 52))
                : GridBuilder.DangerButton(Loc.T("alert.understood"), new Size(200, 52));
            if (kind == AlertKind.Warning) btnOk = GridBuilder.PrimaryButton(Loc.T("alert.understood"), new Size(200, 52));
            btnOk.Font = AgrisTheme.Body(11.5F, FontStyle.Bold);
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Margin = new Padding(0, 4, 32, 20);

            buttons.Controls.Add(btnOk);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = btnOk;
            CancelButton = btnOk;
        }

        private static Label TextLabel(string text, Font font, Color color, int width, Padding? margin = null) => new()
        {
            Text = text,
            Font = font,
            ForeColor = color,
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Margin = margin ?? new Padding(0, 4, 0, 4)
        };

        /// <summary>Khối "Lý do" / "Cần làm": nền nhạt, vạch màu trái, nhãn nhỏ in hoa + nội dung chữ lớn.</summary>
        private static Control Section(string caption, string text, Color accent, Color soft, int width)
        {
            var panel = new AccentPanel(accent)
            {
                BackColor = soft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20, 12, 16, 14),
                Margin = new Padding(0, 0, 0, 12),
                MinimumSize = new Size(width, 0),
                MaximumSize = new Size(width, 0)
            };

            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            stack.Controls.Add(TextLabel(caption.ToUpperInvariant(), AgrisTheme.Body(9F, FontStyle.Bold), accent, width - 40, new Padding(0, 0, 0, 4)));
            stack.Controls.Add(TextLabel(text, AgrisTheme.Body(12.5F, FontStyle.Bold), AgrisTheme.TextPrimary, width - 40, Padding.Empty));
            panel.Controls.Add(stack);
            return panel;
        }
    }

    /// <summary>Dải màu đầu hộp: vòng tròn biểu tượng + tiêu đề lớn chữ trắng.</summary>
    private sealed class HeaderBand : Control
    {
        private readonly Color _accent;
        private readonly string _glyph;
        private readonly string _title;

        public HeaderBand(Color accent, string glyph, string title)
        {
            _accent = accent;
            _glyph = glyph;
            _title = title;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            var titleFont = AgrisTheme.Body(14F, FontStyle.Bold);
            var textWidth = DialogWidth - 32 - 72 - 24 - 32;
            var measured = TextRenderer.MeasureText(title, titleFont, new Size(textWidth, 0), TextFormatFlags.WordBreak);
            Height = Math.Max(110, measured.Height + 48);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(_accent))
                g.FillRectangle(brush, ClientRectangle);

            const int circle = 72;
            var cx = 32;
            var cy = (Height - circle) / 2;
            using (var white = new SolidBrush(Color.White))
                g.FillEllipse(white, cx, cy, circle, circle);

            TextRenderer.DrawText(g, _glyph, AgrisTheme.Body(20F, FontStyle.Bold),
                new Rectangle(cx, cy, circle, circle), _accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var textLeft = cx + circle + 24;
            TextRenderer.DrawText(g, _title, AgrisTheme.Body(14F, FontStyle.Bold),
                new Rectangle(textLeft, 0, Width - textLeft - 32, Height), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    private sealed class AccentPanel(Color accent) : Panel
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var brush = new SolidBrush(accent);
            e.Graphics.FillRectangle(brush, 0, 0, 5, Height);
        }
    }
}

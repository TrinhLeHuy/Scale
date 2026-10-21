using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Theme;

namespace TTC.Weighing.WinForms.Printing;

// ════════════════════════════════════════════════════════════
// 1. PHIẾU ĐĂNG TÀI
// ════════════════════════════════════════════════════════════
public sealed class PhieuDangTaiTemplate(PhieuDangTaiData d) : IPrintTemplate
{
    public void Render(Graphics g, Rectangle b)
    {
        // ── Company header — centered ──
        var y = b.Top;
        using var companyFont = new Font("Times New Roman", 11, FontStyle.Bold);
        using var addressFont = new Font("Times New Roman", 9, FontStyle.Italic);
        var companySz = g.MeasureString(d.Factory.CompanyName, companyFont);
        g.DrawString(d.Factory.CompanyName, companyFont, Brushes.Black,
            b.Left + (b.Width - companySz.Width) / 2, y);
        y += (int)companySz.Height + 2;
        var addrSz = g.MeasureString(d.Factory.Address, addressFont);
        g.DrawString(d.Factory.Address, addressFont, Brushes.Black,
            b.Left + (b.Width - addrSz.Width) / 2, y);
        y += (int)addrSz.Height + 12;

        // ── Title — centered, bold ──
        using var titleFont = new Font("Times New Roman", 18, FontStyle.Bold);
        var titleText = Loc.T("print.dangtai_title");
        var titleSz = g.MeasureString(titleText, titleFont);
        g.DrawString(titleText, titleFont, Brushes.Black,
            b.Left + (b.Width - titleSz.Width) / 2, y);
        y += (int)titleSz.Height + 14;

        // ── Two-column field layout matching mẫu phiếu ──
        var x = b.Left;
        var rightColX = b.Left + 350;
        var labelW = 160;

        // Row 1: Lệnh đốn số ... Ngày
        y = PrintHelper.DrawFieldBold(g, x, y, Loc.T("print.dangtai_lenh_don"), d.LenhDonSo, labelW);
        DrawDateTimeRight(g, rightColX, y - 22, d.Ngay, d.Gio, b.Right);

        // Row 2: Số đăng tài
        y = PrintHelper.DrawFieldBold(g, x, y, Loc.T("print.dangtai_so_dangtai"), d.SoDangTai, labelW);

        // Row 3: Tên chủ mía
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_chu_mia"), d.TenChuMia ?? "", labelW);

        // Row 4: Hợp đồng số
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_hop_dong"), d.HopDongSo ?? "", labelW);

        // Row 5: Số xe vận chuyển ... Loại xe
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_so_xe"), d.SoXeVanChuyen ?? "", labelW);
        PrintHelper.DrawField(g, rightColX, y - 18, Loc.T("print.field_loai_xe"), d.LoaiXe ?? "", 90);

        // Row 6: Công TH ... Công BX
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_cong_thu_hoach"), d.CongThuHoach ?? "", labelW);
        PrintHelper.DrawField(g, rightColX, y - 18, Loc.T("print.field_cong_boc_xep"), d.CongBocXep ?? "", 90);

        // Row 7: Giống mía ... Loại mía
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_giong_mia"), d.GiongMia ?? "", labelW);
        PrintHelper.DrawField(g, rightColX, y - 18, Loc.T("print.field_loai_mia"), d.LoaiMia ?? "", 90);

        // Row 8: Hình thức thu hoạch
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_hinh_thuc_thu_hoach"), d.HinhThucThuHoach ?? "", labelW);

        // Row 9: Ghi chú
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_ghi_chu"), d.GhiChu ?? "", labelW);

        // ── Barcode (Code 128) ──
        if (!string.IsNullOrWhiteSpace(d.Barcode))
        {
            y += 10;
            if (!Code128Renderer.Draw(g, d.Barcode, x, y, 300, 50))
            {
                // Fallback: vẽ text nếu encode thất bại
                using var fallbackFont = new Font("Consolas", 12, FontStyle.Bold);
                g.DrawString(d.Barcode, fallbackFont, Brushes.Black, x, y);
            }
            y += 54;

            // Barcode text below image
            using var barcodeTextFont = new Font("Consolas", 10);
            g.DrawString(d.Barcode, barcodeTextFont, Brushes.Black, x, y);
            y += 16;
        }

        // ── Print timestamp — bottom right ──
        y += 8;
        var printTime = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        var printLabel = Loc.T("print.dangtai_ngay_in", printTime);
        using var timestampFont = new Font("Times New Roman", 9, FontStyle.Italic);
        var tsSz = g.MeasureString(printLabel, timestampFont);
        g.DrawString(printLabel, timestampFont, Brushes.Black, b.Right - tsSz.Width, y);
    }

    /// <summary>Vẽ ngày tháng năm + giờ phút theo format mẫu phiếu (góc phải).</summary>
    private static void DrawDateTimeRight(Graphics g, int x, int y, DateTime ngay, TimeSpan gio, int rightEdge)
    {
        // "Ngày  dd  tháng  MM  năm  yyyy"  /  "Giờ  hh  thg  mm"
        using var italicFont = new Font("Times New Roman", 10, FontStyle.Italic);
        var dateText = $"{Loc.T("print.field_date")}   {ngay:dd}   {Loc.T("print.dangtai_thang")}   {ngay:MM}   {Loc.T("print.dangtai_nam")}   {ngay:yyyy}";
        g.DrawString(dateText, italicFont, Brushes.Black, x, y);
        var timeText = $"{Loc.T("print.field_time")}   {gio:hh}   {Loc.T("print.dangtai_thang")}   {gio:mm}";
        g.DrawString(timeText, italicFont, Brushes.Black, x, y + 18);
    }
}

// ════════════════════════════════════════════════════════════
// 2. PHIẾU CÂN TRỪ TẠP CHẤT
// ════════════════════════════════════════════════════════════
public sealed class PhieuTapChatTemplate(PhieuTapChatData d) : IPrintTemplate
{
    public void Render(Graphics g, Rectangle b)
    {
        var y = PrintHelper.DrawCompanyHeader(g, b, b.Top, d.Factory.CompanyName, d.Factory.Address);
        y = PrintHelper.DrawTitle(g, b, y + 8, Loc.T("print.tapchat_title"));

        var x = b.Left + 80;
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.tapchat_date", d.Ngay.Day, d.Ngay.Month, d.Ngay.Year), "");
        y += 8;
        y = PrintHelper.DrawFieldBold(g, x, y, Loc.T("print.field_ma_xe"), d.MaSoXeMia);
        y = PrintHelper.DrawFieldBold(g, x, y, Loc.T("print.field_so_xe_short"), d.SoXe);
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_tl_tho"), $"{d.TrongLuongTho:N2}");
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_tl_tinh"), $"{d.TrongLuongTinh:N2}");
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_tc_thuc_te"), $"{d.TapChatThucTe:N2} %");
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_hs_phat_tc"), $"{d.HeSoPhatTapChat:N2}");
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.field_tc_thanh_toan"), $"{d.TapChatThanhToan:N2} %");

        y += 30;
        PrintHelper.DrawSignatures(g, b, y, Loc.T("print.sig_nv_can_mau"), Loc.T("print.sig_chu_mia"));
    }
}

// ════════════════════════════════════════════════════════════
// 3. PHIẾU KIỂM NGHIỆM MÍA (CCS)
// ════════════════════════════════════════════════════════════
public sealed class PhieuKiemNghiemTemplate(PhieuKiemNghiemData d) : IPrintTemplate
{
    public void Render(Graphics g, Rectangle b)
    {
        var y = PrintHelper.DrawCompanyHeader(g, b, b.Top, d.Factory.CompanyName, d.Factory.Address);
        y = PrintHelper.DrawTitle(g, b, y + 8, Loc.T("print.kiemnghiem_title"));

        var x = b.Left + 40;
        // 2 cột
        PrintHelper.DrawField(g, x, y, Loc.T("print.field_date"), d.Ngay.ToString("dd/MM/yyyy"));
        y = PrintHelper.DrawField(g, x + 300, y, Loc.T("print.field_time"), d.Gio.ToString(@"hh\:mm\:ss"));
        y += 8;
        y = PrintHelper.DrawFieldBold(g, x, y, Loc.T("print.field_ma_lo_mia"), d.MaSoLoMia);
        y += 8;
        y = PrintHelper.DrawFieldBold(g, x, y, Loc.T("print.field_chu_duong"), $"{d.ChuDuongPhanTich:N2}");

        y += 50;
        PrintHelper.DrawSignatures(g, b, y, Loc.T("print.sig_kiem_nghiem"), "");
    }
}

// ════════════════════════════════════════════════════════════
// 4. PHIẾU NHẬP MÍA
// ════════════════════════════════════════════════════════════
public sealed class PhieuNhapMiaTemplate(PhieuNhapMiaData d) : IPrintTemplate
{
    public void Render(Graphics g, Rectangle b)
    {
        var y = PrintHelper.DrawCompanyHeader(g, b, b.Top, d.Factory.CompanyName, d.Factory.Address);

        // Quốc hiệu bên phải
        g.DrawString(Loc.T("print.national_title"), PrintHelper.SmallFont, Brushes.Black, b.Right - 280, b.Top);
        g.DrawString(Loc.T("print.national_motto"), PrintHelper.SmallFont, Brushes.Black, b.Right - 240, b.Top + 14);

        y = PrintHelper.DrawTitle(g, b, y + 4, $"{Loc.T("print.nhapmia_title")}    {Loc.T("print.field_so")} {d.SoPhieu}    {Loc.T("print.field_date")} {d.Ngay:dd/MM/yyyy}");

        var x = b.Left;
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.nhapmia_ho_ten"), d.HoTen ?? "");
        PrintHelper.DrawField(g, x + 350, y - 18, Loc.T("print.nhapmia_dia_chi"), d.DiaChi ?? "");
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.nhapmia_vung_mia"), d.VungMia ?? "");
        PrintHelper.DrawField(g, x + 350, y - 18, Loc.T("print.nhapmia_phan_loai"), d.PhanLoai ?? "");
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.nhapmia_xe_van_chuyen"), d.XeVanChuyen ?? "");
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.nhapmia_ln_mia"), d.LenhMia ?? "");
        PrintHelper.DrawField(g, x + 350, y - 18, Loc.T("print.nhapmia_nv_dieu_van"), d.NvDieuVan ?? "");

        y += 4;
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.nhapmia_tl_xe_vao"), $"{d.TlXeVao:N0} kg");
        PrintHelper.DrawField(g, x + 200, y - 18, Loc.T("print.nhapmia_nv_can"), d.NvCanVao ?? "");
        PrintHelper.DrawField(g, x + 380, y - 18, "", d.ThoiGianCanVao?.ToString("dd/MM/yyyy h:mm:ss tt") ?? "");

        y = PrintHelper.DrawField(g, x, y, Loc.T("print.nhapmia_tl_xe_ra"), $"{d.TlXeRa:N0} kg");
        PrintHelper.DrawField(g, x + 200, y - 18, Loc.T("print.nhapmia_nv_can"), d.NvCanRa ?? "");
        PrintHelper.DrawField(g, x + 380, y - 18, "", d.ThoiGianCanRa?.ToString("dd/MM/yyyy h:mm:ss tt") ?? "");

        y = PrintHelper.DrawFieldBold(g, x, y, Loc.T("print.nhapmia_tl_mia_tho"), $"{d.TlMiaTho:N0} kg");
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.nhapmia_tle_tap_chat"), $"{d.TleTapChat:N2} %");
        PrintHelper.DrawField(g, x + 200, y - 18, Loc.T("print.nhapmia_hs_phat_tc"), $"{d.HeSoPhatTapChat:N2} %");
        PrintHelper.DrawField(g, x + 420, y - 18, Loc.T("print.nhapmia_tle_tc_thanh_toan"), $"{d.TleTapChatThanhToan:N2}%");

        y = PrintHelper.DrawField(g, x, y, Loc.T("print.nhapmia_tl_rac"), $"{d.TrongLuongRac:N0} kg");
        PrintHelper.DrawField(g, x + 350, y - 18, Loc.T("print.nhapmia_nv_can_tc"), d.NvCanTapChat ?? "");

        y = PrintHelper.DrawField(g, x, y, Loc.T("print.nhapmia_phieu_kn_ccs"), $"{d.PhieuKiemNghiemCcs:N2}");
        PrintHelper.DrawField(g, x + 350, y - 18, Loc.T("print.nhapmia_nv_kiem_nghiem"), d.NvKiemNghiem ?? "");

        // Bảng chi tiết
        y += 8;
        y = PrintHelper.DrawLine(g, b, y);
        var headers = new[] { Loc.T("print.nhapmia_col_tl_mia"), Loc.T("print.nhapmia_col_don_gia"), Loc.T("print.nhapmia_col_chu_duong"), Loc.T("print.nhapmia_col_tien_mia"), Loc.T("print.nhapmia_col_gia_vc"), Loc.T("print.nhapmia_col_tien_xe") };
        var colW = new[] { 100, 100, 80, 100, 80, 100 };
        var rows = new List<string[]>
        {
            new[] { $"{d.TlMiaThanhToan:N0}", $"{d.DonGia:N0}", $"{d.ChuDuong:N2}", $"{d.TienMia:N0}", $"{d.GiaVc:N0}", $"{d.TienXeVc:N0}" }
        };
        y = PrintHelper.DrawTable(g, b.Left, y, b.Width, headers, colW, rows);

        y += 20;
        PrintHelper.DrawSignatures(g, b, y, Loc.T("print.sig_nguoi_ban"), Loc.T("print.sig_lap_phieu"), Loc.T("print.sig_phong_ke_toan"));
    }
}

// ════════════════════════════════════════════════════════════
// 5. DANH SÁCH ĐĂNG TÀI
// ════════════════════════════════════════════════════════════
public sealed class DanhSachDangTaiTemplate(BaoCaoDanhSachDangTaiData d) : IPrintTemplate
{
    public void Render(Graphics g, Rectangle b)
    {
        var y = PrintHelper.DrawCompanyHeader(g, b, b.Top, d.Factory.CompanyName, d.Factory.Address);
        PrintHelper.DrawDateRange(g, b, b.Top, d.TuNgay, d.DenNgay);
        y = PrintHelper.DrawTitle(g, b, y + 8, Loc.T("print.danhsach_title"));

        var headers = new[] { Loc.T("print.danhsach_col_stt"), Loc.T("print.danhsach_col_so_xe"), Loc.T("print.danhsach_col_gio_vao"), Loc.T("print.danhsach_col_ngay_vao") };
        var colW = new[] { 80, 150, 120, 120 };
        var rows = d.Rows.Select(r => new[] { r.Stt.ToString(), r.SoXe, r.GioVao, r.NgayVao }).ToList();
        PrintHelper.DrawTable(g, b.Left + 40, y, b.Width - 80, headers, colW, rows);
    }
}

// ════════════════════════════════════════════════════════════
// 6. BÁO CÁO CÂN MÍA
// ════════════════════════════════════════════════════════════
public sealed class BaoCaoCanMiaTemplate(BaoCaoCanMiaData d) : IPrintTemplate
{
    public void Render(Graphics g, Rectangle b)
    {
        var y = PrintHelper.DrawCompanyHeader(g, b, b.Top, d.Factory.CompanyName, d.Factory.Address);
        PrintHelper.DrawDateRange(g, b, b.Top, d.TuNgay, d.DenNgay);
        y = PrintHelper.DrawTitle(g, b, y + 4, Loc.T("print.baocao_canmia_title"));

        // Tổng hợp
        var x = b.Left;
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.baocao_canmia_tong_chuyen"), $"{d.TongSoChuyenXeMia:N0}", 200);
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.baocao_canmia_tong_tho"), $"{d.TongKlMiaTho:N0}", 200);
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.baocao_canmia_tong_sach"), $"{d.TongKlMiaSach:N0}", 200);
        y = PrintHelper.DrawField(g, x, y, Loc.T("print.baocao_canmia_tc_bq"), $"{d.TyLeTcBinhQuan:N2}", 200);
        y += 8;

        var headers = new[] { Loc.T("print.baocao_canmia_col_ngay"), Loc.T("print.baocao_canmia_col_ca"), Loc.T("print.baocao_canmia_col_so_chuyen"), Loc.T("print.baocao_canmia_col_mia_tho"), Loc.T("print.baocao_canmia_col_mia_sach"), Loc.T("print.baocao_canmia_col_mia_chay"), Loc.T("print.baocao_canmia_col_mia_cay"), Loc.T("print.baocao_canmia_col_mia_khuc"), Loc.T("print.baocao_canmia_col_tc_bq") };
        var colW = new[] { 70, 30, 55, 70, 70, 65, 65, 65, 55 };
        var rows = d.Rows.Select(r => new[]
        {
            r.Ngay, r.Ca, r.SoChuyenXeMia.ToString(), $"{r.MiaTho:N0}", $"{r.MiaSach:N0}",
            $"{r.MiaChay:N0}", $"{r.MiaCay:N0}", $"{r.MiaKhuc:N0}", $"{r.TcBinhQuan:N2}"
        }).ToList();

        PrintHelper.DrawTable(g, b.Left, y, b.Width, headers, colW, rows);
    }
}


// ════════════════════════════════════════════════════════════
// 7. BÁO CÁO KIỂM TRA TRỌNG TẢI XE VẬN CHUYỂN
// ════════════════════════════════════════════════════════════
public sealed class BaoCaoTrongTaiTemplate(BaoCaoTrongTaiData d) : IPrintTemplate
{
    public void Render(Graphics g, Rectangle b)
    {
        var y = PrintHelper.DrawCompanyHeader(g, b, b.Top, d.Factory.CompanyName, d.Factory.Address);
        PrintHelper.DrawDateRange(g, b, b.Top, d.TuNgay, d.DenNgay);
        y = PrintHelper.DrawTitle(g, b, y + 4, Loc.T("print.baocao_trongtai_title"));

        var headers = new[] { Loc.T("print.baocao_trongtai_col_so_xe"), Loc.T("print.baocao_trongtai_col_ten_chu"), Loc.T("print.baocao_trongtai_col_so_chuyen"), Loc.T("print.baocao_trongtai_col_vao_max"), Loc.T("print.baocao_trongtai_col_vao_min"), Loc.T("print.baocao_trongtai_col_vao_lech"), Loc.T("print.baocao_trongtai_col_ra_max"), Loc.T("print.baocao_trongtai_col_ra_min"), Loc.T("print.baocao_trongtai_col_ra_lech"), Loc.T("print.baocao_trongtai_col_tl_bq") };
        var colW = new[] { 65, 80, 50, 55, 55, 50, 55, 55, 50, 65 };
        var rows = d.Rows.Select(r => new[]
        {
            r.SoXe, r.TenChuXe ?? "", r.SoChuyenDaVanChuyen.ToString(),
            $"{r.TrongLuongVaoMax:N0}", $"{r.TrongLuongVaoMin:N0}", $"{r.TrongLuongVaoLech:N0}",
            $"{r.TrongLuongRaMax:N0}", $"{r.TrongLuongRaMin:N0}", $"{r.TrongLuongRaLech:N0}",
            $"{r.TrongLuongBqChuyen:N0}"
        }).ToList();

        PrintHelper.DrawTable(g, b.Left, y, b.Width, headers, colW, rows);
    }
}

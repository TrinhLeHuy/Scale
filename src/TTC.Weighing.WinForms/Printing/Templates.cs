namespace TTC.Weighing.WinForms.Printing;

// ════════════════════════════════════════════════════════════
// 1. PHIẾU ĐĂNG TÀI
// ════════════════════════════════════════════════════════════
public sealed class PhieuDangTaiTemplate(PhieuDangTaiData d) : IPrintTemplate
{
    public void Render(Graphics g, Rectangle b)
    {
        var y = PrintHelper.DrawCompanyHeader(g, b, b.Top, d.Factory.CompanyName, d.Factory.Address);
        y = PrintHelper.DrawTitle(g, b, y + 8, "PHIẾU ĐĂNG TÀI");
        y += 4;

        var x = b.Left;
        // 2 cột: trái = thông tin, phải = ngày giờ
        y = PrintHelper.DrawField(g, x, y, "Lệnh đốn số", d.LenhDonSo);
        PrintHelper.DrawField(g, x + 350, y - 18, "Ngày", d.Ngay.ToString("dd/MM/yyyy"));
        y = PrintHelper.DrawField(g, x, y, "Số đăng tài", d.SoDangTai);
        PrintHelper.DrawField(g, x + 350, y - 18, "Giờ", d.Gio.ToString(@"hh\:mm"));
        y = PrintHelper.DrawField(g, x, y, "Tên chủ mía", d.TenChuMia ?? "");
        y = PrintHelper.DrawField(g, x, y, "Hợp đồng số", d.HopDongSo ?? "");
        y = PrintHelper.DrawField(g, x, y, "Số xe vận chuyển", d.SoXeVanChuyen ?? "");
        PrintHelper.DrawField(g, x + 350, y - 18, "Loại xe", d.LoaiXe ?? "");
        y = PrintHelper.DrawField(g, x, y, "Công thu hoạch", d.CongThuHoach ?? "");
        PrintHelper.DrawField(g, x + 350, y - 18, "Công bốc xếp", d.CongBocXep ?? "");
        y = PrintHelper.DrawField(g, x, y, "Giống mía", d.GiongMia ?? "");
        PrintHelper.DrawField(g, x + 350, y - 18, "Loại mía", d.LoaiMia ?? "");
        y = PrintHelper.DrawField(g, x, y, "Hình thức thu hoạch", d.HinhThucThuHoach ?? "");
        y += 4;
        y = PrintHelper.DrawField(g, x, y, "Ghi chú", d.GhiChu ?? "");

        // Barcode (text)
        if (!string.IsNullOrWhiteSpace(d.Barcode))
        {
            y += 10;
            using var barcodeFont = new Font("Consolas", 14, FontStyle.Bold);
            g.DrawString(d.Barcode, barcodeFont, Brushes.Black, x, y);
        }

        y += 30;
        PrintHelper.DrawSignatures(g, b, y, "ĐƠN VỊ NHẬN MÍA", "ĐƠN VỊ GIAO HÀNG", "NGƯỜI CÂN");
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
        y = PrintHelper.DrawTitle(g, b, y + 8, "PHIẾU CÂN TRỪ TẠP CHẤT");

        var x = b.Left + 80;
        y = PrintHelper.DrawField(g, x, y, $"Ngày {d.Ngay:dd} tháng {d.Ngay:MM} năm {d.Ngay:yyyy}", "");
        y += 8;
        y = PrintHelper.DrawFieldBold(g, x, y, "Mã số xe mía", d.MaSoXeMia);
        y = PrintHelper.DrawFieldBold(g, x, y, "Số xe", d.SoXe);
        y = PrintHelper.DrawField(g, x, y, "Trọng lượng thô", $"{d.TrongLuongTho:N2}");
        y = PrintHelper.DrawField(g, x, y, "Trọng lượng tinh", $"{d.TrongLuongTinh:N2}");
        y = PrintHelper.DrawField(g, x, y, "Tỷ lệ tạp chất thực tế", $"{d.TapChatThucTe:N2} %");
        y = PrintHelper.DrawField(g, x, y, "Hệ số phạt tạp chất", $"{d.HeSoPhatTapChat:N2}");
        y = PrintHelper.DrawField(g, x, y, "Tỷ lệ tạp chất thanh toán", $"{d.TapChatThanhToan:N2} %");

        y += 30;
        PrintHelper.DrawSignatures(g, b, y, "NHÂN VIÊN CÂN MẪU", "CHỦ MÍA XÁC NHẬN");
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
        y = PrintHelper.DrawTitle(g, b, y + 8, "PHIẾU KIỂM NGHIỆM MÍA");

        var x = b.Left + 40;
        // 2 cột
        PrintHelper.DrawField(g, x, y, "Ngày", d.Ngay.ToString("dd/MM/yyyy"));
        y = PrintHelper.DrawField(g, x + 300, y, "Giờ", d.Gio.ToString(@"hh\:mm\:ss"));
        y += 8;
        y = PrintHelper.DrawFieldBold(g, x, y, "Mã số lô mía", d.MaSoLoMia);
        y += 8;
        y = PrintHelper.DrawFieldBold(g, x, y, "Chữ đường phân tích được", $"{d.ChuDuongPhanTich:N2}");

        y += 50;
        PrintHelper.DrawSignatures(g, b, y, "KIỂM NGHIỆM VIÊN", "");
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
        g.DrawString("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", PrintHelper.SmallFont, Brushes.Black, b.Right - 280, b.Top);
        g.DrawString("Độc lập - Tự do - Hạnh phúc", PrintHelper.SmallFont, Brushes.Black, b.Right - 240, b.Top + 14);

        y = PrintHelper.DrawTitle(g, b, y + 4, $"PHIẾU NHẬP MÍA    SỐ: {d.SoPhieu}    NGÀY {d.Ngay:dd/MM/yyyy}");

        var x = b.Left;
        y = PrintHelper.DrawField(g, x, y, "Họ và tên", d.HoTen ?? "");
        PrintHelper.DrawField(g, x + 350, y - 18, "Địa chỉ", d.DiaChi ?? "");
        y = PrintHelper.DrawField(g, x, y, "Vùng mía", d.VungMia ?? "");
        PrintHelper.DrawField(g, x + 350, y - 18, "Phân loại", d.PhanLoai ?? "");
        y = PrintHelper.DrawField(g, x, y, "Xe vận chuyển", d.XeVanChuyen ?? "");
        y = PrintHelper.DrawField(g, x, y, "LN Mía", d.LenhMia ?? "");
        PrintHelper.DrawField(g, x + 350, y - 18, "NV điều vận", d.NvDieuVan ?? "FRM");

        y += 4;
        y = PrintHelper.DrawField(g, x, y, "TL xe vào", $"{d.TlXeVao:N0} kg");
        PrintHelper.DrawField(g, x + 200, y - 18, "NV cân", d.NvCanVao ?? "");
        PrintHelper.DrawField(g, x + 380, y - 18, "", d.ThoiGianCanVao?.ToString("dd/MM/yyyy h:mm:ss tt") ?? "");

        y = PrintHelper.DrawField(g, x, y, "TL xe ra", $"{d.TlXeRa:N0} kg");
        PrintHelper.DrawField(g, x + 200, y - 18, "NV cân", d.NvCanRa ?? "");
        PrintHelper.DrawField(g, x + 380, y - 18, "", d.ThoiGianCanRa?.ToString("dd/MM/yyyy h:mm:ss tt") ?? "");

        y = PrintHelper.DrawFieldBold(g, x, y, "TL mía thô", $"{d.TlMiaTho:N0} kg");
        y = PrintHelper.DrawField(g, x, y, "TLệ tạp chất", $"{d.TleTapChat:N2} %");
        PrintHelper.DrawField(g, x + 200, y - 18, "H.số phạt tạp chất", $"{d.HeSoPhatTapChat:N2} %");
        PrintHelper.DrawField(g, x + 420, y - 18, "Tỷ lệ tạp chất thanh toán", $"{d.TleTapChatThanhToan:N2}%");

        y = PrintHelper.DrawField(g, x, y, "Trọng lượng rác", $"{d.TrongLuongRac:N0} kg");
        PrintHelper.DrawField(g, x + 350, y - 18, "NV cân tạp chất", d.NvCanTapChat ?? "QC");

        y = PrintHelper.DrawField(g, x, y, "Phiếu kiểm nghiệm CCS", $"{d.PhieuKiemNghiemCcs:N2}");
        PrintHelper.DrawField(g, x + 350, y - 18, "NV kiểm nghiệm", d.NvKiemNghiem ?? "");

        // Bảng chi tiết
        y += 8;
        y = PrintHelper.DrawLine(g, b, y);
        var headers = new[] { "TL Mía T_Nhập", "Đơn giá Đ/CCS/T", "Chữ Đường", "Tiền Mía (đồng)", "Giá VC (đ/tấn)", "Tiền xe VC (đồng)" };
        var colW = new[] { 100, 100, 80, 100, 80, 100 };
        var rows = new List<string[]>
        {
            new[] { $"{d.TlMiaThanhToan:N0}", $"{d.DonGia:N0}", $"{d.ChuDuong:N2}", $"{d.TienMia:N0}", $"{d.GiaVc:N0}", $"{d.TienXeVc:N0}" }
        };
        y = PrintHelper.DrawTable(g, b.Left, y, b.Width, headers, colW, rows);

        y += 20;
        PrintHelper.DrawSignatures(g, b, y, "NGƯỜI BÁN", "LẬP PHIẾU", "PHÒNG KẾ TOÁN");
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
        y = PrintHelper.DrawTitle(g, b, y + 8, "DANH SÁCH ĐĂNG TÀI");

        var headers = new[] { "Số tt", "Số xe", "Giờ vào", "Ngày vào" };
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
        y = PrintHelper.DrawTitle(g, b, y + 4, "BÁO CÁO CÂN MÍA");

        // Tổng hợp
        var x = b.Left;
        y = PrintHelper.DrawField(g, x, y, "Tổng số chuyến xe mía", $"{d.TongSoChuyenXeMia:N0}", 200);
        y = PrintHelper.DrawField(g, x, y, "Tổng khối lượng mía thô (kg)", $"{d.TongKlMiaTho:N0}", 200);
        y = PrintHelper.DrawField(g, x, y, "Tổng khối lượng mía sạch (kg)", $"{d.TongKlMiaSach:N0}", 200);
        y = PrintHelper.DrawField(g, x, y, "Tỷ lệ tạp chất bình quân", $"{d.TyLeTcBinhQuan:N2}", 200);
        y += 8;

        var headers = new[] { "Ngày", "Ca", "Số chuyến", "Mía thô", "Mía sạch", "Mía cháy", "Mía cây", "Mía khúc", "%TC BQ" };
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
        y = PrintHelper.DrawTitle(g, b, y + 4, "BÁO CÁO KIỂM TRA TRỌNG TẢI XE VẬN CHUYỂN");

        var headers = new[] { "Số xe", "Tên chủ xe", "Số chuyến", "Vào Max", "Vào Min", "Vào Lệch", "Ra Max", "Ra Min", "Ra Lệch", "TL BQ/chuyến" };
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

namespace TTC.Weighing.WinForms.Printing;

/// <summary>Cấu hình nhà máy cho header phiếu.</summary>
public sealed record FactoryInfo(
    string CompanyName,
    string Address);

/// <summary>Data phiếu đăng tài.</summary>
public sealed record PhieuDangTaiData(
    FactoryInfo Factory,
    string LenhDonSo,
    string SoDangTai,
    DateTime Ngay,
    TimeSpan Gio,
    string? TenChuMia,
    string? HopDongSo,
    string? SoXeVanChuyen,
    string? LoaiXe,
    string? CongThuHoach,
    string? CongBocXep,
    string? GiongMia,
    string? LoaiMia,
    string? HinhThucThuHoach,
    string? GhiChu,
    string? Barcode);

/// <summary>Data phiếu cân trừ tạp chất.</summary>
public sealed record PhieuTapChatData(
    FactoryInfo Factory,
    DateTime Ngay,
    string MaSoXeMia,
    string SoXe,
    decimal TrongLuongTho,
    decimal TrongLuongTinh,
    decimal TapChatThucTe,
    decimal HeSoPhatTapChat,
    decimal TapChatThanhToan);

/// <summary>Data phiếu kiểm nghiệm mía (CCS).</summary>
public sealed record PhieuKiemNghiemData(
    FactoryInfo Factory,
    DateTime Ngay,
    TimeSpan Gio,
    string MaSoLoMia,
    decimal ChuDuongPhanTich);

/// <summary>Data phiếu nhập mía.</summary>
public sealed record PhieuNhapMiaData(
    FactoryInfo Factory,
    string SoPhieu,
    DateTime Ngay,
    string? HoTen,
    string? DiaChi,
    string? VungMia,
    string? PhanLoai,
    string? XeVanChuyen,
    string? LenhMia,
    string? NvDieuVan,
    decimal TlXeVao,
    string? NvCanVao,
    DateTime? ThoiGianCanVao,
    decimal TlXeRa,
    string? NvCanRa,
    DateTime? ThoiGianCanRa,
    decimal TlMiaTho,
    decimal TleTapChat,
    decimal HeSoPhatTapChat,
    decimal TleTapChatThanhToan,
    decimal TrongLuongRac,
    string? NvCanTapChat,
    decimal? PhieuKiemNghiemCcs,
    string? NvKiemNghiem,
    // Bảng chi tiết
    decimal TlMiaThanhToan,
    decimal DonGia,
    decimal ChuDuong,
    decimal TienMia,
    decimal GiaVc,
    decimal TienXeVc);

/// <summary>Dòng trong danh sách đăng tài.</summary>
public sealed record DanhSachDangTaiRow(
    int Stt,
    string SoXe,
    string GioVao,
    string NgayVao);

/// <summary>Data báo cáo danh sách đăng tài.</summary>
public sealed record BaoCaoDanhSachDangTaiData(
    FactoryInfo Factory,
    string TuNgay,
    string DenNgay,
    List<DanhSachDangTaiRow> Rows);

/// <summary>Dòng báo cáo cân mía theo ngày.</summary>
public sealed record BaoCaoCanMiaRow(
    string Ngay,
    string Ca,
    int SoChuyenXeMia,
    decimal MiaTho,
    decimal MiaSach,
    decimal MiaChay,
    decimal MiaCay,
    decimal MiaKhuc,
    decimal MiaTu,
    decimal MiaGoc,
    decimal TcBinhQuan,
    decimal BaBun,
    decimal Cat);

/// <summary>Data báo cáo cân mía tổng hợp.</summary>
public sealed record BaoCaoCanMiaData(
    FactoryInfo Factory,
    string TuNgay,
    string DenNgay,
    // Tổng cộng
    int TongSoChuyenXeMia,
    decimal TongKlMiaTho,
    decimal TongKlMiaSach,
    decimal TongKlMiaChay,
    decimal TongKlMiaCay,
    decimal TongKlMiaKhuc,
    decimal TongKlMiaTu,
    decimal TongKlMiaGoc,
    decimal TyLeTcBinhQuan,
    decimal TongKlBaBun,
    decimal TongKlCat,
    List<BaoCaoCanMiaRow> Rows);


/// <summary>Dòng báo cáo kiểm tra trọng tải xe vận chuyển.</summary>
public sealed record BaoCaoTrongTaiRow(
    string SoXe,
    string? TenChuXe,
    int SoChuyenDaVanChuyen,
    decimal TrongLuongVaoMax,
    decimal TrongLuongVaoMin,
    decimal TrongLuongVaoLech,
    decimal TrongLuongRaMax,
    decimal TrongLuongRaMin,
    decimal TrongLuongRaLech,
    decimal TrongLuongBqChuyen);

/// <summary>Data báo cáo kiểm tra trọng tải xe vận chuyển.</summary>
public sealed record BaoCaoTrongTaiData(
    FactoryInfo Factory,
    string TuNgay,
    string DenNgay,
    List<BaoCaoTrongTaiRow> Rows);

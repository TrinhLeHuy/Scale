using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Services;

/// <summary>
/// Giá trị hiển thị của lệnh đốn FRM cho các popup "Xem thêm" (Đăng tài, Cân):
/// hiệu lực, hợp đồng, mía cháy, thu hoạch, phân loại (cháy / khúc / cây), thẻ xe tạm, biển số dùng.
/// </summary>
public static class LenhDonDetails
{
    public static Dictionary<string, string?> Values(FrmLenhDonInfo? ld)
    {
        if (ld is null) return [];

        static string Yn(bool? v) => v switch { true => Loc.T("frm.yes"), false => Loc.T("frm.no"), null => "—" };
        static string Dt(string? s) => s is not null && DateTimeOffset.TryParse(s, out var d)
            ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "—";

        var burnt = ld.MiaChay == true
            ? $"{Loc.T("frm.yes")}{(string.IsNullOrWhiteSpace(ld.LoaiMiaChayName) ? "" : $" ({ld.LoaiMiaChayName})")}"
            : Yn(ld.MiaChay);
        var harvest = ld.ThuHoachMay switch
        {
            true => Loc.T("reg.harvest_machine"),
            false => Loc.T("reg.harvest_manual"),
            null => "—"
        };

        return new Dictionary<string, string?>
        {
            ["ma_ld"] = ld.MaLD,
            ["status"] = ld.StatusCodeName,
            ["validity"] = $"{Dt(ld.NgayCap)} → {Dt(ld.NgayHetHan)}",
            ["priority"] = ld.UuTien == "1" ? Loc.T("frm.yes") : Loc.T("frm.no"),
            ["contract_type"] = ld.LoaiHopDongName,
            ["contract"] = ld.HopDongDauTuMiaName,
            ["customer_company"] = ld.KHDNName,
            ["burnt"] = burnt,
            ["harvest"] = harvest,
            ["cane_form"] = SugarcaneCommodity.Label(ld),
            ["harvest_partner"] = ld.DoiTacThuHoachName,
            ["plot"] = ld.ThuadatcanhtacName,
            ["temp_card"] = Yn(ld.TheXeTam),
            ["temp_plate"] = ld.SoXeTam,
            ["original_plate"] = ld.BienSoXe,
            ["used_plate"] = ld.VehiclePlate,
        };
    }
}
